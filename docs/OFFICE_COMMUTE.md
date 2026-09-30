# Inspectable office commute

This continuation preserves the existing Godot 4.7.2 .NET/C# project, transport simulation and management systems. The baseline audit reran all 111 existing regression cases and the integrated Godot scene successfully. Office journeys, capacity-limited elevators, finances and save/load already worked. The missing slice was a repeatable playable commute with individual person inspection and direct elevator selection.

## Scenario and controls

Open **Menu → New district** and find **Office commute scenario**. Choose the location/site, office count and elevator capacity, then choose **Start office commute** and confirm replacing the current district. Defaults are **three offices, 24 workers and six seats**. The scenario always uses normal rules and the normal $2.5 million starting budget; ordinary construction commands spend $173,000 and leave $2,327,000 in the default configuration.

The shared `OfficeCommuteScenario.Create` factory builds floors G–3, a ground lobby, one elevator at column 27 serving every floor, and offices on floor 3. Choosing four through six offices adds offices on floor 2. Each office currently supplies eight workers. Office count is configurable from 1–6 and seats from 1–256. There are **no stairs** or alternative vertical routes: every office commute must use the elevator. Small capacities can produce worse waits or abandonment; the all-worker acceptance result applies to the default configuration.

The clock starts at 07:55. Workers enter through the lobby at 08:00, walk to the lift, queue and board only as seats become available, walk into their assigned office, and begin returning at 18:00. Completing the return requires another elevator trip and walking to the lobby exit. The corrected office schedule uses an absolute shift deadline; a longer inbound journey no longer postpones departure. A worker arriving after the shift ends turns back without entering the office or creating rent. Rent follows the existing occupied-office contract rule, once per office per day.

- Use **V / Inspect** and click a visible person, elevator car or shaft. The inspector updates from current simulation data. Queue sprite spacing is visual only.
- Open **People** to find off-screen, overlapping or riding people. Search by ID/role and filter waiting, riding, room activity, walking or missing routes. **Locate selected person** centers the camera on that person or their car. Large lists show the first 500 matches; a narrower search reaches other IDs.
- A person shows stable ID, role, activity, destination, location, wait/patience, accumulated journey wait and any applicable scheduled action. Departure leaves an explicit unavailable-person message instead of inventing a continuing occupant.
- Select a car/shaft to see served floors, capacity, current state, occupancy, onboard IDs, requested stops, directional floor queues and oldest waits. **Configure selected bank** opens the existing Transport controls for that bank.
- **Space** pauses; the speed buttons select 1×, 2× or 4×. Inspection and camera movement do not advance authoritative simulation. F6 saves and F9 opens Load; loading resumes paused.

## Acceptance and evidence

The core and Godot adapter use the same factory; the test tower is not a second movement implementation.

| Acceptance | Executed evidence |
| --- | --- |
| Costed, structurally valid normal-game setup; configurable offices and actual access | New scenario factory regression covers one, three and six offices and rejects invalid counts. |
| More workers than seats; all workers accounted for; no stairs bypass | Default 24-worker/six-seat regression checks IDs, room/queue/car ownership and capacity every simulation tick. Every worker rides both ways; all start leaving at 18:00, with 48 completed trips and no remaining people, queue or abandonment. |
| Income depends on occupancy | The default commute earns each of its three office contracts once. A deliberately late arrival returns without admission or rent. |
| Active save/load and unsafe edits | A full moving car plus waiting queue restores and continues identically. Occupied configuration/removal is rejected without mutation. Disconnecting stops strands departing workers; restoring stops lets all 24 leave. |
| Pause and speed equivalence | The scenario produces identical authoritative state at equal simulation time under 1×/4×; pausing preserves people, cars, money and clock. |
| Inspection uses live state without changing it | Four new persistence-suite cases test repeated read purity, real wait/patience and moving positions, activity deadlines, departure cleanup, transfer stops, service destinations/return depots and save/load parity. |
| Click/list/locate and adapter integration | Expanded Godot smoke passes actual viewport person/car selection, all 24 people in the browser, riding-person filtering, selected-bank configuration, paused inspection/camera state invariance and active-travel save/load with stale selections cleared. Desktop review also exercised the scenario form, riding filter, locate, direct car selection and selected-bank configuration. |

The complete verifier passes **121 cases**: 22 construction, 22 transport, 29 persistence, 22 geography and 26 simulation, with zero build warnings/errors and the expanded `CONSTRUCTION_SMOKE_PASS` marker in [the current scene log](../artifacts/headless-smoke.log). This includes six new commute and four new inspection cases. Existing transport regressions separately cover stairs, alternative routes, direction, starvation and breakdown recovery; this scenario extends those systems.

Targeted desktop review confirmed the scenario form, all 24 people, a real six-rider/18-waiter queue, filtering to Worker #1 inside the car at floor 1.50, locating the car, selecting it directly and opening its configuration. The bank showed 6/6 passengers, an upward ground-floor call for 18 people, oldest wait nine seconds, onboard IDs 1–6 and requested floor 3. The People list retained its scroll position during refresh. Reviewed captures show [person inspection](../artifacts/commute-person-inspection.png), [riding inspection](../artifacts/commute-rider-inspection.png) and [the selected elevator](../artifacts/commute-bank-inspection.png). This is targeted acceptance, not an exhaustive desktop/accessibility check or FPS measurement.

The scheduling correction also passed the separate **13/13 normal progression rerun**. Each current location still reaches rank 7 by Day 3 01:55; see [PROGRESSION.md](PROGRESSION.md) and its [current execution log](../artifacts/commute-progression-regression.log). These 13 extended scenarios are additional to the 121 regression cases.

A fresh Windows export also passes the expanded commute/inspection smoke outside the editor, with zero `ExportRelease` build warnings/errors. Run [VerticalDistrict.exe](../artifacts/windows/VerticalDistrict.exe) with its complete `artifacts/windows` folder intact. [EXPORT.md](EXPORT.md) records packaging and [the exported-process log](../artifacts/export-smoke-process.log). Full interactive acceptance of that exported build and rendered FPS remain unverified.

From the repository root in PowerShell:

```powershell
dotnet build VerticalDistrict.sln
dotnet run --project tests/VerticalDistrict.Simulation.Tests -c Release -- "Office commute"
dotnet run --project tests/VerticalDistrict.Persistence.Tests -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
.\artifacts\windows\VerticalDistrict.exe
& 'I:\Program Files\Godot\Godot_v4.7.2-stable_mono_win64.exe' --path . --editor
```

Use the installed matching engine path and press F5 in the editor. For a manual check, pause when the lobby queue forms, inspect a worker and the bank, then resume through arrival and departure. Save while the car moves, reload, and confirm the same IDs resume their journeys. Try changing a moving occupied car's stops and verify the rejection; keep a normal save before deliberately disrupting service. These are manual steps, not claims that every step has been executed visually.

## Changed files

- New UI: [Scripts/Main.Inspection.cs](../Scripts/Main.Inspection.cs), [Scripts/TowerCanvas.Inspection.cs](../Scripts/TowerCanvas.Inspection.cs).
- UI integration: [Scripts/Main.cs](../Scripts/Main.cs), [Scripts/Main.Management.cs](../Scripts/Main.Management.cs), [Scripts/TowerCanvas.cs](../Scripts/TowerCanvas.cs), [Scripts/TransportPanel.cs](../Scripts/TransportPanel.cs).
- Scenario, inspection and shift deadline: [src/VerticalDistrict.Core/Simulation/OfficeCommuteScenario.cs](../src/VerticalDistrict.Core/Simulation/OfficeCommuteScenario.cs), [src/VerticalDistrict.Core/Simulation/GameSession.Inspection.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Inspection.cs), [src/VerticalDistrict.Core/Simulation/GameSession.Businesses.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Businesses.cs).
- Transport projections: [src/VerticalDistrict.Core/Transport/TransportModels.cs](../src/VerticalDistrict.Core/Transport/TransportModels.cs), [src/VerticalDistrict.Core/Transport/TransportSystem.cs](../src/VerticalDistrict.Core/Transport/TransportSystem.cs).
- Regression coverage: [tests/VerticalDistrict.Simulation.Tests/Program.cs](../tests/VerticalDistrict.Simulation.Tests/Program.cs), [tests/VerticalDistrict.Persistence.Tests/Program.cs](../tests/VerticalDistrict.Persistence.Tests/Program.cs).
- Handoff/evidence: [README.md](../README.md), [docs/OFFICE_COMMUTE.md](OFFICE_COMMUTE.md), [docs/PROJECT_STATUS.md](PROJECT_STATUS.md), [docs/FEATURE_MATRIX.md](FEATURE_MATRIX.md), [docs/KNOWN_ISSUES.md](KNOWN_ISSUES.md), [docs/NEXT_STEPS.md](NEXT_STEPS.md), [docs/PROGRESSION.md](PROGRESSION.md), [docs/EXPORT.md](EXPORT.md).

No save schema or transport dispatcher was replaced.

## Scope and next dependency

This is an inspectable commute milestone, not full tutorial/scenario tooling or a finished game. The existing 20-floor management systems remain in place. The next coherent slice is a guided 20-floor management scenario using those existing systems: occupancy, cash flow, a staffed service depot, hotel turnaround, readable improvement objectives and persistent objective state. Approved GDD/catalogue reconciliation and wider release acceptance remain separate gaps in [PROJECT_STATUS.md](PROJECT_STATUS.md) and [KNOWN_ISSUES.md](KNOWN_ISSUES.md).
