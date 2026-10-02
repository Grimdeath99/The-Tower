namespace VerticalDistrict.Core.Simulation;

public sealed record RoomProductSelection(long RoomId, string ProductId);
public sealed record RetailSnapshot(RoomProductSelection[] Selections, RetailDemandState[] Demand, long LegacyThroughOrderId);

public sealed partial class GameSession
{
    private long _legacyRetailThroughOrderId;
    private static RetailSnapshot EmptyRetailSnapshot() => new([], [new("Food", 0, 0, 0), new("Shop", 0, 0, 0)], 0);
    private RetailSnapshot CaptureRetailSnapshot() => new(_selectedProducts.Select(pair => new RoomProductSelection(pair.Key, pair.Value)).ToArray(),
        _customerDemand.Values.ToArray(), _legacyRetailThroughOrderId);

    private FoodOrder UpgradeLegacyOrder(FoodOrder order)
    {
        var room = Room(order.RoomId);
        Require(room != null && Rules.For(room.DefinitionId)?.Model is "Food" or "Shop", "An imported purchase references an unknown food venue or shop.");
        var rule = Rules.For(room.DefinitionId)!;
        return order with { ProductId = Rules.DefaultProductFor(rule.Model).Id, AgreedServiceSeconds = rule.ServiceSeconds };
    }

    private void ValidateOrderProduct(FoodOrder order, bool legacy)
    {
        Require(!string.IsNullOrWhiteSpace(order.ProductId) && Rules.ProductFor(order.ProductId) != null,
            "A purchase references an unavailable product.");
        var product = Rules.ProductFor(order.ProductId)!;
        var rule = Rules.For(Room(order.RoomId)!.DefinitionId)!;
        Require(product.Model == rule.Model && order.AgreedServiceSeconds is > 0 and <= ProductRule.MaximumAgreedServiceSeconds
            && order.AgreedServiceSeconds == (legacy ? rule.ServiceSeconds : product.ServiceSecondsFor(rule)),
            "A purchase's frozen product model or service duration is invalid.");
        if (legacy)
            Require(product.Id == Rules.DefaultProductFor(rule.Model).Id,
                "An imported purchase cannot claim a product that did not exist in its original save.");
        if (order.Status == FoodOrderStatus.Serving)
            Require(order.CompletesAt >= checked(order.ServiceStartedAt + order.AgreedServiceSeconds),
                "A purchase finishes before its accepted service duration.");
    }

    private void RestoreRetailSnapshot(RetailSnapshot? saved)
    {
        Require(saved != null && saved.Selections != null && saved.Demand != null
            && saved.LegacyThroughOrderId >= 0 && saved.LegacyThroughOrderId < _nextFoodOrderId,
            "Missing retail selections, demand state, or invalid purchase migration boundary.");
        foreach (var selected in saved.Selections)
        {
            Require(selected != null && !_selectedProducts.ContainsKey(selected.RoomId)
                && Room(selected.RoomId) is { } room && Rules.For(room.DefinitionId)?.Model is "Food" or "Shop"
                && !string.IsNullOrWhiteSpace(selected.ProductId) && Rules.ProductFor(selected.ProductId) is { } product
                && product.Model == Rules.For(room.DefinitionId)!.Model,
                "Invalid or duplicate selected product, facility model, or room reference.");
            _selectedProducts.Add(selected.RoomId, selected.ProductId);
        }
        Require(saved.Demand.Length == 2, "Retail demand must contain exactly the Food and Shop cursors.");
        _customerDemand.Clear();
        foreach (var cursor in saved.Demand)
        {
            Require(cursor != null && cursor.Model is "Food" or "Shop" && !_customerDemand.ContainsKey(cursor.Model)
                && cursor.LastAttemptTick >= 0 && cursor.LastAttemptTick <= Tick && cursor.NextAttemptTick >= 0
                && cursor.Attempts >= 0 && cursor.Attempts <= Tick / 60 + 1,
                "Invalid or duplicate customer demand model, attempt count, or timestamp.");
            Require(cursor.Attempts == 0
                    ? cursor.LastAttemptTick == 0 && cursor.NextAttemptTick <= Tick
                    : cursor.LastAttemptTick > 0 && cursor.LastAttemptTick % 60 == 0
                        && cursor.NextAttemptTick == checked(cursor.LastAttemptTick + CustomerArrivalInterval(cursor.Model)),
                "A customer demand cursor cannot reproduce its next scheduled attempt.");
            _customerDemand.Add(cursor.Model, cursor);
        }
        _legacyRetailThroughOrderId = saved.LegacyThroughOrderId;
        foreach (var order in _foodOrders.Values)
        {
            Require(!_ownershipResidents.ContainsKey(order.PersonId), "A purchase customer reuses a condominium resident identity.");
            ValidateOrderProduct(order, order.Id <= saved.LegacyThroughOrderId);
        }
    }

    private void MigrateLegacyRetail()
    {
        foreach (var person in _people.Values.OrderBy(person => person.Id))
        {
            var room = Room(person.RoomId)!; var rule = Rules.For(room.DefinitionId)!;
            if (rule.Model != "Shop" || person.Role is not ("Customer" or "Tourist")) continue;
            var receipt = BusinessSale(room.Id, person.Id, "Sales.Shop");
            var completed = receipt != null || person.Activity == PersonActivity.Visiting;
            var entered = receipt?.TimestampTicks ?? Math.Clamp(person.ActionAt - rule.VisitSeconds, person.CreatedAt, Tick);
            var status = completed ? FoodOrderStatus.Completed : person.Activity == PersonActivity.Arriving ? FoodOrderStatus.Traveling : FoodOrderStatus.Abandoned;
            var id = _nextFoodOrderId; _nextFoodOrderId = checked(id + 1);
            var order = new FoodOrder(id, room.Id, person.Id, person.CreatedAt,
                completed ? receipt?.AmountMinor ?? 0 : _operations[room.Id].PriceMinor, status,
                completed ? entered : 0, completed ? entered : 0, completed ? entered : 0,
                checked((completed ? entered : person.CreatedAt) + Rules.Management.FoodPatienceSeconds),
                completed ? entered : status == FoodOrderStatus.Abandoned ? Tick : 0,
                Rules.DefaultProductFor("Shop").Id, rule.ServiceSeconds);
            if (completed) RequireSale(room.Id, person.Id, "Sales.Shop", order.AgreedPriceMinor, entered);
            _foodOrders.Add(order.Id, order); _foodByPerson.Add(person.Id, order.Id);
            if (status == FoodOrderStatus.Traveling) _activeFoodOrders.Add(order.Id);
        }
        _legacyRetailThroughOrderId = _nextFoodOrderId - 1;
        RestoreRetailSnapshot(new RetailSnapshot([], [new("Food", 0, Tick, 0), new("Shop", 0, Tick, 0)], _legacyRetailThroughOrderId));
    }
}
