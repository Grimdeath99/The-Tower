using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Geography;
using VerticalDistrict.Core.Simulation;
using VerticalDistrict.Core.Transport;

if (args.Length != 2) throw new ArgumentException("Use output.json and data-directory arguments from repository root.");
var catalog = ContentCatalog.Load(File.ReadAllText(Path.Combine(args[1], "construction.catalog.json")));
var rules = SimulationRules.Load(File.ReadAllText(Path.Combine(args[1], "simulation.rules.json")), catalog);
var locations = LocationCatalog.Load(File.ReadAllText(Path.Combine(args[1], "locations.json")));
#if COORDINATED
const bool coordinated = true;
#else
const bool coordinated = false;
#endif
GameSession Fixture(int floors)
{
    var game = new GameSession(catalog, rules, locations, sandbox: true);
    for (var floor = 1; floor < floors; floor++) Must(game.World.BuildFloor(floor));
    var mid = floors / 2 - 1; var top = floors - 1;
    Must(game.BuildRoom("lobby", 0, 0)); Must(game.BuildRoom("service-room", 6, 0));
    Must(game.BuildRoom("utility-room", 10, 0)); Must(game.BuildRoom("security-room", 15, 0));
    Must(game.BuildRoom("lobby", 16, mid));
    for (var row = 0; row < 2; row++)
        for (var i = 0; i < 6; i++) Must(game.BuildRoom("office", i * 5, top - row));
    for (var i = 0; i < 3; i++) Must(game.BuildRoom("studio", i * 4, top - 2));
    for (var i = 0; i < 3; i++) Must(game.BuildRoom("hotel-room", i * 3, top - 3));
    Must(game.BuildRoom("cafe", 0, top - 4)); Must(game.BuildRoom("shop", 5, top - 4));
    var upperStops = Enumerable.Range(top - 4, 5).Prepend(mid).ToArray();
    // Same four physical shafts/capacities before and after. The old core treats every shaft as a separate bank.
    Must(game.Transport.InstallBank(new BankDefinition(1, 27, 0, mid, [0, mid], 16, 1, 2), rules.ElevatorCostMinor));
    Must(game.Transport.InstallBank(new BankDefinition(2, 30, mid, top, upperStops, 16, 1, 2), rules.ElevatorCostMinor));
#if COORDINATED
    Must(game.Transport.AddCar(1, 2, 28, rules.ElevatorCostMinor));
    Must(game.Transport.AddCar(2, 2, 31, rules.ElevatorCostMinor));
#else
    Must(game.Transport.InstallBank(new BankDefinition(3, 28, 0, mid, [0, mid], 16, 1, 2), rules.ElevatorCostMinor));
    Must(game.Transport.InstallBank(new BankDefinition(4, 31, mid, top, upperStops, 16, 1, 2), rules.ElevatorCostMinor));
#endif
    return game;
}
static void Must(CommandResult result) { if (!result.Success) throw new InvalidOperationException(result.Message); }
Fixture(20).Advance(1800);
var measurements = new List<object>();
foreach (var floors in new[] { 20, 100, 250 })
{
    var game = Fixture(floors);
    const int ticks = 86400;
    var durations = new double[ticks]; var peaks = new int[3];
    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    var process = Process.GetCurrentProcess(); var managedBefore = GC.GetTotalMemory(false);
    var allocated = GC.GetAllocatedBytesForCurrentThread(); var elapsed = Stopwatch.StartNew();
    for (var second = 0; second < ticks; second++)
    {
        var stamp = Stopwatch.GetTimestamp(); game.Step(); durations[second] = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
        if (second % 60 == 0)
        {
            peaks[0] = Math.Max(peaks[0], game.PhysicalPopulation);
            peaks[1] = Math.Max(peaks[1], game.Transport.Metrics.Waiting);
            peaks[2] = Math.Max(peaks[2], game.People.Count(p => p.Role == "Staff"));
        }
    }
    elapsed.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
    process.Refresh(); var managedAfter = GC.GetTotalMemory(false); Array.Sort(durations);
    var snapshotTimer = Stopwatch.StartNew(); var snapshot = game.Serialize(); snapshotTimer.Stop();
    var loadTimer = Stopwatch.StartNew(); var resumed = GameSession.Deserialize(catalog, rules, locations, snapshot); loadTimer.Stop();
    if (resumed.Serialize() != snapshot) throw new InvalidOperationException("Immediate save/restore mismatch.");
    game.Advance(3600); resumed.Advance(3600);
    if (game.Serialize() != resumed.Serialize()) throw new InvalidOperationException("Resumed simulation diverged.");
    var measurement = new {
        floors, rooms = game.World.Rooms.Count, officeWorkers = 96, logicalSeconds = ticks,
        wallMilliseconds = elapsed.Elapsed.TotalMilliseconds, meanStepMilliseconds = durations.Average(),
        p95StepMilliseconds = durations[(int)(ticks * .95)], maxStepMilliseconds = durations[^1],
        allocatedBytes = allocated, managedBeforeBytes = managedBefore, managedAfterBytes = managedAfter,
        workingSetBytes = process.WorkingSet64, peakPhysicalPeopleSampledEachMinute = peaks[0],
        peakWaitingSampledEachMinute = peaks[1], peakServiceWorkersSampledEachMinute = peaks[2],
        finalPeople = game.PhysicalPopulation, completedTrips = game.CompletedTrips,
        abandonedTrips = game.Transport.Metrics.AbandonedTrips,
        completedServices = game.ServiceTasks.Count(t => t.Status == ServiceTaskStatus.Completed),
        sales = game.World.Ledger.Count(e => e.Category is "Sales.Food" or "Sales.Shop" or "Sales.Hotel"),
        saveBytes = System.Text.Encoding.UTF8.GetByteCount(snapshot), serializeMilliseconds = snapshotTimer.Elapsed.TotalMilliseconds,
        loadMilliseconds = loadTimer.Elapsed.TotalMilliseconds, continuationMatches = true
    };
    measurements.Add(measurement); Console.WriteLine(JsonSerializer.Serialize(measurement));
}
File.WriteAllText(args[0], JsonSerializer.Serialize(new {
    measuredUtc = DateTimeOffset.UtcNow, operatingSystem = RuntimeInformation.OSDescription,
    runtime = RuntimeInformation.FrameworkDescription, processor = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER"),
    logicalProcessors = Environment.ProcessorCount, harnessBuild = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
    coreBuild = typeof(GameSession).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
    coreSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(GameSession).Assembly.Location))), coordinated,
    fixture = "25 rooms:12offices/96workers,3studios,3hotels,cafe,shop,lobby,transfer-lobby,depot,utility,security; four16-seat shafts, genuine lower/upper transfers; unchanged business rules; sandbox pays all construction. One day plus1h exact resumed comparison.",
    scope = "Full authoritative mixed business/service simulation, unthrottled logical seconds. Same shaft geometry before/after; coordination changes traffic and workload. No renderer/audio, so frame time and FPS are not measured. One run per height, not statistical regression certification.",
    measurements
}, new JsonSerializerOptions { WriteIndented = true }));
