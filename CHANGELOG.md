# Changelog

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
