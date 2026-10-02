using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

public static partial class Program
{
    private static readonly string Data = Path.Combine(AppContext.BaseDirectory, "Data");
    private static readonly ContentCatalog Catalog = ContentCatalog.Load(File.ReadAllText(Path.Combine(Data, "construction.catalog.json")));
    private static readonly SimulationRules Rules = SimulationRules.Load(File.ReadAllText(Path.Combine(Data, "simulation.rules.json")), Catalog);
    private static readonly LocationCatalog Locations = LocationCatalog.Load(File.ReadAllText(Path.Combine(Data, "locations.json")));
    public static int Main(string[] args)
    {
        var cases = new List<(string Name, Action Run)>();
        void Test(string name, Action run) => cases.Add((name, run));
        Test("Tenancies: office lease survives the night and returning workers keep stable member identities", OfficeTenancy);
        Test("Tenancies: residents retain identity and agreed rent while absent during the day", HomeTenancy);
        Test("Tenancies: inaccessible leases suspend rent and leave after grace through real routes", InaccessibleTenancy);
        Test("Tenancies: explicit departure releases the contracted capacity only after physical exit", TenantDeparture);
        Test("Tenancies: renewal preserves the last old-term bill and then accepts the new asking price", TenantRenewal);
        Test("Food: a timed completed service charges the accepted price exactly once across save", CompletedFood);
        Test("Food: waiting customers can abandon without generating a purchase", AbandonedFood);
        Test("Food: queue capacity and simultaneous service slots remain bounded under demand", FoodCapacity);
        Test("Hotel: booking freezes its quote and paid checkout creates cleaning before resale", HotelLifecycle);
        Test("Hotel: unreachable reservation cancels without sale and retains physical ownership", HotelCancellation);
        Test("Business persistence: active tenant members orders and bookings reject corrupt references", BusinessValidation);
        Test("Progression: the first earned award survives a boundary save and is emitted once", FirstPromotion);
        RegisterServiceCases(cases);
        SatisfactionCases.Register(Test, Catalog, Rules, Locations);
        OwnershipCases.Register(Test, Catalog, Rules, Locations);
        RetailCases.Register(Test, Catalog, Rules, Locations);
        RegisterCatalogueCases(cases);
        RegisterProductCatalogueCases(cases);
        var chosen = cases.Where(c => args.Length == 0 || c.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).ToArray();
        var failures = 0;
        foreach (var (name, run) in chosen)
        {
            var timer = Stopwatch.StartNew();
            try { run(); Console.WriteLine($"PASS {name} ({timer.ElapsedMilliseconds}ms)"); }
            catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
        }
        Console.WriteLine($"{chosen.Length - failures}/{chosen.Length} management cases passed; {failures} failed.");
        return failures == 0 && chosen.Length > 0 ? 0 : 1;
    }
    static partial void RegisterServiceCases(List<(string Name, Action Run)> cases);
    static partial void RegisterCatalogueCases(List<(string Name, Action Run)> cases);
    static partial void RegisterProductCatalogueCases(List<(string Name, Action Run)> cases);
    private static GameSession NewGame() => new(Catalog, Rules, Locations);
    private static long Build(GameSession game, string id, int x, int floor)
    { var result = game.BuildRoom(id, x, floor); Succeed(result); return result.EntityId!.Value; }
    private static void Succeed(CommandResult result) => Check(result.Success, result.Message);
    private static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Until(GameSession game, Func<bool> condition, int limit, string message)
    { for (var second = 0; second < limit && !condition(); second++) game.Step(); Check(condition(), message); }
    private static GameSession Restore(GameSession game) => GameSession.Deserialize(Catalog, game.Rules, Locations, game.Serialize());
    private static (GameSession Game, long Room) TenantTower(string kind)
    {
        var game = NewGame(); Succeed(game.World.BuildFloor(1)); Build(game, "lobby", 0, 0);
        Build(game, "service-room", 8, 0); var room = Build(game, kind, 0, 1);
        Succeed(game.Transport.InstallBank(new BankDefinition(1, 27, 0, 1, [0, 1], 12), Rules.ElevatorCostMinor));
        return (game, room);
    }
    private static void OfficeTenancy()
    {
        var (game, room) = TenantTower("office");
        Until(game, () => game.World.Ledger.Any(e => e.Category == "Lease.Office"), 7200, "Office lease never commenced after real arrival");
        var tenant = game.TenantFor(room)!; Check(tenant.MemberIds.Length == 8 && game.Occupancy(room) == 8, "Office assignment or physical occupancy is wrong.");
        Succeed(game.SetPrice(room, tenant.AgreedRentMinor * 2));
        Until(game, () => game.Hour >= 19 && game.People.All(p => p.RoomId != room), 12 * 3600, "Workers did not leave at the end of their shift");
        Check(game.ContractedOccupancy(room) == 8 && game.TenantFor(room)!.Id == tenant.Id && game.OperationFor(room)!.ContractActive,
            "Nighttime physical vacancy erased the office agreement.");
        var resumed = Restore(game); game.Advance(15 * 3600); resumed.Advance(15 * 3600);
        Check(game.Serialize() == resumed.Serialize(), "Offsite tenant members rerolled on load.");
        Check(game.TenantFor(room)!.MemberIds.SequenceEqual(tenant.MemberIds) && game.People.Where(p => p.RoomId == room).All(p => tenant.MemberIds.Contains(p.Id)),
            "A returning worker became a new tenant/member.");
        var receipts = game.World.Ledger.Where(e => e.EntityId == room && e.Category == "Lease.Office").ToArray();
        Check(receipts.Length == 2 && receipts.All(e => e.AmountMinor == tenant.AgreedRentMinor), "Current asking price retroactively changed a live office lease.");
    }
    private static void HomeTenancy()
    {
        var (game, room) = TenantTower("studio");
        Until(game, () => game.World.Ledger.Any(e => e.Category == "Lease.Home"), 18 * 3600, "Residential rent never followed a physical move-in");
        var tenant = game.TenantFor(room)!; Succeed(game.SetPrice(room, tenant.AgreedRentMinor * 2));
        Until(game, () => game.Day == 2 && game.Hour >= 13 && game.People.All(p => p.RoomId != room), 16 * 3600, "Residents never went outside");
        Check(game.TenantFor(room)!.Id == tenant.Id && game.ContractedOccupancy(room) == 2 && game.OperationFor(room)!.ContractActive,
            "A resident leaving for the day cancelled the home contract.");
        Until(game, () => game.Day >= 3, 12 * 3600, "Second rental boundary was not reached");
        Check(game.People.Where(p => p.RoomId == room).Select(p => p.Id).Order().SequenceEqual(tenant.MemberIds.Order()), "Returning household identities changed.");
        var receipts = game.World.Ledger.Where(e => e.EntityId == room && e.Category == "Lease.Home").ToArray();
        Check(receipts.Length == 2 && receipts.All(e => e.AmountMinor == tenant.AgreedRentMinor), "Residential rent ignored the agreed term price.");
    }
    private static void InaccessibleTenancy()
    {
        var (game, room) = TenantTower("office");
        Until(game, () => game.Occupancy(room) == 8 && game.Hour >= 9, 7200, "Office did not become occupied");
        var tenant = game.TenantFor(room)!; var receipts = game.World.Ledger.Count(e => e.Category == "Lease.Office");
        Succeed(game.Transport.SetBankOutOfService(1, true));
        game.Advance(Rules.Management.TenantGraceDays * 86400);
        Check(game.World.Ledger.Count(e => e.Category == "Lease.Office") == receipts, "Disconnected contract kept earning rent.");
        Check(game.TenantFor(room)?.Status == TenancyStatus.Departing && game.ContractedOccupancy(room) == 8,
            "Tenant disappeared or retained a healthy lease after unresolved inaccessible reviews.");
        Succeed(game.Transport.SetBankOutOfService(1, false));
        Until(game, () => game.Tenants.Single(t => t.Id == tenant.Id).Status == TenancyStatus.Ended, 3600, "Departing workers never physically exited after access recovered");
        Check(game.ContractedOccupancy(room) == 0 && tenant.MemberIds.All(id => game.Transport.JourneyFor(id) == null), "Ended tenant retained capacity or journeys.");
    }
    private static void TenantDeparture()
    {
        var (game, room) = TenantTower("office"); Until(game, () => game.Occupancy(room) == 8, 3600, "Office never filled");
        var tenant = game.TenantFor(room)!; Succeed(game.SetOpen(room, false));
        Check(game.ContractedOccupancy(room) == 8 && !game.DemolishRoom(room).Success, "Closure erased people or enabled occupied demolition.");
        Until(game, () => game.TenantFor(room) == null, 3600, "Closed tenancy never ended after its members left");
        Check(game.Tenants.Single(t => t.Id == tenant.Id).Status == TenancyStatus.Ended && game.ContractedOccupancy(room) == 0, "Wrong tenant/capacity was released.");
        Succeed(game.DemolishRoom(room)); Check(Restore(game).Serialize() == game.Serialize(), "Departed/removed room retained stale business references.");
    }
    private static void TenantRenewal()
    {
        var (game, room) = TenantTower("studio");
        Until(game, () => game.World.Ledger.Any(e => e.Category == "Lease.Home"), 18 * 3600, "Home never started its first agreement");
        var initial = game.TenantFor(room)!; var nextRent = initial.AgreedRentMinor + 1000;
        Succeed(game.SetPrice(room, nextRent));
        Until(game, () => game.Day >= initial.RenewalDay, Rules.Management.LeaseDays * 86400, "Renewal day was never reached");
        Check(game.World.Ledger.Where(e => e.EntityId == room && e.Category == "Lease.Home").All(e => e.AmountMinor == initial.AgreedRentMinor),
            "The final old-term midnight bill used a future renewal price.");
        Until(game, () => game.TenantFor(room)?.RenewalDay > initial.RenewalDay, 24 * 3600, "A healthy tenant did not renew");
        var renewed = game.TenantFor(room)!;
        Check(renewed.Id == initial.Id && renewed.MemberIds.SequenceEqual(initial.MemberIds) && renewed.AgreedRentMinor == nextRent,
            "Renewal replaced the household or ignored the accepted asking price.");
        var nextDay = game.Day + 1;
        Until(game, () => game.Day == nextDay, 24 * 3600, "Renewed rental period did not close");
        Check(game.World.Ledger.Last(e => e.EntityId == room && e.Category == "Lease.Home").AmountMinor == nextRent,
            "The new agreement did not control subsequent rent.");
    }
    private static (GameSession Game, long Room) FoodFixture(bool busy = false)
    {
        var tuning = Rules;
        if (busy)
        {
            var json = JsonNode.Parse(JsonSerializer.Serialize(Rules, SimulationRules.JsonOptions))!;
            var food = json["businesses"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "cafe")!;
            food["arrivalIntervalSeconds"] = 30; food["serviceSeconds"] = 300;
            tuning = SimulationRules.Load(json.ToJsonString(), Catalog);
        }
        var game = new GameSession(Catalog, tuning, Locations); Build(game, "lobby", 0, 0);
        return (game, Build(game, "cafe", 6, 0));
    }
    private static void CompletedFood()
    {
        var (game, room) = FoodFixture();
        Until(game, () => game.FoodOrders.Any(o => o.Status == FoodOrderStatus.Serving), 7200, "Reachable customer never began real food service");
        var order = game.FoodOrders.First(o => o.Status == FoodOrderStatus.Serving);
        Check(!game.World.Ledger.Any(e => e.Category == "Sales.Food"), "Food was charged at arrival before service.");
        Succeed(game.SetPrice(room, order.AgreedPriceMinor * 2)); var resumed = Restore(game);
        game.Advance((int)(order.CompletesAt - game.Tick - 1)); resumed.Advance((int)(order.CompletesAt - resumed.Tick - 1));
        Check(!game.World.Ledger.Any(e => e.Category == "Sales.Food"), "Food charged before its work duration elapsed.");
        game.Step(); resumed.Step(); Check(game.Serialize() == resumed.Serialize(), "Serving order did not resume exactly.");
        var receipt = game.World.Ledger.Single(e => e.Category == "Sales.Food");
        Check(receipt.AmountMinor == order.AgreedPriceMinor && receipt.Description.Contains($"Order #{order.Id},"), "Completed purchase lost its frozen quote or order identity.");
        game.Advance(30); Check(game.World.Ledger.Count(e => e.Category == "Sales.Food") == 1, "Completed order charged twice.");
    }
    private static void AbandonedFood()
    {
        var (game, room) = FoodFixture();
        Until(game, () => game.FoodOrders.Any(o => o.Status == FoodOrderStatus.Queued), 7200, "No customer reached the queue");
        var order = game.FoodOrders.First(o => o.Status == FoodOrderStatus.Queued); Succeed(game.SetStaff(room, 0));
        Until(game, () => game.FoodOrders.Single(o => o.Id == order.Id).Status == FoodOrderStatus.Abandoned,
            Rules.Management.FoodPatienceSeconds + 1, "Unserved customer never abandoned its wait");
        Check(game.World.Ledger.All(e => e.Category != "Sales.Food"), "An abandoned purchase generated a sale.");
        Until(game, () => game.People.All(p => p.Id != order.PersonId), 1200, "Abandoned customer never physically left");
        Succeed(game.SetStaff(room, 2));
        Until(game, () => game.World.Ledger.Any(e => e.Category == "Sales.Food"), 7200, "Corrected staffing never restored completed sales");
    }
    private static void FoodCapacity()
    {
        var (game, room) = FoodFixture(busy: true); var queued = false; var reduced = false;
        Succeed(game.SetStaff(room, 3));
        for (var tick = 0; tick < 7200; tick++)
        {
            if (!reduced && game.FoodOrders.Count(o => o.Status == FoodOrderStatus.Serving) == 3)
            { Succeed(game.SetStaff(room, 2)); reduced = true; }
            game.Step(); queued |= game.FoodQueueCount(room) > 0;
            Check(game.ReservedCapacity(room) <= game.Rules.For("cafe")!.Capacity, "Food accepted more customers than physical capacity.");
            Check(game.FoodOrders.Count(o => o.Status == FoodOrderStatus.Serving) <= game.OperationFor(room)!.Staff, "Food exceeded actual parallel service slots.");
        }
        Check(queued && reduced && game.FoodOrders.Any(o => o.Status == FoodOrderStatus.Completed), "Stress fixture did not exercise waiting, staffing reduction, and completed purchases.");
    }
    private static void HotelLifecycle()
    {
        var (game, room) = TenantTower("hotel-room");
        Until(game, () => game.HotelBookingFor(room)?.Status == HotelBookingStatus.Reserved, 16 * 3600, "Hotel never accepted a ready reservation");
        var booking = game.HotelBookingFor(room)!; Succeed(game.SetPrice(room, booking.AgreedNightlyPriceMinor * 2));
        Until(game, () => game.HotelBookingFor(room)?.Status == HotelBookingStatus.CheckedIn, 1200, "Guest never physically checked into the assigned room");
        var checkedIn = game.HotelBookingFor(room)!;
        Check(game.World.Ledger.Single(e => e.Category == "Sales.Hotel").AmountMinor == booking.AgreedNightlyPriceMinor * booking.Nights,
            "Hotel charged an edited price or the wrong stay length.");
        Check(game.HotelStateFor(room) == HotelReadiness.Occupied && game.HotelBookings.Count(b => b.RoomId == room && b.Status is HotelBookingStatus.Reserved or HotelBookingStatus.CheckedIn) == 1,
            "Hotel admitted overlapping assignments.");
        var resumed = Restore(game); game.Advance(3600); resumed.Advance(3600); Check(game.Serialize() == resumed.Serialize(), "Active paid stay changed on save/load.");
        game.Advance((int)(checkedIn.CheckoutAt - game.Tick));
        Check(game.HotelBookings.Single(b => b.Id == booking.Id).Status == HotelBookingStatus.CheckedOut && game.OperationFor(room)!.Dirty,
            "Checkout failed to create a dirty room.");
        Check(game.HotelStateFor(room) is HotelReadiness.Dirty or HotelReadiness.Cleaning, "Dirty checkout was resold as available inventory.");
        Until(game, () => game.HotelStateFor(room) == HotelReadiness.Available, 7200, "Physical cleaning never returned the room to available inventory");
        Check(game.ServiceTasks.Any(t => t.RoomId == room && t.Status == ServiceTaskStatus.Completed), "Hotel became clean without a completed worker task.");
    }
    private static void HotelCancellation()
    {
        var (game, room) = TenantTower("hotel-room");
        Until(game, () => game.HotelBookingFor(room)?.Status == HotelBookingStatus.Reserved, 16 * 3600, "No hotel reservation formed");
        var booking = game.HotelBookingFor(room)!; Succeed(game.Transport.SetBankOutOfService(1, true));
        Until(game, () => game.HotelBookings.Single(b => b.Id == booking.Id).Status == HotelBookingStatus.Cancelled,
            Rules.Management.HotelArrivalTimeoutSeconds + 60, "Unreachable guest kept a reservation forever");
        Check(game.World.Ledger.All(e => e.Category != "Sales.Hotel") && game.OperationFor(room)!.ReservationPersonId == null,
            "Unreachable reservation charged revenue or retained its inventory assignment.");
        Check(game.People.All(p => game.Transport.JourneyFor(p.Id) != null), "Cancellation orphaned physical people.");
        Check(Restore(game).Serialize() == game.Serialize(), "Cancelled booking failed strict persistence.");
    }
    private static void BusinessValidation()
    {
        var (game, room) = TenantTower("office"); Until(game, () => game.Occupancy(room) == 8, 3600, "Office never filled");
        var before = game.Serialize();
        var changed = JsonNode.Parse(before)!;
        changed["management"]!["businesses"]!["tenants"]![0]!["memberIds"]![0] = long.MaxValue;
        try { GameSession.Deserialize(Catalog, Rules, Locations, changed.ToJsonString()); }
        catch (ArgumentException) { Check(before == game.Serialize(), "Rejected business load mutated active state."); return; }
        throw new InvalidOperationException("An invented tenant member reference was accepted.");
    }
    private static void FirstPromotion()
    {
        var game = OperatingExampleScenario.Create(Catalog, Rules, Locations);
        Until(game, () => game.World.Rank == 1 && game.UnmetRankRequirements().Count == 0, 2 * 86400,
            "Costed operating example never earned all first-rank requirements");
        var awardTick = game.Tick + 3600 - game.Tick % 3600;
        game.Advance((int)(awardTick - game.Tick - 1));
        Check(game.World.Rank == 1, "Promotion occurred before its deterministic evaluation boundary.");
        var resumed = Restore(game); game.Step(); resumed.Step();
        Check(game.World.Rank == 2 && resumed.Serialize() == game.Serialize(), "Saving before earned progression changed the award or unlock.");
        Check(game.Notices.Count(n => n.Kind == "Promotion" && n.Text.StartsWith("Rank 2:", StringComparison.Ordinal)) == 1,
            "First promotion did not emit exactly one notice.");
        var after = Restore(game); game.Advance(3600); after.Advance(3600);
        Check(after.Serialize() == game.Serialize() && game.Notices.Count(n => n.Kind == "Promotion" && n.Text.StartsWith("Rank 2:", StringComparison.Ordinal)) == 1,
            "Reloading an earned award emitted it again.");
    }
}
