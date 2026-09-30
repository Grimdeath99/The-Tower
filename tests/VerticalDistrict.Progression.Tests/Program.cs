using System.Diagnostics;
using System.Globalization;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

// A gameplay scenario, never a shortcut to promotion. Only public construction/staffing
// commands and ordinary logical ticks are used. Content, prices and thresholds are unchanged.
var dataPath = Path.Combine(AppContext.BaseDirectory, "Data");
var catalog = ContentCatalog.Load(File.ReadAllText(Path.Combine(dataPath, "construction.catalog.json")));
var rules = SimulationRules.Load(File.ReadAllText(Path.Combine(dataPath, "simulation.rules.json")), catalog);
var locations = LocationCatalog.Load(File.ReadAllText(Path.Combine(dataPath, "locations.json")));
var allLocations = args.Contains("--all-locations");
var wallLimit = ReadInt("--wall-seconds", 120);
var dayLimit = ReadInt("--days", 8);
var selected = allLocations ? locations.Locations.ToArray() : [locations.Get("tokyo")];
var failures = 0;
foreach (var location in selected)
{
    try { Run(location); }
    catch (Exception error) { failures++; Console.Error.WriteLine($"FAIL {location.Id}: {error.Message}"); }
}
Console.WriteLine($"Normal progression: {selected.Length - failures}/{selected.Length} profiles reached rank 7.");
return failures == 0 ? 0 : 1;

int ReadInt(string name, int defaultValue)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out var value) && value > 0 ? value : defaultValue;
}

void Run(LocationDefinition location)
{
    var timer = Stopwatch.StartNew();
    var site = location.Sites.First(s => !s.SubwayConnection && !s.Waterfront);
    var game = new GameSession(catalog, rules, locations, location.Id, site.Id, sandbox: false);
    Check(game.World.Rank == 1 && game.World.CashMinor == 250_000_000 && !game.Sandbox, "Scenario must use an ordinary new district.");
    for (var floor = 1; floor <= 19; floor++) Succeed(game.World.BuildFloor(floor));
    for (var floor = 0; floor < 19; floor++) Succeed(game.Transport.BuildStair(floor, 31, rules.StairCostMinor));
    Succeed(game.Transport.InstallBank(new BankDefinition(1, 29, 0, 9, Enumerable.Range(0, 10).ToArray(), Capacity: 64), rules.ElevatorCostMinor));
    Succeed(game.Transport.InstallBank(new BankDefinition(2, 30, 0, 19, [0, .. Enumerable.Range(10, 10)], Capacity: 64), rules.ElevatorCostMinor));
    Build("lobby"); Build("cafe"); Build("shop"); Build("studio");
    var stages = new[]
    {
        new Stage(1, 12, 2, 3, 1, 1), new Stage(2, 24, 4, 6, 2, 2),
        new Stage(3, 40, 6, 9, 3, 3), new Stage(4, 55, 8, 12, 4, 3),
        new Stage(5, 70, 10, 15, 4, 3)
    };
    var nextStage = 0;
    var observedRank = 1;
    var priorDay = game.Day;
    var positiveReceipts = 0L;
    var lowestCash = game.World.CashMinor;
    var paidConstruction = game.World.Ledger.Where(e => e.AmountMinor < 0).Sum(e => -e.AmountMinor);
    var reached = new List<int> { 1 };
    Console.WriteLine($"START {location.Id}/{site.Id}, unchanged content, default prices, two configured 64-seat banks, wall limit {wallLimit}s.");
    while (game.World.Rank < 7 && game.Day <= dayLimit)
    {
        if (timer.Elapsed.TotalSeconds >= wallLimit) throw new InvalidOperationException($"Wall-time budget reached: {Status()}");
        while (nextStage < stages.Length && game.World.Rank >= stages[nextStage].Rank)
        {
            if (!BuildStage(stages[nextStage])) break;
            nextStage++;
            Console.WriteLine($"BUILD stage {nextStage}: {game.World.Rooms.Count} rooms, cash {Money(game.World.CashMinor)}.");
        }
        game.Advance(60);
        lowestCash = Math.Min(lowestCash, game.World.CashMinor);
        Check(game.World.CashMinor >= 0, "Construction or running costs required debt.");
        if (game.World.Rank != observedRank)
        {
            Check(game.World.Rank == observedRank + 1, "Promotion skipped a rank.");
            observedRank = game.World.Rank; reached.Add(observedRank);
            Console.WriteLine($"RANK {observedRank}: {Status()}");
        }
        if (game.Day != priorDay)
        {
            priorDay = game.Day;
            Console.WriteLine($"DAY CLOSED: {Status()}");
        }
    }
    Check(game.World.Rank == 7, $"Simulation day budget reached: {Status()}");
    Check(reached.SequenceEqual(Enumerable.Range(1, 7)), "Not every rank was earned in order.");
    Check(game.World.Rooms.All(r => r.DefinitionId is not ("subway" or "dock")), "Common progression path used an optional arrival terminal.");
    Check(game.Operations.All(op => op.PriceMinor == rules.For(game.World.Rooms.Single(r => r.Id == op.RoomId).DefinitionId)!.PriceMinor), "A price was changed.");
    Check(game.People.All(p => p.Activity != PersonActivity.Stranded), "Scenario left stranded people.");
    var cash = 250_000_000L;
    foreach (var entry in game.World.Ledger)
    {
        cash = checked(cash + entry.AmountMinor);
        Check(cash == entry.BalanceAfterMinor && cash >= 0, "Scenario ledger does not reconcile or used debt.");
        if (entry.AmountMinor > 0)
        {
            Check(entry.Category.StartsWith("Lease.", StringComparison.Ordinal) || entry.Category.StartsWith("Sales.", StringComparison.Ordinal), "Unexpected subsidy or non-operating receipt.");
            positiveReceipts += entry.AmountMinor;
        }
    }
    Check(cash == game.World.CashMinor && positiveReceipts > 0, "No earned operating income.");
    Check(game.World.Ledger.Any(e => e.Category == "Maintenance.Repair"), "No routed maintenance completed.");
    Check(game.CompletedTrips >= 300 && game.Transport.Metrics.WaitSampleCount > 0, "Progression lacked real journeys/elevator use.");
    Check(game.Transport.Metrics.AbandonedTrips == 0, "Demand exceeded transport patience during progression.");
    Console.WriteLine($"PASS {location.Id}: {Status()}; minimum cash {Money(lowestCash)}; earned receipts {Money(positiveReceipts)}; initial works {Money(paidConstruction)}; ranks {string.Join("→", reached)}.");

    bool BuildStage(Stage stage)
    {
        // Capacity and coverage come before new demand. Any unaffordable next room waits for
        // real operating receipts; no special starting cash or transaction injections are used.
        foreach (var (id, target) in new[] { ("utility-room", stage.Utilities), ("service-room", stage.Depots),
                     ("security-room", stage.Security), ("hotel-room", stage.Hotels), ("office", stage.Offices) })
        {
            while (game.World.Rooms.Count(r => r.DefinitionId == id) < target)
            {
                if (game.World.CashMinor < catalog.Get(id).CostMinor + 5_000_000) return false;
                var entity = Build(id);
                if (id == "service-room") Succeed(game.SetStaff(entity, 4));
            }
        }
        return true;
    }

    long Build(string definition)
    {
        var d = catalog.Get(definition);
        foreach (var floor in game.World.Floors.Order())
            for (var x = 0; x + d.Width <= 29; x++)
            {
                if (!game.ValidateRoom(definition, x, floor).Success) continue;
                var command = game.BuildRoom(definition, x, floor);
                Succeed(command); return command.EntityId!.Value;
            }
        throw new InvalidOperationException($"No affordable place for {definition}; {Status()}");
    }

    string Status() => $"{game.ClockText}; rank {game.World.Rank}; cash {Money(game.World.CashMinor)}; "
        + $"last profit {Money(game.Reports.LastOrDefault()?.ProfitMinor ?? 0)}; peak {game.PeakPopulation}; "
        + $"satisfaction {game.Satisfaction}; clean {game.Cleanliness}; trips {game.CompletedTrips}; "
        + $"abandoned {game.Transport.Metrics.AbandonedTrips}; elapsed {timer.Elapsed.TotalSeconds:F1}s; "
        + $"remaining [{string.Join("; ", game.UnmetRankRequirements())}]";
}

static void Succeed(CommandResult result) => Check(result.Success, result.Message);
static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
static string Money(long minor) => "$" + (minor / 100m).ToString("N0", CultureInfo.InvariantCulture);
internal sealed record Stage(int Rank, int Offices, int Hotels, int Security, int Depots, int Utilities);
