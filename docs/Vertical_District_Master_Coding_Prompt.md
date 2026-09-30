# Vertical District: Master Coding Prompt

## 1. YOUR ROLE AND DELIVERABLE

Act as the lead Godot developer, simulation engineer, gameplay programmer, UI developer, and QA engineer for Vertical District, a complete single-player tower-management simulation.

Your task is to IMPLEMENT the game, not merely describe its architecture or generate isolated example scripts. Work toward a playable, tested, exportable desktop game through small, integrated milestones. The prototype is the first milestone, not the final product.

Use the available project workspace directly. Inspect existing files before editing, preserve working systems and user changes, and implement the next dependency-complete slice. Do not replace the repository with a new project unless it is genuinely empty.

Do not attempt an enormous, untested one-response code dump. Do not stop after writing a plan. Begin implementation in the first session and leave the project runnable at each milestone boundary.

## 2. SOURCE OF TRUTH AND SCOPE CONTROL

Read the latest available GDD, project instructions, architecture documents, content catalogues, and existing code. Resolve conflicts using this order: latest explicit user decisions, latest approved GDD, established repository conventions, then this prompt's implementation defaults. Record unresolved conflicts rather than silently changing the design.

The confirmed constraints below must not be weakened by older documents. Additional implementation details in this prompt are defaults, not claims that the user previously approved every technical choice.

Maintain docs/FEATURE_MATRIX.md with requirement ID, source, acceptance criteria, implementation location, dependencies, status, and test evidence. Use statuses such as Not Started, In Progress, Implemented, Verified, and Blocked. Documentation or a class definition alone does not mean a feature is implemented.

The scope includes the Tower II / Yoot Tower mechanics and rooms requested in the GDD. No particular reference edition has been selected. Distinguish verified base-game features, edition-specific features, regional/expansion content, original Vertical District additions, and unverified claims. Do not invent reference-game facts or claim complete parity without a checked inventory.

Earlier design work described an 87-facility draft. Reconcile its exact entries against available source documents; do not invent 87 names to satisfy a count. When source material is missing, record the coverage gap, implement the independently specified systems, and request only the missing material needed to resolve that gap. Do not declare reference-content completion while that gap remains.

Use original or appropriately licensed assets and original implementation code. Do not extract or copy proprietary game assets, branding, or source code.

## 3. NON-NEGOTIABLE GAME IDENTITY

Name: Vertical District.
Engine: Godot.
Genre: single-player tower construction, management, and transport simulation.
View: fixed side-on 2D architectural cutaway, not isometric or a freely rotating 3D camera.
Art: detailed architectural pixel art, modular rooms, semi-realistic furnishings, textured surfaces, tiny inhabitants, readable floors, and visible elevator routes.

Progression has SEVEN ranks. Rank 6 represents worldwide recognition; Rank 7 represents a seven-star hotel experience. “Global Icon” and “Seven-Star Destination” are draft names unless superseded by the GDD. The seven-star designation is an in-game progression concept.

Support 250 above-ground floors INCLUDING the ground floor, plus up to 10 basement floors. Use an explicit coordinate convention: ground = 0, above-ground indices = 0 through 249, basements = -1 through -10. Rank 5 unlocks the full above-ground cap; earlier limits come from progression data. The MVP supports 20 above-ground floors without imposing that limit on the final architecture.

Provide these 13 selectable locations: Tokyo, Osaka, Seoul, Dubai, Abu Dhabi, New York, Shanghai, Hong Kong, Beijing, Las Vegas, San Francisco, Chicago, and Hawaii.

Subway facilities require BOTH a location with an operating subway network AND a compatible fictional building site. Planned networks, monorails, and elevated-only rail must not automatically qualify. A mixed network can qualify through its actual subway infrastructure. Keep network eligibility separate from site connection availability.

Docks and boat-arrival terminals are HAWAII ONLY and require a compatible waterfront site. No other location may unlock them merely because it has a waterfront. Dock construction is optional; use the GDD's rank gate, with Rank 4 as the draft default. Do not make docks a universal progression requirement.

## 4. TECHNICAL DEFAULTS

For a new repository, use Godot 4 .NET with C# and a compatible .NET SDK. C# is the proposed implementation default, not a previously confirmed user language choice. Preserve an established Godot language choice in an existing repository instead of starting an unsolicited rewrite.

Inspect the installed toolchain and verify version-specific APIs using official documentation. Pin the exact engine, SDK, and package versions. Prefer an appropriate stable release for a new project, but do not upgrade an existing project without justification. Never invent API signatures or copy GDScript syntax into C#.

Target Windows desktop first. Structure platform-specific services so other desktop platforms can be added. Mobile, browser export, multiplayer, accounts, online services, and paid plugins are not requirements unless the GDD explicitly adds them.

Default to the Compatibility renderer. Use TileMapLayer where suitable for static structure, lightweight 2D presentation for rooms and people, and Control/Container-based UI. Use one validated content-definition format, preferably typed JSON loaded into immutable definitions. Keep runtime instance state separate from content definitions.

Avoid unnecessary frameworks, dependency-injection containers, native extensions, and premature multithreading. Do not introduce C++ GDExtension without a demonstrated bottleneck and a measured benefit.

## 5. ARCHITECTURE AND PROJECT ORGANIZATION

Keep the authoritative simulation independent of Godot nodes and UI. In a C# project, put it in a plain .NET library that can be tested without launching Godot. Let Godot adapt input into commands and render simulation snapshots or change notifications.

Suggested structure for a new project:

VerticalDistrict.sln
src/VerticalDistrict.Core/
  World/
  Construction/
  Transport/
  Population/
  Economy/
  Operations/
  Progression/
  Content/
  Persistence/
game/
  project.godot
  VerticalDistrict.Game.csproj
  Scenes/
  Scripts/
  Data/
  Assets/
  UI/
tests/VerticalDistrict.Core.Tests/
tests/GodotIntegration/
tools/
docs/

Adapt names to existing conventions. Ensure the Godot project's compilation scope does not accidentally include test projects or duplicated source files.

Use stable IDs for entities and content. Store money in checked integer minor units with explicit rounding rules. Use a seeded simulation random generator, a fixed logical clock, stable update order, and explicit command processing. Rendering randomness must not affect gameplay randomness.

Implement a configurable fixed-step simulation with pause and 1x, 2x, and 4x speed defaults. Document how real time maps to game time. Rendering frame rate must not change financial outcomes or schedules. When overloaded, bound catch-up work and report effective simulation speed rather than silently skipping authoritative events.

Keep scene-tree updates on the main thread. Add background work only around isolated data with clear ownership and synchronization. UI panels must not directly edit authoritative collections. Avoid a giant GameManager and global state shared by every system.

## 6. CONSTRUCTION AND BUILDING MODEL

Implement floors, basements, structural space, lobbies, corridors, room footprints, entrances, stairs, escalators, elevator shafts, and the GDD's connected wings and skybridges.

Construction needs grid snapping, a placement preview, clear valid/invalid feedback, cost previews, selection, inspection, demolition, and cancellation. Support variable room widths and multi-floor footprints where definitions require them.

Validate boundaries, floor and rank limits, footprint collisions, structural prerequisites, access requirements, location restrictions, affordability, and facility dependencies. Define explicit overlap rules for structures, corridors, shafts, and room footprints instead of treating every cell as one interchangeable object.

Apply construction and its financial transaction atomically. A failed placement must not remove money or partially change the building. Destructive actions must handle occupants, leases, condo obligations, reservations, services, and active routes predictably.

Revalidate affected access and routes after structural changes. People must not walk through deleted connections or become permanently stuck without a recoverable state. Multi-wing construction must not provide a loophole around the intended floor-height cap.

Any undo system must be limited to safe construction transactions or restore the complete associated state; it must not duplicate refunds or reverse time-dependent income.

## 7. FACILITIES AND CONTENT COVERAGE

Build a data-driven facility registry rather than one bespoke script per room. Validate unique IDs, cross-references, required assets, unlock conditions, and definition ranges during loading and tests.

Each definition should describe identity, category, footprint, construction cost, upkeep, capacity, staffing, utilities, opening hours, access rules, noise or environmental effects, operating model, pricing parameters, unlock requirements, location/site restrictions, visual references, and source/provenance status.

Each placed instance should track condition, occupancy, operating state, prices, finances, assigned staff, maintenance needs, reservations, and other state appropriate to its operating model.

Reconcile and implement the GDD's full catalogue across housing and offices; hotel rooms and suites; food and shopping; entertainment and cinemas; services and housekeeping; utilities and maintenance; transport and parking; events and prestige facilities; advertising and billboards; structural connectors and outdoor facilities.

Rooms must behave according to their actual role. A condominium sale, an office lease, a hotel booking, a restaurant sale, and a cinema ticket cannot all be the same generic periodic income function.

Provide representative content early, then complete every approved catalogue entry. Keep deferred entries visible in the feature matrix. A room is not complete until it has functioning gameplay, UI, persistence, validation, and appropriate tests.

## 8. PEOPLE, SCHEDULES, AND PATHFINDING

Simulate residents, office workers, hotel guests, customers, tourists, visitors, and service staff using the categories defined by the GDD.

Give them appropriate destinations, schedules, needs, budgets or spending behavior, satisfaction, patience, and departure conditions. Include commuting peaks, lunch demand, shopping and entertainment trips, hotel arrivals/departures, and service tasks where applicable.

Separate logical people from visual sprites. Off-screen people must still consume transport capacity, contribute to queues, generate demand, and affect operations. Camera position must never create or destroy economic activity.

Use a building transport graph covering horizontal circulation, entrances, stairs, directional escalators, elevator stops, transfer lobbies, and supported connectors. Include access permissions, travel time, congestion, and reasonable transfer penalties in route selection.

Cache routes against topology versions and invalidate affected routes after edits or failures. Do not rebuild every person's route every render frame. Represent unreachable destinations explicitly and support retry, alternative destinations, complaints, cancellation, or departure.

Destination capacity, entry, exit, and reservations must remain consistent. A logical person must not simultaneously occupy a room, elevator, and queue. Aggregation may reduce computation but must preserve demand and capacity accounting.

## 9. ELEVATORS AND TRANSPORT

Implement actual elevator simulation, not decorative moving boxes or instant floor-to-floor teleportation.

Provide configurable banks, cars, served floors, local/express behavior, service access, capacity, travel time, acceleration where useful, door timing, direction, dispatch rules, maintenance, and breakdown state according to the GDD.

Start with a deterministic, understandable dispatch algorithm and document it. Support hall calls, destination requests, matching-direction boarding, full cars leaving passengers behind, transfers, car assignment, idle behavior, and starvation prevention.

Model car states such as idle, traveling, opening doors, unloading, boarding, closing doors, and out of service. Keep boarding and alighting transactions consistent with passenger locations and queue ownership.

Treat bank coordination separately from shaft geometry. Do not introduce physically overlapping cars or multi-car shaft behavior unless explicitly designed.

Show queues and useful metrics: average wait, high-percentile wait, utilization, abandoned trips, and overloaded floors. Provide inspection and configuration interfaces for stops, banks, cars, and access policies.

Test rush-hour demand, insufficient capacity, disconnected stops, demolished routes, breakdowns, and recovery. Preserve waiting passengers and in-flight journeys through save/load.

## 10. ECONOMY AND BUSINESS OPERATIONS

Implement construction spending, upkeep, salaries, utilities, maintenance, rent, retail sales, hotel revenue, entertainment income, advertising, and other GDD-defined financial flows.

Use an auditable transaction ledger with timestamps, categories, entity references, and clear summaries. Scheduled charges and revenues must occur exactly once, including after save/load or speed changes. Never generate income from closed, inaccessible, unoccupied, or unserved facilities without a documented business rule.

Residential and office operations need occupancy, demand, contracts or leases, rent, complaints, departures, and their approved conditions. Condominium ownership needs distinct sale, refund, demolition, and replacement rules that cannot be exploited for unlimited money.

Hotels need reservations or arrival demand, check-in, room assignment, stay duration, check-out, cleaning, dirty/clean states, staff access, room quality, and the GDD's VIP or luxury expectations. An unclean or unavailable room must not be sold repeatedly as ready inventory.

Food and retail need operating hours, customer throughput, prices, demand, satisfaction, and the approved menu/product controls. Cinemas need film/program selection, scheduled screenings, capacity, ticket sales, and audience satisfaction. Events need scheduling, preparation, capacity, attendance, and operational costs.

Implement parking capacity, vehicle arrivals/departures, fees, and pedestrian connections. Model advertising and billboards through their actual approved contracts or effects.

Keep balance values in data. Label invented starting values as provisional game tuning, not real-world economic facts or verified Tower II values. Implement borrowing, taxes, insurance, or similar systems only where required by the GDD or explicitly accepted as additions.

## 11. SERVICES, INCIDENTS, AND LOCATION RULES

Implement the GDD's cleaning, housekeeping, maintenance, security, waste handling, utility coverage, staffing, and emergency systems. Services need capacity, coverage, cost, and observable effects.

Where service work requires physical access, use routed tasks with travel and work time. Workers must not magically clean or repair disconnected rooms. Failures should create clear warnings and recovery actions.

Implement approved incidents and disruptions with trigger rules, consequences, response options, cooldowns, and difficulty controls. Do not create arbitrary unavoidable disasters that destroy a stable tower without readable warning or counterplay unless an approved scenario specifically requires them.

Represent subway network facts, site connectivity, waterfront suitability, arrival profiles, demand modifiers, and location-specific facilities as separate data. Verify current network eligibility using authoritative sources and record source/date; do not guess from a city's reputation or a proposed transit project. Pin the verified eligibility snapshot for a release instead of changing it silently during play.

Subway and dock arrivals must create real demand and journeys subject to platform, terminal, building, and transport capacity. Docks remain Hawaii-only. Missing local transport options must not make an otherwise valid location unwinnable.

## 12. PROGRESSION, MODES, AND ENDGAME

Implement all seven ranks with data-driven prerequisites, unlocks, evaluation timing, notifications, and explanations of unmet requirements. Preserve Rank 5's full floor-cap unlock and the distinct Rank 6/7 themes.

Evaluate relevant population, profitability, reputation, transport performance, service coverage, facility diversity, and luxury quality according to the GDD. Do not hardcode every rank as only a population threshold.

Progression must work in all 13 locations. Gate location-exclusive objectives with appropriate alternatives. Neither Hawaii-only docks nor unavailable subway infrastructure may become a universal requirement.

Implement the GDD's tutorial, scenarios, sandbox settings, difficulty options, victory/failure conditions, and continued play after reaching the final rank. Sandbox may relax economic or rank rules when configured, but it must not silently bypass the confirmed location restrictions.

Use original scenario text and data-driven objectives. Provide at least one tested normal progression route for every location profile before declaring progression complete.

## 13. PRESENTATION, UI, AUDIO, AND USABILITY

Keep the detailed architectural cutaway direction throughout development. Use consistent pixel scale, readable floor heights, modular furnishings, tiny animated people, clear transport connections, and distinct room silhouettes.

Temporary original placeholder art is acceptable for early milestones, but do not label it finished production artwork. Record asset replacement needs separately from functional completion. Missing assets must not crash the game; use recognizable temporary fallbacks.

Provide camera panning, zooming, floor navigation, selection, tooltips, construction categories/search, placement costs, room inspection, editable prices, transport controls, staffing controls, and management reports.

The HUD should expose money, population, rank, date/time, speed controls, notifications, and critical warnings. Add useful overlays for traffic, satisfaction, noise, services, accessibility, and utilities where the corresponding systems exist.

Include main menu, new game/location selection, save/load, pause menu, settings, tutorial guidance, and contextual help. Support scalable text, keyboard shortcuts, remapping where practical, and warnings that are not color-only. UI clicks must not accidentally place or demolish rooms behind panels.

Include appropriate construction, interface, elevator, ambient, and notification audio through replaceable assets and volume buses. No control should appear functional while silently doing nothing; unfinished controls must be visibly disabled with an explanation.

## 14. SAVE/LOAD AND DATA SAFETY

Build persistence early. Use versioned, validated save data containing the authoritative world, simulation time, random state, next IDs, finances, progression, buildings, room states, people, journeys, queues, elevator states, reservations, service tasks, incidents, and pending scheduled events.

Capture a consistent simulation boundary. Store stable IDs and serializable state, not Godot node references or executable callbacks. Rebuild presentation and derived caches after loading without changing authoritative outcomes.

Implement manual saves, configurable rotating autosaves, recoverable backups, and safe temporary-file replacement. Handle corrupt/truncated saves, unavailable storage, unknown content IDs, and unsupported versions with clear errors. Do not overwrite the last good save during a failed operation.

Define a migration strategy and test at least one schema migration once the schema changes. Never execute code embedded in content or save files. Loading invalid data must not partially replace the current session.

## 15. AUTOMATED TESTING AND PERFORMANCE

Create a repeatable test suite, headless integration checks, and desktop smoke-test instructions. Pin test dependencies. Include setup and commands that actually match the repository.

Cover construction transactions and bounds; floor indices -10/0/249 and invalid neighbors; rank-gated construction; graph connectivity; route invalidation; elevator capacity, direction, and starvation; no duplicated passengers; exact-once finances; hotel cleaning/assignment; service access; progression and all location gates; save corruption; save/load during active travel; and content-definition validation.

Use fixed seeds and controlled commands. Compare equivalent simulation time at different render rates and speed settings. Verify that saving and resuming a simulation produces the same subsequent authoritative state as uninterrupted execution for supported deterministic tests.

Run integration scenarios containing morning commuting, lunch traffic, evening departures, retail/entertainment demand, hotel turnover, maintenance work, and a recoverable disruption.

Benchmark representative 20-, 100-, and 250-floor towers, including basement and dense-traffic cases. Record hardware, build type, population, visible sprites, simulation speed, frame time, simulation time, memory, and save/load duration.

Set explicit performance targets before measuring; treat 60 FPS at 1080p on a documented desktop baseline as an initial presentation target, not a result you can assert without testing. Optimize measured bottlenecks through visibility culling, batched updates, allocation reduction, route caching, and appropriate data structures. Do not remove essential simulation behavior to conceal performance failures.

## 16. IMPLEMENTATION MILESTONES

Phase 0: Audit and setup. Inspect repository and GDD, resolve toolchain, create feature matrix and assumptions log, establish build/test commands, and verify an executable project baseline.

Phase 1: Construction foundation. Deliver a launchable scene with camera controls, the building model, floor and room placement, validation, demolition, selection, a small functional HUD, costs, and automated construction tests.

Phase 2: Real movement. Add entrances, corridors, stairs, the first working elevator bank, logical people, schedules, visible journeys, queues, accessibility warnings, and transport tests. Demonstrate that workers can actually reach an office and leave again.

Phase 3: Playable 20-floor MVP. Integrate representative residential, office, food, hotel, and service facilities; basic finances; satisfaction; early progression; save/load; and an understandable build-operate-improve loop. Run a multi-day simulation without state corruption.

Phase 4: Full business systems and catalogue. Complete distinct operating models, hotel workflows, condo rules, food/menu controls, cinema programming, events, advertising, and every approved facility definition with its UI and persistence.

Phase 5: Advanced transport and geography. Complete elevator configuration, service routing, escalators, transfers, parking, connected wings/skybridges, eligible subway connections, Hawaii-only docks, and all 13 location profiles.

Phase 6: Complete progression and scenarios. Implement seven ranks, approved incidents, tutorial/scenario objectives, failure/recovery, endgame, and the 250-floor/10-basement rules. Verify location-specific progression remains achievable.

Phase 7: Presentation and usability. Complete management screens, overlays, onboarding, accessibility, settings, audio integration, and art integration. Track unfinished production assets explicitly.

Phase 8: Release candidate. Close approved feature gaps, run regression and long-session tests, profile large towers, verify clean-checkout builds, produce the desktop export, smoke-test it outside the editor, and document remaining defects honestly.

Respect dependencies rather than treating these phases as rigid silos. For example, introduce service access and persistence when their first dependent systems appear. Each phase must extend the same game, not create a separate throwaway prototype.

## 17. WORKING RULES AND CONTINUATION

Before each implementation batch, select one coherent feature slice and state its acceptance criteria. Implement it completely, connect it to the game, add tests, run available checks, fix regressions, and update progress records.

Never leave pseudocode, empty success-returning methods, fake data flows, or TODO placeholders in a feature marked complete. Future features may remain unimplemented only when honestly tracked and not presented as working gameplay.

With workspace access, write the files and run commands directly. Without workspace access, provide complete files with exact paths, including required scenes, project configuration, resources, and tests. Do not use “the rest is unchanged” for a file the user has never received. Provide precise patches for existing files only when the base content is known.

Do not fabricate successful builds, screenshots, benchmarks, exports, or playtests. Report which checks ran, their actual results, and which checks could not run. Distinguish code review from execution-based verification.

Maintain docs/PROJECT_STATUS.md, docs/FEATURE_MATRIX.md, docs/DECISIONS.md, docs/KNOWN_ISSUES.md, and docs/NEXT_STEPS.md. Keep them concise and accurate enough for another coding session to resume without redesigning the project.

After each batch, report the implemented behavior, changed files, exact build/run/test commands, actual verification results, unresolved defects, and next dependency-ready task. Include manual acceptance steps when visual checks are needed.

On “continue,” inspect the current repository and these status files, verify the last milestone still works, and implement the next unfinished slice. Do not restart, rewrite working systems without cause, or ask the user to restate established requirements.

Ask a focused question only when an unresolved design conflict, unavailable source, destructive change, or required external dependency genuinely blocks progress. Continue independent work without repeatedly asking permission for routine implementation.

## 18. DEFINITION OF DONE AND FIRST ACTION

The game is not finished merely because it launches, renders a tower, or contains classes named after every system.

Completion requires the approved scope to be playable end-to-end; the reconciled content catalogue to be functional; seven-rank progression and all locations to work; transport and service rules to affect actual outcomes; save/load to preserve active state; tests to pass; documented performance targets to be checked; production asset status to be accurate; and a desktop export to run outside the editor.

Unresolved reference coverage, missing production assets, failing tests, or unverified exports must remain visible. Do not hide them behind a claim of “production-ready.”

START NOW: inspect the available repository and design sources, briefly state the implementation assumptions and immediate milestone, perform Phase 0, and implement the first runnable construction slice from Phase 1. Deliver actual integrated code and test evidence in this session, not another design-only proposal.
