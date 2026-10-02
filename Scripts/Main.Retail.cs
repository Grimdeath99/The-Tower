using Godot;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Game;

public partial class Main
{
    private OptionButton _offeringSelect = null!;
    private Label _offeringPreview = null!;
    private Button _offeringApply = null!;

    private void BuildOfferingControls(long roomId, SpinBox price)
    {
        var product = Session.ProductForRoom(roomId)!;
        var offerings = _rules.ProductsFor(product.Model).ToArray();
        var body = new VBoxContainer();
        body.AddChild(Text(product.Model == "Food" ? "CAFÉ MENU" : "SHOP OFFERING", 16, Accent));
        _offeringSelect = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        foreach (var item in offerings) _offeringSelect.AddItem(item.Name + " · " + item.Category);
        _offeringSelect.Select(Array.FindIndex(offerings, item => item.Id == product.Id));
        body.AddChild(_offeringSelect);
        _offeringPreview = Wrap("", 13, Muted); body.AddChild(_offeringPreview);
        void Preview()
        {
            var chosen = offerings[_offeringSelect.Selected];
            _offeringPreview.Text = $"Default price {FinanceMoney(Session.ProductSuggestedPrice(roomId, chosen.Id))} · service {Session.ProductServiceSeconds(roomId, chosen.Id)}s per staff slot\n"
                + $"Demand ×{chosen.DemandPercent / 100d:0.##}; additional lunch factor ×{chosen.LunchDemandPercent / 100d:0.##} (11:00–14:00). Original provisional offering.";
        }
        _offeringSelect.ItemSelected += _ => Preview();
        _offeringApply = Button("Apply offering & default price", () =>
        {
            var result = Session.SetProduct(roomId, offerings[_offeringSelect.Selected].Id);
            if (result.Success) price.Value = Session.OperationFor(roomId)!.PriceMinor / 100d;
            ApplyOperation(result);
        }, "Applies this offering and resets the asking price. Accepted purchases keep their product, price and service duration.");
        body.AddChild(_offeringApply); Preview();
        _facilityBody.AddChild(body); _facilityBody.MoveChild(body, 0);
    }

    private void AppendRetailInspection(long roomId, List<string> lines)
    {
        var product = Session.ProductForRoom(roomId)!;
        var performance = Session.RetailPerformanceFor(roomId)!;
        var orders = (product.Model == "Food" ? Session.FoodOrders : Session.RetailOrders).Where(o => o.RoomId == roomId).ToArray();
        lines.Add($"PURCHASES · {product.Name}\n"
            + $"Arriving {performance.Traveling} · queued {performance.Queued} · serving {performance.Serving}/{performance.ServiceSlots} slots ({performance.StaffUtilization:P0})\n"
            + $"Reserved capacity {Session.ReservedCapacity(roomId)}/{_rules.For(World.Rooms.Single(r => r.Id == roomId).DefinitionId)!.Capacity}\n"
            + $"Retained history: {performance.RetainedOrders} accepted · {performance.Visits} arrived · {performance.Completed} completed · {performance.Abandoned} abandoned\n"
            + "Counts cover active purchases and the retained recent history. Older departed purchases are trimmed across food venues and shops.");
        lines.Add($"LIFETIME OPERATING LEDGER\nRevenue {FinanceMoney(performance.RevenueMinor)} · expenses {FinanceMoney(performance.ExpensesMinor)} · result {FinanceMoney(performance.OperatingProfitMinor)}\n"
            + "Payment occurs once after completed service, including a receipt for a free purchase. Abandoned purchases are not charged. Wages and upkeep remain daily expenses.");
        lines.Add("ACCEPTED PURCHASES · latest 4\n" + (orders.Length == 0 ? "No purchases accepted yet." : string.Join("\n", orders.TakeLast(4).Reverse().Select(order =>
            $"#{order.Id} · customer #{order.PersonId} · {_rules.ProductFor(order.ProductId)?.Name ?? order.ProductId} · {order.Status}\n"
            + $"  Agreed {FinanceMoney(order.AgreedPriceMinor)} · service {order.AgreedServiceSeconds}s"
            + (order.Status == FoodOrderStatus.Serving ? $" · {Math.Max(0, order.CompletesAt - Session.Tick)}s remaining" : "")))));
        lines.Add($"CUSTOMER DEMAND\nOffering multiplier now ×{Session.ProductDemandMultiplier(roomId) / 100d:0.##}; asking-price value is measured against {FinanceMoney(Session.ProductSuggestedPrice(roomId))}.\n"
            + $"One base customer attempt per {Session.CustomerArrivalInterval(product.Model)}s is shared by all {product.Model.ToLowerInvariant()} venues, weighted by demand and free capacity. Terminal visitor batches are additional. Each customer has one purchase.\n"
            + "Missing staff, access, utilities or poor condition can prevent service. Closing cancels unpaid purchases; customers return through physical routes. No stock or delivery system.");
    }

    private async System.Threading.Tasks.Task VerifyRetailInspection()
    {
        Session = new GameSession(Catalog, _rules, _locations, sandbox: true);
        void Must(VerticalDistrict.Core.CommandResult result) { if (!result.Success) throw new Exception(result.Message); }
        Must(World.BuildFloor(1)); Must(Session.BuildRoom("lobby", 0, 0)); Must(Session.BuildRoom("service-room", 6, 0));
        var built = Session.BuildRoom("shop", 0, 1); Must(built); var shop = built.EntityId!.Value;
        built = Session.BuildRoom("cafe", 6, 1); Must(built); var cafe = built.EntityId!.Value;
        Must(Session.Transport.InstallBank(new BankDefinition(1, 27, 0, 1, [0, 1], 12), _rules.ElevatorCostMinor));
        for (var second = 0; second < 24000 && !Session.RetailOrders.Any(o => o.RoomId == shop && o.Status == FoodOrderStatus.Serving); second++) Session.Step();
        var accepted = Session.RetailOrders.FirstOrDefault(o => o.RoomId == shop && o.Status == FoodOrderStatus.Serving)
            ?? throw new Exception("Shop customer did not reach timed service.");
        _runner.Reset(); _runner.SetSpeed(0); ClearEntitySelection(); SelectedId = shop; Canvas.ResetView(); Refresh();
        SetStatus("Preview an offering; accepted purchases keep their original terms.");
        var before = Session.Serialize(); OpenSelectedFacility(); RefreshFacilityLiveState();
        if (!BlocksWorldInput || !_facilityLiveText.Text.Contains("PURCHASES") || !_facilityLiveText.Text.Contains("LIFETIME OPERATING LEDGER"))
            throw new Exception("Retail inspector is missing actual purchase and ledger state or input isolation.");
        var gifts = Array.FindIndex(_rules.ProductsFor("Shop").ToArray(), p => p.Id == "shop-gifts");
        _offeringSelect.Select(gifts); _offeringSelect.EmitSignal(OptionButton.SignalName.ItemSelected, (long)gifts);
        await CaptureManagementView("retail-offering");
        if (Session.Serialize() != before) throw new Exception("Offering preview mutated paused state.");
        _offeringApply.EmitSignal(BaseButton.SignalName.Pressed);
        if (Session.ProductForRoom(shop)?.Id != "shop-gifts" || Session.OperationFor(shop)!.PriceMinor != Session.ProductSuggestedPrice(shop)
            || Session.RetailOrders.Single(o => o.Id == accepted.Id) != accepted)
            throw new Exception("Offering control failed to apply future terms or changed an accepted quote.");
        var checkpoint = Session.Serialize();
        Session = GameSession.Deserialize(Catalog, _rules, _locations, checkpoint);
        if (Session.Serialize() != checkpoint) throw new Exception("Retail UI checkpoint did not retain exact accepted terms, selection and demand cursors.");
        Session.Advance(checked((int)(accepted.CompletesAt - Session.Tick + 1))); Refresh(); RefreshFacilityLiveState();
        var receipt = World.Ledger.Where(e => e.Category == "Sales.Shop" && e.Description.Contains($"customer #{accepted.PersonId}:")).ToArray();
        if (receipt.Length != 1 || receipt[0].AmountMinor != accepted.AgreedPriceMinor
            || Session.RetailOrders.Single(o => o.Id == accepted.Id).Status != FoodOrderStatus.Completed)
            throw new Exception("Retail service did not complete exactly once at its original quote after loading.");
        var scroll = _facility.GetChildren().OfType<ScrollContainer>().Single();
        scroll.ScrollVertical = 370;
        before = Session.Serialize(); await CaptureManagementView("retail-purchases");
        if (Session.Serialize() != before) throw new Exception("Purchase inspection changed paused state.");
        scroll.ScrollVertical = 0; _facility.Hide();
        for (var second = 0; second < 7200 && !Session.RetailOrders.Any(o => o.Status == FoodOrderStatus.Serving); second++) Session.Step();
        var unpaid = Session.RetailOrders.FirstOrDefault(o => o.Status == FoodOrderStatus.Serving)
            ?? throw new Exception("Shop did not accept another customer for closure check.");
        OpenSelectedFacility(); _facilityOpen!.ButtonPressed = false;
        if (Session.OperationFor(shop)!.Open || Session.RetailOrders.Single(o => o.Id == unpaid.Id).Status != FoodOrderStatus.Abandoned
            || World.Ledger.Any(e => e.Category == "Sales.Shop" && e.Description.Contains($"customer #{unpaid.PersonId}:")))
            throw new Exception("Closing through the inspector did not cancel unpaid service without a sale.");
        _facility.Hide(); SelectedId = cafe; OpenSelectedFacility();
        var lunch = Array.FindIndex(_rules.ProductsFor("Food").ToArray(), p => p.Id == "cafe-lunch");
        _offeringSelect.Select(lunch); _offeringSelect.EmitSignal(OptionButton.SignalName.ItemSelected, (long)lunch);
        _offeringApply.EmitSignal(BaseButton.SignalName.Pressed);
        if (Session.ProductForRoom(cafe)?.Id != "cafe-lunch" || Session.ProductServiceSeconds(cafe) != 270
            || Session.OperationFor(cafe)!.PriceMinor != 2700) throw new Exception("Café menu command did not apply its data-defined terms.");
        before = Session.Serialize(); await CaptureManagementView("retail-cafe");
        if (Session.Serialize() != before) throw new Exception("Café inspection changed paused state.");
        _facility.Hide(); await CaptureManagementView("retail-cutaway");
        GD.Print("RETAIL_SMOKE_PASS: offering preview is pure; café/shop controls apply future terms; accepted service survives menu changes and save/load; completed receipt is exact; closure cancels unpaid service; inspector uses actual orders and ledger.");
    }
}
