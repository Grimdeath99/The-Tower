# Vertical District

A single-player tower management prototype built in Godot 4 .NET. Construct a side-on district, connect rooms with physical transport, operate businesses, maintain hotel rooms, manage cash, and work toward seven provisional ranks.

The [first advanced transport slice](docs/ADVANCED_TRANSPORT.md) adds coordinated cars in separate shafts, real two-bank transfers, service routes, car controls and a remaining-route overlay. It preserves the [management loop](docs/MANAGEMENT_LOOP.md), [ownership/catalogue](docs/BUSINESS_OPERATIONS.md) and [completed retail purchases](docs/RETAIL_OPERATIONS.md). **260/260 automated cases, expanded Godot acceptance, sixteen reviewed graphical captures and standalone Windows smoke pass; the solution builds with zero warnings/errors.** This is not the finished game or complete transport milestone. [Seventeen original facilities](docs/CONTENT_CATALOGUE.md) are audited; the approved full GDD/catalogue and several operating models remain unfinished. [Project status](docs/PROJECT_STATUS.md) records exact evidence and limits.

## Run and verify

Use Godot **4.7.2 stable .NET** and .NET SDK **10.0.301**. Projects target net8.0. From the repository root in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
```

The script locates the matching installed engine, generates an ignored local NuGet feed configuration, builds the solution, runs seven test suites, and launches the headless scene. It installs no software. To supply an engine path explicitly:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1 -GodotPath 'I:\Program Files\Godot\Godot_v4.7.2-stable_mono_win64.exe'
& 'I:\Program Files\Godot\Godot_v4.7.2-stable_mono_win64.exe' --path . --editor
```

Substitute your matching .NET engine path. Open the existing `project.godot` and press **F5**. Individual checks, after setup:

```powershell
dotnet build VerticalDistrict.sln
dotnet run --project tests/VerticalDistrict.Core.Tests -c Release
dotnet run --project tests/VerticalDistrict.Transport.Tests -c Release
dotnet run --project tests/VerticalDistrict.Persistence.Tests -c Release
dotnet run --project tests/VerticalDistrict.Geography.Tests -c Release
dotnet run --project tests/VerticalDistrict.Simulation.Tests -c Release
dotnet run --project tests/VerticalDistrict.Finance.Tests -c Release
dotnet run --project tests/VerticalDistrict.Management.Tests -c Release
dotnet run --project tools/VerticalDistrict.Benchmarks -c Release
```

All test runners are dependency-free executables and return nonzero on failure. The [full verifier](artifacts/advanced-transport-verification.log) passed **260 cases plus Godot**: 22 construction, 36 transport, 63 persistence, 22 geography, 28 simulation, 18 finance and 71 management. The unchanged 236-case baseline passed before this slice. An earlier [fresh-source-copy verification](artifacts/clean-source-summary.txt) passed the old 111-case baseline on the same installed machine; it is not current fresh-machine/Git-checkout certification. [Current scale measurements](docs/ADVANCED_TRANSPORT.md#verification-and-scale) cover mixed business/service transfers at 20/100/250 floors with 25 rooms and 96 scheduled workers. All six before/after saved continuations matched; the coordinated 250-floor workload still recorded four abandoned journeys. [PERFORMANCE.md](docs/PERFORMANCE.md) retains earlier core measurements. None establishes rendered FPS.

The fresh schema-7 [Windows executable](artifacts/windows/VerticalDistrict.exe) passed the expanded transfer, retail and management scene **outside the editor**. Keep all 189 files (189,940,242 bytes) in `artifacts/windows` together, including the PCK and runtime data, and launch `.\artifacts\windows\VerticalDistrict.exe`. The [export workflow](docs/EXPORT.md) rebuilds it with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\export-windows.ps1`; this slice exported successfully on its first attempt. Sixteen current management/business/retail/transport views were reviewed through real Godot controls; full interactive exported acceptance and graphical FPS remain unverified.

The preceding milestone's optional [normal progression scenario](docs/PROGRESSION.md) earned all seven ranks in **13/13 locations**, using unchanged prices and requirements, costed construction, real journeys, and the normal starting budget. That extended run has not been repeated after the finance changes; current tests cover all locations' gates and ordinary office income, plus the seven-day baseline's early promotions. Run the extended scenario separately:

```powershell
dotnet run --project tests/VerticalDistrict.Progression.Tests -c Release -- --all-locations --wall-seconds 120 --days 8
```

## Play

Choose **New district → Two-bank transfer scenario → Start transfer scenario** for the new normal-budget 20-floor fixture. Twenty-four workers use two lower-bank cars, walk across floor 9 and join the upper-bank queue for offices on floor 19. Select a person to inspect their remaining route, or select a car/shaft to configure its bank. Transport controls add separate coordinated shafts and pause/restore individual cars; actual capacity, physical walking and service permissions govern travel. [Acceptance steps and dispatch policies](docs/ADVANCED_TRANSPORT.md) explain safe edits and saved journeys.

The [business slices](docs/RETAIL_OPERATIONS.md) now include completed shop purchases, café menus, durable condominium ownership, returning households, safe buyback, and a facility guide with categories and site filters. The [content catalogue](docs/CONTENT_CATALOGUE.md) separates working models from unfinished cinema, event, advertising and infrastructure workflows. Full catalogue completion remains open.

Inspect a café or shop to preview its offering, then use **Apply offering & default price**. Accepted customers keep their original product, price and service duration. Purchases charge once when actual service completes; closing cancels unpaid work while customers return physically. The inspector separates retained recent purchase counts from lifetime operating finances. Comparable shops share finite customer demand; café lunch demand changes from 11:00 to 14:00. These are original provisional offerings, with no inventory simulation.

The [management loop](docs/MANAGEMENT_LOOP.md) extends the existing [finances and recurring billing](docs/FINANCES.md). Choose **New district → Start with an operating example** for the compact four-floor tower, or **Start with a 20-floor operating example** for the same facility mix spread through a taller tower. Both pay normal construction costs and use actual movement, business activity and service staff. The final game still supports 250 above-ground floors.

Open **Finances** for operating result, capital spending, net cash flow, category breakdowns, transactions and closing balances. Filter by billing day or facility. **Upcoming bills** separates upkeep from wages; full daily allocations are captured at midnight, including the partial first day. Saved outstanding bills retain their amounts after later staffing or price changes. Select a facility and choose **View facility transactions** for its history.

Open **Manage** for staff and service tasks, demand/satisfaction contributors and next-rank progress. Hire or dismiss at a service depot; dispatched staff must physically return before dismissal. Room controls distinguish contracted members from people inside, show fixed agreed rent and renewal, and expose actual booking/order state and complaints. Cafe purchases charge only after service; hotel checkout creates dirty inventory that requires routed work. See the [documented provisional policies and acceptance steps](docs/MANAGEMENT_LOOP.md).

The [inspectable office commute](docs/OFFICE_COMMUTE.md) remains available. Choose **New district**, scroll to **Office commute scenario**, keep **3 offices / 6 elevator seats**, and choose **Start office commute**. Confirm the reset. All construction is paid from the normal budget. Twenty-four workers arrive at 08:00, share the only elevator, occupy their offices, and begin their return at 18:00 regardless of commute duration. There are no stairs in this preset.

Use **People** to filter by ID, role or journey state, select riders or off-screen workers, and **Locate selected person**. Clicking a person, elevator car or shaft opens its live inspector. A selected bank shows hall calls, onboard IDs and requested stops; **Configure selected bank** opens the existing controls for that bank. Pause to examine queues without changing them. Inspection and camera movement do not advance the simulation.

Start from the operating example or choose **New district** for a city, fictional site, and normal/sandbox settings. Normal games begin with $2,500,000 at rank 1; sandbox starts at rank 7 with $25,000,000 and retains geographic restrictions.

Build a ground lobby, add supported floors, and reserve empty bays for shafts or stairs. An upper-floor office earns rent after workers actually arrive. A staffed cafe serves customers; a hotel reserves guests and cannot reassign dirty rooms until a worker from a service depot reaches and cleans them. Use **Transport** to configure banks, selected stops, service access, capacity and timing, or to pause/restore service. Select a room and open its operating controls to change prices, staffing and opening status. **Reports** shows daily results, traffic metrics, notices and next-rank requirements.

| Action | Control |
| --- | --- |
| Build / select / demolish | Left click with the active tool |
| Floor / inspect / demolish tool | F / V / X |
| Cancel tool | Escape or right click |
| Pause / resume | Space |
| Simulation speed | Pause / 1x / 2x / 4x buttons |
| Save / load menu | F6 / F9 |
| Pan | Middle-button drag or arrow keys |
| Zoom | Mouse wheel or - / + buttons |
| Ground / floor navigation | Home / Page Up / Page Down |
| Find facilities | Search stable ID/name/category/model; category/unlock filters; Facility guide |
| Manage district | Manage / Finances / Transport / Reports / Menu buttons |
| Inspect people, including riders | People, then select a row or use its filter |
| Inspect elevator | Click car/shaft, then Configure selected bank |

At 1x, one real second advances one game minute. Overloaded frames retain simulation work and display effective speed/backlog. Room salvage refunds half the construction cost; slabs do not refund money. Occupants and ownership contracts must be resolved before demolition.

Ground is floor 0. Above-ground floors are 0–249 and basements are -1–-10. Rank 5 unlocks the full above-ground cap. Subway construction requires both verified eligible infrastructure and a connected fictional site. Docks are optional and require a Hawaii waterfront site and normally rank 4. These restrictions remain in sandbox. Source evidence and Abu Dhabi's unresolved subway status are documented in [LOCATION_SOURCES.md](docs/LOCATION_SOURCES.md).

## Saves and settings

Manual saves, previous-save backups and three rotating autosave slots use Godot's `user://saves` directory; the Load menu displays its resolved path. Loading validates a candidate before replacing the live session and resumes paused. Backup recovery writes subsequent manual saves to `recovered.json`. Settings control autosave interval, interface volume and text size. In-game save/load passes the headless scene check; extended desktop acceptance remains pending.

## Desktop acceptance checklist

1. Launch and verify menus, readable cutaway/HUD, and city/site notes. Start a normal example and observe workers reaching offices and later leaving.
2. Try valid construction and rejected overlaps, bounds, missing support, unaffordable purchases and geographic gates. Rejections must preserve cash and state.
3. Inspect prices/staffing/open state. Close a business, wait for departure, and confirm no new sales. Check dirty hotel inventory and routed housekeeping.
4. Change stops on a stopped empty elevator, pause/restore service, and inspect real queues, passengers and warnings. Click panels while a building tool is active to check input isolation.
5. Pause, save during travel, reload, resume, and compare continued operation. Exercise backup/autosave choices and a corrupt-save rejection without replacing the live district.
6. Exercise overlays, text sizing, volume, camera and keyboard controls. Review daily financial reports and the explanation of unmet rank requirements.

These are broader acceptance steps, not a statement that every combination passed. Current captures show [staff/services](artifacts/management-services.png), [demand](artifacts/management-demand.png), [contracted occupancy](artifacts/management-room.png), [progression](artifacts/management-progression.png), [shop offerings](artifacts/retail-offering.png), [completed purchases](artifacts/retail-purchases.png), [café menus](artifacts/retail-cafe.png) and [retail cutaway](artifacts/retail-cutaway.png), plus the ownership views. Reproduce all twelve with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\review-management.ps1`; actual hire/dismiss, offering/apply and closure signals, paused snapshot purity and input isolation pass. Historical finance/general captures retain their earlier scope. Full manual/accessibility acceptance, fresh-machine reproduction and rendered FPS remain open.

Architecture: `src/VerticalDistrict.Core` owns authoritative state; `Scripts` and `Scenes` provide the Godot adapter; `Data` contains original provisional definitions. See [feature matrix](docs/FEATURE_MATRIX.md), [decisions](docs/DECISIONS.md), [known issues](docs/KNOWN_ISSUES.md), and [next steps](docs/NEXT_STEPS.md).
