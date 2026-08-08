# Changelog

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
