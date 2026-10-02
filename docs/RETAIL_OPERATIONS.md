# Food menus and completed retail purchases

Slice C extends the existing `cafe` and `shop` without adding facility IDs. Shops now accept a product quote, reserve capacity, receive a physical customer, queue, perform timed service and post one completed-purchase receipt. The café uses the same purchase engine and retains its dining interval after service. This follows [catalogue and ownership work](BUSINESS_OPERATIONS.md); persistent cinema screenings are the next dependency-ready model. The full business milestone remains open.

No approved complete GDD, earlier exact 87-entry list or reference-game edition is available. The four offerings below are original provisional choices, not approved facility variants or reference-game facts. They live in the existing [simulation rules](../Data/simulation.rules.json), which retains every earlier rule value. Existing rank and independent site/network restrictions are unchanged.

## Playable policy

| Offering | Facility | Suggested price | Service per staff slot | Base demand factor | Additional 11:00–14:00 factor |
| --- | --- | ---: | ---: | ---: | ---: |
| Classic cafe menu | `cafe` | $18.00 | 180 seconds | 1.00 | 1.00 |
| Lunch menu | `cafe` | $27.00 | 270 seconds | 0.80 | 1.60 |
| Everyday essentials | `shop` | $45.00 | 120 seconds | 1.00 | 1.00 |
| Gift assortment | `shop` | $72.00 | 216 seconds | 0.70 | 1.00 |

Selecting an offering previews its terms without changing the simulation. **Apply offering & default price** changes future orders and resets the editable asking price to that offering's suggestion. **Apply price** can then override the asking price. Accepted orders keep their product ID, agreed price and agreed service duration. Default prices round down to minor units and retain the existing $1,000,000 ceiling; service duration rounds up to whole logical seconds. Product definitions validate IDs, models, default references, required names/categories/provenance and percentage ranges before play.

One base customer attempt is shared by each model: every 600 seconds for food and 900 seconds for shops under current tuning. The existing minute scheduler processes those attempts, so custom sub-minute intervals are limited to one attempt per minute. Attempts occur while the model exists, even if its rooms are unavailable. Eligible destinations require opening hours, staffing, access, utilities, condition, cleanliness and free reserved capacity. An attempt succeeds using the highest candidate demand score, then chooses a room by seeded weights of `demand × free capacity`. Adding comparable shops shares this finite stream; it does not create another per-room stream. Existing bounded terminal batches are additional and use the same purchase lifecycle for food/shop destinations. Each customer is assigned exactly one purchase.

Demand still uses location, asking-price value, satisfaction, condition and cleanliness. Food/shop value compares against the selected offering's suggested price. Its base and current lunch factor then multiply demand, bounded to 0–100. Lunch is 11:00 inclusive to 14:00 exclusive. The offering's popularity is not itself a dissatisfaction complaint. Location economic modifiers remain neutral provisional values.

Orders follow `Traveling → Queued → Serving → Completed`, with `Abandoned` available before payment. Patience uses the existing 600-second food policy, refreshed on physical arrival. Required staffing enables service; each hired staff member provides one simultaneous slot. Dismissing staff requeues displaced work with the same quote and patience, restarting its service duration when a slot is available. Temporary access/service failure can pause remaining work; unresolved failures or closure abandon it. Closing time cancels unfinished service. A completed purchase posts one `Sales.Food` or `Sales.Shop` row, including an explicit zero-value receipt. An abandoned purchase posts none. Overflow leaves payment pending for retry without duplicating it.

Shop customers return after completion; café customers retain the existing dining visit. Closed venues stop accepting customers and preserve real actors and elevator riders until their physical return can resolve. Active people/capacity and financial obligations continue to block demolition. After safe departure, removal clears the room's offering and bounded purchase history. There is no stock, supply chain, delivery or spoilage simulation.

## Interface and persistence

The shared facility inspector provides offering preview/application, price, staffing and closure controls; current arriving/queued/serving counts; reserved capacity and staff utilization; accepted/completed/abandoned recent history; frozen terms for recent purchases; and lifetime operating revenue, expenses and result derived from the sole ledger. Counts are explicitly retained-history counts, not lifetime totals. The shared bound is 256 departed terminal purchase records across food venues and shops, plus records still required by actors. Demand reasons, condition, physical access and transactions remain in the same inspector. The catalogue guide now describes both implemented models, and demand management includes shops. Shop shelving and a checkout counter are original code-drawn cutaway placeholders.

Session schema **6** persists selected offerings, frozen product/service terms and both model-level customer-attempt cursors. Two genuine schema-5 fixtures were captured with the unmodified previous DLL and verified by its original loader: mixed operations with paid shop visitors and an in-flight shop arrival. Migration retains existing café quotes and service deadlines, converts already-paid shop visitors into completed purchases, and gives unpaid arriving shoppers the new service lifecycle. Historical cash and ledger rows are preserved. Earlier supported schema-2/3/4 saves also upgrade through their existing migrations.

Old rule fingerprints are checked against the precise prior definition shape with newly added Products removed; historical schema-2/3 projections also account for Management. Current schema-6 saves require the full current fingerprint. Changed or missing non-product rules cannot silently migrate. Product/model references, service times, actor ownership, receipts, cursor schedules and complete required fields are validated before replacing a session or saving over a valid file. The previous-good backup workflow remains in force. [Fixture provenance](../tests/VerticalDistrict.Persistence.Tests/Fixtures/README.md) records the original assembly hash.

## Verification and measurements

The unchanged baseline passed **205/205 cases plus Godot** in [retail-baseline.log](../artifacts/retail-baseline.log). The integrated slice passed **236/236**: 22 construction, 23 transport, 54 persistence, 22 geography, 26 simulation, 18 finance and 71 management, followed by the expanded Godot smoke. The solution built with zero warnings/errors. [Final verification](../artifacts/retail-verification.log) includes all 15 retail cases, eight product-definition cases and eight new persistence cases; none of the existing policy assertions was weakened.

Retail cases cover completion/frozen terms, lunch and price value, missing staff, queuing/closure/riders, route loss and recovery, opening/closing/midnight, fair finite allocation, explicit zero receipts, overflow retry, pure inspection, terminal customers, three-day mixed saved continuation, last-shop removal/rebuild and exhausted IDs. The audit found a service-worker counter edge case: exhausted IDs could previously allocate a transport journey before failing the increment. Dispatch now blocks before allocating that route, with a regression preserving a valid blocked task. Genuine migration cases preserve old paid shop visitors and traveling arrivals, active café deadlines, changed product defaults, complete historical fingerprints, detached projections, corrupt-state rejection and both last-good save files.

The expanded graphical review requires twelve fresh captures and exercises actual buttons, offering preview/apply, preserved purchase terms after loading, cancellation without a sale, paused state purity and input isolation. The [review log](../artifacts/retail-graphical-review.log) and captures show [offering preview](../artifacts/retail-offering.png), [completed purchase and ledger](../artifacts/retail-purchases.png), [café menu](../artifacts/retail-cafe.png) and [distinct cutaway](../artifacts/retail-cutaway.png), alongside the eight prior management/ownership views. These are targeted rendered checks, not exhaustive manual acceptance or graphical performance measurements. The offline certificate-store startup diagnostic is retained in logs and remains the only allowed engine warning.

The fresh schema-6 Windows package exported on the first attempt, with zero ExportRelease warnings/errors, and its actual executable passed both `RETAIL_SMOKE_PASS` and the expanded `CONSTRUCTION_SMOKE_PASS` outside the editor. Keep all **189 files / 189,888,530 bytes** together and launch [VerticalDistrict.exe](../artifacts/windows/VerticalDistrict.exe). [Export workflow](../artifacts/retail-export-workflow.log), [build log](../artifacts/export-build.log) and [standalone smoke](../artifacts/export-smoke-process.log) are current evidence. Normal project dependency restore completed successfully afterward; [EXPORT.md](EXPORT.md) records the complete packaging procedure. The earlier native startup retry belongs to slice A/B, not this export.

Exact commands from the repository root in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\review-management.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\export-windows.ps1
dotnet restore VerticalDistrict.Game.csproj
```

The benchmark is a Release harness against the explicitly measured Debug core, with three runs of 259,200 logical seconds and a further hour comparing uninterrupted and restored execution. Both versions build the same 20-floor, 17-room mixed tower, including two shops on floors 7 and 14, a café and condo, with one 12-seat elevator and 19 stair links. The original core uses its preserved pre-Products definitions. It records actual assembly configuration/hash, hardware, allocations, population, trips, ledger receipts and save size. It measures authoritative simulation, without rendering; neither its small population nor its step timings establishes 250-floor performance or graphical FPS.

```powershell
dotnet run --project tools/VerticalDistrict.BusinessBenchmarks -c Release -p:BaselineCore=C:/Users/alexa/Documents/the-tower/.build/schema5-retail-baseline/VerticalDistrict.Core.dll -- artifacts/retail-performance-before.json .build/schema5-retail-baseline retail
dotnet run --project tools/VerticalDistrict.BusinessBenchmarks -c Release -p:BaselineCore=C:/Users/alexa/Documents/the-tower/src/VerticalDistrict.Core/bin/Debug/net8.0/VerticalDistrict.Core.dll -- artifacts/retail-performance-after.json Data retail
```

The preserved baseline binary is local ignored evidence, not a versioned dependency needed to build the game. Reproducing the before measurement elsewhere requires preserving the corresponding schema-5 source/assembly and definitions before upgrading. Retail receipts have deliberately different meaning: admission under schema 5 versus completed service under schema 6. Finite shared demand also changes population and trip counts, so timing differences cannot be interpreted as a pure algorithm speed comparison.

Measured on 2026-10-01, Windows build 26200, .NET 8.0.28, Intel64 Family 6 Model 170 Stepping 4, 22 logical processors. The [before JSON](../artifacts/retail-performance-before.json) and [after JSON](../artifacts/retail-performance-after.json) contain all repetitions and full assembly hashes. Both report Debug core / Release harness. Three-run medians:

| Metric | Before: schema 5 | After: schema 6 |
| --- | ---: | ---: |
| 259,200-step wall time | 2,988.84 ms | 3,522.06 ms |
| Mean logical step | 0.011492 ms | 0.013543 ms |
| Per-run p95 step | 0.0189 ms | 0.0249 ms |
| Allocated bytes during timed ticks | 2,564,706,056 | 2,603,620,912 |
| Peak physical people, sampled each minute | 42 | 39 |
| Physical people after resumed comparison | 35 | 35 |
| Completed trips after resumed comparison | 1,529 | 1,265 |
| Shop ledger receipts after resumed comparison | 250 admission receipts | 127 completed purchases |
| Shop revenue | $11,250 | $5,715 |
| Serialized snapshot bytes | 454,626 | 452,599 |
| Restore elapsed time | 42.22 ms | 56.30 ms |
| Exact resumed comparison | 3/3 | 3/3 |

Median mean-step cost increased about 17.9%, with about 1.5% more allocation. The observed maximum individual step across runs was 5.8834 ms before and 4.9981 ms after; these maxima include host scheduling noise. All timings exclude rendering. The added purchase records, selection/demand logic and changed customer stream are part of the comparison; lower sales are the documented finite-demand/completed-service policy, not a claim of equal-workload speedup. Final cash was $24,464,201 before and $24,458,827 after; each fixture recorded one condo sale.

## Manual acceptance

1. Launch `artifacts/windows/VerticalDistrict.exe` with the complete packaged folder, or open `project.godot` in the pinned Godot .NET editor and press F5. Start a sandbox example, build a connected shop, and advance to its 10:00 opening.
2. Inspect the shop while a customer is being served. Pause; preview Gift assortment and confirm that the active order and cash remain unchanged. Apply the offering and observe the asking price change to $72 while the old customer's quote remains $45 and 120 seconds.
3. Save during service, load, and resume. Check that one receipt appears only when that customer's accepted service completes. The accepted product ID and price must remain the original ones.
4. Close a shop during a later unpaid service. Confirm abandonment without a sale, physical departure and occupied demolition rejection until the customer leaves. Reopen and restore staffing to resume future service.
5. Apply the Lunch menu to a café. Inspect the $27 suggested price, 270-second service and current demand factor before, during and after 11:00–14:00. Earlier accepted meals retain their terms.
6. Operate two comparable shops and inspect the shared demand explanation, distributed customers, queue/capacity utilization and actual finances. Try unavailable access and insufficient staffing; demand reasons and unresolved customers must remain explainable. Inspect the new shop shelves and counter in the cutaway.

## Changed files and limits

Core changes are in `SimulationRules.cs`, `ProductRule.cs`, `BusinessState.cs`, `RetailState.cs`, `GameSession.Retail.cs`, `GameSession.Commerce.cs`, business/command/satisfaction hooks, the exhausted-ID guard in `GameSession.Services.cs`, and the session/management/retail persistence partials. Data adds Products to the existing simulation registry. Godot changes are in `Main.Retail.cs`, shared management/catalogue/smoke hooks and `TowerCanvas.cs`. Tests add retail/product cases, genuine schema-5 fixtures and persistence corruption/migration cases; synthetic earlier-version test headers now use their real historical rule shapes. The existing benchmark tool accepts the retail fixture and preserved data directory; the graphical review script requires four additional retail captures. The current status, matrix, catalogue, known issues, next steps, README, decisions and export handoff are updated alongside this document.

This slice covers the existing café and shop, not missing approved variants. Cinema screenings, persistent events, advertising contracts, approved hotel extensions and environmental/support behaviors remain unfinished. The next slice is persistent cinema schedules, reservations/admission, cancellation policy and exactly-once receipts with its own UI, persistence and tests. Production art/audio, broader final balance, full interactive exported acceptance, fresh-machine reproduction and rendered FPS remain unverified.
