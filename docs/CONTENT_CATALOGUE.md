# Content catalogue and operating coverage

Updated 2026-10-02. The workspace still contains **17 facility definitions**, plus four original provisional food/shop offerings. The latest request adds coordinated bank/transfer infrastructure through the existing journey system without new facility IDs. Current **260-case, Godot, targeted graphical and standalone export checks passed**; scale results and limits are recorded in [PROJECT_STATUS.md](PROJECT_STATUS.md). The prior retail slice's 236-case acceptance remains historical. The approved complete facility catalogue remains unavailable.

## Sources and classification

The source order is the latest user request, an approved GDD if available, established project conventions, then the [master prompt](Vertical_District_Master_Coding_Prompt.md). The current continuation follows **ADVANCED TRANSPORT AND LOCATION-SPECIFIC INFRASTRUCTURE**, preserving the earlier business request's catalogue/ownership and food/retail work. The master prompt's sections 6–7, 9–11 and 18 specify construction, transport, distinct operations, support systems and complete-scope acceptance.

| Source class | Current finding |
| --- | --- |
| Approved Vertical District requirements | The master prompt and latest request establish system requirements, thirteen locations, seven ranks, final floor limits and geographic restrictions. They do not supply an approved complete per-facility inventory or all business policies. |
| Original representative content | Sixteen entries below use original representative definitions. Their names, capacities, prices, timing and detailed operating policies remain provisional. **O** in the inventory identifies this class. |
| Original addition | The double-height atrium is explicitly labelled an original addition. **OA** identifies it; no reference-game provenance is claimed. |
| Verified reference-game content | No entries have verified Tower II/Yoot Tower edition provenance. The broad similarity of an office, hotel or cinema is insufficient evidence. |
| Edition-specific/regional reference content | No edition has been selected and no authoritative edition inventory is available. Nothing is asserted as verified regional or expansion content. |
| Unresolved source | The approved GDD and exact earlier 87-facility draft are absent. Missing names, variants, ranks and dependencies remain unknown; no filler entries are added to reach 87. |

Actual definitions live in [construction.catalog.json](../Data/construction.catalog.json), runtime balance in [simulation.rules.json](../Data/simulation.rules.json), and independent geography in [locations.json](../Data/locations.json). **Slice C adds only the Products list to rules data; every previous value and property order is preserved.** No facility ID, construction price, base operating rule, rank or location snapshot changes. The existing `PlannedOperations` type and `Prototype` metadata status remain for compatibility; the behavior table distinguishes verified lifecycles from earlier prototypes.

## Inventory

Costs below are original provisional USD construction costs. `W×H` counts bays and floors. Every definition has catalogue minimum rank **1**; a dock additionally requires location rank **4**. Subway's separate geography minimum is **1**, not 4. Rank does not bypass network or site eligibility.

**Standard site** means any of the thirteen locations on a structurally valid footprint; functioning businesses additionally require real entrance access, utilities, staffing, condition and cleanliness. A supported definition is not a promise that every room placement is operational.

| ID | Display name / category | Model / footprint / cost | Source requirement | Effective rank and site restriction | Art / audio |
| --- | --- | --- | --- | --- | --- |
| `lobby` | Entrance Lobby / Circulation | Public; 6×1; $12,000 | O; MP §§6–7 | 1; only an open ground-floor lobby acts as the simulation entrance. Other-floor placement currently remains legal but does not supply an entrance. | A1 / U |
| `office` | Office / Workplaces | Office; 5×1; $22,000 | O; MP §§7,10 | 1; standard site | A1 / U |
| `studio` | Studio Apartment / Homes | Home; 4×1; $16,000 | O; MP §§7,10 | 1; standard site | A1 / U |
| `cafe` | Corner Cafe / Food | Food; 4×1; $18,000 | O; MP §§7,10 | 1; standard site | A1 / U |
| `hotel-room` | Hotel Room / Hospitality | Hotel; 3×1; $20,000 | O; MP §§7,10–11 | 1; standard site | A1 / U |
| `service-room` | Service Room / Services | Service; 3×1; $10,000 | O; MP §§7,11 | 1; standard site; service-permitted physical routes required | A1 / U |
| `atrium` | Double-height Atrium / Circulation | Public; 6×2; $30,000 | OA; MP §§6–7 | 1; standard site; two supported floor slabs | A2 / U |
| `shop` | Corner Shop / Shopping | Shop; 4×1; $17,000 | O; MP §§7,10 | 1; standard site | A1 / U |
| `cinema` | Screening Room / Entertainment | Cinema; 8×1; $50,000 | O; MP §§7,10 | 1; standard site | A2 / U |
| `event-hall` | Event Hall / Events | Event; 8×1; $65,000 | O; MP §§7,10 | 1; standard site | A2 / U |
| `condo` | Condominium / Homes | Condo; 5×1; $40,000 | O; MP §§7,10; latest request §4 | 1; standard site | A1 / U |
| `billboard` | Billboard Studio / Advertising | Advertising; 3×1; $25,000 | O; MP §§7,10 | 1; standard site | A2 / U |
| `parking` | Parking Garage / Parking | Parking; 8×1; $50,000 | O; MP §§7,10 | 1; basements only | A2 / U |
| `utility-room` | Utility Plant / Utilities | Utility; 4×1; $25,000 | O; MP §§7,11 | 1; standard site | A2 / U |
| `security-room` | Security Office / Services | Security; 3×1; $18,000 | O; MP §§7,11 | 1; standard site | A2 / U |
| `subway` | Subway Concourse / Arrivals | Terminal; 8×1; $150,000 | O; MP §§3,7,11 | 1; basement plus qualifying operating subway network **and** connected fictional site | A2 / U |
| `dock` | Boat Terminal / Arrivals | Terminal; 8×1; $120,000 | O; MP §§3,7,11 | 4; ground floor, **Hawaii only**, compatible waterfront site; optional | A2 / U |

**A1**: original code-drawn placeholder furnishings specific to this ID (bed furnishings shared by studio/hotel; condo sleeping alcove/sofa/kitchenette; shop shelving/checkout counter). **A2**: generic original code-drawn placeholder furnishings. All entries use colored cutaway walls/windows/labels; none has completed production pixel art. **U**: shared synthesized interface cues only; no facility-specific production audio. Source: [TowerCanvas.cs](../Scripts/TowerCanvas.cs) and [Main.Management.cs](../Scripts/Main.Management.cs). There is no per-facility asset manifest to validate yet, and no claim of licensed/reference-game art.

Subway eligibility is pinned to the **2026-09-30** source snapshot, independently of fictional parcels. Ten locations qualify through actual underground passenger rail; Las Vegas and Hawaii/Honolulu do not, and Abu Dhabi is Unknown/disabled. Only hypothetical connected transit parcels qualify; a citywide network does not grant every site access. Waterfront elsewhere never grants a dock. [LOCATION_SOURCES.md](LOCATION_SOURCES.md) retains the old snapshot; the [2026-10-02 source audit](LOCATION_TRANSPORT_MATRIX.md) corroborates classifications, records remaining uncertainty and corrects the Shanghai research basis without changing release data.

## Actual operation, dependencies and evidence

All entries share structural validation, a paid construction ledger, saved room operation state and live management inspection. `Capacity / staff / hours` below comes from runtime rules; utility/security capacities describe coverage, not people. Opening hours describe new activity; existing leases/ownership and already accepted activities have their separately documented schedules.

Evidence abbreviations: **C** = [construction cases](../tests/VerticalDistrict.Core.Tests/Program.cs); **S** = [simulation cases](../tests/VerticalDistrict.Simulation.Tests/Program.cs); **M** = [management business cases](../tests/VerticalDistrict.Management.Tests/Program.cs); **SV** = [service cases](../tests/VerticalDistrict.Management.Tests/Services.cs); **D** = [demand/endurance cases](../tests/VerticalDistrict.Management.Tests/SatisfactionCases.cs); **F** = [finance cases](../tests/VerticalDistrict.Finance.Tests/Program.cs); **P** = [management persistence cases](../tests/VerticalDistrict.Persistence.Tests/ManagementPersistenceCases.cs); **G** = [geography cases](../tests/VerticalDistrict.Geography.Tests/Program.cs). The new **K** [catalogue cases](../tests/VerticalDistrict.Management.Tests/CatalogueCases.cs) add all-entry placement/closed-operation/continuation coverage; all eleven passed in the current integrated test run. A shared test is not proof of an untested full business workflow.

| ID | Capacity / staff / hours | Actual supporting systems and operating coverage | Tests and remaining acceptance |
| --- | --- | --- | --- |
| `lobby` | 0 / 0 / 00–24 | **Verified entrance dependency:** open ground lobby connects actual people to rooms and exit. No rent or customer business. | C/S/M commute/access failures. K foundation passed. Explicit non-ground-lobby placement policy still provisional. |
| `office` | 8 / 0 / 08–18 | **Verified representative tenancy:** stable tenant/member identities, agreed rent, recurring commute, renewal/notice/physical departure; needs demand, access and ongoing services. | M/P lease identity, price commitment, inaccessible/departing contracts; S commute; F/D billing/endurance. More approved office variants unresolved. |
| `studio` | 2 / 0 / 00–24 | **Verified representative tenancy:** stable household, evening returns/daytime absence, agreed midnight rent and renewal/departure; same physical/service dependencies. | M/P identity and agreed rent; F/D multi-day continuation. Approved residential variants unresolved. |
| `cafe` | 8 / 2 / 08–22 | **Verified completed-service/menu model:** two offerings, accepted product/price/duration, real arrival/queue, staff service, one completion receipt and abandonment without sale. Competes for finite Food demand with an explicit lunch modifier. | Existing M/P/S coverage and slice-C product, retail, migration and Godot checks passed in the 236-case run; targeted graphical and standalone export smoke passed. Further approved food variants unresolved. |
| `hotel-room` | 1 / 0 / 14–23 | **Verified basic lodging:** exclusive quoted reservation, check-in payment, retained stay, checkout/dirty room, routed cleaning; reserved guests and claimed service work cannot share inventory. | M/P/S/SV reservation failure, active stay, dirty/clean cycle, service race. Suites, quality tiers, VIP/luxury rules require additional approved content/policy. |
| `service-room` | 2 / 2 / 00–24 | **Verified routed services:** stable tasks, staff ownership, service-compatible path, work deadline, atomic cleaning/repair cost, cancellation/recovery and bounded history. | Nine SV cases plus P/D endurance and UI staff controls in the prior milestone. This is not waste/fire/security response. |
| `atrium` | 0 / 0 / 00–24 | **Verified structural shell:** reserves two floors and incurs upkeep. No implemented prestige, social demand, light or environmental benefit. | C multi-floor collisions/support; K closed-operation passed. Do not count the shell as a complete prestige business. |
| `shop` | 6 / 1 / 10–22 | **Verified completed-purchase model:** two offerings, frozen accepted terms, physical arrival, queue/service slots, real work time and one receipt only after completion. Shared Shop demand, closure/patience/route consequences and continued physical departure. | Fifteen retail cases plus schema-6 persistence, actual Godot controls, targeted graphical review and standalone export smoke passed, including quotes, failures, competition, terminal tourists, identity exhaustion and mixed saved continuation. No stock/delivery simulation. |
| `cinema` | 16 / 2 / 12–24 | **Earlier prototype:** two-hour audience batches, real journeys, capacity and admission charge; three program values change duration to 120/90/60 minutes. No durable screening/ticket entity or committed program/schedule. | Shared construction/save coverage; K foundation passed. Focused lifecycle tests, future schedules, late/cancel/refund policy and audience outcomes remain unfinished. |
| `event-hall` | 24 / 2 / 08–24 | **Earlier prototype:** manual preparation expense, scheduled start flag and actual guest admission. No durable event identity, contracted booking, cleanup/reuse or cancellation policy. | S preparation charged once/actual guest receipts; K foundation passed. Full event lifecycle remains unfinished. |
| `condo` | 2 / 0 / 00–24 | **Verified core ownership model:** explicit quoted pending sale, ownership/owner/resident IDs, ledger-linked purchase, repeat physical household schedules, original-price reacquisition and evacuation before reuse. No office lease or invented recurring owner charges. | Eleven [ownership cases](../tests/VerticalDistrict.Management.Tests/OwnershipCases.cs) and [schema-5 migration/corruption cases](../tests/VerticalDistrict.Persistence.Tests/OwnershipPersistenceCases.cs) passed in the preceding 205-case baseline. Earlier scoped graphical/export evidence remains in [BUSINESS_OPERATIONS.md](BUSINESS_OPERATIONS.md). |
| `billboard` | 0 / 0 / 00–24 | **Earlier prototype:** eligible ready room receives a midnight exposure payment when historical peak population is at least four. The `Advertising.Contract` ledger label is not an advertiser/campaign entity. | F ledger/billing machinery and K foundation passed. No accepted advertiser, campaign term, expiry/renewal or advertising-spend model yet. |
| `parking` | 12 / 1 / 06–24 | **Earlier prototype with infrastructure gap:** basement placement, arriving Driver actors and a fee when a completed visitor begins departure. Uses pedestrian routes; no explicit vehicles, road entrance/parking reservations or traffic capacity. | S basement/route gates; K foundation passed. Full parking acceptance awaits a defined vehicle/access model. |
| `utility-room` | 40 coverage / 1 / 00–24 | **Verified abstract support:** initial municipal allowance serves 16 rooms; accessible open staffed plants add room coverage. Power/Water remain combined, not independent networks. | S capacity and staff changes alter availability; D adverse operations. Separate utilities, breakdowns and service networks remain broader scope. |
| `security-room` | 40 coverage / 1 / 00–24 | **Implemented abstract support:** accessible open staffed coverage offsets the displayed security shortage factor after historical peak exceeds 16. No dispatched guard or incident lifecycle. | D shared demand/complaint/endurance and K foundation passed; dedicated incidents/security response not implemented. |
| `subway` | 20 batch bound / 2 / 06–24 | **Routed-arrival prototype, complete infrastructure blocked:** permitted concourse can originate tourists toward available destinations through the existing graph. No railway/platform occupancy, station transfer or detailed arrival model. | G and S eligibility/placement restrictions; K permitted and rejected placement passed. Do not treat the geography validator or tourist batch as a finished subway system. |
| `dock` | 12 batch bound / 2 / 08–20 | **Routed-arrival prototype, complete infrastructure blocked:** same bounded tourist mechanism with Hawaii/waterfront gate. No vessel, berth, disembarkation or marine schedule model. | G and S all-location/sandbox restrictions; K Hawaii placement and rejected alternatives passed. Optional; not a universal rank objective. |

## Food and shop offerings

These are product/menu choices within existing rooms, not four new facilities or verified reference-game products. Their source is original provisional slice-C policy; an approved menu or retail inventory has not been supplied. [ProductRule.cs](../src/VerticalDistrict.Core/Simulation/ProductRule.cs) and the Products list in runtime rules define:

| Stable ID | Offering / model | Suggested price / service with current base rule | Base demand / additional lunch factor |
| --- | --- | --- | --- |
| `cafe-classic` | Classic cafe menu / Food | $18.00 / 180 seconds | 100% / 100% |
| `cafe-lunch` | Lunch menu / Food | $27.00 / 270 seconds | 80% / 160% |
| `shop-essentials` | Everyday essentials / Shop | $45.00 / 120 seconds | 100% / 100% |
| `shop-gifts` | Gift assortment / Shop | $72.00 / 216 seconds | 70% / 100% |

Prices and durations are multipliers of the existing BusinessRule. Whole minor-unit prices round down and retain the $1,000,000 cap; physical work rounds up to at least one second. Price/service multipliers permit 1–500%, demand/lunch 1–200%. The lunch factor multiplies base product demand from **11:00 inclusive to 14:00 exclusive**; final demand is clamped. Default products preserve the old price, service duration and demand factors.

Applying an offering sets its ID and suggested asking price for future purchases. Already accepted orders retain their product, agreed price and service duration. Food and shops share the physical order lifecycle, with model-specific ledger categories and departure behavior; successful shoppers leave after purchase while cafe customers retain their dining visit. Each accepted order has one customer. Closure/closing time cancels unpaid work; lost readiness pauses work within patience, and removed staff slots return orders to the queue. Failed monetary posting creates no completion or receipt until a successful retry.

One base customer attempt is shared across each model's venues: currently nominally every **600 seconds for Food** and **900 seconds for Shop**, checked by the minute scheduler. Eligible destinations are weighted by current demand and free capacity. Extra identical shops divide this pool; they do not each create an independent stream. Terminal tourist batches remain an additional limited source and create actual purchase orders. Inspection distinguishes retained recent purchase counts from lifetime operating ledger money. Inventory, stock and delivery systems are not added.

Eight [product-definition cases](../tests/VerticalDistrict.Management.Tests/ProductCatalogueCases.cs) and fifteen [retail cases](../tests/VerticalDistrict.Management.Tests/RetailCases.cs) passed in the full 236-case run, alongside 54 persistence cases and actual Godot retail smoke. [Integrated log](../artifacts/retail-verification.log). Schema 6 preserves accepted terms and shared demand, while genuine schema-5 fixtures retain paid shop receipts and existing cafe deadlines. [RETAIL_OPERATIONS.md](RETAIL_OPERATIONS.md) records policies, measurements, reviewed graphical captures and the passed fresh Windows export.

## Registry boundaries and validation

- [ContentCatalog.cs](../src/VerticalDistrict.Core/ContentCatalog.cs) owns immutable static identity, footprint, construction cost, rank, display metadata and provenance. It rejects duplicate IDs, impossible widths/heights, invalid ranges, missing provenance, unsupported models and duplicate utility references.
- [SimulationRules.cs](../src/VerticalDistrict.Core/Simulation/SimulationRules.cs) owns operational balance. Loading requires one rule per registered facility, rejects orphan/duplicate IDs and catalogue/model disagreement, requires explicit numeric fields and reports affected IDs/range groups. Occupant/arrival models need positive capacity in both catalogues; public space and advertising may validly use zero. Supported additional variants can load without hardcoding the production catalogue count. Only the inventory regression intentionally fixes today's reviewed 17-entry list.
- Product definitions remain within those rules. Current JSON requires an explicit nonempty Products array and all product fields. Validation rejects duplicate/bad IDs, unsupported models, orphan business references, missing/wrong-model defaults, blank provenance and invalid multipliers. Stable default IDs survive array reordering. Legacy save migration uses the real prior rules fingerprint, rather than accepting missing current product data silently.
- [ConstructionWorld.cs](../src/VerticalDistrict.Core/ConstructionWorld.cs) owns placed room IDs/coordinates and structural transactions. [GameSession.Commands.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Commands.cs) applies route/shaft/geography gates. These command gates are necessary because current static definitions do not contain a declarative restriction/dependency schema.
- Active leases, ownership, orders, bookings and service tasks have separate saved entities. Changing an asking price does not rewrite committed terms or historical ledger entries. The UI reads authoritative runtime values rather than treating duplicated `PlannedOperations` figures as a second balance authority.
- All definitions currently list Power/Water and noise 10. These are descriptive metadata: there are no independent power/water consumption networks or implemented neighbor/noise effects. No missing production asset references are invented simply to make validation pass.

Registry validation does not establish complete gameplay, production art, approved source parity, or all future dependency contradictions. The current explicit geography/footprint gates and executable scenarios cover existing restrictions; a broader declarative dependency system should follow concrete approved content rather than speculative schema growth.

## Checks and continuation

From the repository root:

```powershell
dotnet build VerticalDistrict.sln
dotnet run --project tests/VerticalDistrict.Management.Tests -c Release -- "Catalogue:"
dotnet run --project tests/VerticalDistrict.Management.Tests -c Release -- "Product catalogue:"
dotnet run --project tests/VerticalDistrict.Management.Tests -c Release -- "Retail:"
dotnet run --project tests/VerticalDistrict.Management.Tests -c Release
dotnet run --project tests/VerticalDistrict.Geography.Tests -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
.\artifacts\windows\VerticalDistrict.exe
```

The catalogue/ownership slice passed 205/205; retail then passed 236/236 plus its graphical/export acceptance. Those historical records remain in [BUSINESS_OPERATIONS.md](BUSINESS_OPERATIONS.md) and [RETAIL_OPERATIONS.md](RETAIL_OPERATIONS.md). Current transport verification passed 260/260, including the existing catalogue/product/retail cases, and expanded Godot. [ADVANCED_TRANSPORT.md](ADVANCED_TRANSPORT.md) records costed separate shafts, actual worker/service transfers, pure room-access explanations and schema-7 continuation; [PROJECT_STATUS.md](PROJECT_STATUS.md) records current graphical/scale/export evidence. None of this supplies a parking, station, vessel or bridge lifecycle merely by improving internal elevators.

The latest request prioritizes **coordinated banks and transfer journeys**, followed by the remaining advanced-transport dependencies. [LOCATION_TRANSPORT_MATRIX.md](LOCATION_TRANSPORT_MATRIX.md) identifies `parking`, `subway` and `dock` as incomplete infrastructure models and records the absence of approved concrete wing/skybridge definitions. Cinema screenings/tickets, events, advertiser contracts and remaining business/support behavior stay unfinished; the new priority does not mark them completed. Source reconciliation, production art/audio and full-game acceptance remain open.
