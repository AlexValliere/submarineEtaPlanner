# Changelog

## 0.5.46.0

- Restored Operations fleet column titles and separate, aligned FC tag and world columns.
- Aligned Income numeric headings with their values, restored separate FC tag and world columns, and replaced oversized proportional columns with content-based widths.
- Shared column boundaries between legends and fleet rows, retaining compact wrapped layouts in narrow windows.

## 0.5.45.0 — compact interface refresh

- Reduced the decorative header, wrapped narrow controls and explanatory text, and softened table styling.
- Simplified Operations to submarine, status, return, route, and action, with expandable planning details and a narrow layout.
- Added attention filters for collection, today's returns, low fuel, and missing farming setup.
- Added independently saved FC stars and contextual Setup, Unlock map, Income, and Fuel setup shortcuts, with safe handling of staged FC edits.
- Condensed fuel into expandable summaries, retained collapsed-FC warnings, and cached route/fuel presentation calculations.
- Added persistent sector selection, cross-map search, prerequisite links, and remaining-sector filtering.
- Simplified Income FC headers while retaining the expanded statistics and existing gross-income calculations.
- Standardized saving labels and documented automatic saving. The optional income chart is deferred.

## 0.5.44.0

- Right-aligned Income gil and voyage values, removed Gross gil highlighting, and shortened the average column header.

## 0.5.43.0

- Filled trailing space in the Income and FC Setup tables while preserving their compact and scrollable layouts.

## 0.5.42.0

- Tightened the Operations and Leveling submarine tables with a compact Rank header and a route column that fills spare width.

## 0.5.41.0

- Moved Income build-and-rank details to four aligned Sub #1–4 columns at the end of each FC row.

## 0.5.40.0

- Removed the unstable observed run rate from Income and kept one recorded daily average based on each selected FC set's shared history coverage.

## 0.5.39.0

- Aligned the Core library, test suite, plugin, and CI build environment on .NET 10.

## 0.5.38.0

- Limited Income voyage counts, per-voyage averages, and history coverage to returns containing tracked salvaged accessories so earlier leveling voyages do not dilute farming income.

## 0.5.37.0

- Added individual return countdowns to submarine state cells in expanded Leveling fleet rows.

## 0.5.36.0

- Added individual return countdowns to submarine state cells in expanded Operations fleet rows.

## 0.5.35.0

- Repositioned Submarine ETA Planner as a Free Company fleet-operations tool across its README and installer metadata.

## 0.5.34.0

- Improved sector unlock tooltips and tables with concise FC-relative remaining paths beginning at the latest accessible prerequisite.

## 0.5.33.0

- Improved sector unlock maps with deterministic graph-aware positioning that keeps nodes readable while retaining approximate geographic orientation.

## 0.5.32.0

- Added FC-specific sector unlock maps with live unlocked, explored, discoverable, locked, and active-attempt states, complete unlock paths, and cross-map connections.

## 0.5.29.0

- Added player-friendly fuel forecasting, responsive content-aware table columns, compact FC route displays with full-name tooltips, a validated farming-route picker, and persistent FC Setup actions.

## 0.5.28.0

- Added staged FC ceruleum source and reserve controls with FC-scoped live and timestamped local observations, immediate stale-safe runway previews, and confirmed local snapshot removal.

## 0.5.27.0

- Forecasted ceruleum runway from live, manual, or schedule-compatible last-observed stock, with grouped departure simulation, reserve-aware refill deadlines, and explicit stale-stock warnings.

## 0.5.26.0

- Added recurring farming dispatch-cycle projections with per-submarine collection-delay overrides, next-departure timing, and explicit tracking of already-paid current voyages.

## 0.5.25.0

- Resolved each farming-role submarine's effective repeated route from its pinned route or current ordered SubmarineTracker route, with explicit build, sector, fuel, and duration validation warnings.

## 0.5.24.0

- Persisted manually observed character ceruleum inventories locally with throttled atomic writes, character and FC identity updates, timestamp refreshes, and corrupt-file recovery.

## 0.5.23.0

- Added a framework-thread-only, read-only reader for the logged-in character's ceruleum tank inventory, identity, home world, and free company ID.

## 0.5.22.0

- Added read-only fuel-stock observations and deterministic manual, selected-character, and automatic resolution without summing inventories across characters.

## 0.5.21.0

- Added historical ceruleum tank totals, tanks per voyage, gross gil per tank, and gross gil aggregation by deterministic route signature.

## 0.5.20.0

- Corrected farming fuel configuration by adding an explicit stock-source mode and a forward migration from configuration version 12 to 13.

## 0.5.19.0

- Separated historical recorded income averages from observed submarine run rates, corrected FC and global aggregation for staggered tracking start dates, and added explicit UI labels and sorting for both metrics.

## 0.5.18.0

- Read canonical voyage history from SubmarineTracker and derived the existing recorded-salvage summaries from those observations without changing operational ETA cache fingerprints.

## 0.5.17.0

- Added canonical historical voyage observations and pure grouping of submarine loot rows with deterministic sectors, salvage totals, and inconsistent-stat warnings.

## 0.5.16.0

- Added route ceruleum fuel and ordered operational profiles with explicit unknown-sector reporting.

## 0.5.15.0

- Added staged per-submarine assignment and pinned farming route controls to FC Setup, including route validation and catalog-name previews.

## 0.5.14.0

- Replaced English action strings in Core projections with typed recommended actions, centralized their UI wording, and made explicit Farming and Paused recommendations role-aware.

## 0.5.13.0

- Honored mixed submarine assignments in ETA simulations so only leveling targets receive future routes or determine FC completion, while relevant farming and paused voyages can still apply shared unlock effects.

## 0.5.12.0

- Passed role-aware submarine scope through ETA calculations and invalidated cached forecasts when assignments change without changing simulator behavior.

## 0.5.11.0

- Added explicit per-submarine leveling, farming, and paused roles with mixed-role FC summaries and filtering.

## 0.5.10.0

- Added versioned farming configuration storage for submarine assignments, pinned routes, fuel holders, manual tank inventory, reserves, and per-submarine collection delays without changing planner behavior or UI.

## 0.5.9.0

- Exposed decoded numeric game Free Company IDs while preserving existing tracker BLOBs and hexadecimal settings keys.

## 0.5.8.0

- Split fleet UI pages, shared components, and custom headers into focused partial files without changing planner behavior.

## 0.5.7.0

- Refactored core fleet presentation into focused Operations and Farming modules without changing planner behavior.

## 0.5.6.0

- Compactly display fully upgraded tracker builds using community notation (for example, `S+C+U+S+` becomes `SCUS++`) throughout Operations, Leveling, and Income.
- Keep partial upgrades and planned-build codes unchanged.

## 0.5.5.0

- Restored current tracked submarine build codes to Operations and Leveling detail tables and fleet-header tooltips.
- Added current build-and-rank context to Income headers and submarine detail rows for easier fleet-income comparison.
- Kept Income headers on one compact line and clarified that build and rank values reflect the current tracker snapshot.

## 0.5.4.0

- Added All fleets, Leveling, and Farming filters to Income, with Farming as the default aggregate view.
- Added a rolling one-year income period while preserving existing saved Lifetime selections.
- Simplified Income labels to Gil / day, Gil / voyage, and Voyages, with clearer current-mode and historical-data guidance.

## 0.5.3.0

- Corrected Operations sorting so actions affect only the actions-first order, while farm-ready ETA and FC name follow their selected keys.
- Brought Leveling fleet headers and submarine tables up to the aligned, responsive Operations layout and restored complete per-submarine voyage forecasts with route-purpose explanations.
- Added aligned Income metrics, compact one-to-four-row detail tables, and stable FC expansion while live gil/day values change.

## 0.5.2.0

- Replaced the redundant Returning soon view with All fleets, Leveling, and Farming filters.
- Simplified submarine states and combined current and projected ranks into one compact column.
- Replaced percentile terminology with plain-language expected and likely readiness dates.

## 0.5.1.0

- Restored aligned Operations header columns and current-voyage progress backgrounds.
- Simplified collapsed fleet rows to rank-only rosters with details shown only after expansion.
- Combined state and next-action guidance and sized expanded tables to their one-to-four submarine rows.

## 0.5.0.0

- Replaced Dashboard with favorite-first Operations and added fleet-wide Leveling, Income, and FC Setup screens.
- Added per-FC target-rank and leveling-strategy overrides with isolated incremental recalculation.
- Added action guidance, route EXP and rank projections, complete submarine rosters, fleet completion ranges, and persistent view controls.
- Added voyage-level salvage history and 7/30/90-day and lifetime gross-gil reporting, including valid zero-salvage returns.
- Consolidated the existing configuration pages into one Settings screen with internal tabs.

## 0.4.17.0

- Fixed the dashboard crash caused by transient SubmarineTracker rows with a missing return time and stale route while collecting or redispatching submarines.
- Prevented those transition rows from being treated as active voyages in ETA calculations.

## 0.4.16.0

- Centered compact sidebar icons and aligned expanded navigation icons and labels consistently.

## 0.4.15.0

- Aligned FC tags, worlds, target ETAs, salvage values, and current voyages into responsive dashboard columns.
- Added a two-line FC header layout for narrow windows while preserving voyage progress and tooltips.
- Simplified ready-FC submarine tables by removing leveling-only ETA, voyage-count, and next-route columns.

## 0.4.14.0

- Fixed expanded submarine progress backgrounds drawing beyond the table and planner window when rows are clipped.

## 0.4.13.0

- Added live current-voyage progress backgrounds and next-return countdowns to FC dashboard headers.
- Added per-submarine voyage progress, countdowns, and exact timing tooltips to expanded FC tables.
- Preserved honest countdown-only states when route or build data cannot provide a reliable percentage.

## 0.4.12.0

- Added each FC's compact recorded salvage-gil total to its always-visible dashboard header.
- Added exact gross-value guidance when hovering completed and pending FC headers.
- Removed the duplicate salvage pill from expanded FC content while preserving detailed submarine breakdowns.

## 0.4.11.0

- Replaced the ambiguous future-only voyage count with an inclusive `Voyages left` lifecycle display.
- Kept underway and returned-but-uncollected voyages counted until SubmarineTracker records their actual result.
- Added clear underway, ready-to-collect, and tracker-syncing states with contextual guidance.

## 0.4.10.0

- Added per-submarine and per-FC gross NPC value totals for salvaged accessories recorded by SubmarineTracker.
- Added itemized salvage quantities, per-item prices, voyage counts, and recorded-history date ranges to dashboard details.
- Read current NPC prices from local game data while retaining verified offline fallback values.

## 0.4.9.0

- Added practical first-install defaults centered on target rank 90 and realistic turnaround timing.
- Added a confirmed all-tabs reset that stages defaults for review before Apply & refresh.
- Preserved every saved setting for existing users during upgrade.

## 0.4.8.0

- Added exact ranked route search to reuse build-specific route scoring across forecast trials.
- Replaced clear-all route caching with bounded LRU eviction and added detailed search diagnostics.
- Reduced multi-sector unlock searches to the smallest applicable route bucket.

## 0.4.7.0

- Changed voyage forecast route cells to compact sector codes.
- Kept full localized destination names available in numbered hover tooltips.

## 0.4.6.0

- Added compact current and next-route columns to the expanded FC submarine table.
- Kept full localized destination names and conditional unlock outcomes available in route tooltips.

## 0.4.5.0

- Restored median ETA and ready-now headlines for unchanged forecasts reused during incremental refreshes.
- Kept reuse visible through dashboard metrics and the expanded `Up to date` status pill.

## 0.4.4.0

- Fixed recommended-leveling forecasts getting stuck when a main-path sector had an earlier sibling unlock prerequisite.
- Applied the same ordered prerequisite handling to submarine-slot progression.
- Clarified the distinction between total voyage EXP, per-voyage EXP, and EXP/hour in route diagnostics.

## 0.4.3.0

- Added deterministic semantic fingerprints for each FC and its submarines.
- Reused unchanged complete forecasts when refreshing after SubmarineTracker database changes.
- Added up-to-date and waiting-for-SubmarineTracker states while preserving previous results.
- Kept explicit UI and `/seta refresh` actions as full recalculations.

## 0.4.2.0

- Added an MIT project license, complete SubmarineTracker notice, route-data provenance, and AI-use disclosure.
- Added compatibility warnings when live game destinations are newer than bundled route or unlock data.
- Added a real SQLite integration test for the supported SubmarineTracker schema.
- Removed WAL journal-mode negotiation from read-only database connections.
- Added reproducible route-data verification, release checklists, D17 submission guidance, and downloadable CI artifacts.
- Fixed the fallback release package to include `CalculatedData.msgpack`.

## 0.4.1.0

- Streamed FC forecasts progressively and displayed queued/calculating states immediately.
- Applied calculation deadlines independently per FC.
- Added probability convergence stopping and expanded route caching for much faster complete refreshes.

## 0.4.0.0

- Added FC-wide probabilistic sector-unlock forecasting with deterministic P10/P50/P90 estimates.
- Added concurrent unlock-attempt modeling, conditional next routes, and an editable unlock-success assumption.

## 0.3.x

- Introduced the ocean dashboard, navigation rail, cards, status treatments, and icon-led controls.
- Added `/seta` commands, dependency checks, configurable target ranks, route-name tooltips, current-voyage display, and stale-data detection.
