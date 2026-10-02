using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;

// Identical commands run against either a preserved baseline assembly or the current core.
// The extra condo is bought through the sandbox's existing rank unlock, never changed rules.
if (args.Length is < 1 or > 3) throw new ArgumentException("Supply output JSON path, optional data directory, and optional 'retail' fixture (run from repository root).");
var dataDirectory = args.Length > 1 ? args[1] : "Data";
var retailFixture = args.Length > 2 && args[2] == "retail";
var catalog = ContentCatalog.Load(File.ReadAllText(Path.Combine(dataDirectory, "construction.catalog.json")));
var rules = SimulationRules.Load(File.ReadAllText(Path.Combine(dataDirectory, "simulation.rules.json")), catalog);
var locations = LocationCatalog.Load(File.ReadAllText(Path.Combine(dataDirectory, "locations.json")));
GameSession Fixture()
{
    var game = OperatingExampleScenario.Create(catalog, rules, locations, sandbox: true, floorCount: 20);
    var built = game.BuildRoom("condo", 15, 4);
    if (!built.Success) throw new InvalidOperationException(built.Message);
    if (retailFixture)
        foreach (var floor in new[] { 7, 14 })
        {
            var shop = game.BuildRoom("shop", 15, floor);
            if (!shop.Success) throw new InvalidOperationException(shop.Message);
        }
    return game;
}
Fixture().Advance(3600); // JIT warm-up excluded from timed repetitions.
var runs = new List<object>();
for (var repetition = 1; repetition <= 3; repetition++)
{
    var game = Fixture();
    const int ticks = 3 * 86400;
    var durations = new double[ticks];
    var peak = 0;
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var allocated = GC.GetAllocatedBytesForCurrentThread();
    var elapsed = Stopwatch.StartNew();
    for (var second = 0; second < ticks; second++)
    {
        var stamp = Stopwatch.GetTimestamp();
        game.Step();
        durations[second] = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
        if (second % 60 == 0) peak = Math.Max(peak, game.PhysicalPopulation);
    }
    elapsed.Stop();
    allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    Array.Sort(durations);
    var snapshot = game.Serialize();
    var restoreStamp = Stopwatch.GetTimestamp();
    var resumed = GameSession.Deserialize(catalog, rules, locations, snapshot);
    var restoreMs = Stopwatch.GetElapsedTime(restoreStamp).TotalMilliseconds;
    game.Advance(3600); resumed.Advance(3600);
    if (game.Serialize() != resumed.Serialize()) throw new InvalidOperationException("Saved continuation diverged.");
    var measurement = new {
        repetition, ticks, wallMilliseconds = elapsed.Elapsed.TotalMilliseconds,
        meanStepMilliseconds = durations.Average(), p95StepMilliseconds = durations[(int)(ticks * .95)],
        maxStepMilliseconds = durations[^1], allocatedBytes = allocated, peakPhysicalPeopleSampledEachMinute = peak,
        finalPhysicalPeople = resumed.PhysicalPopulation, rooms = resumed.World.Rooms.Count,
        completedTrips = resumed.CompletedTrips, cashMinor = resumed.World.CashMinor,
        condoSaleCount = resumed.World.Ledger.Count(e => e.Category == "Sales.Condo"),
        shopReceiptCount = resumed.World.Ledger.Count(e => e.Category == "Sales.Shop"),
        shopRevenueMinor = resumed.World.Ledger.Where(e => e.Category == "Sales.Shop").Sum(e => e.AmountMinor),
        restoredBytes = System.Text.Encoding.UTF8.GetByteCount(snapshot), restoreMilliseconds = restoreMs,
        continuationMatches = true
    };
    runs.Add(measurement);
    Console.WriteLine(JsonSerializer.Serialize(measurement));
}
var report = new {
    measuredUtc = DateTimeOffset.UtcNow, operatingSystem = RuntimeInformation.OSDescription,
    runtime = RuntimeInformation.FrameworkDescription,
    processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"), logicalProcessors = Environment.ProcessorCount,
    harnessBuild = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
    coreBuild = typeof(GameSession).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
    coreAssemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GameSession).Assembly.Location))), seed = 20260930,
    fixture = "20 floors, " + (retailFixture ? "17 rooms (including two shops on floors 7 and 14)" : "15 rooms") + "; three offices, three studios, three hotels, cafe, condo, lobby, service, utility, security; one 12-seat elevator, 19 stair links; sandbox capital/rank, normal business tuning; 3 days plus 1-hour resumed comparison",
    scope = "Authoritative business simulation; no rendering. Small mixed-use population, not a 250-floor capacity or FPS claim. Business semantics may differ between versions; compare population, trips and ledger receipts alongside timing. Shop receipts represent the payment policy of each version, not a common completed-service metric.",
    runs
};
File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
