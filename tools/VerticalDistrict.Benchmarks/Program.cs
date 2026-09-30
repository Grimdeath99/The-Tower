using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VerticalDistrict.Core;
using VerticalDistrict.Core.Persistence;
using VerticalDistrict.Core.Transport;

namespace VerticalDistrict.Benchmarks;

internal static class Program
{
    private const int Population = 300;
    private const int Ticks = 10_000;
    private const double StepTargetMilliseconds = 16.67;
    private const double PersistenceTargetMilliseconds = 1000;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true
    };

    private static int Main(string[] args)
    {
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
        if (args.Length > 2) throw new ArgumentException("Usage: benchmark [output-directory] [catalogue-path]");
        var outputDirectory = Path.GetFullPath(args.Length >= 1 ? args[0] : "artifacts");
        var catalogPath = Path.GetFullPath(args.Length >= 2 ? args[1] : "Data/construction.catalog.json");
        Directory.CreateDirectory(outputDirectory);
        var catalog = ContentCatalog.Load(File.ReadAllText(catalogPath));
        var warmup = Fixture(catalog, 20, Population);
        for (var tick = 1; tick <= 200; tick++) warmup.Transport.Step(tick);
        var measurements = new List<Measurement>();
        foreach (var floors in new[] { 20, 100, 250 })
        {
            Console.WriteLine($"Measuring {floors} above-ground floors + 10 basements, {Population} logical passengers, {Ticks} consecutive ticks...");
            var result = Measure(catalog, floors, outputDirectory);
            measurements.Add(result);
            Console.WriteLine(Describe(result));
        }
        var report = new Report(DateTimeOffset.UtcNow, RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "Not reported by environment",
            Environment.ProcessorCount,
#if DEBUG
            "Debug",
#else
            "Release",
#endif
            "Core transport only; zero rendered sprites; unthrottled one-second logical ticks.",
            StepTargetMilliseconds, PersistenceTargetMilliseconds, measurements.ToArray());
        var jsonPath = Path.Combine(outputDirectory, "performance-results.json");
        var textPath = Path.Combine(outputDirectory, "performance-results.txt");
        File.WriteAllText(jsonPath, JsonSerializer.Serialize(report, JsonOptions));
        File.WriteAllText(textPath, DescribeReport(report));
        Console.WriteLine($"Recorded results: {jsonPath}");
        return measurements.All(measurement => measurement.CompletedTicks == Ticks && measurement.ContinuationMatches) ? 0 : 1;
    }

    private static Measurement Measure(ContentCatalog catalog, int aboveGroundFloors, string outputDirectory)
    {
        var (world, transport) = Fixture(catalog, aboveGroundFloors, Population);
        var random = new Random(19_903 + aboveGroundFloors);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var managedBefore = GC.GetTotalMemory(forceFullCollection: false);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var process = Process.GetCurrentProcess();
        var workingSetBefore = process.WorkingSet64;
        var durations = new double[Ticks];
        var timer = Stopwatch.StartNew();
        var completed = 0;
        long finishedTrips = 0;
        for (var tick = 1; tick <= Ticks; tick++)
        {
            var start = Stopwatch.GetTimestamp();
            transport.Step(tick);
            durations[completed++] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (tick % 100 == 0)
            {
                foreach (var journey in transport.Journeys.Where(journey => journey.State == JourneyState.Arrived))
                {
                    finishedTrips++;
                    var destination = random.Next(ConstructionWorld.MinFloor, aboveGroundFloors);
                    if (destination == journey.Floor) destination = destination == aboveGroundFloors - 1 ? -10 : destination + 1;
                    Succeed(transport.RequestJourney(journey.PersonId, journey.Floor, journey.DestinationX,
                        destination, random.Next(0, 31), patienceTicks: 86_400));
                }
                if (timer.Elapsed.TotalSeconds > 60) break;
            }
        }
        timer.Stop();
        var simulationAllocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        var managedAfter = GC.GetTotalMemory(forceFullCollection: false);
        process.Refresh();
        var workingSetAfter = process.WorkingSet64;
        var samples = durations.Take(completed).Order().ToArray();
        var mean = samples.Average();
        var p95 = samples[(int)Math.Ceiling(samples.Length * .95) - 1];
        var max = samples[^1];

        var stamp = Stopwatch.GetTimestamp();
        var snapshot = new BenchmarkSnapshot(world.CaptureSnapshot(), transport.CaptureSnapshot());
        var captureMs = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
        stamp = Stopwatch.GetTimestamp();
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        var serializeMs = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
        var path = Path.Combine(outputDirectory, $"benchmark-{aboveGroundFloors}-{Guid.NewGuid():N}.json");
        double saveMs;
        double loadMs;
        bool continuationMatches;
        try
        {
            stamp = Stopwatch.GetTimestamp();
            SaveFileStore.Save(path, json, candidate => Validate(catalog, candidate));
            saveMs = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
            stamp = Stopwatch.GetTimestamp();
            var restored = Restore(catalog, SaveFileStore.Load(path));
            loadMs = Stopwatch.GetElapsedTime(stamp).TotalMilliseconds;
            continuationMatches = json == Serialize(restored.World, restored.Transport);
            for (var next = 0; next < 100; next++)
            {
                transport.Step(transport.CurrentTick + 1);
                restored.Transport.Step(restored.Transport.CurrentTick + 1);
            }
            continuationMatches &= Serialize(world, transport) == Serialize(restored.World, restored.Transport);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(path + ".bak")) File.Delete(path + ".bak");
        }
        var states = snapshot.Transport.Journeys.GroupBy(journey => journey.State.ToString())
            .ToDictionary(group => group.Key, group => group.Count());
        return new Measurement(aboveGroundFloors, 10, Population, 0, 1, 8, completed,
            timer.Elapsed.TotalMilliseconds, completed / timer.Elapsed.TotalSeconds, mean, p95, max,
            p95 < StepTargetMilliseconds, finishedTrips, states, managedBefore, managedAfter,
            workingSetBefore, workingSetAfter, simulationAllocated, captureMs, serializeMs, saveMs, loadMs,
            Encoding.UTF8.GetByteCount(json), captureMs < PersistenceTargetMilliseconds
                && serializeMs < PersistenceTargetMilliseconds && saveMs < PersistenceTargetMilliseconds
                && loadMs < PersistenceTargetMilliseconds,
            continuationMatches, completed == Ticks ? "All logical ticks executed." : "INCOMPLETE: exceeded the 60-second scenario budget.");
    }

    private static (ConstructionWorld World, TransportSystem Transport) Fixture(ContentCatalog catalog, int floors, int population)
    {
        var world = new ConstructionWorld(catalog, 2_000_000_000, rank: 5);
        for (var floor = -1; floor >= -10; floor--) Succeed(world.BuildFloor(floor));
        for (var floor = 1; floor < floors; floor++) Succeed(world.BuildFloor(floor));
        var transport = new TransportSystem(world);
        Succeed(transport.InstallBank(new BankDefinition(1, 31, -10, floors - 1,
            Enumerable.Range(-10, floors + 10).ToArray(), Capacity: 8)));
        for (var i = 1; i <= population; i++)
        {
            var destination = -10 + i % (floors + 10);
            if (destination == 0) destination = floors - 1;
            Succeed(transport.RequestJourney(i, 0, i % 31, destination, (i * 7) % 31, patienceTicks: 86_400));
        }
        return (world, transport);
    }

    private static string Serialize(ConstructionWorld world, TransportSystem transport)
        => JsonSerializer.Serialize(new BenchmarkSnapshot(world.CaptureSnapshot(), transport.CaptureSnapshot()), JsonOptions);

    private static bool Validate(ContentCatalog catalog, string json)
    {
        _ = Restore(catalog, json);
        return true;
    }

    private static (ConstructionWorld World, TransportSystem Transport) Restore(ContentCatalog catalog, string json)
    {
        var snapshot = JsonSerializer.Deserialize<BenchmarkSnapshot>(json, JsonOptions)
            ?? throw new SaveValidationException("Missing benchmark snapshot.");
        var world = ConstructionWorld.FromSnapshot(catalog, snapshot.Construction);
        var transport = TransportSystem.RestoreSnapshot(world, snapshot.Transport);
        return (world, transport);
    }

    private static void Succeed(CommandResult result)
    {
        if (!result.Success) throw new InvalidOperationException(result.Message);
    }

    private static string Describe(Measurement measurement)
        => $"{measurement.AboveGroundFloors}+{measurement.Basements} floors: {measurement.CompletedTicks:N0} ticks; "
            + $"step mean {measurement.StepMeanMilliseconds:F4} ms, p95 {measurement.StepP95Milliseconds:F4} ms "
            + $"(target {(measurement.MeetsStepTarget ? "PASS" : "FAIL")}); validated load {measurement.LoadValidationMilliseconds:F2} ms; "
            + $"continuation {(measurement.ContinuationMatches ? "PASS" : "FAIL")}.";

    private static string DescribeReport(Report report)
    {
        var text = new StringBuilder();
        text.AppendLine($"Vertical District core transport benchmark — {report.MeasuredAtUtc:O}");
        text.AppendLine($"{report.BuildConfiguration}; {report.OperatingSystem}; {report.Runtime}; {report.Architecture}");
        text.AppendLine($"{report.ProcessorIdentifier}; {report.LogicalProcessors} logical processors");
        text.AppendLine(report.Scope);
        text.AppendLine($"Targets declared in docs/PERFORMANCE.md: step p95 < {report.StepTargetMilliseconds:F2} ms; each persistence phase < {report.PersistenceTargetMilliseconds:F0} ms.");
        foreach (var measurement in report.Measurements)
        {
            text.AppendLine();
            text.AppendLine(Describe(measurement));
            text.AppendLine($"Total simulation harness {measurement.SimulationWallMilliseconds:F2} ms; effective {measurement.LogicalTicksPerSecond:F0} logical ticks/s; max step {measurement.StepMaxMilliseconds:F4} ms.");
            text.AppendLine($"Capture {measurement.CaptureMilliseconds:F2} ms; serialize {measurement.SerializeMilliseconds:F2} ms; validated safe save {measurement.SaveMilliseconds:F2} ms; validated load {measurement.LoadValidationMilliseconds:F2} ms; persistence target {(measurement.MeetsPersistenceTarget ? "PASS" : "FAIL")}; JSON {measurement.SnapshotBytes:N0} bytes.");
            text.AppendLine($"Managed heap {measurement.ManagedBytesBefore:N0} -> {measurement.ManagedBytesAfter:N0} bytes; working set {measurement.WorkingSetBytesBefore:N0} -> {measurement.WorkingSetBytesAfter:N0} bytes; harness allocated {measurement.SimulationAllocatedBytes:N0} bytes.");
            text.AppendLine($"Recycled completed trips {measurement.CompletedTrips}; final states: {string.Join(", ", measurement.JourneyStates.Select(pair => $"{pair.Key}={pair.Value}"))}. {measurement.Note}");
        }
        text.AppendLine();
        text.AppendLine("No graphical frame-rate or full business-simulation claim follows from these results.");
        return text.ToString();
    }
}

internal sealed record BenchmarkSnapshot(ConstructionSnapshot Construction, TransportSnapshot Transport);
internal sealed record Report(DateTimeOffset MeasuredAtUtc, string OperatingSystem, string Runtime,
    string Architecture, string ProcessorIdentifier, int LogicalProcessors, string BuildConfiguration,
    string Scope, double StepTargetMilliseconds, double PersistenceTargetMilliseconds, Measurement[] Measurements);
internal sealed record Measurement(int AboveGroundFloors, int Basements, int LogicalPassengers, int VisibleSprites,
    int ElevatorBanks, int ElevatorCapacity, int CompletedTicks, double SimulationWallMilliseconds,
    double LogicalTicksPerSecond, double StepMeanMilliseconds, double StepP95Milliseconds,
    double StepMaxMilliseconds, bool MeetsStepTarget, long CompletedTrips, Dictionary<string, int> JourneyStates,
    long ManagedBytesBefore, long ManagedBytesAfter, long WorkingSetBytesBefore, long WorkingSetBytesAfter,
    long SimulationAllocatedBytes, double CaptureMilliseconds, double SerializeMilliseconds,
    double SaveMilliseconds, double LoadValidationMilliseconds, int SnapshotBytes, bool MeetsPersistenceTarget,
    bool ContinuationMatches, string Note);
