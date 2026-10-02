# Playable management loop

This is the preceding management-milestone handoff. The later [business-operations slice](BUSINESS_OPERATIONS.md) adds explicit condominium ownership, schema-5 migration and catalogue discovery; [PROJECT_STATUS.md](PROJECT_STATUS.md) records the latest 205-case evidence. The counts and schema-4 policies below retain their original milestone scope.

Updated 2026-10-01. **The representative management milestone is playable and verified: 173 current automated cases covered, expanded Godot acceptance, targeted graphical review and a fresh standalone Windows smoke pass.** This extends the existing finance slice through persistent tenancy, completed cafe purchases, hotel bookings, explicit service jobs, explainable demand/satisfaction and their panels. The preceding 139-case finance baseline and Godot scene were rerun successfully before editing. Verification accounting below distinguishes the full wrapper from the final focused check.

The approved GDD and exact earlier catalogue remain unavailable. All new policy below is original provisional tuning, recorded in [simulation.rules.json](../Data/simulation.rules.json) and [ManagementRules.cs](../src/VerticalDistrict.Core/Simulation/ManagementRules.cs). The 250-floor cap, seven ranks, thirteen locations and independent subway/Hawaii dock restrictions remain intact.

## What the player can operate

Start **New district → Start with an operating example** for the four-floor tower, or **Start with a 20-floor operating example** for the taller integration setup. Both use the same 14 facilities, normal $2.5m budget and costed construction. Offices, studios, a cafe, three hotel rooms and service/support rooms connect through real transport. A small tower can operate without constructing 20 floors.

Use **Finances** for the authoritative cash history and daily obligations. **Manage** opens **Tower management**, containing **Staff and services**, **Demand and satisfaction**, and **Progression**. Select a room and choose its management controls for capacity, contracted members, actual people, agreed rent/booking/order state, prices, condition, cleanliness, causes and recent room transactions. Commands validate changes; panel refreshes do not create income, people, jobs or bills.

The HUD's physical population includes service workers. Contracted office/residential members and hotel bookings are separate counts; the progression objective explicitly measures peak customers/tenants rather than service workers.

## Provisional operating policy

| System | Current policy |
| --- | --- |
| Tenant identity | Each office/residential contract has a stable tenant ID and stable member IDs. Members may leave the tower and return without ending the contract or receiving new identities. Contracted capacity is separate from people physically inside. |
| Assignment and renewal | Accessible ready offices/residences are considered at 08:00/18:00 when demand meets the configured minimum of 40. Contracts reserve actual room capacity and last seven days. Renewal considers current asking price, demand and readiness; accepted rent is fixed for that term. |
| Rent | Office rent is charged once during 09:00–18:00 after that tenancy has first achieved physical occupancy, while the room remains ready. Residential rent is a once-daily midnight obligation after first occupancy, subject to readiness. A night-time empty office or temporarily absent resident is still contracted. |
| Access/service consequences | Inaccessible or unready rooms stop eligible rent. Daily reviews place affected tenants on notice; two unresolved daily reviews request departure. Restored service clears the notice. Unaccepted renewal or manager closure/end-tenancy also requests departure. Members inside use physical routes before capacity is released. |
| Cafe orders | Arrival creates a stable order with its quoted price. Customers travel, join a queue, receive one staff service slot and spend the configured service duration. Payment occurs once on completed service, then the customer remains for its visit and leaves. Queue patience defaults to 600 seconds; abandonment or closure before completion earns no sale. Price changes affect future orders, not accepted quotes. |
| Hotel booking | A stable booking reserves one current single-capacity hotel room. The quoted nightly rate and nights are captured before travel. Actual arrival/check-in takes one payment for the stay. Check-in must meet the configured 1,200-second deadline and room readiness; cancellation before check-in charges nothing. |
| Hotel checkout | Checkout is at 11:00 after the booked number of nights. The prepaid charge is retained for completed check-in, including early manager closure; no refund subsystem is added. Dirty checkout inventory cannot be sold again until routed cleaning completes. Inventory exposes Available, Reserved, Occupied, Dirty, Cleaning, Maintenance or Unavailable. Reserved guests and claimed service jobs cannot overlap. |
| Service staff | Hired allocations incur existing daily wages, including while the depot is closed. Each dispatched worker owns a real service journey. Dismissal below the number still dispatched is rejected with an explanation; close a depot to cancel work and let workers physically return, then dismiss them. |
| Service tasks | Stable tasks progress through Pending, Assigned, Traveling, InProgress and Completed, with Blocked/Cancelled outcomes. One unresolved job per room and one claimed job per worker prevent duplicate assignments. A blocked job retains its ID for recovery. |
| Work and costs | Cleaning/maintenance begin after arrival and consume the service depot's configured work duration. Completed cleaning costs $1 in materials; repair uses the existing $250 material cost. Combined work posts both costs atomically before restoring cleanliness/condition. Insufficient funds leave the room unserviced and the job blocked. Wages/upkeep remain separate. |
| Degradation | Each game hour loses one condition point and one cleanliness point, or two cleanliness points when occupied. Service is requested below 85 or for dirty hotel inventory. These rates, thresholds, work durations, wages, staff allocations and costs remain configurable. |
| Disruption and removal | Unreachable work cannot complete remotely. An affected worker returns from its actual location when a valid route exists. A cancelled rider remains owned by the car until a safe floor. Active workers block unsafe demolition; successful demolition cancels unclaimed work. Completed/cancelled task history is bounded to 128 records. |

The earlier [financial policy](FINANCES.md) remains: integer minor-unit money, one authoritative ledger, full midnight upkeep/wage allocations, frozen unpaid bills, exact settlement markers, and operating/capital/other separation. Cleaning and repair are operating expenses. No loans, taxes or insurance are added.

## Demand, satisfaction and recovery

Room demand uses category/location modifiers, asking price relative to its reference price, cleanliness, condition, current room satisfaction and actual availability/access. The current location modifiers are explicitly neutral; the calculation does not invent economic differences between cities. Tenant assignment uses a threshold; customer and hotel demand use the saved seeded generator.

Room satisfaction combines six basic displayed factors: value, cleanliness, condition, service/access, travel/waiting, and completed activities. Security coverage adds a factor when historical peak demand exceeds the initial municipal allowance. The equal-weight target is approached by at most five points per game hour. Actual travel and queued elevator waits contribute; completed/failed activities update the outcome factor. District satisfaction is derived from room scores. The hourly trend retains 168 samples.

Warnings have stable IDs and reflect actual causes such as unavailable staff, dirty rooms, bad condition, high prices or inaccessible floors. They resolve when the cause disappears at a simulation review; resolved history is bounded to 128. The panels expose positive/negative contributors so a player can lower prices, restore access, hire staff or fund work and observe recovery. This is a provisional management model, not final difficulty balance or an advanced incident system.

## Persistence and early progression

Session schema 4 extends the existing save with tenants/member IDs, orders, bookings, tasks, room experience, complaints and trends. It preserves the original clock, random state, finances, frozen obligations and physical journeys. Supported earlier sessions migrate their implicit business/service state while retaining historical transactions and existing active travel; old prepaid food visits are not sold again. Save validation checks cross-references and uniqueness before replacing the live district. Manual saves, previous-good backups and rotating autosaves remain the existing system.

The seven-rank structure remains. **Progression** shows actual counters and unmet requirements for the next provisional objective. Rank 2 requires peak population 4, a closed-day operating profit of $100, two accessible facility types, satisfaction 45, cleanliness 30 and ten completed journeys. Exclusive terminals are unnecessary. Awards occur at the hourly progression review and remain saved; advanced reference-specific luxury/scenario requirements remain outside this milestone.

## Verification

The full verifier passed **172 cases** (22 construction, 22 transport, 36 persistence, 22 geography, 26 simulation, 18 finance and 26 management), followed by the expanded Godot scene. A final narrow legacy-hotel migration correction then passed **37/37 persistence cases**, and the solution rebuilt with zero warnings/errors. Thus **173 current cases are covered**; this is not a claim of one 173-case wrapper run. [Integrated log](../artifacts/management-verification.log), [final persistence log](../artifacts/management-persistence-tests.log), [build log](../artifacts/management-build.log), [management cases](../artifacts/management-tests.log). Executed acceptance covers:

- Persistent member identity, agreed rent, renewal/notice/departure and released capacity.
- Completed cafe payment, queued abandonment with no sale and future-price semantics.
- Exclusive hotel booking, arrival failure, dirty checkout and physical cleaning recovery.
- Stable service claims, blocked routes, real duration/costs, safe cancellation/dismissal and in-progress save continuation.
- Explained demand/complaints, bounded satisfaction recovery, pure inspection, pause/speed equivalence and once-only early progression.
- Normal seven-day operations, a separate 20-floor scenario, adverse management and a practical longer headless run with repeated validated checkpoints.

The current normal four-floor fixture completed seven closed days with **$12,444 closed-period operating profit, $2,149,940 cash, 2,059 completed trips and rank 4**, using default prices/staff/budget without extra investment. The separate **30-day / 20-floor** run passed daily save/load checkpoints, ending with **$41,120 lifetime operating profit, $2,042,620 cash, 8,879 trips, nine people, three unresolved service jobs, zero abandonment and rank 4**. These supersede the older finance-only outcomes for this rule set; profitability is not guaranteed for adverse management.

The adverse seven-day scenario loses demand and tenants and finishes with negative daily operating profit. Focused recovery tests restore sensible prices/staff/access, resolve complaints and recover blocked tasks. The early objective is earned once and retained through a boundary save; pure inspection and equivalent logical time preserve outcomes.

The final [graphical review script](../tools/review-management.ps1) passed and its four fresh captures were inspected: [staff/services](../artifacts/management-services.png), [demand](../artifacts/management-demand.png), [room contract](../artifacts/management-room.png) and [progression](../artifacts/management-progression.png). The night-time office shows eight contracted members and zero people in the room; service jobs explain hotel-checkout blocking; the first day's objective correctly waits for a closed-day profit. Actual hire/dismiss button signals restored the allocation, and paused serialization/input isolation checks passed. [Graphical review log](../artifacts/management-graphical-review.log).

The fresh Windows ExportRelease build passed with zero warnings/errors; its executable passed the expanded management scene outside the editor. The complete package contains **189 files / 189,778,466 bytes**. See [EXPORT.md](EXPORT.md) and the [exported-process log](../artifacts/export-smoke-process.log). Full manual exported play, fresh-machine/Git-checkout reproduction and rendered FPS remain unverified. Previous [finance captures](FINANCES.md), [all-location progression](PROGRESSION.md) and [core benchmarks](PERFORMANCE.md) retain their historical scope.

## Build, run and acceptance

From the repository root in PowerShell:

```powershell
dotnet build VerticalDistrict.sln
dotnet run --project tests/VerticalDistrict.Management.Tests -c Release
dotnet run --project tests/VerticalDistrict.Persistence.Tests -c Release
dotnet run --project tests/VerticalDistrict.Simulation.Tests -c Release
dotnet run --project tests/VerticalDistrict.Finance.Tests -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\review-management.ps1
& 'I:\Program Files\Godot\Godot_v4.7.2-stable_mono_win64.exe' --path . --editor
.\artifacts\windows\VerticalDistrict.exe
```

Use the matching installed Godot .NET path and press F5, or launch the current standalone executable with its whole adjacent package. The graphical review command reproduces the four management captures through real Godot controls and verifies paused state/input isolation.

1. Start the normal four-floor example. Observe workers physically arrive and a stable office tenant remain when they leave at 18:00. Inspect contracted capacity separately from the current population.
2. Change asking rent and inspect the existing agreed rent/renewal day. Raise a cafe price; accepted orders keep their quote while later demand changes. Observe a completed sale in Finances.
3. Inspect a hotel booking, checkout/dirty inventory, an assigned cleaner's route and completed work. Confirm no overlapping booking and no cleaning before physical arrival/work duration.
4. Open Manage while paused. Inspect staff counts, task reasons, demand contributors and objective progress. Refresh/filter/close panels without changing serialized state or placing objects behind them.
5. Close a depot while a worker travels. Verify cancellation and physical return; dismiss after return. Restore access/staff/funding and observe blocked tasks and complaints recover.
6. Save before a bill, during a moving elevator, an active stay and on-site work. Resume each and compare charges, IDs, deadlines, task state and progression with uninterrupted operation.
7. Run the operating baseline for seven days, then a 20-floor mixed tower. Try excessive prices and inadequate service; these must have understandable consequences rather than guaranteed profit.

## Changed implementation files

- Business state and operations: [BusinessState.cs](../src/VerticalDistrict.Core/Simulation/BusinessState.cs), [GameSession.Tenancy.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Tenancy.cs), [GameSession.Commerce.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Commerce.cs), [GameSession.Businesses.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Businesses.cs), [OperationState.cs](../src/VerticalDistrict.Core/Simulation/OperationState.cs).
- Services: [ServiceState.cs](../src/VerticalDistrict.Core/Simulation/ServiceState.cs), [GameSession.Services.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Services.cs).
- Demand and satisfaction: [SatisfactionState.cs](../src/VerticalDistrict.Core/Simulation/SatisfactionState.cs), [GameSession.Satisfaction.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Satisfaction.cs).
- Session commands, financial integration and tuning: [GameSession.cs](../src/VerticalDistrict.Core/Simulation/GameSession.cs), [GameSession.Commands.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Commands.cs), [GameSession.Finances.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Finances.cs), [ManagementRules.cs](../src/VerticalDistrict.Core/Simulation/ManagementRules.cs), [SimulationRules.cs](../src/VerticalDistrict.Core/Simulation/SimulationRules.cs), [simulation.rules.json](../Data/simulation.rules.json).
- Persistence: [GameSession.ManagementPersistence.cs](../src/VerticalDistrict.Core/Simulation/GameSession.ManagementPersistence.cs), [GameSession.Persistence.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Persistence.cs).
- Location metadata wording: [LocationCatalog.cs](../src/VerticalDistrict.Core/Geography/LocationCatalog.cs); existing geographic gates remain unchanged.
- Godot UI: [Main.ManagementLoop.cs](../Scripts/Main.ManagementLoop.cs), [Main.Management.cs](../Scripts/Main.Management.cs), [Main.Finances.cs](../Scripts/Main.Finances.cs), [Main.cs](../Scripts/Main.cs), [GameSession.Inspection.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Inspection.cs).
- Acceptance and wiring: [Management Program.cs](../tests/VerticalDistrict.Management.Tests/Program.cs), [Services.cs](../tests/VerticalDistrict.Management.Tests/Services.cs), [SatisfactionCases.cs](../tests/VerticalDistrict.Management.Tests/SatisfactionCases.cs), [Management test project](../tests/VerticalDistrict.Management.Tests/VerticalDistrict.Management.Tests.csproj), [Persistence Program.cs](../tests/VerticalDistrict.Persistence.Tests/Program.cs), [ManagementPersistenceCases.cs](../tests/VerticalDistrict.Persistence.Tests/ManagementPersistenceCases.cs), [Persistence test project](../tests/VerticalDistrict.Persistence.Tests/VerticalDistrict.Persistence.Tests.csproj), [schema-3 fixture](../tests/VerticalDistrict.Persistence.Tests/Fixtures/schema3-office-night.json), [Finance Program.cs](../tests/VerticalDistrict.Finance.Tests/Program.cs), [VerticalDistrict.sln](../VerticalDistrict.sln), [tools/verify.ps1](../tools/verify.ps1), [tools/review-management.ps1](../tools/review-management.ps1).
- Handoff: [README.md](../README.md), this file, [PROJECT_STATUS.md](PROJECT_STATUS.md), [FEATURE_MATRIX.md](FEATURE_MATRIX.md), [KNOWN_ISSUES.md](KNOWN_ISSUES.md), [NEXT_STEPS.md](NEXT_STEPS.md), [DECISIONS.md](DECISIONS.md), [EXPORT.md](EXPORT.md) and the historical note in [FINANCES.md](FINANCES.md).

No failing automated case remains in this milestone's executed acceptance. The full game still lacks the approved catalogue, advanced wings/terminals/security/waste/emergencies/luxury/scenarios, production art/audio, complete manual/accessibility acceptance and measured full-game rendering performance. The next dependency-ready implementation is a guided management scenario that teaches the verified operating loop and recovery controls, alongside broader usability and approved-scope reconciliation.
