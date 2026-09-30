# Vertical District

A single-player tower management prototype built in Godot 4 .NET. Construct a side-on district, connect rooms with physical transport, operate businesses, maintain hotel rooms, manage cash, and work toward seven provisional ranks.

The integrated prototype builds with zero warnings/errors and passes **139 automated cases plus Godot scene checks**. The current finance slice includes an unchanged-price seven-day operating baseline and a 20-floor saved-continuation fixture. **It does not complete the approved game scope.** Seventeen representative facilities are present; the approved GDD and exact 87-entry catalogue are still missing. [Project status](docs/PROJECT_STATUS.md) distinguishes executed checks, targeted desktop review and outstanding acceptance.

## Run and verify

Use Godot **4.7.2 stable .NET** and .NET SDK **10.0.301**. Projects target net8.0. From the repository root in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
```

The script locates the matching installed engine, generates an ignored local NuGet feed configuration, builds the solution, runs all six test suites, and launches the headless scene. It installs no software. To supply an engine path explicitly:

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
dotnet run --project tools/VerticalDistrict.Benchmarks -c Release
```

All test runners are dependency-free executables and return nonzero on failure. An earlier [fresh-source-copy verification](artifacts/clean-source-summary.txt) rebuilt and passed the preceding 111-case baseline with fresh local caches on the installed development machine; the current 139-case suite has not been repeated in that separate source copy. This is not a fresh-machine or Git-checkout certification. Performance results cover transport and persistence in a core-only process; [PERFORMANCE.md](docs/PERFORMANCE.md) explains why they do not establish rendered FPS.

The current [Windows executable](artifacts/windows/VerticalDistrict.exe) passed the expanded headless scene **outside the editor**, including finances and midnight save continuity. An earlier export also produced a reviewed [graphical startup capture](artifacts/export-frame00000002.png); the current finance UI and 20-floor menu example were reviewed in the editor's running game. Keep the entire `artifacts/windows` folder together, including the PCK and runtime data. The [export workflow](docs/EXPORT.md) rebuilds it with `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\export-windows.ps1`. The earlier three-frame capture is not a performance benchmark; full interactive exported acceptance and graphical FPS remain unverified.

The preceding milestone's optional [normal progression scenario](docs/PROGRESSION.md) earned all seven ranks in **13/13 locations**, using unchanged prices and requirements, costed construction, real journeys, and the normal starting budget. That extended run has not been repeated after the finance changes; current tests cover all locations' gates and ordinary office income, plus the seven-day baseline's early promotions. Run the extended scenario separately:

```powershell
dotnet run --project tests/VerticalDistrict.Progression.Tests -c Release -- --all-locations --wall-seconds 120 --days 8
```

## Play

The current management slice is [finances and recurring billing](docs/FINANCES.md). Choose **New district → Start with an operating example** for the compact four-floor tower, or **Start with a 20-floor operating example** for the same facility mix spread through a taller tower. Both pay normal construction costs and use actual movement, business activity and service staff. The final game still supports 250 above-ground floors.

Open **Finances** for operating result, capital spending, net cash flow, category breakdowns, transactions and closing balances. Filter by billing day or facility. **Upcoming bills** separates upkeep from wages; full daily allocations are captured at midnight, including the partial first day. Saved outstanding bills retain their amounts after later staffing or price changes. Select a facility and choose **View facility transactions** for its history. The complete management loop still needs persistent tenancies, completed food service, explicit task lifecycles and active complaints; see [next steps](docs/NEXT_STEPS.md).

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
| Find facilities | Catalogue search by name or category |
| Manage district | Finances / Transport / Reports / Menu buttons |
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

These are acceptance steps, not a statement that every step passed. Current finance desktop review is captured in [overview](artifacts/finance-overview.png), [facility bills](artifacts/finance-bills.png) and [transactions](artifacts/finance-transactions.png). Earlier reviews show [operating district](artifacts/desktop-operating.png), [reports](artifacts/desktop-reports.png), and [location selection](artifacts/desktop-location.png). These local artifacts do not establish full input/accessibility or performance acceptance. Current exported headless execution passes.

Architecture: `src/VerticalDistrict.Core` owns authoritative state; `Scripts` and `Scenes` provide the Godot adapter; `Data` contains original provisional definitions. See [feature matrix](docs/FEATURE_MATRIX.md), [decisions](docs/DECISIONS.md), [known issues](docs/KNOWN_ISSUES.md), and [next steps](docs/NEXT_STEPS.md).
