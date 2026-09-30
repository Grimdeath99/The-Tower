using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

var dataPath = Path.Combine(AppContext.BaseDirectory, "Data");
var catalog = ContentCatalog.Load(File.ReadAllText(Path.Combine(dataPath, "construction.catalog.json")));
var locations = LocationCatalog.Load(File.ReadAllText(Path.Combine(dataPath, "locations.json")));
var rulesJson = File.ReadAllText(Path.Combine(dataPath, "simulation.rules.json"));
var rules = SimulationRules.Load(rulesJson, catalog);
var cases = new List<(string Name, Action Test)>();
void Check(bool value, string message) { if (!value) throw new Exception(message); }
void Succeed(CommandResult result) { Check(result.Success, result.Message); }
long Build(GameSession game, string id, int x, int floor)
{
    var result = game.BuildRoom(id, x, floor);
    Succeed(result);
    return result.EntityId!.Value;
}
GameSession Tower(bool bank = true, string location = "tokyo", string site = "central", bool sandbox = false, SimulationRules? tuning = null)
{
    var game = new GameSession(catalog, tuning ?? rules, locations, location, site, sandbox);
    for (var floor = 1; floor <= 3; floor++) Succeed(game.World.BuildFloor(floor));
    Build(game, "lobby", 0, 0);
    if (bank) Succeed(game.Transport.InstallBank(new BankDefinition(1, 29, 0, 3, [0, 1, 2, 3]), game.Rules.ElevatorCostMinor));
    return game;
}
void Until(GameSession game, Func<bool> condition, int maximumSeconds, string failure)
{
    for (var elapsed = 0; elapsed < maximumSeconds && !condition(); elapsed++) game.Step();
    Check(condition(), $"{failure} (at {game.ClockText}, tick {game.Tick}).");
}
RoomOperation Op(GameSession game, long room) => game.OperationFor(room) ?? throw new Exception("Missing commissioned operation.");
void ValidateLedger(GameSession game)
{
    var cash = game.Sandbox ? 2_500_000_000L : 250_000_000L;
    long sequence = 0;
    foreach (var entry in game.World.Ledger)
    {
        Check(entry.Sequence > sequence, "Duplicate or unordered ledger sequence.");
        cash = checked(cash + entry.AmountMinor);
        Check(cash == entry.BalanceAfterMinor, "Ledger intermediate balance does not reconcile.");
        sequence = entry.Sequence;
    }
    Check(cash == game.World.CashMinor, "Cash does not match signed transactions.");
}
void ValidateOwnership(GameSession game)
{
    var people = game.People.Select(p => p.Id).ToHashSet();
    Check(people.Count == game.People.Count, "Duplicate logical person.");
    var journeys = game.Transport.Journeys.ToDictionary(j => j.PersonId);
    Check(journeys.Keys.All(people.Contains), "Transport owns a journey for a missing logical person.");
    var riders = game.Transport.Cars.SelectMany(c => c.PassengerIds).ToArray();
    Check(riders.Distinct().Count() == riders.Length, "A passenger occupies more than one car.");
    Check(riders.All(id => people.Contains(id) && journeys[id].State == JourneyState.Riding), "Passenger/car ownership mismatch.");
    Check(game.Transport.Cars.All(c => c.PassengerCount <= c.Capacity), "Car overloaded.");
    foreach (var op in game.Operations)
        Check(op.ReservationPersonId is null || people.Contains(op.ReservationPersonId.Value), "Hotel reservation references a missing guest.");
}

cases.Add(("An inaccessible upper-floor office earns no rent and receives no workers", () =>
{
    var game = Tower(bank: false);
    var office = Build(game, "office", 0, 1);
    game.Advance(12 * 3600);
    Check(!game.IsAccessible(office), "Upper-floor office unexpectedly accessible.");
    Check(Op(game, office).GrossRevenueMinor == 0 && game.People.Count == 0, "Income or people created without an access route.");
    Check(game.OperatingWarning(office).Contains("route"), "Missing route warning.");
    ValidateLedger(game);
}));
cases.Add(("Office workers physically arrive, pay one contract rent, and depart", () =>
{
    var game = Tower();
    var office = Build(game, "office", 0, 1);
    Until(game, () => game.Occupancy(office) > 0, 3600, "No office worker completed arrival");
    Check(game.CompletedTrips > 0, "Arrival did not count a completed trip.");
    Until(game, () => Op(game, office).GrossRevenueMinor > 0, 7200, "Occupied office did not pay its scheduled lease");
    Check(Op(game, office).GrossRevenueMinor == rules.For("office")!.PriceMinor, "Rent must be charged once per contract/day, not once per worker.");
    var revenue = Op(game, office).GrossRevenueMinor;
    game.Advance(1800);
    Check(Op(game, office).GrossRevenueMinor == revenue, "Office rent charged repeatedly within the same day.");
    Until(game, () => game.Hour >= 19 && game.People.All(p => p.RoomId != office), 12 * 3600, "Office workers did not physically leave");
    Check(game.CompletedTrips >= 2, "Departure did not complete a journey.");
    ValidateOwnership(game); ValidateLedger(game);
}));
cases.Add(("Food staffing and closure control real sales", () =>
{
    var game = Tower();
    var cafe = Build(game, "cafe", 0, 1);
    Succeed(game.SetStaff(cafe, 0)); game.Advance(7200);
    Check(Op(game, cafe).GrossRevenueMinor == 0, "Unstaffed cafe sold meals.");
    Succeed(game.SetStaff(cafe, rules.For("cafe")!.Staff));
    Until(game, () => Op(game, cafe).GrossRevenueMinor > 0, 3600, "Staffed cafe did not serve an arriving guest");
    Succeed(game.SetOpen(cafe, false));
    var revenue = Op(game, cafe).GrossRevenueMinor;
    game.Advance(3600);
    Check(Op(game, cafe).GrossRevenueMinor == revenue, "Closed cafe made additional sales.");
    Check(game.People.All(p => p.RoomId != cafe), "Closing failed to release customers through transport.");
    ValidateLedger(game);
}));
cases.Add(("Management rejects invalid changes without changing state", () =>
{
    var game = Tower(); var cafe = Build(game, "cafe", 0, 1);
    var original = Op(game, cafe);
    Check(!game.SetPrice(cafe, -1).Success && !game.SetPrice(cafe, 100_000_001).Success, "Invalid price accepted.");
    Check(!game.SetStaff(cafe, -1).Success && !game.SetStaff(cafe, 21).Success, "Invalid staffing accepted.");
    Check(!game.SetOpen(long.MaxValue, false).Success, "Unknown room managed.");
    Check(Op(game, cafe) == original, "Failed management command mutated operation.");
}));
cases.Add(("Hotel reserves one room before arrival and charges one stay", () =>
{
    var game = Tower(); var hotel = Build(game, "hotel-room", 0, 2);
    Until(game, () => Op(game, hotel).ReservationPersonId != null, 8 * 3600, "Hotel failed to reserve a guest");
    Check(game.ReservedCapacity(hotel) == 1, "Hotel reservation exceeds one room.");
    Check(game.Occupancy(hotel) == 0, "Reservation teleported guest into hotel.");
    Until(game, () => game.Occupancy(hotel) == 1, 1800, "Reserved hotel guest never arrived");
    var paid = Op(game, hotel).GrossRevenueMinor;
    Check(paid == rules.For("hotel-room")!.PriceMinor * rules.For("hotel-room")!.StayDays, "Stay was not billed once for the agreed duration.");
    game.Advance(3600);
    Check(game.ReservedCapacity(hotel) == 1 && Op(game, hotel).GrossRevenueMinor == paid, "Duplicate guest or repeated hotel charge.");
    ValidateOwnership(game); ValidateLedger(game);
}));
cases.Add(("Hotel checkout remains dirty until staffed cleaning physically completes", () =>
{
    var game = Tower(); var hotel = Build(game, "hotel-room", 0, 2);
    Until(game, () => game.Occupancy(hotel) == 1, 8 * 3600, "Hotel never opened a stay");
    Succeed(game.SetOpen(hotel, false));
    Until(game, () => Op(game, hotel).Dirty && game.People.All(p => p.RoomId != hotel), 3600, "Checkout did not leave the room dirty");
    Succeed(game.SetOpen(hotel, true));
    game.Advance(3600);
    Check(Op(game, hotel).Dirty && game.ReservedCapacity(hotel) == 0, "Dirty unserviced room was reassigned.");
    var depot = Build(game, "service-room", 6, 0);
    Succeed(game.SetStaff(depot, 0)); game.Advance(600);
    Check(Op(game, hotel).Dirty, "An unstaffed depot cleaned a room.");
    Succeed(game.SetStaff(depot, rules.For("service-room")!.Staff));
    Until(game, () => game.People.Any(p => p.ServiceTargetId == hotel), 600, "No physical cleaning job dispatched");
    Check(Op(game, hotel).Dirty, "Cleaning completed before staff reached the room.");
    Until(game, () => !Op(game, hotel).Dirty, 3600, "Cleaning worker did not complete hotel turnaround");
    Check(Op(game, hotel).Cleanliness >= 90, "Completed cleaning did not restore cleanliness.");
    ValidateOwnership(game);
}));
cases.Add(("Condominium sale and buyback preserve the original ownership price", () =>
{
    var game = Tower(); var condo = Build(game, "condo", 0, 1);
    Until(game, () => Op(game, condo).CondoSold, 12 * 3600, "Condo never sold after evening arrival");
    var salePrice = Op(game, condo).CondoSaleMinor;
    Check(salePrice > 0, "Missing recorded ownership price.");
    Check(!game.DemolishRoom(condo).Success && !game.SetOpen(condo, false).Success, "Owned condo allowed destructive closure.");
    Succeed(game.SetPrice(condo, 0));
    var beforeBuyback = game.World.CashMinor;
    Succeed(game.BuyBackCondo(condo));
    Check(game.World.CashMinor == beforeBuyback - salePrice, "Buyback used the edited asking price instead of recorded sale price.");
    Check(!game.BuyBackCondo(condo).Success && game.World.CashMinor == beforeBuyback - salePrice, "Repeated buyback changed cash.");
    Until(game, () => game.People.All(p => p.RoomId != condo), 3600, "Bought-back owner did not depart");
    Succeed(game.DemolishRoom(condo)); ValidateLedger(game);
}));
cases.Add(("Maintenance cannot repair across a disconnected service route", () =>
{
    var game = Tower(); var office = Build(game, "office", 0, 1);
    game.Advance(18 * 3600);
    Check(Op(game, office).Condition < 85, "Test room did not age into a maintenance need.");
    Succeed(game.Transport.SetBankOutOfService(1, true));
    Build(game, "service-room", 6, 0);
    var condition = Op(game, office).Condition;
    game.Advance(1200);
    Check(Op(game, office).Condition <= condition, "Disconnected room was remotely repaired.");
    Check(game.People.All(p => p.ServiceTargetId != office), "Staff dispatched through a nonexistent service route.");
    Check(!game.RepairRoom(office).Success, "Maintenance request claimed success without an available route.");
    Succeed(game.Transport.SetBankOutOfService(1, false));
    Succeed(game.RepairRoom(office));
    Check(game.People.Any(p => p.ServiceTargetId == office), "Maintenance command did not immediately dispatch a physical worker.");
    var assignedCount = game.People.Count(p => p.ServiceTargetId == office);
    Succeed(game.RepairRoom(office));
    Check(game.People.Count(p => p.ServiceTargetId == office) == assignedCount, "Repeated request duplicated an assigned worker.");
    Until(game, () => game.People.Any(p => p.ServiceTargetId == office), 1800, "Restoring transport did not dispatch maintenance");
    Check(Op(game, office).Condition < 85, "Maintenance finished before worker travel and work.");
    Until(game, () => Op(game, office).Condition >= 90, 1800, "Routed maintenance never completed");
    Check(game.World.Ledger.Any(l => l.EntityId == office && l.Category == "Maintenance.Repair" && l.AmountMinor < 0), "Physical repair did not charge its expense.");
    ValidateOwnership(game); ValidateLedger(game);
}));
cases.Add(("Event preparation charges once and ticket revenue requires guests", () =>
{
    var game = Tower(); var hall = Build(game, "event-hall", 0, 1);
    var before = game.World.CashMinor;
    Succeed(game.ScheduleEvent(hall));
    var prepared = game.World.CashMinor;
    Check(prepared < before, "Event preparation was free.");
    Check(!game.ScheduleEvent(hall).Success && game.World.CashMinor == prepared, "Duplicate event preparation charged again.");
    game.Advance(1800);
    Check(Op(game, hall).GrossRevenueMinor == 0, "Event earned ticket revenue before its scheduled time.");
    Until(game, () => Op(game, hall).GrossRevenueMinor > 0, 7200, "Scheduled event received no ticketed arrivals");
    Check(game.CompletedTrips > 0, "Event ticket revenue did not require a trip."); ValidateLedger(game);
}));
cases.Add(("Occupied rooms and active shafts cannot be demolished", () =>
{
    var game = Tower(); var office = Build(game, "office", 0, 1);
    Until(game, () => game.Occupancy(office) > 0, 3600, "Office never occupied");
    var before = game.World.CashMinor;
    Check(!game.DemolishRoom(office).Success, "Demolished an occupied office.");
    Check(!game.DemolishFloor(3).Success, "Demolished a floor carrying a shaft.");
    Check(game.World.CashMinor == before, "Rejected demolition changed cash.");
    Succeed(game.SetOpen(office, false));
    Until(game, () => game.People.All(p => p.RoomId != office), 3600, "Office failed to empty");
    Succeed(game.DemolishRoom(office)); ValidateLedger(game);
}));
cases.Add(("Room placement respects reserved transport geometry", () =>
{
    var game = Tower();
    Check(!game.BuildRoom("hotel-room", 27, 1).Success, "Room intersects an elevator shaft.");
    Succeed(game.Transport.BuildStair(1, 25));
    Check(!game.BuildRoom("studio", 22, 1).Success, "Room intersects a staircase.");
    Check(!game.BuildRoom("parking", 0, 1).Success, "Basement parking built above ground.");
}));
cases.Add(("Utility capacity and staffed plants affect which rooms operate", () =>
{
    var game = Tower(); var rooms = new List<long>();
    for (var floor = 1; floor <= 3; floor++)
        for (var x = 0; x < 24; x += 4) rooms.Add(Build(game, "studio", x, floor));
    var last = rooms[^1];
    Check(!game.HasUtilities(last), "Municipal utility capacity silently serves unlimited rooms.");
    var plant = Build(game, "utility-room", 6, 0);
    Check(game.HasUtilities(last), "Staffed accessible utility plant did not increase capacity.");
    Succeed(game.SetStaff(plant, 0));
    Check(!game.HasUtilities(last), "Unstaffed utility plant kept capacity online.");
}));
cases.Add(("All thirteen locations support ordinary office income without exclusive transport", () =>
{
    foreach (var location in locations.Locations)
    {
        var game = Tower(location: location.Id); var office = Build(game, "office", 0, 1);
        game.Advance(2 * 3600);
        Check(Op(game, office).GrossRevenueMinor > 0, $"Ordinary operating path failed in {location.Name}.");
        Check(game.World.Rooms.All(r => r.DefinitionId is not ("subway" or "dock")), "Ordinary path depends on exclusive transport.");
    }
}));
cases.Add(("Session construction keeps all geography restrictions in sandbox", () =>
{
    foreach (var location in locations.Locations)
    foreach (var site in location.Sites)
    {
        var game = new GameSession(catalog, rules, locations, location.Id, site.Id, true);
        Succeed(game.World.BuildFloor(-1));
        Check(game.ValidateRoom("subway", 0, -1).Success == (location.HasOperatingSubway && site.SubwayConnection), $"Subway gate bypass in {location.Id}/{site.Id}.");
        Check(game.ValidateRoom("dock", 0, 0).Success == (location.Id == "hawaii" && site.Waterfront), $"Dock gate bypass in {location.Id}/{site.Id}.");
        Check(!game.ValidateRoom("subway", 0, 0).Success && !game.ValidateRoom("dock", 0, -1).Success, "Transport facility allowed wrong elevation.");
    }
    var normal = new GameSession(catalog, rules, locations, "hawaii", "waterfront");
    Check(!normal.ValidateRoom("dock", 0, 0).Success, "Normal rank-one tower bypassed optional dock rank.");
}));
cases.Add(("Fixed steps produce identical sessions across render deltas and speeds", () =>
{
    GameSession Run(double delta, int frames, int speed)
    {
        var game = Tower(); Build(game, "office", 0, 1); Build(game, "cafe", 5, 1);
        var runner = new FixedStepRunner(rules); runner.SetSpeed(speed);
        for (var i = 0; i < frames; i++) runner.Advance(delta, game.Step);
        while (runner.BacklogSeconds >= 1) runner.Advance(0, game.Step);
        Check(game.Tick == 3600, "Equal simulated hour did not produce 3600 seconds.");
        return game;
    }
    var sixty = Run(1.0 / 60, 3600, 1);
    var thirty = Run(1.0 / 30, 1800, 1);
    var fast = Run(1.0 / 60, 900, 4);
    Check(sixty.Serialize() == thirty.Serialize() && sixty.Serialize() == fast.Serialize(), "Presentation cadence or speed changed simulation outcomes.");
}));
cases.Add(("Bounded catch-up preserves time and pause freezes backlog", () =>
{
    var runner = new FixedStepRunner(rules); long ticks = 0;
    var produced = runner.Advance(60, () => ticks++);
    Check(produced == rules.MaxStepsPerFrame && ticks <= rules.MaxStepsPerFrame, "Frame exceeded fixed-step work bound.");
    var queued = runner.BacklogSeconds;
    Check(queued > 0, "Overload silently discarded time.");
    runner.SetSpeed(0); runner.Advance(60, () => ticks++);
    Check(runner.BacklogSeconds == queued && ticks == produced, "Paused simulation consumed backlog or accepted new elapsed game time.");
    runner.SetSpeed(1);
    while (runner.BacklogSeconds >= 1) runner.Advance(0, () => ticks++);
    Check(ticks == 60L * rules.GameSecondsPerRealSecond, "Catch-up skipped or invented ticks.");
    foreach (var invalid in new[] { -1.0, double.NaN, double.PositiveInfinity })
    {
        var rejected = false; try { runner.Advance(invalid, () => ticks++); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid frame delta accepted.");
    }
}));
cases.Add(("Active elevator travel survives session save and deterministic continuation", () =>
{
    var game = Tower(); Build(game, "office", 0, 3); Build(game, "cafe", 5, 2);
    Until(game, () => game.Transport.Journeys.Any(j => j.State == JourneyState.Riding), 3600, "No riding state reached for checkpoint");
    var checkpoint = game.Serialize();
    var restored = GameSession.Deserialize(catalog, rules, locations, checkpoint);
    Check(restored.Serialize() == checkpoint, "Save roundtrip changed active session state.");
    game.Advance(7200); restored.Advance(7200);
    Check(game.Serialize() == restored.Serialize(), "Restored active travel diverged from uninterrupted simulation.");
    ValidateOwnership(restored); ValidateLedger(restored);
}));
cases.Add(("Three-day mixed tower preserves passenger ownership and exact finances", () =>
{
    var game = Tower();
    Build(game, "service-room", 6, 0); Build(game, "security-room", 9, 0);
    Build(game, "office", 0, 1); Build(game, "cafe", 5, 1); Build(game, "studio", 9, 1);
    Build(game, "hotel-room", 0, 2); Build(game, "hotel-room", 3, 2); Build(game, "condo", 6, 2);
    Build(game, "shop", 11, 2); Build(game, "cinema", 0, 3);
    for (var hour = 0; hour < 72; hour++) { game.Advance(3600); ValidateOwnership(game); ValidateLedger(game); }
    Check(game.Reports.Count >= 3, "No daily reports produced during multiday operation.");
    Check(game.CompletedTrips > 50 && game.Operations.Sum(o => o.GrossRevenueMinor) > 0, "Mixed tower did not actually operate.");
    Check(game.Reports.Any(r => r.Arrivals > 0) && game.Reports.Any(r => r.Departures > 0), "Reports failed to record physical arrivals and departures.");
    var restored = GameSession.Deserialize(catalog, rules, locations, game.Serialize());
    game.Advance(3600); restored.Advance(3600);
    Check(game.Serialize() == restored.Serialize(), "Multiday save continuation lost operational state.");
}));
cases.Add(("Release progression requires real success and defines all seven ranks", () =>
{
    Check(rules.Promotions.Select(p => p.Rank).SequenceEqual(Enumerable.Range(2, 6)), "Missing seven-rank promotion thresholds.");
    Check(rules.Provenance.Contains("provisional", StringComparison.OrdinalIgnoreCase), "Unapproved tuning must be identified.");
    var game = new GameSession(catalog, rules, locations);
    game.Advance(3 * 86400);
    Check(game.World.Rank == 1, "Empty tower automatically promoted.");
}));
cases.Add(("An explicit low-threshold fixture exercises rank two through seven and continued play", () =>
{
    var node = JsonNode.Parse(rulesJson)!.AsObject();
    foreach (var promotion in node["promotions"]!.AsArray())
    {
        foreach (var key in new[] { "population", "dailyProfitMinor", "satisfaction", "cleanliness", "hotelRooms", "successfulTrips" }) promotion![key] = 0;
        promotion!["diversity"] = 1;
    }
    var fixture = SimulationRules.Load(node.ToJsonString(), catalog);
    var game = Tower(tuning: fixture); Build(game, "office", 0, 1);
    var seen = new HashSet<int> { game.World.Rank };
    for (var hour = 0; hour < 36 && game.World.Rank < 7; hour++) { game.Advance(3600); seen.Add(game.World.Rank); }
    Check(seen.SetEquals(Enumerable.Range(1, 7)), "Progression did not traverse all seven ranks.");
    var tick = game.Tick; game.Advance(3600);
    Check(game.World.Rank == 7 && game.Tick == tick + 3600, "Final rank prevents continued simulation.");
}));

cases.Add(("Office commute factory purchases one accessible normal-budget scenario", () =>
{
    foreach (var count in new[] { 1, 3, 6 })
    {
        var game = OfficeCommuteScenario.Create(catalog, rules, locations, count);
        Check(!game.Sandbox && game.World.Rank == 1 && game.Tick == 0, "Scenario bypassed normal starting conditions.");
        Check(game.World.Floors.Order().SequenceEqual(new[] { 0, 1, 2, 3 }), "Scenario must have exactly four slabs.");
        var offices = game.World.Rooms.Where(r => r.DefinitionId == "office").ToArray();
        Check(offices.Length == count && game.World.Rooms.Count == count + 1, "Unexpected scenario facilities.");
        Check(count != 3 || offices.All(r => r.Floor == 3), "Default offices must be on floor three.");
        Check(offices.All(r => game.IsAccessible(r.Id)), "Scenario created inaccessible offices.");
        Check(game.Transport.Banks.Count == 1 && game.Transport.Cars[0].Capacity == 6 && game.Transport.Stairs.Count == 0,
            "A bypass route or wrong car capacity changed the queue experiment.");
        var expectedCost = 3 * catalog.FloorConstructionCostMinor + catalog.Get("lobby").CostMinor
            + count * catalog.Get("office").CostMinor + rules.ElevatorCostMinor;
        Check(game.World.CashMinor == 250_000_000 - expectedCost, "Factory omitted construction or transport costs.");
        Check(game.People.Count == 0 && game.Transport.Journeys.Count == 0, "Factory fabricated starting occupants.");
        ValidateLedger(game);
    }
    foreach (var count in new[] { 0, 7 })
    {
        var rejected = false;
        try { OfficeCommuteScenario.Create(catalog, rules, locations, count); } catch (ArgumentOutOfRangeException) { rejected = true; }
        Check(rejected, "Invalid office count was accepted.");
    }
}));
cases.Add(("Office commute accounts for every worker through queues, rent, the 18:00 shift end and physical exit", () =>
{
    var game = OfficeCommuteScenario.Create(catalog, rules, locations);
    var offices = game.World.Rooms.Where(r => r.DefinitionId == "office").ToArray();
    var demand = offices.Length * rules.For("office")!.Capacity;
    Until(game, () => game.People.Count == demand, 600, "The 08:00 office demand did not appear");
    var original = game.People.Select(p => p.Id).ToHashSet();
    var boarded = new HashSet<long>(); var returnedByCar = new HashSet<long>(); var arrived = new HashSet<long>(); var exited = new HashSet<long>();
    var arrivalTimes = new HashSet<long>();
    var maximumQueue = 0; var fullDepartureWithQueue = false; var sawActualTravel = false; var shiftEndObserved = false;
    var previousPeople = game.People.ToDictionary(p => p.Id);
    var previousJourneys = game.Transport.Journeys.ToDictionary(j => j.PersonId);
    var previousTick = game.Tick;
    while (game.Tick < 13 * 3600 && exited.Count < demand)
    {
        game.Step();
        ValidateOwnership(game);
        var people = game.People.ToDictionary(p => p.Id);
        var journeys = game.Transport.Journeys.ToDictionary(j => j.PersonId);
        Check(people.Keys.All(original.Contains), "The commute replaced or invented a worker.");
        foreach (var person in people.Values)
        {
            Check(person.Role == "Worker", "Non-worker demand entered the isolated scenario.");
            var journey = journeys[person.Id];
            if (journey.State == JourneyState.Riding)
            {
                if (person.Activity == PersonActivity.Arriving) boarded.Add(person.Id);
                if (person.Activity == PersonActivity.Leaving) returnedByCar.Add(person.Id);
            }
            if (person.Activity == PersonActivity.Visiting)
            {
                Check(journey.State == JourneyState.Arrived && journey.Floor == 3, "An office worker also owns a queue or car.");
                Check(boarded.Contains(person.Id), "Worker reached an office without an elevator ride.");
                Check(person.ActionAt == person.CreatedAt + 10 * 3600, "Commute duration moved the 18:00 shift deadline.");
                if (arrived.Add(person.Id)) arrivalTimes.Add(game.Tick);
            }
        }
        foreach (var removed in previousPeople.Keys.Except(people.Keys))
        {
            Check(previousPeople[removed].Activity == PersonActivity.Leaving && returnedByCar.Contains(removed), "Worker disappeared without a return trip.");
            var last = previousJourneys[removed];
            Check(last.Floor == 0 && last.DrawFloor == 0 && last.X <= 1, "Worker disappeared before walking through the ground entrance.");
            exited.Add(removed);
        }
        Check(people.Count + exited.Count == demand, "Logical workers were lost or duplicated.");
        foreach (var office in offices)
            Check(game.Occupancy(office.Id) == people.Values.Count(p => p.RoomId == office.Id && p.Activity == PersonActivity.Visiting), "Office occupancy disagrees with logical workers.");
        var car = game.Transport.Cars[0];
        var queue = journeys.Values.Count(j => j.State == JourneyState.Waiting);
        maximumQueue = Math.Max(maximumQueue, queue);
        fullDepartureWithQueue |= car.PassengerCount == car.Capacity && car.State == CarState.Traveling && queue > 0;
        sawActualTravel |= car.State == CarState.Traveling && car.DrawFloor > 0 && car.DrawFloor < 3;
        if (game.Hour == 18 && game.Minute == 0 && !shiftEndObserved)
        {
            Check(previousTick + 1 == game.Tick && arrived.Count == demand, "Not all workers arrived before their shift ended.");
            Check(people.Values.All(p => p.Activity == PersonActivity.Leaving), "Workers with different arrival times did not all leave at 18:00.");
            shiftEndObserved = true;
        }
        previousPeople = people; previousJourneys = journeys; previousTick = game.Tick;
    }
    Check(demand == 24 && maximumQueue > 6 && fullDepartureWithQueue && sawActualTravel, "The scenario did not exercise a full car and real queues/travel.");
    Check(boarded.SetEquals(original) && returnedByCar.SetEquals(original) && arrived.SetEquals(original) && exited.SetEquals(original), "Not every original worker completed both car journeys.");
    Check(arrivalTimes.Count > 1 && shiftEndObserved, "Staggered arrivals and fixed shift end were not observed.");
    Check(game.CompletedTrips == demand * 2 && game.People.Count == 0 && game.Transport.Journeys.Count == 0
        && game.Transport.Metrics.Waiting == 0 && game.Transport.Cars[0].PassengerCount == 0, "Evening demand failed to drain completely.");
    foreach (var office in offices)
    {
        var receipts = game.World.Ledger.Where(e => e.Category == "Lease.Office" && e.EntityId == office.Id).ToArray();
        Check(receipts.Length == 1 && receipts[0].AmountMinor == rules.For("office")!.PriceMinor, "Office contract rent was omitted or charged more than once.");
    }
    Check(game.Transport.Metrics.AbandonedTrips == 0, "Default commute lost workers to patience.");
    ValidateLedger(game);
}));
cases.Add(("Office commute saves a full moving car and waiting queue with identical continuation", () =>
{
    var game = OfficeCommuteScenario.Create(catalog, rules, locations);
    Until(game, () => game.Transport.Cars[0].State == CarState.Traveling && game.Transport.Cars[0].PassengerCount == 6
        && game.Transport.Metrics.Waiting > 0, 900, "No full-car/queue checkpoint occurred");
    var checkpoint = game.Serialize();
    var restored = GameSession.Deserialize(catalog, rules, locations, checkpoint);
    Check(restored.Serialize() == checkpoint, "Checkpoint roundtrip changed active commute ownership.");
    for (var step = 0; step < 60; step++)
    {
        game.Advance(60); restored.Advance(60);
        Check(game.Serialize() == restored.Serialize(), "Restored commute diverged across a car cycle or rent event.");
        ValidateOwnership(restored);
    }
    Check(restored.CompletedTrips == 24 && restored.Population == 24, "Checkpoint continuation lost an arriving worker.");
}));
cases.Add(("Office commute rejects occupied edits atomically and recovers disconnected departing workers", () =>
{
    var game = OfficeCommuteScenario.Create(catalog, rules, locations);
    Until(game, () => game.Transport.Cars[0].State == CarState.Traveling && game.Transport.Cars[0].PassengerCount > 0,
        900, "No occupied traveling car for edit protection");
    var bank = game.Transport.Banks[0].Definition;
    var original = game.Serialize();
    Check(!game.Transport.ConfigureBank(bank with { Stops = [0, 1, 2] }).Success, "Stop edit erased an onboard destination.");
    Check(!game.Transport.RemoveBank(bank.Id).Success, "Moving bank was removed.");
    Check(!game.DemolishFloor(3).Success, "Occupied transport slab was removed.");
    Check(game.Serialize() == original, "A rejected edit changed the authoritative session.");
    Until(game, () => game.People.Count == 24 && game.People.All(p => p.Activity == PersonActivity.Visiting)
        && game.Transport.Cars[0].State == CarState.Idle, 1200, "Workers did not settle into offices");
    var people = game.People.Select(p => p.Id).ToHashSet();
    Succeed(game.Transport.ConfigureBank(bank with { Stops = [0, 1, 2] }));
    foreach (var office in game.World.Rooms.Where(r => r.DefinitionId == "office")) Succeed(game.SetOpen(office.Id, false));
    game.Advance(120);
    Check(game.People.Count == 24 && game.People.All(p => p.Activity == PersonActivity.Stranded), "Disconnected departures disappeared or bypassed the missing stop.");
    Check(game.Transport.Journeys.All(j => j.Floor == 3 && j.State == JourneyState.Arrived), "Disconnected workers changed physical floor.");
    ValidateOwnership(game);
    Succeed(game.Transport.ConfigureBank(bank));
    Until(game, () => game.People.Count == 0, 1800, "Restored stop did not recover physically stranded workers");
    Check(game.CompletedTrips == people.Count * 2 && game.Transport.Journeys.Count == 0, "Controlled retry lost or duplicated a journey.");
    ValidateOwnership(game); ValidateLedger(game);
}));
cases.Add(("Office commute pause and 1x versus 4x preserve identical car and queue state", () =>
{
    GameSession RunCommute(int speed, int frames)
    {
        var game = OfficeCommuteScenario.Create(catalog, rules, locations);
        var runner = new FixedStepRunner(rules); runner.SetSpeed(speed);
        for (var frame = 0; frame < frames; frame++) runner.Advance(1d / 60, game.Step);
        return game;
    }
    var normal = RunCommute(1, 400);
    var fast = RunCommute(4, 100);
    Check(normal.Tick == 400 && normal.Serialize() == fast.Serialize(), "Speed changed a live office commute.");
    Check(normal.Transport.Metrics.Waiting > 0 || normal.Transport.Metrics.Riding > 0, "Pause fixture missed active transport.");
    var paused = normal.Serialize();
    var runner = new FixedStepRunner(rules); runner.SetSpeed(0);
    for (var frame = 0; frame < 600; frame++) runner.Advance(1d / 60, normal.Step);
    Check(normal.Serialize() == paused, "Paused commute advanced people, cars, money, or time.");
}));
cases.Add(("Office commute late-arrival fixture departs physically after the shift without entering or earning rent", () =>
{
    var game = OfficeCommuteScenario.Create(catalog, rules, locations, officeCount: 1, elevatorCapacity: 8);
    Until(game, () => game.Transport.Cars[0].State == CarState.Traveling && game.Transport.Cars[0].PassengerCount == 8,
        900, "No fully boarded late-arrival checkpoint");
    // Explicit edge-case fixture: the same in-flight arriving save is placed at the shift
    // deadline. Arriving ActionAt remains its existing duration format; no rules are changed.
    var state = game.CaptureSnapshot();
    var shiftEnd = state.People.Single(p => p.Id == 1).CreatedAt + state.People.Single(p => p.Id == 1).ActionAt;
    state = state with { Tick = shiftEnd, World = state.World with { Clock = state.World.Clock! with { CurrentTick = shiftEnd } },
        Transport = state.Transport with { CurrentTick = shiftEnd } };
    var late = GameSession.Deserialize(catalog, rules, locations, JsonSerializer.Serialize(state, SimulationRules.JsonOptions));
    for (var tick = 0; tick < 600 && late.People.Count > 0; tick++)
    {
        late.Step(); ValidateOwnership(late);
        Check(late.People.All(p => p.Activity != PersonActivity.Visiting), "Worker entered an office after its original shift ended.");
    }
    Check(late.People.Count == 0 && late.CompletedTrips == 16, "Late workers did not physically return through the elevator.");
    Check(late.World.Ledger.All(e => e.Category != "Lease.Office"), "A closed-shift late arrival earned office rent.");
}));

var failures = 0;
var selected = cases.Where(c => args.Length == 0 || c.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).ToArray();
foreach (var (name, test) in selected)
{
    var timer = System.Diagnostics.Stopwatch.StartNew();
    try { test(); Console.WriteLine($"PASS {name} ({timer.ElapsedMilliseconds}ms)"); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {name}: {error.Message}\n{error.StackTrace}"); }
}
Console.WriteLine($"{selected.Length - failures}/{selected.Length} simulation cases passed; {failures} failed.");
return failures == 0 && selected.Length > 0 ? 0 : 1;
