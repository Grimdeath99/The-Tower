# Normal progression scenario

Executed on 2026-09-30 and rerun after the office shift-end correction: **13/13 location profiles reached rank 7 through ordinary simulation**, starting at rank 1 with the normal $2,500,000 budget. The current-core rerun's complete stdout and stderr are retained in [commute-progression-regression.log](../artifacts/commute-progression-regression.log). Each run uses its fictional `central` site, without a subway connection or waterfront, and builds no subway or dock.

The dependency-free runner is [VerticalDistrict.Progression.Tests](../tests/VerticalDistrict.Progression.Tests/Program.cs). It loads the repository's unmodified construction catalogue, business prices, and promotion requirements. Construction, staffing, real visitor journeys, receipts, repairs, salaries, upkeep, and automatic promotions all use the same public commands and ticks as the game. There are no cash injections, price edits, direct promotions, snapshot edits, or relaxed rank requirements. Every ledger balance is checked and remains nonnegative.

## Reproduce

From the repository root, after the normal toolchain setup:

```powershell
# Tokyo only
dotnet run --project tests/VerticalDistrict.Progression.Tests -c Release -- --wall-seconds 120 --days 8

# All 13 locations, independently and sequentially
dotnet run --project tests/VerticalDistrict.Progression.Tests -c Release -- --all-locations --wall-seconds 120 --days 8
```

The limits apply to each profile. A timeout, unmet rank requirement at the day limit, insufficient physical space, negative cash, duplicate rank jump, stranded person, transport abandonment, or invalid receipt fails with a nonzero exit code and diagnostic state. This extended scenario is separate from the standard 121-case verification suite.

## Costed construction strategy

The normal rank-1 floor allowance supports floors 0–19. Before opening, the runner purchases 19 upper slabs ($95,000), 19 stair connections at bay 31 ($66,500), and two elevator banks ($160,000). Bank 1 occupies bay 29 and serves floors 0–9. Bank 2 occupies bay 30 and serves ground plus floors 10–19. Each bank is configured to 64 seats using the normal bank command, with the unchanged two-second floor travel and door settings. These are deliberately configured high-capacity banks, not the default eight-seat cars; the current rules charge the same bank construction price for permitted capacities.

A ground lobby, cafe, shop, and studio cost $63,000. Other rooms use deterministic first-fit placement in bays 0–28. Coverage is added before new demand. Each service depot employs four staff through the ordinary staffing command and pays the configured salaries.

| Naturally earned rank | Office target | Hotel rooms | Security offices | Service depots | Utility plants |
| --- | ---: | ---: | ---: | ---: | ---: |
| 1 | 12 | 2 | 3 | 1 | 1 |
| 2 | 24 | 4 | 6 | 2 | 2 |
| 3 | 40 | 6 | 9 | 3 | 3 |
| 4 | 55 | 8 | 12 | 4 | 3 |
| 5 | 70 | 10 | 15 | 4 | 3 |

The builder waits for actual operating receipts whenever the next purchase would leave less than $50,000. The final 106 rooms cost $2,188,000; total room, slab, stair, and bank construction is $2,509,500. Growth beyond the original budget is funded by earned receipts. The operating reserve can subsequently fall below the builder's purchase threshold as ordinary costs are charged.

## Observed result

All 13 profiles produced the same deterministic financial and population result with this common-site strategy: Tokyo, Osaka, Seoul, Dubai, Abu Dhabi, New York, Shanghai, Hong Kong, Beijing, Las Vegas, San Francisco, Chicago, and Hawaii.

| Milestone | Simulation time |
| --- | --- |
| Rank 2 | Day 2, 00:55 |
| Rank 3 | Day 2, 01:55 |
| Rank 4 | Day 2, 02:55 |
| Rank 5 | Day 2, 03:55 |
| Rank 6 | Day 3, 00:55 |
| Rank 7 | Day 3, 01:55 |

At rank 7, each current-core run had $33,446 cash, $35,285 last daily operating profit, $82,681 cumulative receipts, peak population 553, satisfaction 83%, average cleanliness 92%, 2,332 completed journeys, and zero transport abandonments. Routed maintenance charges were present. Every rank was earned in order and all current rank requirements were satisfied. The sequential rerun recorded approximately 111 seconds of scenario execution on the verification machine, with individual profiles taking 5.0–10.8 seconds. These current results supersede the earlier 91% cleanliness and 2,380-trip measurements from before workers were corrected to leave at 18:00.

This proves one viable normal progression path under the current original provisional tuning, including locations without optional arrival terminals. It does not establish long-term economic balance, small-car viability at this demand level, differentiated city economies, player difficulty, rendered performance, or completion of the missing approved GDD/content catalogue. The scenario stops on earning rank 7; prolonged operation after victory remains outside this check.
