# Business operations: catalogue foundation and condominium ownership

Historical slice A/B handoff: the subsequent [food and retail slice C](RETAIL_OPERATIONS.md) implements menus and completed shop purchases with schema 6. The measurements and 205-case results below belong to A/B; current verification is recorded in [PROJECT_STATUS.md](PROJECT_STATUS.md).

This is the first dependency-complete slice of the business-operations request: catalogue reconciliation/validation (A) and the existing condominium model (B). It extends the verified [management loop](MANAGEMENT_LOOP.md). The full business milestone remains open: completed retail purchases, menus, persistent screenings/events/advertising and approved support/variants still need their own connected slices. [CONTENT_CATALOGUE.md](CONTENT_CATALOGUE.md) records all 17 actual IDs and their individual limits. No approved full GDD, exact earlier 87-entry list or selected reference-game edition is available.

## Playable policy

The existing `condo` now has a persistent purchase agreement and assigned household, separate from a rental contract. Original provisional rules:

- At 18:00 an available, accessible and sufficiently attractive unit accepts a household, reserving its capacity and current asking price. The owner and resident IDs remain stable. The offer expires at the following 08:00 if nobody completes the purchase.
- The first resident to physically arrive completes one sale at the accepted price. A later asking-price edit cannot change it. A zero-price purchase still records its transaction. Abandoned or cancelled offers earn nothing.
- Residents leave at 08:00 and return at 18:00 through the transport system. Daytime vacancy does not release ownership, create a new household, or produce another sale. Owners pay no rent or recurring owner fee; the tower still owes its normal room upkeep and staff costs.
- An owned unit cannot be closed or demolished. **Repurchase ownership** returns exactly the original paid price, records one capital expense, closes the unit and requests physical departure. A repeated or unfunded buyback is rejected without changes. Reopening and demolition wait for every resident to exit; riders remain in their actual cars until they can leave safely.
- After departure the closed unit can be demolished for the normal construction salvage or reopened for a later, separate sale. Sale/buyback are capital cash flows, never office/home rent. There is no market appreciation, resale marketplace or approved association fee in this policy.

The room inspector shows current availability, agreement/owner/resident IDs, accepted price, physical occupancy and purchase/buyback ledger references. The district resident total includes assigned owned households, while physical population remains separate. The buyback control uses the existing validated gameplay command.

The build list now searches stable IDs, names, categories and models; category and unlock filters share the real location/rank rules. **Facility guide** explains construction/upkeep/wages, capacity, hours, access, support requirements and actual model coverage before placement. A site/rank lock is distinct from unfinished gameplay. Cinema, shop, events, advertising and transport prototypes are explicitly described as incomplete; they are not claimed to satisfy the full operating milestone. Geography remains independently validated at placement.

## Data and save boundary

The existing registry is retained. Validation rejects unsupported models, duplicate IDs, missing/orphan runtime rules, model disagreement, invalid ranges, missing monetary/allocation fields and duplicate utility references. Runtime business rules remain authoritative over older display metadata; no new approved variants or balance changes were invented. JSON content shapes/fingerprints remain unchanged in this slice.

Session schema **5** adds authoritative ownership state with stable counters, household assignments, frozen terms, sale/buyback receipts and lifecycle status. Migration uses real schema-4 fixtures captured with the prior assembly: a sold unit with residents and a sold unit after its previous long visit ended. Earlier schema-2/3 migrations are preserved. Imported legacy ownership does not repost an old sale or invent a rent contract. A changed or missing content definition fails candidate validation instead of substituting a room. Whole-candidate validation and the existing previous-good save/backup workflow remain in force.

Financial history stays in the sole append-only ledger. Validation checks ownership references, household uniqueness, state transitions and matching receipt amounts/sequences before accepting a candidate. Public ownership projections clone resident arrays so UI callers cannot edit authoritative membership.

## Verification

The unchanged management baseline was rerun first: **173/173** cases and integrated Godot smoke passed in [business-baseline.log](../artifacts/business-baseline.log). This is now a single full-wrapper baseline, superseding the earlier split 172+final-persistence accounting.

Final source verification passed **205/205 cases**: 22 construction, 23 transport, 46 persistence, 22 geography, 26 simulation, 18 finance and 48 management. The solution built with zero warnings/errors and the integrated Godot scene passed in the same final [verification run](../artifacts/business-verification.log). The 48 management cases include eleven ownership and eleven catalogue cases. Current slice results are also recorded in [PROJECT_STATUS.md](PROJECT_STATUS.md).

Focused ownership cases cover purchase, frozen prices, returning identity, unavailable/closed offers, occupied/reacquired removal, repeated/unfunded buyback and physical route recovery. Catalogue-wide foundation cases cover all 17 definitions and valid/invalid placement, costs, closed behavior, inspection purity and saved continuation; these do not certify the unfinished models' entire gameplay. The initial all-entry test found a real paused-inspection mutation: `CanReach` observed construction by changing saved transport counters. Access inspection now updates only its derived route cache; commands and simulation ticks adopt authoritative topology changes. A transport regression verifies current geometry, unchanged snapshots and subsequent journey recovery.

From the repository root, exact commands:

```powershell
dotnet build VerticalDistrict.sln
dotnet run --project tests/VerticalDistrict.Management.Tests -c Release
dotnet run --project tests/VerticalDistrict.Persistence.Tests -c Release
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\verify.ps1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\review-management.ps1
dotnet run --project tools/VerticalDistrict.BusinessBenchmarks -c Release -- artifacts/business-performance-after.json
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\export-windows.ps1
& 'I:\Program Files\Godot\Godot_v4.7.2-stable_mono_win64.exe' --path . --editor
.\artifacts\windows\VerticalDistrict.exe
```

The graphical review exercises real ownership/buyback controls, category/search/site filters and paused-state/input isolation, and writes `artifacts/business-ownership.png`, `business-catalogue.png`, `business-buyback.png` and `business-cutaway.png` alongside the existing management captures. The condo has original temporary sleeping-alcove, sofa and kitchenette furnishings; these are not production pixel art. A successful targeted review does not establish exhaustive exported interaction or accessibility acceptance.

The final [graphical review](../artifacts/business-graphical-review.log) passed and all eight fresh 1440×900 captures were inspected. Ownership's original-price action is visible above the live state; the library leaves space to browse filtered results. The fresh Windows ExportRelease build passed with zero warnings/errors and its actual executable passed the expanded smoke outside the editor: **189 files / 189,839,906 bytes**, kept together in `artifacts/windows`. [EXPORT.md](EXPORT.md) records the initial native packaging failure and successful unchanged-source retry. Full exported interactive/FPS acceptance remains broader than this smoke.

## Performance method

[BusinessBenchmarks](../tools/VerticalDistrict.BusinessBenchmarks/Program.cs) measures authoritative `GameSession.Step`, including businesses, transport, service work, complaints and billing. It uses the existing 20-floor operating example plus one costed condo: 15 rooms, one 12-seat elevator, 19 stair links, normal business tuning, seed 20260930, sandbox capital/rank. Each of three repetitions runs 259,200 ticks (three days), then compares one more hour uninterrupted against a validated saved continuation. Population, completed trips, money, allocations, saved bytes and load time are recorded with timings. **The paired before/after runs use a Release harness referencing Debug core assemblies**: the preserved pre-change assembly was a Debug build. Assembly configurations and hashes are measured independently in the report; these are not optimized Release-core performance figures.

The before run uses a preserved schema-4 assembly (SHA256 `9C27D4B12E13F45A1602D92DAAC5987F999CAE391AADD40D673F9C30D3EACF8D`) rather than compiling changed sources. Command used:

```powershell
dotnet run --project tools/VerticalDistrict.BusinessBenchmarks -c Release -p:BaselineCore=C:/Users/alexa/Documents/the-tower/.build/schema4-condo-baseline/VerticalDistrict.Core.dll -- artifacts/business-performance-before.json
dotnet run --project tools/VerticalDistrict.BusinessBenchmarks -c Release -p:BaselineCore=C:/Users/alexa/Documents/the-tower/src/VerticalDistrict.Core/bin/Debug/net8.0/VerticalDistrict.Core.dll -- artifacts/business-performance-after.json
```

That ignored baseline DLL is a local evidence artifact, not a new dependency. The ordinary benchmark command without `BaselineCore` uses the current core project with the selected configuration. Machine: Windows 10.0.26200, .NET 8.0.28, Intel64 Family 6 Model 170 Stepping 4, 22 logical processors. [Before results](../artifacts/business-performance-before.json) and [after results](../artifacts/business-performance-after.json) preserve every repetition. Before: 40 peak physical people sampled each minute, one condo sale, 943 completed trips after the continuation hour. Small-population timings cannot establish 250-floor or rendered FPS performance; the new returning-resident behavior also changes workload, so outcomes accompany the timing comparison.

| Matched Debug-core run | Mean milliseconds / logical second (three repetitions) | Step p95 milliseconds | Three-day wall time | Population and outcome after continuation |
| --- | --- | --- | --- | --- |
| Before ownership | 0.01404 / 0.01056 / 0.01209 | 0.0241 / 0.0173 / 0.0215 | 3.652 / 2.746 / 3.146 seconds | 40 peak sampled; 37 final physical people; 943 trips; one sale |
| After ownership | 0.01506 / 0.01244 / 0.01219 | 0.0247 / 0.0216 / 0.0209 | 3.917 / 3.237 / 3.171 seconds | 38 peak sampled; 35 final physical people; 951 trips; one sale |

All six saved continuation comparisons passed. The median mean-step measurement rose about 2.9%, with overlapping run variation and different household schedules/workloads; this small sample is not a general performance guarantee. No graphical frame time is included.

## Manual acceptance

1. Start a normal operating example. In the build list choose **Homes**, search `condo`, and open **Facility guide**. Check purpose, price, capacity, costs and provisional ownership policy. Switch sites or use the unlock filter to see subway/dock restrictions stay separate from model coverage.
2. Place a condo on a supported floor with access to the lobby and a staffed service depot. Leave the default price and run through 18:00. Inspect the accepted household, then its purchase transaction after arrival. Both residents should have stable IDs; the first arrival creates one sale.
3. Change the asking price. Confirm the agreement and buyback price stay at the original amount. Save while residents are inside, load, and confirm there is no new sale. Inspect **Finances → facility transactions** for the capital receipt.
4. Run through 08:00. Residents should physically depart while the unit remains owned. Save while they are outside, load and run to 18:00. The same IDs return, with no second sale or lease charge.
5. Try closure and demolition while owned. Both must fail without removing the home, ownership or money. Repurchase from the room inspector. Check one original-price refund and a closed unit awaiting exit; a second repurchase must not change cash.
6. While a resident is riding, disable the bank and observe safe landing/route recovery. Reopening or demolition must wait until every resident has exited. Restore service if needed; after exit, demolition should apply only normal construction salvage.
7. Save and reload during a pending purchase and during buyback evacuation. Confirm the quote, owner/member IDs, actual car ownership and financial totals survive. Test a cleanly cancelled unpaid offer separately by closing before arrival.

## Changed implementation and next task

Ownership lives in [OwnershipState.cs](../src/VerticalDistrict.Core/Simulation/OwnershipState.cs) and [GameSession.Ownership.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Ownership.cs), integrated through the existing businesses/commands and schema migration. Presentation is in [Main.Ownership.cs](../Scripts/Main.Ownership.cs) and [Main.Catalogue.cs](../Scripts/Main.Catalogue.cs), using the shared facility dialog and finance formatting. Catalogue/rule validation, ownership/persistence tests, genuine fixtures and the reproducible business benchmark accompany the code.

Other changed implementation files and evidence:

- [ContentCatalog.cs](../src/VerticalDistrict.Core/ContentCatalog.cs) and [SimulationRules.cs](../src/VerticalDistrict.Core/Simulation/SimulationRules.cs): model/reference/range validation and mandatory data fields.
- [GameSession.OwnershipPersistence.cs](../src/VerticalDistrict.Core/Simulation/GameSession.OwnershipPersistence.cs), [GameSession.Persistence.cs](../src/VerticalDistrict.Core/Simulation/GameSession.Persistence.cs) and [GameSession.ManagementPersistence.cs](../src/VerticalDistrict.Core/Simulation/GameSession.ManagementPersistence.cs): schema-5 capture, migration and candidate validation.
- [TransportSystem.cs](../src/VerticalDistrict.Core/Transport/TransportSystem.cs) and [transport tests](../tests/VerticalDistrict.Transport.Tests/Program.cs): read-only reachability after construction.
- [OwnershipCases.cs](../tests/VerticalDistrict.Management.Tests/OwnershipCases.cs), [CatalogueCases.cs](../tests/VerticalDistrict.Management.Tests/CatalogueCases.cs), [OwnershipPersistenceCases.cs](../tests/VerticalDistrict.Persistence.Tests/OwnershipPersistenceCases.cs) and [fixture provenance](../tests/VerticalDistrict.Persistence.Tests/Fixtures/README.md): registered executable tests and genuine previous-version saves.
- [Main.cs](../Scripts/Main.cs), [Main.Management.cs](../Scripts/Main.Management.cs), [Main.ManagementLoop.cs](../Scripts/Main.ManagementLoop.cs) and [TowerCanvas.cs](../Scripts/TowerCanvas.cs): catalogue layout, ownership controls/counts, live closure state and original condo furnishings.
- [review-management.ps1](../tools/review-management.ps1) and [BusinessBenchmarks](../tools/VerticalDistrict.BusinessBenchmarks/VerticalDistrict.BusinessBenchmarks.csproj): reproducible rendered acceptance and before/after measurement. Status, feature matrix, known issues, next steps, decisions and README link this slice to the preceding work.

The next dependency-ready task is **completed retail purchases for `shop`**, with frozen product/price, actual service, capacity, cancellation, inspector, persistence and tests. Food menu identity can then extend the existing completed-service flow. Persistent cinema screenings, venue bookings and advertising follow; approved variants and production art remain source/asset gaps. Advanced transport/infrastructure comes after the business models, not before them.
