# Management slice A: finances and recurring billing

The latest management-loop request is being implemented through its ordered slices A–H. **Slice A passes 139 regression cases and the expanded Godot scene, with zero build warnings/errors.** The audit first reran the existing 121-case baseline. Construction, physical journeys, business receipts, recurring costs and saves already existed; this slice adds consistent financial classification, an explicit recurring-billing schedule and a usable financial view. It does not complete tenant contracts, service tasks, complaints or the full 20-floor management milestone.

The approved GDD and exact earlier facility catalogue remain unavailable. Existing representative facilities and balance values remain original provisional tuning in `Data/simulation.rules.json`; no loans, taxes, insurance or new financial subsystem is introduced.

## One ledger, distinct results

The construction world's append-only ledger remains the authoritative record. Entries retain stable sequence IDs, simulation timestamps, categories, signed integer minor-unit amounts, related room/entity references when applicable, reasons and resulting balances. Current-period, lifetime and historical reports derive their amounts from that ledger; opening a panel does not post a transaction.

| Flow | Classification |
| --- | --- |
| Rent, supported business sales, hotel charges, parking and advertising | Operating revenue |
| Facility upkeep, staff wages, maintenance and event preparation | Operating expense |
| Construction and condominium buyback | Capital spending |
| Room salvage and condominium sale | Capital receipts |
| Financing | Zero; no financing system is added |
| Unknown external categories | Other cash flow; never silently classified as operating profit |

Operating profit is operating revenue minus operating expenses. Net cash flow also includes capital, financing and other flows. Opening cash plus net cash flow must equal closing cash. A room purchase reduces cash without becoming an operating loss; a condominium sale increases cash without becoming operating revenue.

Reports expose revenue/expense category breakdowns, capital spending/receipts, other flows, net cash flow and opening/closing balance. A zero-cash `Billing.Settlement` ledger marker records the exact closing sequence and settlement tick. Sequence boundaries assign midnight bills to the financial period being closed, even though the simulation clock is then at the next day. Historical transactions are never rewritten by a price/staff change or report refresh.

## Provisional billing policy

Daily upkeep and salaries retain the established policy: **one full daily charge at midnight, based on the rooms and staff allocations present at that boundary**. The first session day starts at 07:55 and still has a full first bill. There is no hourly accrual or proration in this slice.

Closing a facility or leaving it understaffed does not remove its upkeep. Reducing an allocation before midnight changes that boundary's salary amount; active service workers must return before the existing command permits a reduction below their count. This is the current prototype policy, not an approved employment or accounting model.

New entries separate `Operations.FacilityUpkeep` from `Operations.Wages`. Existing combined `Operations.Upkeep` entries remain intact and are shown as combined legacy costs rather than guessed into two amounts. Before the boundary, upcoming bills are estimates from current rooms, allocations and tuning; they can change and are not already-incurred accruals.

At midnight, the due batch captures its amounts and eligible residential/advertising receipts. If a supported-range arithmetic check prevents settlement, the whole batch remains pending without a partial payment. Later allocation/price changes cannot alter that captured invoice. Pending invoices persist through saves, and later midnights capture their own obligations. Demolition of a room referenced by unsettled charges is rejected. The oldest unsettled financial period can differ from the calendar day; the interface identifies that overdue state and distinguishes captured bills from future estimates. Cash-flow periods close at actual settlement: activity occurring before a delayed recovery stays in that open period. This is cash reporting, not accrual accounting. A regression captures two overdue midnights, preserves their different staffing amounts and settles each once after save/load.

The saved billing schedule records the last settled day/tick and next due tick. Pausing, changing speed, reopening panels and saving/loading must not repeat a settled bill or skip the next one. Rent and customer/hotel charge policies remain the existing business rules in this slice; durable lease terms and completed food-service charging are later dependencies.

## Persistence

Session schema 3 adds billing, pending invoices and financial-period sequence metadata. Supported schema-2 sessions migrate without changing the historical ledger, transaction IDs or timestamps. Existing combined cost entries remain unsplit. Legacy operating totals are reclassified from that unchanged ledger, so condo ownership payments cease to count as operating profit. Imported ordinal-only history cannot establish calendar timestamps; affected views flag undated legacy history rather than claiming historical calendar reconstruction. The authoritative simulation and transport continue from the saved boundary; derived report views are rebuilt from the ledger.

## Executed checks

The complete `tools/verify.ps1` run passed **22 construction + 22 transport + 29 persistence + 22 geography + 26 simulation + 18 finance = 139 cases**, then the expanded Godot scene. The solution build reported zero warnings/errors. The current [finance test log](../artifacts/finance-tests.log) records all 18 finance outcomes; [headless-smoke.log](../artifacts/headless-smoke.log) records the integrated scene.

The finance cases cover classification and cash reconciliation, rejected transactions, pure report reads, pause/speed equivalence, once-only bills and staffing estimates, midnight save continuation, corrupt metadata, two frozen overdue invoices, schema-2 combined costs, condo reclassification, and an active hotel/service checkpoint with identical 24-hour continuation. Godot checks cover the overview, transaction view, scheduled bills, panel input isolation, paused read purity and in-game save/load immediately before a billing boundary. Facility filtering was exercised separately in the desktop review below.

The shared **14-facility/four-floor normal example** completed seven closed days without changes to prices, staffing or starting budget and without extra investment. Cumulative operating profit was **$7,677**, closing cash **$2,145,177**, with **2,167 completed trips and zero abandonments**. Ranks 2–4 were each awarded once through existing provisional rules. A separate **20-floor** version spreads the same facilities through upper floors and passed physical office demand plus exact saved continuation across two billing boundaries. These are finance acceptance fixtures using the existing business models; they do not certify the unfinished B–H requirements or final balance.

Targeted desktop review confirmed the **20-floor operating example** creates 20 floors and 14 facilities. At Day 1 08:53 with 26 people, the readable [overview](../artifacts/finance-overview.png) showed $108 operating revenue, $498,500 capital spending, −$498,392 net cash flow and $2,001,608 cash. The cafe-filtered [upcoming bills](../artifacts/finance-bills.png) showed $135: $25 upkeep plus $110 wages. Its [transaction view](../artifacts/finance-transactions.png) reported seven entries, comprising construction and six actual $18 admission charges, with stable IDs, timestamps, reasons and balances. Those charges use the existing admission policy; completed food service remains slice C.

The fresh matching-template Windows export passed with zero build warnings/errors. Its standalone executable passed the expanded finance/midnight smoke outside the editor. The complete package contains **189 files / 189,635,538 bytes**; see [EXPORT.md](EXPORT.md) and the [exported-process log](../artifacts/export-smoke-process.log). This is current export/headless evidence; the older graphical startup capture, source-copy check and all-location progression run remain historical. Full exported interactive acceptance and rendered FPS have not been established.

## Interface and manual acceptance

The new **Finances** button opens three views: **Overview**, **Transactions** and **Upcoming bills**. Choose **Open billing day**, a closed day or **All recorded history**. Transactions and upcoming bills can be filtered to a facility; transaction history remains available for demolished facilities. The overview distinguishes operating result from cash flow and includes category totals and recent closing balances. The facility operations dialog also opens its own transactions directly. Room-inspector lifetime receipts/payments are labeled as business cash totals, not operating profit.

The automated and targeted desktop checks above have run. These repeatable manual steps support broader acceptance:

1. Start the existing default office commute. Before arrivals, its paid construction should appear as $173,000 of capital spending, zero operating revenue/expense and $2,327,000 current cash.
2. Open Finances while paused. Change views, day and facility filters; inspect reasons and balances. No transaction or simulation time should change merely from inspection. Confirm a facility's maintenance expense remains associated with that facility, not an elevator with the same numeric ID.
3. Inspect the upcoming daily bill. In a staffed operating example, change an allowed allocation before midnight and reopen the estimate. It should reflect the current allocation without already posting a wage expense.
4. Save before midnight, pass the boundary, and inspect the closed day's separate upkeep and wage entries. Load the earlier save and cross the boundary again. The resumed outcome should match the uninterrupted result, without an extra or missed bill.
5. Inspect construction, salvage and any condominium ownership payments separately from operating income. Review the open billing day and the closed day's balances after crossing midnight; a later command at that same clock tick must not rewrite the closed period.

From the repository root in PowerShell:

```powershell
dotnet build VerticalDistrict.sln
dotnet run --project tests/VerticalDistrict.Finance.Tests -c Release
dotnet run --project tests/VerticalDistrict.Persistence.Tests -c Release
dotnet run --project tests/VerticalDistrict.Simulation.Tests -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
& 'I:\Program Files\Godot\Godot_v4.7.2-stable_mono_win64.exe' --path . --editor
.\artifacts\windows\VerticalDistrict.exe
```

Use the installed matching Godot .NET path and press F5, or launch the standalone executable with its complete adjacent package. In **New district**, choose **Start with a 20-floor operating example** to open the shared scenario.

## Implementation files

- Financial projections and persistence: [src/VerticalDistrict.Core/Simulation/GameSession.Finances.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Finances.cs), [src/VerticalDistrict.Core/Simulation/GameSession.FinancePersistence.cs](../src/VerticalDistrict.Core/Simulation/GameSession.FinancePersistence.cs).
- Session/billing integration: [src/VerticalDistrict.Core/Simulation/GameSession.cs](../src/VerticalDistrict.Core/Simulation/GameSession.cs), [src/VerticalDistrict.Core/Simulation/GameSession.Businesses.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Businesses.cs), [src/VerticalDistrict.Core/Simulation/GameSession.Commands.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Commands.cs), [src/VerticalDistrict.Core/Simulation/GameSession.Persistence.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Persistence.cs).
- Shared playable four-/20-floor setup: [src/VerticalDistrict.Core/Simulation/OperatingExampleScenario.cs](../src/VerticalDistrict.Core/Simulation/OperatingExampleScenario.cs).
- Provisional cost bounds: [src/VerticalDistrict.Core/Simulation/SimulationRules.cs](../src/VerticalDistrict.Core/Simulation/SimulationRules.cs).
- UI and scene checks: [Scripts/Main.Finances.cs](../Scripts/Main.Finances.cs), [Scripts/Main.Management.cs](../Scripts/Main.Management.cs), [Scripts/Main.cs](../Scripts/Main.cs).
- Finance acceptance: [tests/VerticalDistrict.Finance.Tests/Program.cs](../tests/VerticalDistrict.Finance.Tests/Program.cs), [tests/VerticalDistrict.Finance.Tests/VerticalDistrict.Finance.Tests.csproj](../tests/VerticalDistrict.Finance.Tests/VerticalDistrict.Finance.Tests.csproj). Existing persistence/simulation runners retain their regression role.
- Project verification: [VerticalDistrict.sln](../VerticalDistrict.sln), [tools/verify.ps1](../tools/verify.ps1).
- Handoff: [README.md](../README.md), [docs/FINANCES.md](FINANCES.md), [docs/PROJECT_STATUS.md](PROJECT_STATUS.md), [docs/FEATURE_MATRIX.md](FEATURE_MATRIX.md), [docs/KNOWN_ISSUES.md](KNOWN_ISSUES.md), [docs/DECISIONS.md](DECISIONS.md), [docs/NEXT_STEPS.md](NEXT_STEPS.md).

## Remaining management slices

| Slice | Existing foundation and remaining acceptance |
| --- | --- |
| B: residential/office occupancy | Physical occupants and daily office schedules exist. Durable tenant IDs, lease periods/renewal, contract occupancy separate from people inside, price-sensitive assignment and documented inaccessible-lease consequences remain incomplete. Residential departure currently clears its simple contract flag. |
| C: completed food purchases | Cafes use capacity, opening hours, staffing, price-sensitive arrivals and actual transport. They currently charge on admission; explicit service completion and queue-abandonment outcomes remain to be implemented. |
| D: hotel stays/cleaning | Reservation, arrival, one stay charge, checkout, dirty inventory and routed cleaning exist. An active-stay/service checkpoint continues identically for 24 hours. Expanded explicit readiness/task states, failure/refund policy and acceptance of those additions remain open. |
| E: staff/maintenance | Routed staff work, wages, repair and safe rejection of unsafe dismissal/demolition exist. Persistent task IDs/lifecycles and a readable pending/assigned/blocked task board remain incomplete. |
| F: satisfaction/management | Existing room warnings, condition, cleanliness and reputation affect operations. An explainable contributor model, active resolving complaints, tenant/guest counts and demand/service panels remain incomplete. Location demand is explicitly neutral metadata. |
| G: persistence/early progression | Saves and seven provisional ranks exist. The seven-day finance baseline observes rank 2 and subsequent early awards once under existing provisional requirements, independently of exclusive terminals. Persist future tenancy/tasks/complaints alongside each slice and extend their progression acceptance. |
| H: endurance/20-floor loop | Seven-day small-tower finance and a separate 20-floor physical-demand/save fixture now pass. Full management acceptance still depends on B–G: durable tenancy, completed food service, explicit tasks, complaints and meaningful poor-management recovery. Broader endurance and final balance remain open. |

The next dependency-ready implementation is persistent residential and office tenancy, extending the existing business model and billing schedule. Preserve the final 250-floor cap, seven ranks, thirteen locations and all geographic gates.
