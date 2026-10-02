namespace VerticalDistrict.Core.Simulation;

public sealed partial class GameSession
{
    private readonly SortedDictionary<long, string> _selectedProducts = new();
    private readonly SortedDictionary<string, RetailDemandState> _customerDemand = new(StringComparer.Ordinal)
    {
        ["Food"] = new("Food", 0, 0, 0), ["Shop"] = new("Shop", 0, 0, 0)
    };

    public IReadOnlyList<FoodOrder> RetailOrders => _foodOrders.Values.Where(order => OrderModel(order) == "Shop").ToArray();
    public IReadOnlyList<RetailDemandState> CustomerDemand => _customerDemand.Values.ToArray();
    public int RetailQueueCount(long roomId) => _foodOrders.Values.Count(order => order.RoomId == roomId && order.Status == FoodOrderStatus.Queued);
    private string? OrderModel(FoodOrder order) => Room(order.RoomId) is { } room ? Rules.For(room.DefinitionId)?.Model : null;
    public ProductRule? ProductForRoom(long roomId)
    {
        if (Room(roomId) is not { } room || Rules.For(room.DefinitionId)?.Model is not ("Food" or "Shop")) return null;
        return _selectedProducts.TryGetValue(roomId, out var id) ? Rules.ProductFor(id) : Rules.DefaultProductFor(Rules.For(room.DefinitionId)!.Model);
    }
    private ProductRule? OfferedProduct(long roomId, string? productId)
    {
        var selected = ProductForRoom(roomId);
        if (productId == null) return selected;
        return Rules.ProductFor(productId) is { } candidate && candidate.Model == selected?.Model ? candidate : null;
    }
    public long ProductSuggestedPrice(long roomId, string? productId = null)
        => OfferedProduct(roomId, productId) is { } product ? product.PriceFor(Rules.For(Room(roomId)!.DefinitionId)!) : 0;
    public int ProductServiceSeconds(long roomId, string? productId = null)
        => OfferedProduct(roomId, productId) is { } product ? product.ServiceSecondsFor(Rules.For(Room(roomId)!.DefinitionId)!) : 0;
    public int ProductDemandMultiplier(long roomId) => ProductForRoom(roomId) is { } product
        ? checked(product.DemandPercent * (Hour is >= 11 and < 14 ? product.LunchDemandPercent : 100) / 100) : 100;

    public CommandResult SetProduct(long roomId, string productId)
    {
        if (string.IsNullOrWhiteSpace(productId) || !_operations.TryGetValue(roomId, out var op) || OfferedProduct(roomId, productId) is not { } product)
            return new(false, "Select an offering supported by this food venue or shop.");
        var suggested = product.PriceFor(Rules.For(Room(roomId)!.DefinitionId)!);
        _selectedProducts[roomId] = product.Id;
        SetOp(op with { PriceMinor = suggested });
        return new(true, "Offering and its suggested asking price applied. Accepted purchases keep their product, price and service duration.");
    }

    public RetailPerformance? RetailPerformanceFor(long roomId)
    {
        if (ProductForRoom(roomId) == null) return null;
        var orders = _foodOrders.Values.Where(order => order.RoomId == roomId).ToArray();
        var serving = orders.Count(order => order.Status == FoodOrderStatus.Serving);
        var op = _operations[roomId]; var rule = Rules.For(Room(roomId)!.DefinitionId)!;
        var slots = op.Open && op.Staff >= rule.Staff ? op.Staff : 0;
        long revenue = 0, expenses = 0;
        foreach (var entry in World.Ledger.Where(entry => entry.EntityId == roomId))
        {
            var kind = FinanceClassification.Classify(entry.Category, entry.AmountMinor);
            if (kind == FinancialFlowKind.OperatingRevenue) revenue = checked(revenue + entry.AmountMinor);
            else if (kind == FinancialFlowKind.OperatingExpense) expenses = checked(expenses - entry.AmountMinor);
        }
        return new(roomId, orders.Length, orders.Count(order => order.QueuedAt > 0 || order.Status == FoodOrderStatus.Completed),
            orders.Count(order => order.Status == FoodOrderStatus.Completed), orders.Count(order => order.Status == FoodOrderStatus.Abandoned),
            orders.Count(order => order.Status == FoodOrderStatus.Traveling), orders.Count(order => order.Status == FoodOrderStatus.Queued),
            serving, slots, slots == 0 ? 0 : Math.Min(1, (double)serving / slots), revenue, expenses);
    }

    public int CustomerArrivalInterval(string model) => Rules.Businesses.Where(rule => rule.Model == model)
        .Select(rule => rule.ArrivalIntervalSeconds).DefaultIfEmpty(86400).Min();

    private void UpdateCustomerDemand()
    {
        foreach (var model in new[] { "Food", "Shop" })
        {
            var cursor = _customerDemand[model];
            if (Tick < cursor.NextAttemptTick) continue;
            var rooms = World.Rooms.Where(room => Rules.For(room.DefinitionId)?.Model == model).OrderBy(room => room.Id).ToArray();
            if (rooms.Length == 0) continue;
            _customerDemand[model] = new(model, Tick, checked(Tick + CustomerArrivalInterval(model)), checked(cursor.Attempts + 1));
            if (ChooseCustomerDestination(rooms) is { } selected) Spawn(selected, "Customer", Rules.For(selected.DefinitionId)!.VisitSeconds);
        }
    }

    private RoomInstance? ChooseCustomerDestination(IEnumerable<RoomInstance> rooms)
    {
        var choices = rooms.Select(room => (Room: room, Rule: Rules.For(room.DefinitionId)!, Op: _operations[room.Id]))
            .Where(candidate => Ready(candidate.Room, candidate.Op, candidate.Rule) && ReservedCapacity(candidate.Room.Id) < candidate.Rule.Capacity)
            .Select(candidate => (candidate.Room, Score: DemandFor(candidate.Room.Id).Score,
                Free: candidate.Rule.Capacity - ReservedCapacity(candidate.Room.Id)))
            .Where(candidate => candidate.Score > 0).OrderBy(candidate => candidate.Room.Id).ToArray();
        if (choices.Length == 0 || RandomValue() % 100 >= choices.Max(candidate => candidate.Score)) return null;
        if (choices.Length == 1) return choices[0].Room;
        var total = choices.Sum(candidate => checked(candidate.Score * candidate.Free));
        var draw = (long)RandomValue() % total;
        foreach (var candidate in choices)
        {
            draw -= candidate.Score * candidate.Free;
            if (draw < 0) return candidate.Room;
        }
        return choices[^1].Room;
    }
}
