# Performance measurement

Targets declared before the first benchmark run:

- Transport simulation: p95 duration below 16.67 ms per logical tick with 300 logical passengers in 20-, 100-, and 250-floor towers, each including all 10 basements.
- Snapshot capture, JSON serialization, safe disk save, and validated load are measured separately. Initial interactive target: each operation below 1,000 ms for these scenarios.
- Desktop presentation: 60 FPS at 1920×1080 remains an unverified target. A pure .NET benchmark cannot establish graphical frame rate.

Run from the repository root:

```powershell
dotnet run --project tools/VerticalDistrict.Benchmarks -c Release
```

The optional first argument is the output directory, and the optional second argument is the catalogue path. Defaults are `artifacts` and `Data/construction.catalog.json`. The harness writes `performance-results.json` and `performance-results.txt`, including operating system, .NET runtime, processor identifier, logical processor count, process architecture, build configuration, and measurement UTC time. Run a Release build on an otherwise idle machine when collecting a baseline.

Each scenario builds every contiguous slab, installs a single eight-seat elevator at column 31 with every floor served, and creates 300 logical passengers. Trips include basement and upper-floor destinations. Completed passengers receive new trips every 100 ticks. All 10,000 logical ticks run consecutively; the harness records incomplete execution if a scenario exceeds its 60-second wall-clock safety limit. A separate warmup precedes measurement. Queueing and limited elevator capacity remain active; a slow passenger queue is not mistaken for CPU load failure.

Reported step mean and p95 measure `TransportSystem.Step` only. Total scenario duration and allocated bytes also include demand recycling and inspection. Memory reports include managed heap and process working set. Visible sprites are zero because this is a core-only process. Simulation runs unthrottled with one logical second per tick; the report records effective logical ticks per wall second. These fixtures exercise transport and construction persistence; they do not represent the cost of business operations, the Godot renderer, audio, or the full production catalogue.

After the measured ticks, construction and transport snapshots are captured, serialized, saved through the safe file store, loaded, and fully validated. The restored pair must serialize identically and then match the original for 100 further consecutive ticks. This continuation check is correctness verification and is excluded from measured step times. Disk save validation includes both structural and transport restoration.

Targets are recorded as pass/fail measurements rather than brittle test assertions. An incomplete scenario or state mismatch fails the harness. The [JSON](../artifacts/performance-results.json) and [text](../artifacts/performance-results.txt) artifacts are the authoritative latest results.

The 2026-09-30 Release run completed all 30,000 measured ticks and all three 100-tick restored continuations. The environment reported .NET 8.0.28, x64 Windows build 26200, Intel Family 6 Model 170, and 22 logical processors. Observed step p95 was 0.0839 / 0.0550 / 0.0252 ms for the 20 / 100 / 250 above-ground cases; validated load was 29.27 / 30.75 / 23.72 ms. Every declared core target passed in that run. These are measurements under concurrent development activity, not an isolated hardware certification.

The taller fixtures complete fewer elevator trips because they retain the same single car and longer journeys. Lower CPU time there does not demonstrate superior scaling or acceptable passenger service. The report retains final journey states and completed-trip counts so that this capacity bottleneck stays visible. Full game and graphical profiling remain outstanding.
