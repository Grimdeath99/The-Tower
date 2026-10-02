# Coordinated elevator banks and physical transfers

This is the first integrated slice of the 2026-10-02 advanced transport request. It extends the existing C# journey system, Godot scene, construction ledger and save format. It does not complete parking, subway/boat schedules, approved wings or bridges. The earlier cinema/events/advertising work remains unfinished; the latest request explicitly moves transport ahead of it.

## Play and acceptance

Choose **New district → Two-bank transfer scenario → Start transfer scenario**, then confirm the reset. The selected location/site is retained. Ordinary rank-1 funds pay for nineteen additional slabs, six rooms and three shaft/car installations. Ground is 0; the transfer is floor 9 and the three offices are on floor 19. Twenty-four workers need both elevator rides; there is no stairs bypass. Lower bank 1 has six-seat cars 1 and 2 at separate bays 27 and 28; upper bank 2 has one six-seat car at bay 25. Bay coordinates in the transport editor start at 0.

1. Run past 08:00. Select either lower-bank car or shaft and open **Configure selected bank**. Its car/shaft identity, onboard IDs and actual hall requests should agree with the cutaway. Select car 2 in the transport panel to pause and restore that car independently.
2. Pause at the transfer floor. Use **People**, choose a waiting worker and **Locate selected person**. The inspector shows the next upper-bank stop and remaining legs; the overlay follows the actual corridor and rides. A passenger must alight, walk to the boarding point, wait and board. Camera movement and inspecting panels do not advance paused state.
3. Resume until all 24 workers arrive, then through 18:00. Each office earns its daily lease once; workers return physically. Change selected stops on an empty, stopped bank to choose local or express service. Passing an unserved floor never permits boarding there.
4. On an empty stopped upper bank, enable **Service staff only**. Public room access becomes unavailable; service workers retain permission. The automated service fixture creates explicit room wear, dispatches a real repair task through both rides, charges materials once and waits for the worker's return. It does not weaken production maintenance thresholds.
5. Save while a car moves or a person waits for bank 2. Load and resume: car ownership, door/travel progress, queues and accepted business obligations must remain intact.

All controls call validated core commands. A new coordinated car requires a separate unoccupied shaft through the bank's full extent and costs the existing shaft/car construction price. Capacity, stops, travel/door timing and service policy are shared within a bank. Unsafe settings/removal fail before charging or modifying state. Primary car 1 defines the bank; remove the bank to remove that primary shaft.

## Dispatch, routes and measurements

Cars and shafts have stable IDs within each bank. One car occupies each shaft. Compatible hall requests are derived from the individual journeys, so cancelling one person cannot erase another person's request. Oldest unassigned requests are considered first, with person ID as the tie-breaker. Car selection estimates pickup time from the current travel phase, distance, onboard destinations, committed load and capacity; equal estimates choose the lower car ID. Loaded cars serve nearby pending destinations and compatible pickups; empty cars serve their oldest committed request. The existing oldest-request rule also prevents a busy terminal from continually filling an empty car while an older intermediate-floor request waits.

Assignments are committed rather than continuously reshuffled as queues change. They add a timed walk to the assigned shaft and, where needed, a walk from its landing back to the onward route. Disabling a moving car retains its riders until the next safe landing, then unloads/replans them; other cars remain available. Adding a car or restoring effective service releases stationary waiting assignments once; their positions and queue ages survive, while walking approaches and onboard riders keep their commitments. Replacing a bank at another bay also requires a real walk to its new boarding point. Ordinary rendered frames do not run a new global assignment process.

The existing versioned route cache and logical clock remain authoritative. Routes price horizontal walking, each configured ride and door time, and a fixed ten-second expected wait per ride. Actual boarding capacity and queue age are applied during traversal. This wait estimate is provisional, not a live prediction. Service workers may use public banks under the existing policy; public passengers cannot use service-only banks. Separate resident/hotel permissions and restricted corridor types await approved definitions.

Horizontal geometry is the existing continuous supported full-width slab. A shared floor connects banks because the slab provides their real walkable corridor. Independently disconnected wings, segmented corridors and bridge endpoints do not exist yet; matching a floor number in a future wing will not be sufficient. The transfer-lobby room identifies the space visually; it does not create a second entrance or duplicate demand.

Pure route diagnostics distinguish available routes, temporary unavailability, denied service access and disconnected geometry/stops. Inspections expose actual remaining legs and completed transfers (`max(0, completed elevator rides − 1)`). Waiting and riding counts are current. Average/p95 waits use the most recent 2,048 completed **boarding** waits, not whole journeys; a transfer may add another sample, and the assigned-shaft approach is included from the original hall request. Oldest unresolved queue age is displayed separately. Abandonments are lifetime journey counts. Seat utilization is cumulative occupied-car ticks divided by available-seat ticks. Current pressure flags a floor when any bank's waiting queue there exceeds that same bank's available fleet seats. This is not a measured sustained arrival/service rate or a promise that overloaded demand has bounded waits.

Facility controls also explain failed public/service access using the same pure diagnostic query; a missing open ground lobby is reported separately from restricted or unavailable elevators.

## Persistence

Session schema 7 embeds transport schema 2. A bank saves its primary car and every additional shaft's car, each with its own movement/door phase, outage flag and passenger list. Journeys save assigned cars, explicit segments, progress, queue age and completed rides. Schemas 2–6 remain supported: old single-car banks map to car 1 without changing cash, ledger, clocks, random state, riders or physical routes. Genuine pre-change schema-6 fixtures cover first-bank riders and a transfer-floor queue with a second-bank rider; both passed the original loader before capture. Existing content and rules fingerprints are unchanged.

Current saves require the new fields and matching versions. Duplicate passenger ownership, overlapping shafts, invalid assignments/timers and malformed candidates are rejected before replacing the live session or last-good files. Paused stale routes after construction remain recoverable and replan at the logical simulation boundary. [Fixture provenance](../tests/VerticalDistrict.Persistence.Tests/Fixtures/README.md) records the frozen old core and captures.

## Verification and scale

The untouched baseline passed all 236 cases and the Godot scene before expansion; see [baseline log](../artifacts/advanced-transport-baseline.log). The [full verifier](../artifacts/advanced-transport-verification.log) then passed **260/260**: 22 construction, 36 transport, 63 persistence, 22 geography, 28 simulation, 18 finance and 71 management, followed by `RETAIL_SMOKE_PASS`, `TRANSFER_SMOKE_PASS` and `CONSTRUCTION_SMOKE_PASS`. The [final solution build](../artifacts/advanced-transport-build.log) has zero warnings/errors. The known offline certificate-store startup warning is the only accepted engine diagnostic.

The [final graphical run](../artifacts/advanced-transport-graphical-review.log) regenerated sixteen captures after control cleanup. The twelve established management/business/retail views and four transport views were inspected; no blocking layout regression remained. The new views show [car 2 controls](../artifacts/transport-bank.png), the [real transfer queue](../artifacts/transport-transfer.png), [remaining route inspection](../artifacts/transport-route.png) and an [actual staff member in the restricted upper bank](../artifacts/transport-service.png). Known-invalid busy-car removal/settings controls disable, while core validation still governs every command. These are targeted graphical checks, not exhaustive interactive or FPS certification.

Final audit also reproduced an assigned-approach bug: a capacity change during the walk could discard its commitment/queue age at the shaft. The [new regression failed before the fix](../artifacts/advanced-transport-approach-before-fix.log), then passed for both added and restored cars through actual boarding and exact saved continuation. The final 260-case wrapper, scale run, graphical smoke and export were repeated after correcting that handoff.

The [Windows export workflow](../artifacts/advanced-transport-export-workflow.log) passed on its first attempt. The fresh schema-7 package contains **189 files / 189,940,242 bytes**; keep the full `artifacts/windows` folder together. ExportRelease built with zero warnings/errors and the executable reported all three smoke markers outside the editor. Normal dependency restore passed afterward. See [EXPORT.md](EXPORT.md) and the [standalone log](../artifacts/export-smoke-process.log).

From the repository root, using the pinned Godot 4.7.2 .NET engine and SDK 10.0.301:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/verify.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/review-management.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools/export-windows.ps1
dotnet restore VerticalDistrict.Game.csproj
& .\artifacts\windows\VerticalDistrict.exe
```

The matched scale harness uses 20, 100 and 250 floors, 25 rooms, twelve offices/96 scheduled workers, three studios, three hotels, café/shop demand and actual service tasks. Four sixteen-seat physical shafts require lower/upper transfers. The before build has four independent single-car banks; the after build coordinates the same shafts into two two-car banks. Sandbox unlocks height but pays construction and preserves geographic rules. Each height runs 86,400 logical seconds unthrottled, then compares an uninterrupted and restored hour exactly. This is one run per height, not a statistical regression study. Coordination changes actual traffic and workload, so timings are not an isolated algorithm-speed comparison.

Both use a Release harness with the actual Debug core identity recorded in JSON. Hardware/runtime/build hashes, tick mean/p95/max, wall time, allocations, memory, sampled populations/queues, completed services/trips, abandonment and save/load durations are retained in [before JSON](../artifacts/advanced-transport-performance-before.json) and [after JSON](../artifacts/advanced-transport-performance-after.json). These runs exclude rendering/audio; frame time, graphical FPS and the full 250-floor production workload remain unverified.

Measured 2026-10-02 on Windows build 26200, .NET 8.0.28, Intel Core Ultra 9 185H / 22 logical processors. The timed/sample window is the first 24 game hours; final trip, abandonment, service and sale counters include the additional one-hour continuation check (25 total). Completed-service counts are retained completed tasks, not an unbounded lifetime counter. Peaks are sampled once per game minute and may miss shorter bursts.

| Floors | Mean tick ms before → after | p95 tick ms before → after | Peak queued before → after | Completed trips before → after | Abandoned journeys before → after |
| --- | --- | --- | --- | --- | --- |
| 20 | 0.07920 → 0.04083 | 0.2078 → 0.1091 | 102 → 48 | 669 → 669 | 0 → 0 |
| 100 | 0.05503 → 0.04028 | 0.1440 → 0.0689 | 97 → 80 | 645 → 664 | 16 → 0 |
| 250 | 0.03484 → 0.04071 | 0.0507 → 0.0730 | 81 → 96 | 456 → 653 | 123 → 4 |

| Current coordinated run | 20 floors | 100 floors | 250 floors |
| --- | --- | --- | --- |
| Timed wall seconds / maximum tick ms | 3.580 / 7.149 | 3.518 / 3.414 | 3.555 / 3.566 |
| Peak physical people / service workers | 108 / 2 | 109 / 2 | 110 / 2 |
| Process working set MB / managed heap after MB | 72.48 / 5.50 | 82.46 / 16.89 | 81.34 / 15.18 |
| Cumulative thread allocations GB | 2.399 | 2.419 | 2.492 |
| Saved UTF-8 bytes | 242,351 | 266,620 | 312,837 |
| Serialize / validated load ms | 137.48 / 117.81 | 3.49 / 32.87 | 1.28 / 15.97 |
| Retained completed service tasks / sale receipts | 70 / 120 | 68 / 118 | 66 / 114 |
| Immediate restore and resumed hour exact | Yes | Yes | Yes |

All **six** before/after resumed comparisons matched exactly. The first height includes cold serialization/validation initialization; do not read its save/load time as a steady-state height regression. Allocations rose from 1.765/1.806/1.510 GB before; those are cumulative allocations over a simulated day, not resident memory. Coordination serves substantially more of the tall-tower workload, and four abandoned journeys still occur at 250 floors under unchanged patience. These observations neither promise starvation bounds for excess demand nor establish a timing regression threshold from one non-isolated run. Assignment scans and route-view allocations are candidates for profiling if larger demand becomes the next bottleneck.

```powershell
# Frozen pre-change core and data, retained locally for this comparison:
dotnet run --project tools/VerticalDistrict.TransferBenchmarks -c Release -p:CoreAssembly=C:/Users/alexa/Documents/the-tower/.build/schema6-transport-baseline/VerticalDistrict.Core.dll -- artifacts/advanced-transport-performance-before.json .build/schema6-transport-baseline
# Current core, after the normal solution build:
dotnet run --project tools/VerticalDistrict.TransferBenchmarks -c Release -p:Coordinated=true -p:CoreAssembly=C:/Users/alexa/Documents/the-tower/src/VerticalDistrict.Core/bin/Debug/net8.0/VerticalDistrict.Core.dll -- artifacts/advanced-transport-performance-after.json Data
```

## Changed files and remaining dependencies

Core changes under `src/VerticalDistrict.Core` are in `Transport/TransportModels.cs`, `TransportSystem.cs`, new `TransportBanks.cs`, `TransportPersistence.cs`, `Simulation/GameSession.Persistence.cs`, new `GameSession.TransportPersistence.cs`, `GameSession.Inspection.cs` and new `TransferScenario.cs`. Presentation changes under `Scripts` are in `TransportPanel.cs`, `Main.cs`, `Main.Management.cs`, `Main.ManagementLoop.cs`, `Main.Inspection.cs`, new `Main.Transfers.cs`, `TowerCanvas.cs` and `TowerCanvas.Inspection.cs`. New focused transport/persistence/simulation cases and old-format fixtures exercise the behavior; historical synthetic snapshots in Finance/Persistence tests omit newer metadata. `tools/review-management.ps1` adds four transport captures; `tools/VerticalDistrict.TransferBenchmarks` provides the scale workload. The required status/catalogue/feature/issue/next-step/location documents, README, decisions and export handoff describe this slice. Existing unrelated workspace changes are retained. SHA256 comparison confirms all three production Data files are byte-for-byte unchanged from the frozen pre-transport baseline.

All thirteen profiles keep their production site/rank restrictions. The [location transport matrix](LOCATION_TRANSPORT_MATRIX.md) separates the 2026-09-30 release snapshot from the 2026-10-02 official-source audit: ten qualifying networks corroborated, Las Vegas/Honolulu exclusions retained, Abu Dhabi unresolved and disabled. No live web dependency or invented local economic tuning was added. Parking has no vehicle-space lifecycle yet; existing subway/dock prototypes lack authoritative schedules, terminal capacities and outbound trips. Hawaii-only waterfront docks remain optional and gated.

Next: complete safe stairs/escalator mutation and occupied-connection persistence through this same journey system, then approved connector geometry/access classes, vehicle parking and bounded eligible terminal schedules. Full progression/scenarios/incidents follows reliable transport. Approved GDD/connector/catalogue definitions, deeper balance, production assets, fresh-machine reproduction and exhaustive interactive exported acceptance remain open.
