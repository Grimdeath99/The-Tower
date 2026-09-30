# Decisions and assumptions

The user's current request authorizes continuing implementation through management slices A–H, with slice A selected first. The supplied master source is [Vertical_District_Master_Coding_Prompt.md](Vertical_District_Master_Coding_Prompt.md); its sections 9–18 describe requirements and continuation rules, while its phases are numbered 0–8. Missing GDD material is a source gap, not a reason to stop independent work.

| ID | Decision | Basis and consequence |
| --- | --- | --- |
| D01 | Extend the existing root Godot project and retain its addon. | Preserve repository work; no second game project. |
| D02 | Godot 4.7.2 .NET, SDK 10.0.301, net8.0, Compatibility rendering. | Installed engine and local binding evidence; versions pinned in project/global files. |
| D03 | Authoritative construction, transport, money and simulation remain plain C#. | Godot draws state and issues commands; rendering cadence must not change outcomes. |
| D04 | Ground 0; above-ground 0–249; basements -1–-10; width 32 bays. | Confirmed prompt limits. Floor caps count ground. |
| D05 | Provisional rank caps: 20, 40, 80, 150, 250, 250, 250. | Rank 5 full-height unlock is confirmed; intermediate values and names remain original draft tuning. |
| D06 | Use 17 representative original prototype facilities. | No approved GDD, edition, or exact earlier 87-entry list exists in this workspace. Do not invent reference parity or fill a target count. |
| D07 | Typed JSON separates construction, business tuning, and geography. | Stable IDs; immutable catalogue definitions; separate runtime room/person/transport state. |
| D08 | Integer minor-unit cash and an append-only signed ledger. | Checked arithmetic and atomic commands; replay validates balances and structural history. |
| D09 | Fixed logical seconds with seeded RNG and stable update order. | Default 60 game seconds per real second; pause/1x/2x/4x; cap work per frame and retain backlog rather than dropping simulated time. Starts Day 1 at 07:55. |
| D10 | Implicit horizontal corridors coexist with room footprints. | Slabs support rooms; shafts/stairs reserve bays. One car per shaft, independent banks and transfers; no overlapping multi-car shaft simulation. |
| D11 | People must reach destinations before applicable admission revenue. | Offices pay a daily occupied lease; hotels reserve before check-in and require routed cleaning after checkout. Owned condos retain their sale price for buyback. |
| D12 | Municipal utilities initially support 16 rooms; staffed accessible plants add capacity. | Original prototype coverage rule, not GDD or real-world engineering data. |
| D13 | Geography snapshot is dated and local. | Network eligibility and fictional parcel connectivity are independent. Unknown does not grant access; see LOCATION_SOURCES.md. |
| D14 | Docks require Hawaii + waterfront + draft rank 4 and are optional. | No universal dock objective. Sandbox never bypasses location or site restrictions. |
| D15 | Seven-rank requirements use mixed provisional thresholds. | Population, profit, diversity, satisfaction, cleanliness, hotel quality and completed trips. The preceding commute build passed an unchanged-rule normal-budget route for all 13 central/inland profiles; current finance acceptance observes ranks 2–4. Thresholds remain original prototype tuning, not approved-GDD or final difficulty balance. |
| D16 | Embedded construction schema 2 retains an explicit clock boundary and imports construction schema 1. | Stable IDs, strict validated restoration and content fingerprints; preserve legacy timestamps and rebuild caches/presentation rather than serialize Godot nodes. Session schema evolves separately for finance in D24. |
| D17 | Same-directory flushed file replacement, previous-good backup, three autosave slots. | Validate before changing files; invalid loads must not replace the active session. |
| D18 | Procedural original cutaway art and replaceable synthesized audio are prototype assets. | Do not describe them as finished production assets. |
| D19 | Dependency-free executable .NET test projects. | Six deterministic runners, including finance, return a failing exit code on any failed case; Godot smoke covers adapter integration separately. |
| D20 | Benchmarks distinguish transport-core timing from whole-game performance. | Core timing with zero visible sprites cannot establish 60 FPS at 1080p. |
| D21 | The latest management request is implemented in slices A–H, starting with financial consistency and recurring billing. | Existing business primitives are extended; one verified finance slice does not complete durable tenancy, food service, tasks, complaints or seven-day management acceptance. |
| D22 | Financial reports distinguish operating, capital, financing and other cash flows from the sole ledger. | Condo sales/buybacks are capital; construction is not an operating loss. Unknown external categories are other cash flow. Financing remains zero because no such system is introduced. |
| D23 | Preserve the provisional full daily midnight bill using rooms/staff present at the boundary. | No proration/accrual; closure does not remove upkeep. New upkeep and wage entries are separate; old combined entries remain unchanged. Estimates become frozen due obligations at midnight, retaining their amounts through overflow/retry and save/load. |
| D24 | Session schema 3 adds billing, frozen pending invoices and financial-period boundaries with schema-2 migration. | Preserve historical ledger IDs/timestamps; zero-cash settlement markers validate new period boundaries. Retain legacy combined expenses without fabricating a split, and disclose undated ordinal history. Implementation acceptance is tracked in FINANCES.md. |

## Provisional values and limits

Normal starting cash is 250,000,000 minor units ($2,500,000); sandbox starts at 2,500,000,000 minor units and rank 7. Full-width slabs cost 500,000 minor units. Ground is free and permanent. Room demolition returns half its construction price, rounded down; slabs return no salvage. The example tower pays through ordinary commands.

Business prices, staffing, costs, timing and mixed promotion thresholds are in `Data/simulation.rules.json`. No borrowing, taxes, insurance, unavoidable catastrophe, or reference-game economic claim is added without approved scope.

Live-session construction and operating ledger entries use the adopted logical simulation clock. Standalone construction and migrated historical entries retain their original command-ordinal timestamps. Construction schema 2 records the clock-adoption boundary so reports can label legacy entries correctly. Global `Sequence` remains the unambiguous transaction order; migration never invents timestamps for old history.

Location demand modifiers are neutral placeholders with explicit provisional status; they do not claim economic differences between real cities. Hawaii's fictional sites mean Honolulu on Oahu. Abu Dhabi remains unverified for subway eligibility; planned rail, rail-less ART buses, Las Vegas Monorail/road Loop, and Honolulu Skyline alone do not qualify.

Production asset sourcing, the complete reference catalogue, approved luxury/service/scenario rules, and broader final balance remain unresolved. Current implementation is an original prototype extending the approved constraints; its tested normal progression route is documented separately.
