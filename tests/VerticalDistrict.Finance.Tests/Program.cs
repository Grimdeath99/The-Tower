using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

// Finance acceptance only: these fixtures deliberately retain the existing transient
// residential/office visits, admission-based food charges, and implicit service jobs.
// They do not certify the later tenancy, completed-service, or complaint milestones.
var data = Path.Combine(AppContext.BaseDirectory, "Data");
var catalog = ContentCatalog.Load(File.ReadAllText(Path.Combine(data, "construction.catalog.json")));
var rules = SimulationRules.Load(File.ReadAllText(Path.Combine(data, "simulation.rules.json")), catalog);
var locations = LocationCatalog.Load(File.ReadAllText(Path.Combine(data, "locations.json")));
var cases = new (string Name, Action Test)[]
{
    ("Construction, transport and salvage stay separate from operating profit", CapitalClassification),
    ("Condominium ownership and legacy expenses have explicit financial classifications", CategoryPolicy),
    ("Rejected construction and invalid transactions preserve money and financial views", AtomicFailures),
    ("A rejected recurring batch preserves every bill and retries once after recovery", AtomicBillingFailure),
    ("Two overdue periods keep their own staffing snapshots and settle once after reload", MultiplePendingPeriods),
    ("Daily wages and upkeep settle exactly once across a saved billing boundary", SavedBillingBoundary),
    ("Upcoming bills reflect current staffing and settle their exact signed amounts", StaffingEstimate),
    ("Closed facilities retain their documented upkeep and allocated wage obligations", ClosedFacilityBills),
    ("Financial history, cash flow and every ledger period reconcile", PeriodReconciliation),
    ("Financial reads are pure and returned summaries remain detached", ReadPurity),
    ("Financial schedules and outcomes agree across pause and supported speeds", SpeedEquivalence),
    ("Tampered billing cursors and ledger period boundaries are rejected atomically", InvalidFinanceMetadata),
    ("Legacy session migration preserves the ledger and future billing outcomes", LegacyMigration),
    ("Legacy combined upkeep migrates without rewriting historical cash entries", LegacyCombinedUpkeep),
    ("Physical condominium sales and buybacks remain capital after legacy migration", CondoMigration),
    ("Active hotel stays and service work preserve their financial continuation", MixedActiveCheckpoint),
    ("Seven-day small mixed tower funds ordinary operating costs with earned receipts", SevenDayMixedTower),
    ("Twenty-floor finance fixture retains physical demand and exact saved billing", TwentyFloorFixture)
};
var selected = cases.Where(c => args.Length == 0 || c.Name.Contains(args[0], StringComparison.OrdinalIgnoreCase)).ToArray();
var failed = 0;
foreach (var (name, run) in selected)
{
    var timer = Stopwatch.StartNew();
    try { run(); Console.WriteLine($"PASS {name} ({timer.ElapsedMilliseconds}ms)"); }
    catch (Exception error) { failed++; Console.Error.WriteLine($"FAIL {name}: {error}"); }
}
Console.WriteLine($"{selected.Length - failed}/{selected.Length} finance cases passed; {failed} failed.");
return failed == 0 && selected.Length > 0 ? 0 : 1;

GameSession NewGame() => new(catalog, rules, locations);
long Build(GameSession game, string definition, int x, int floor)
{
    var result = game.BuildRoom(definition, x, floor); Succeed(result); return result.EntityId!.Value;
}
GameSession SmallTower()
{
    var game = NewGame();
    for (var floor = 1; floor <= 3; floor++) Succeed(game.World.BuildFloor(floor));
    Succeed(game.Transport.InstallBank(new BankDefinition(1, 29, 0, 3, [0, 1, 2, 3], 16), rules.ElevatorCostMinor));
    for (var floor = 0; floor < 3; floor++) Succeed(game.Transport.BuildStair(floor, 31, rules.StairCostMinor));
    BuildFirst("lobby"); BuildFirst("cafe"); BuildFirst("shop"); BuildFirst("studio");
    var depot = BuildFirst("service-room"); Succeed(game.SetStaff(depot, 4));
    BuildFirst("utility-room");
    BuildFirst("security-room"); BuildFirst("office"); BuildFirst("hotel-room");
    return game;

    long BuildFirst(string id)
    {
        var definition = catalog.Get(id);
        foreach (var floor in game.World.Floors.Order())
            for (var x = 0; x + definition.Width <= 29; x++)
                if (game.ValidateRoom(id, x, floor).Success) return Build(game, id, x, floor);
        throw new InvalidOperationException($"No space for financial fixture facility {id}.");
    }
}
GameSession Restore(string json) => GameSession.Deserialize(catalog, rules, locations, json);
void AdvanceToBill(GameSession game, bool immediatelyBefore = false)
{
    var target = game.Billing.NextDueTick - (immediatelyBefore ? 1 : 0);
    Check(target >= game.Tick && target - game.Tick <= 86400, "Unexpected financial due-time range.");
    game.Advance(checked((int)(target - game.Tick)));
}
void AssertLedger(GameSession game)
{
    var balance = game.Sandbox ? 2_500_000_000L : 250_000_000L;
    long sequence = 0;
    foreach (var entry in game.World.Ledger)
    {
        Check(entry.Sequence == ++sequence, "Nonconsecutive ledger sequence.");
        balance = checked(balance + entry.AmountMinor);
        Check(balance == entry.BalanceAfterMinor, "Ledger intermediate cash does not reconcile.");
    }
    Check(balance == game.World.CashMinor, "Ledger ending cash does not reconcile.");
    AssertSummary(game.LifetimeFinances);
    Check(game.LifetimeFinances.OpeningCashMinor == 250_000_000 && game.LifetimeFinances.ClosingCashMinor == balance,
        "Lifetime opening or closing cash is wrong.");
}
void AssertSummary(FinancialSummary summary)
{
    Check(summary.OperatingProfitMinor == summary.OperatingRevenueMinor - summary.OperatingExpensesMinor,
        "Operating profit includes a capital or other flow.");
    Check(summary.NetCashFlowMinor == summary.OperatingProfitMinor - summary.CapitalSpendingMinor
        + summary.CapitalReceiptsMinor + summary.FinancingNetMinor + summary.OtherNetMinor, "Cash flow categories do not add up.");
    Check(summary.OpeningCashMinor + summary.NetCashFlowMinor == summary.ClosingCashMinor, "Period cash bridge does not reconcile.");
    Check(summary.Breakdown.Sum(row => row.AmountMinor) == summary.NetCashFlowMinor, "Category breakdown does not reconcile.");
}
void AssertOwnership(GameSession game)
{
    var people = game.People.Select(p => p.Id).ToHashSet();
    Check(people.Count == game.People.Count, "Duplicate logical person.");
    var journeys = game.Transport.Journeys.ToDictionary(j => j.PersonId);
    Check(people.SetEquals(journeys.Keys), "A person or journey has no owner.");
    var riders = game.Transport.Cars.SelectMany(c => c.PassengerIds).ToArray();
    Check(riders.Distinct().Count() == riders.Length && riders.All(id => people.Contains(id) && journeys[id].State == JourneyState.Riding), "Passenger ownership is inconsistent.");
    Check(game.Transport.Cars.All(c => c.PassengerCount <= c.Capacity), "A car exceeded capacity.");
    foreach (var room in game.World.Rooms)
        Check(game.ReservedCapacity(room.Id) <= rules.For(room.DefinitionId)!.Capacity, "Room capacity exceeded.");
}

void CapitalClassification()
{
    var game = NewGame();
    Succeed(game.World.BuildFloor(1));
    var office = Build(game, "office", 0, 1);
    Succeed(game.Transport.InstallBank(new BankDefinition(1, 27, 0, 1, [0, 1]), rules.ElevatorCostMinor));
    var spent = catalog.FloorConstructionCostMinor + catalog.Get("office").CostMinor + rules.ElevatorCostMinor;
    Succeed(game.DemolishRoom(office));
    var summary = game.CurrentFinances;
    Check(summary.CapitalSpendingMinor == spent && summary.CapitalReceiptsMinor == catalog.Get("office").CostMinor / 2,
        "Capital payments or refunds were omitted.");
    Check(summary.OperatingRevenueMinor == 0 && summary.OperatingExpensesMinor == 0 && summary.OperatingProfitMinor == 0,
        "Building or demolition became operating income/expense.");
    Check(summary.FinancingNetMinor == 0 && summary.OtherNetMinor == 0, "Known construction flow became financing/other.");
    AssertLedger(game);
}
void CategoryPolicy()
{
    Check(FinanceClassification.Classify("Sales.Condo", 100) == FinancialFlowKind.CapitalReceipt, "Condominium sale counted as operating sales.");
    Check(FinanceClassification.Classify("Condo.Buyback", -100) == FinancialFlowKind.CapitalSpending, "Ownership buyback counted as daily operations.");
    Check(FinanceClassification.Classify("Operations.Upkeep", -100) == FinancialFlowKind.OperatingExpense, "Legacy combined expense lost its category.");
    Check(FinanceClassification.Classify("Operations.Wages", -100) == FinancialFlowKind.OperatingExpense
        && FinanceClassification.Classify("Operations.FacilityUpkeep", -100) == FinancialFlowKind.OperatingExpense, "Split wages/upkeep not operating costs.");
    Check(FinanceClassification.Classify("Unrecognized.Adjustment", 100) == FinancialFlowKind.Other, "Unknown cash movement was silently called revenue.");
    Check(FinanceClassification.Classify("Lease.Office", 100) == FinancialFlowKind.OperatingRevenue
        && FinanceClassification.Classify("Sales.Food", 100) == FinancialFlowKind.OperatingRevenue, "Known receipts lost operating classification.");
}
void AtomicFailures()
{
    var game = SmallTower();
    var original = game.Serialize();
    Check(!game.BuildRoom("office", 0, 0).Success, "Overlapping construction succeeded.");
    Check(!game.World.BuildFloor(99).Success, "Unsupported floor succeeded.");
    Check(!game.World.ApplyOperatingTransaction(long.MaxValue, game.Tick, "Operations.FacilityUpkeep", null, "Overflow rejection fixture").Success,
        "Overflowing transaction succeeded.");
    Check(game.Serialize() == original, "Rejected financial command mutated saved state.");
    AssertLedger(game);
}
void AtomicBillingFailure()
{
    var game = NewGame(); Build(game, "lobby", 0, 0); var home = Build(game, "studio", 6, 0);
    var cafe = Build(game, "cafe", 10, 0); Succeed(game.SetOpen(cafe, false));
    AdvanceToBill(game, immediatelyBefore: true);
    Check(game.OperationFor(home)!.ContractActive, "Overflow fixture requires a genuinely occupied home.");
    // Explicit range injection only in this adversarial fixture; playable baseline budgets remain unchanged.
    Succeed(game.World.ApplyOperatingTransaction(long.MaxValue - game.World.CashMinor, game.Tick,
        "Test.RangeInjection", null, "Exercise all-or-nothing billing near the supported cash limit."));
    var cash = game.World.CashMinor; var ledger = JsonSerializer.Serialize(game.World.Ledger);
    var operation = game.OperationFor(home); var schedule = game.Billing;
    game.Step();
    Check(game.World.CashMinor == cash && JsonSerializer.Serialize(game.World.Ledger) == ledger,
        "An overflowing later rent posting left earlier upkeep partially committed.");
    Check(game.Billing == schedule && game.FinancialHistory.Count == 0 && game.Reports.Count == 0,
        "Failed recurring batch consumed its billing period.");
    Check(game.OperationFor(home)!.LastRentDay == operation!.LastRentDay
        && game.OperationFor(home)!.GrossRevenueMinor == operation.GrossRevenueMinor
        && game.OperationFor(home)!.CostsMinor == operation.CostsMinor, "Failed batch changed room financial caches.");
    Succeed(game.SetStaff(cafe, 3)); Succeed(game.SetPrice(home, 110_000));
    Check(!game.DemolishRoom(cafe).Success, "Demolition erased an unsettled room obligation.");
    Check(game.UpcomingBills.Single(b => b.RoomId == cafe && b.Category == "Operations.Wages").AmountMinor == -2 * rules.For("cafe")!.StaffSalaryMinor,
        "Changing staff retroactively changed the pending invoice.");
    var pendingSave = game.Serialize();
    RejectPending(root => root["finance"]!["pendingBatches"] = new JsonArray());
    RejectPending(root => root["finance"]!["pendingBatches"]!.AsArray().Add(root["finance"]!["pendingBatches"]![0]!.DeepClone()));
    RejectPending(root => root["finance"]!["pendingBatches"]![0]!["obligations"]![0]!["amountMinor"] = -1);
    RejectPending(root => root["finance"]!["pendingBatches"]![0]!["obligations"]![0]!["roomId"] = long.MaxValue);
    var restored = Restore(pendingSave);
    foreach (var target in new[] { game, restored })
    {
        Succeed(target.World.ApplyOperatingTransaction(-200_000_000, target.Tick,
            "Test.RangeInjection", null, "Restore room within the supported cash limit."));
        target.Step();
    }
    Check(game.Serialize() == restored.Serialize(), "Saving an unsettled bill changed its later recovery.");
    Check(game.Billing.LastSettledDay == 1 && game.World.Ledger.Count(e => e.Category == "Lease.Home") == 1,
        "Recovered bill did not settle rent exactly once.");
    Check(game.World.Ledger.Count(e => e.Category == "Operations.FacilityUpkeep") == 3,
        "Recovered batch duplicated or skipped upkeep.");
    Check(game.World.Ledger.Single(e => e.Category == "Lease.Home").AmountMinor == operation.PriceMinor,
        "Changing residential price retroactively changed the pending receipt.");
    Check(game.UpcomingBills.Single(b => b.RoomId == cafe && b.Category == "Operations.Wages").AmountMinor == -3 * rules.For("cafe")!.StaffSalaryMinor,
        "The following billing period did not use the edited staffing allocation.");
    // Keep next-day residential eligibility physical: an unserviced home would otherwise close for cleaning.
    Build(game, "service-room", 14, 0); Build(restored, "service-room", 14, 0);
    AdvanceToBill(game); AdvanceToBill(restored);
    Check(game.Serialize() == restored.Serialize(), "Recovered invoice changed the next saved billing period.");
    Check(game.World.Ledger.Last(e => e.EntityId == cafe && e.Category == "Operations.Wages").AmountMinor == -3 * rules.For("cafe")!.StaffSalaryMinor
        && game.World.Ledger.Last(e => e.Category == "Lease.Home").AmountMinor == 110_000,
        "The next settled period failed to adopt the edited staffing or residential price.");
    AssertLedger(game);
    void RejectPending(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(pendingSave)!.AsObject(); change(root);
        Throws(() => Restore(root.ToJsonString()));
        Check(game.Serialize() == pendingSave, "Rejected pending invoice changed the live session.");
    }
}
void MultiplePendingPeriods()
{
    var game = NewGame(); Build(game, "lobby", 0, 0); Build(game, "studio", 6, 0);
    var cafe = Build(game, "cafe", 10, 0); Succeed(game.SetOpen(cafe, false));
    AdvanceToBill(game, immediatelyBefore: true);
    Succeed(game.World.ApplyOperatingTransaction(long.MaxValue - game.World.CashMinor, game.Tick,
        "Test.RangeInjection", null, "Hold two invoice periods at the supported cash limit."));
    game.Step(); Check(game.PendingBillingPeriods == 1, "First failed invoice was not retained.");
    Succeed(game.SetStaff(cafe, 3)); game.Advance(86400);
    Check(game.PendingBillingPeriods == 2 && game.Billing.LastSettledDay == 0,
        "A second midnight replaced or prematurely settled the first invoice.");
    var invoices = game.CaptureSnapshot().Finance.PendingBatches;
    Check(invoices[0].Obligations.Single(b => b.RoomId == cafe && b.Category == "Operations.Wages").AmountMinor == -11_000
        && invoices[1].Obligations.Single(b => b.RoomId == cafe && b.Category == "Operations.Wages").AmountMinor == -16_500,
        "Separate overdue periods did not retain their own staffing allocation.");
    Check(!game.DemolishRoom(cafe).Success, "A room referenced by overdue invoices could be demolished.");
    var restored = Restore(game.Serialize()); var firstPosted = game.World.Ledger.Count + 1;
    foreach (var target in new[] { game, restored })
    {
        Succeed(target.World.ApplyOperatingTransaction(-200_000_000, target.Tick,
            "Test.RangeInjection", null, "Free monetary range to settle both captured invoices."));
        target.Step();
    }
    Check(game.Serialize() == restored.Serialize(), "Saved overdue queue changed ordered recovery.");
    Check(game.PendingBillingPeriods == 0 && game.Billing.LastSettledDay == 2 && game.FinancialHistory.Count == 2,
        "Recovery failed to settle exactly both captured periods.");
    var expected = invoices.SelectMany(i => i.Obligations).Select(o => (o.RoomId, o.AmountMinor, o.Category));
    var actual = game.World.Ledger.Skip(firstPosted).Select(e => (e.EntityId, e.AmountMinor, e.Category));
    Check(actual.SequenceEqual(expected), "Recovery reordered, recomputed, omitted, or duplicated a captured obligation.");
    var postingCount = game.World.Ledger.Count;
    game.Advance(60); restored.Advance(60);
    Check(game.World.Ledger.Count == postingCount && game.Serialize() == restored.Serialize(),
        "An already settled overdue invoice was charged again.");
    Check(Restore(game.Serialize()).Serialize() == game.Serialize(), "Two invoices settled at one tick could not reload.");
    AssertLedger(game);
}
void SavedBillingBoundary()
{
    var game = SmallTower();
    Check(game.Billing.LastSettledDay == 0 && game.Billing.LastSettledTick == 0 && game.Billing.NextDueTick == 57900,
        "Initial billing schedule disagrees with the 07:55 start.");
    AdvanceToBill(game, immediatelyBefore: true);
    var bills = game.UpcomingBills.ToArray();
    var beforeSequence = game.World.Ledger.Count;
    var dueTick = game.Billing.NextDueTick;
    var restored = Restore(game.Serialize());
    game.Step(); restored.Step();
    Check(game.Serialize() == restored.Serialize(), "Saving immediately before a bill changed settlement.");
    Check(game.Billing.LastSettledDay == 1 && game.Billing.LastSettledTick == dueTick && game.Billing.NextDueTick == dueTick + 86400,
        "Billing cursor did not advance exactly one period.");
    var posted = game.World.Ledger.Skip(beforeSequence).Where(e => e.Category is "Operations.Wages" or "Operations.FacilityUpkeep").ToArray();
    foreach (var bill in bills)
    {
        var matching = posted.Where(e => e.EntityId == bill.RoomId && e.Category == bill.Category).ToArray();
        Check(matching.Length == 1 && matching[0].AmountMinor == bill.AmountMinor && matching[0].TimestampTicks == dueTick,
            "An estimated obligation was skipped, duplicated, or changed at settlement.");
    }
    Check(posted.Length == bills.Length, "Settlement created an unestimated recurring bill.");
    var secondRestore = Restore(game.Serialize());
    game.Advance(120); secondRestore.Advance(120);
    Check(game.Serialize() == secondRestore.Serialize(), "Loading just after settlement repeated the bill.");
    Check(game.World.Ledger.Count(e => e.TimestampTicks == dueTick && e.Category is "Operations.Wages" or "Operations.FacilityUpkeep") == bills.Length,
        "An obligation was charged more than once.");
    AssertLedger(game);
}
void StaffingEstimate()
{
    var game = NewGame(); Build(game, "lobby", 0, 0);
    var cafe = Build(game, "cafe", 6, 0);
    var original = game.UpcomingBills.Single(b => b.RoomId == cafe && b.Category == "Operations.Wages");
    Succeed(game.SetStaff(cafe, 3));
    var changed = game.UpcomingBills.Single(b => b.RoomId == cafe && b.Category == "Operations.Wages");
    Check(original.AmountMinor == -2 * rules.For("cafe")!.StaffSalaryMinor && changed.AmountMinor == -3 * rules.For("cafe")!.StaffSalaryMinor,
        "Wage estimate does not reflect configured staffing.");
    Check(game.World.Ledger.All(e => e.Category != "Operations.Wages"), "Editing staffing prematurely charged wages.");
    AdvanceToBill(game);
    var wage = game.World.Ledger.Single(e => e.EntityId == cafe && e.Category == "Operations.Wages");
    Check(wage.AmountMinor == changed.AmountMinor, "Settled wage differs from the current allocation estimate.");
}
void ClosedFacilityBills()
{
    var game = NewGame(); Build(game, "lobby", 0, 0);
    var cafe = Build(game, "cafe", 6, 0); Succeed(game.SetOpen(cafe, false));
    var bills = game.UpcomingBills.Where(b => b.RoomId == cafe).ToArray();
    Check(bills.Length == 2, "Closing silently discarded allocated wages or upkeep.");
    AdvanceToBill(game);
    Check(game.World.Ledger.Where(e => e.EntityId == cafe && e.Category.StartsWith("Operations.", StringComparison.Ordinal)).Sum(e => e.AmountMinor)
        == -rules.For("cafe")!.UpkeepMinor - 2 * rules.For("cafe")!.StaffSalaryMinor, "Closed-facility costs do not follow the documented policy.");
    Check(game.OperationFor(cafe)!.GrossRevenueMinor == 0, "A closed facility earned revenue.");
}
void PeriodReconciliation()
{
    var game = SmallTower(); AdvanceToBill(game); AdvanceToBill(game); game.Advance(3600);
    Check(game.FinancialHistory.Count == 2, "Missing closed financial periods.");
    long closing = 250_000_000;
    var windows = new List<long>();
    foreach (var summary in game.FinancialHistory.Append(game.CurrentFinances))
    {
        AssertSummary(summary);
        Check(summary.OpeningCashMinor == closing, "Adjacent financial periods have a cash gap.");
        closing = summary.ClosingCashMinor;
        var entries = game.FinanceTransactions(summary.Day);
        Check(entries.Sum(e => e.AmountMinor) == summary.NetCashFlowMinor, "Transaction window differs from its period report.");
        windows.AddRange(entries.Select(e => e.Sequence));
    }
    Check(closing == game.World.CashMinor && windows.SequenceEqual(game.World.Ledger.Select(e => e.Sequence)),
        "Financial windows overlap, omit, or reorder ledger transactions.");
    var lifetime = game.LifetimeFinances;
    Check(lifetime.OperatingRevenueMinor == game.FinancialHistory.Sum(s => s.OperatingRevenueMinor) + game.CurrentFinances.OperatingRevenueMinor,
        "Daily and lifetime operating receipts disagree.");
    AssertLedger(game);
}
void ReadPurity()
{
    var game = SmallTower(); game.Advance(7200);
    var before = game.Serialize();
    var frozen = game.CurrentFinances;
    var frozenJson = JsonSerializer.Serialize(frozen);
    for (var i = 0; i < 50; i++)
    {
        _ = game.CurrentFinances; _ = game.LifetimeFinances; _ = game.FinancialHistory;
        _ = game.Billing; _ = game.UpcomingBills; _ = game.FinanceTransactions();
    }
    Check(game.Serialize() == before, "Opening reports advanced time or posted bills.");
    Succeed(game.BuildRoom("billboard", 24, 3));
    Check(JsonSerializer.Serialize(frozen) == frozenJson, "A previously returned summary changed after construction.");
}
void SpeedEquivalence()
{
    var checkpoint = SmallTower(); AdvanceToBill(checkpoint, immediatelyBefore: true);
    var normal = Restore(checkpoint.Serialize()); var fast = Restore(checkpoint.Serialize());
    var slowRunner = new FixedStepRunner(rules); var fastRunner = new FixedStepRunner(rules); fastRunner.SetSpeed(4);
    for (var i = 0; i < 120; i++) slowRunner.Advance(1d / 60, normal.Step);
    for (var i = 0; i < 30; i++) fastRunner.Advance(1d / 60, fast.Step);
    Check(normal.Serialize() == fast.Serialize(), "Speed changed billing or financial periods at the same logical tick.");
    var paused = normal.Serialize(); slowRunner.SetSpeed(0);
    for (var i = 0; i < 60; i++) slowRunner.Advance(1, normal.Step);
    Check(normal.Serialize() == paused, "Pause allowed financial obligations or counters to advance.");
}
void InvalidFinanceMetadata()
{
    var game = SmallTower(); AdvanceToBill(game);
    Build(game, "billboard", 24, 3);
    var before = game.Serialize();
    Reject(root => root["finance"]!["billing"]!["lastSettledDay"] = 0);
    Reject(root => root["finance"]!["billing"]!["nextDueTick"] = game.Billing.NextDueTick + 1);
    Reject(root => root["finance"]!["currentPeriodFirstSequence"] = 1);
    Reject(root => root["finance"]!["periods"]![0]!["lastSequence"] = long.MaxValue);
    Reject(root => root["finance"]!["legacyThroughSequence"] = long.MaxValue);
    Reject(root => root["todayRevenue"] = 1);
    Reject(root =>
    {
        var finance = root["finance"]!;
        finance["periods"]![0]!["lastSequence"] = game.World.Ledger.Count;
        finance["currentPeriodFirstSequence"] = game.World.Ledger.Count + 1;
    });
    Check(game.Serialize() == before, "Rejected financial load mutated the live session.");
    void Reject(Action<JsonObject> change)
    {
        var root = JsonNode.Parse(before)!.AsObject(); change(root);
        Throws(() => Restore(root.ToJsonString()));
    }
}
void LegacyMigration()
{
    var game = SmallTower(); game.Advance(7200);
    var modern = game.Serialize();
    var legacy = JsonNode.Parse(modern)!.AsObject(); legacy["schemaVersion"] = 2; legacy.Remove("finance");
    var migrated = Restore(legacy.ToJsonString());
    Check(JsonSerializer.Serialize(migrated.World.Ledger) == JsonSerializer.Serialize(game.World.Ledger), "Migration rewrote financial history.");
    Check(migrated.World.CashMinor == game.World.CashMinor && migrated.Billing == game.Billing, "Migration changed cash or the next obligation.");
    AdvanceToBill(game); AdvanceToBill(migrated);
    Check(JsonSerializer.Serialize(migrated.World.Ledger) == JsonSerializer.Serialize(game.World.Ledger), "Legacy migration changed future bill postings.");
    Check(migrated.World.CashMinor == game.World.CashMinor && migrated.Billing == game.Billing, "Legacy billing continuation diverged.");
    AssertLedger(migrated);
}
void LegacyCombinedUpkeep()
{
    var game = SmallTower(); AdvanceToBill(game); game.Advance(7200);
    var legacy = JsonNode.Parse(game.Serialize())!.AsObject(); legacy["schemaVersion"] = 2; legacy.Remove("finance");
    var source = legacy["world"]!["ledger"]!.AsArray(); var entries = new JsonArray();
    foreach (var entry in source)
    {
        var row = entry!.DeepClone().AsObject(); var category = row["category"]!.GetValue<string>();
        if (category == "Billing.Settlement") continue;
        if (category is "Operations.Wages" or "Operations.FacilityUpkeep")
        {
            if (entries.LastOrDefault() is JsonObject previous && previous["category"]!.GetValue<string>() == "Operations.Upkeep"
                && JsonNode.DeepEquals(previous["entityId"], row["entityId"])
                && JsonNode.DeepEquals(previous["timestampTicks"], row["timestampTicks"]))
            {
                previous["amountMinor"] = previous["amountMinor"]!.GetValue<long>() + row["amountMinor"]!.GetValue<long>();
                previous["balanceAfterMinor"] = row["balanceAfterMinor"]!.GetValue<long>();
                continue;
            }
            row["category"] = "Operations.Upkeep"; row["description"] = "Legacy combined daily upkeep and allocated wages.";
        }
        row["sequence"] = entries.Count + 1; entries.Add(row);
    }
    legacy["world"]!["ledger"] = entries;
    legacy["world"]!["commandSequence"] = entries.Count;
    var migrated = Restore(legacy.ToJsonString());
    Check(JsonNode.DeepEquals(JsonNode.Parse(migrated.Serialize())!["world"]!["ledger"], entries), "Migration rewrote a legacy cash entry.");
    Check(migrated.FinancialHistory.Single().OperatingExpensesMinor == game.FinancialHistory.Single().OperatingExpensesMinor,
        "Legacy wages/upkeep lost their operating expense classification.");
    Check(migrated.World.Ledger.Any(e => e.Category == "Operations.Upkeep")
        && migrated.World.Ledger.All(e => e.Category is not ("Operations.Wages" or "Operations.FacilityUpkeep")), "Fixture did not represent combined legacy expenses.");
    var priorCount = migrated.World.Ledger.Count;
    AdvanceToBill(migrated);
    Check(migrated.Billing.LastSettledDay == 2 && migrated.World.Ledger.Skip(priorCount).Any(e => e.Category == "Operations.Wages")
        && migrated.World.Ledger.Skip(priorCount).All(e => e.Category != "Operations.Upkeep"), "Migrated future bills did not adopt the new split categories.");
    Check(Restore(migrated.Serialize()).Serialize() == migrated.Serialize(), "Migrated historical and new billing periods did not reload.");
    AssertLedger(migrated);
}
void CondoMigration()
{
    var game = NewGame(); Build(game, "lobby", 0, 0); var condo = Build(game, "condo", 6, 0);
    game.Advance(12 * 3600);
    var sale = game.World.Ledger.Single(e => e.Category == "Sales.Condo");
    Check(game.OperationFor(condo)!.CondoSold && game.Occupancy(condo) > 0, "A physical owner never completed the purchase.");
    Check(game.CurrentFinances.CapitalReceiptsMinor == sale.AmountMinor && game.CurrentFinances.OperatingRevenueMinor == 0,
        "A condominium ownership receipt became recurring operating profit.");
    var legacy = JsonNode.Parse(game.Serialize())!.AsObject(); legacy["schemaVersion"] = 2; legacy.Remove("finance");
    legacy["todayRevenue"] = sale.AmountMinor;
    var migrated = Restore(legacy.ToJsonString());
    Check(JsonSerializer.Serialize(game.World.Ledger) == JsonSerializer.Serialize(migrated.World.Ledger), "Condominium migration rewrote its purchase receipt.");
    Check(migrated.CurrentFinances.OperatingRevenueMinor == 0 && migrated.CurrentFinances.CapitalReceiptsMinor == sale.AmountMinor,
        "Legacy ownership cash was not removed from operating revenue.");
    Succeed(migrated.BuyBackCondo(condo));
    Check(migrated.CurrentFinances.OperatingExpensesMinor == 0
        && migrated.CurrentFinances.CapitalSpendingMinor == game.CurrentFinances.CapitalSpendingMinor + sale.AmountMinor,
        "Repurchasing ownership became an operating expense.");
    Check(Restore(migrated.Serialize()).Serialize() == migrated.Serialize(), "Capital reclassification did not preserve canonical saved counters.");
    AssertLedger(migrated);
}
void MixedActiveCheckpoint()
{
    var game = OperatingExampleScenario.Create(catalog, rules, locations);
    bool Ready() => game.People.Any(p => p.Role == "Guest" && p.Activity == PersonActivity.Visiting)
        && game.People.Any(p => p.Role == "Staff" && p.Activity == PersonActivity.Working && p.ServiceTargetId != null);
    for (var step = 0; step < 2 * 24 * 60 && !Ready(); step++) game.Advance(60);
    Check(Ready(), "Mixed fixture never reached an active hotel stay with actual service work.");
    var restored = Restore(game.Serialize());
    game.Advance(86400); restored.Advance(86400);
    Check(game.Serialize() == restored.Serialize(), "Active guest/service save changed later receipts, bills, or jobs.");
    Check(game.World.Ledger.Any(e => e.Category == "Sales.Hotel") && game.World.Ledger.Any(e => e.Category == "Maintenance.Repair"),
        "Mixed continuation did not exercise real paid hotel and service activity.");
    AssertOwnership(game); AssertLedger(game);
}
void SevenDayMixedTower()
{
    var game = OperatingExampleScenario.Create(catalog, rules, locations);
    var firstCapital = game.LifetimeFinances.CapitalSpendingMinor;
    for (var hour = 0; hour < 7 * 24; hour++)
    {
        game.Advance(3600); AssertOwnership(game);
        Check(game.World.CashMinor >= 0, "Provisional baseline exhausted its budget.");
    }
    AssertLedger(game);
    Check(game.FinancialHistory.Count == 7 && game.Billing.LastSettledDay == 7, "Seven-day fixture skipped a settlement.");
    Check(game.FinancialHistory.Sum(s => s.OperatingProfitMinor) > 0 && game.FinancialHistory.Count(s => s.OperatingProfitMinor > 0) >= 3,
        "Documented small tower did not produce a positive operating baseline.");
    Check(game.LifetimeFinances.CapitalSpendingMinor == firstCapital, "Fixture introduced hidden growth or subsidies.");
    foreach (var category in new[] { "Lease.Office", "Lease.Home", "Sales.Food", "Sales.Hotel", "Operations.Wages", "Operations.FacilityUpkeep", "Maintenance.Repair" })
        Check(game.World.Ledger.Any(e => e.Category == category && e.AmountMinor != 0), $"Seven-day fixture omitted real {category} activity.");
    Check(game.CompletedTrips > 100 && game.Transport.Metrics.AbandonedTrips == 0, "Financial baseline bypassed movement or overloaded transport.");
    Check(game.World.Rank >= 2, "Normal operating example did not earn its existing first promotion.");
    for (var rank = 2; rank <= game.World.Rank; rank++)
        Check(game.Notices.Count(n => n.Kind == "Promotion" && n.Text.StartsWith($"Rank {rank}:", StringComparison.Ordinal)) == 1,
            "A naturally earned rank emitted a missing or repeated promotion.");
    Console.WriteLine($"  BASELINE: {game.World.Rooms.Count} rooms/{game.World.Floors.Count} floors; 7 closed days; operating profit {Money(game.FinancialHistory.Sum(s => s.OperatingProfitMinor))}; cash {Money(game.World.CashMinor)}; trips {game.CompletedTrips}; rank {game.World.Rank}.");
}
void TwentyFloorFixture()
{
    var game = OperatingExampleScenario.Create(catalog, rules, locations, floorCount: 20);
    AdvanceToBill(game, immediatelyBefore: true);
    Check(game.World.Floors.Count == 20 && game.People.Count > 0 && game.CompletedTrips > 0, "Twenty-floor fixture did not generate real connected demand.");
    var restored = Restore(game.Serialize()); game.Advance(86401); restored.Advance(86401);
    Check(game.Serialize() == restored.Serialize(), "Twenty-floor save changed two billing boundaries or physical operation.");
    Check(game.FinancialHistory.Count == 2 && game.Billing.LastSettledDay == 2, "Twenty-floor fixture omitted recurring bills.");
    foreach (var office in game.World.Rooms.Where(r => r.DefinitionId == "office"))
        Check(game.World.Ledger.Any(e => e.EntityId == office.Id && e.Category == "Lease.Office"), "An occupied upper-floor office never produced its receipt.");
    AssertOwnership(game); AssertLedger(game);
}

static void Succeed(CommandResult result) => Check(result.Success, result.Message);
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static void Throws(Action action)
{
    try { action(); }
    catch (ArgumentException) { return; }
    catch (JsonException) { return; }
    throw new InvalidOperationException("Invalid financial save was accepted.");
}
static string Money(long amount) => "$" + (amount / 100m).ToString("N2", CultureInfo.InvariantCulture);
