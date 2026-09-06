# Recorded income chart verification

## Version 0.5.50.0

The chart adds an automatically saved `ShowIncomeChart` preference, defaulting to visible for existing configurations. FC/global drafts operate on their existing settings/preferences, independently of this top-level display preference. The approved FC column layout is unchanged.

The chart uses canonical voyage observations and the same scoped FC membership and inclusive rolling-period boundaries as Income. Recorded-zero markers require observations. Days without entries remain unknown; grouped bars report the observed sum and retain missing-day counts rather than estimating unrecorded income. Summary arithmetic and salvage-only voyage counts are unchanged.

## Automated coverage

Local validation on 2026-09-06:

- Release test suite: 483 passed, 0 failed, 0 skipped.
- Release plugin build: passed with 0 warnings and 0 errors.
- Bundled route-data verification: passed (SHA-256 `24996254FAB3FFC4A74F1AFA2C9212732888A0C6387DAB026B75EA566B6D67FF`).
- Release package inspected: version `0.5.50.0`, both plugin/Core DLLs, manifest, and bundled route data present.

Focused coverage:

- Positive, zero, absent, empty, unavailable, and partly available history.
- Gross-total parity with existing Income metrics for Lifetime, 7, 30, 90, and 365 days, including exact boundaries and future exclusion.
- Mixed-role fleets retaining farming/leveling/paused companion history, scoped FC membership, and staggered history across FCs.
- Local midnight, spring/autumn DST days, incomplete calendar boundaries, Monday-based weeks, leap days/month boundaries, and capped long-Lifetime grouping.
- Finite axis ranges for zero and large values.
- Cache reuse across sort/favorite ordering; no work while collapsed; invalidation for history-only changes despite unchanged forecast fingerprints, scope, period, timezone rules, midnight, minute expiry, clock rollback, future-return entry, and inclusive rolling-window expiry.
- SQLite reader integration distinguishes an empty loot table from a missing/malformed table while retaining operational state.

## In-game acceptance pending installation

- Review the chart at 1040 x 700 and 780 x 520, at 100% and 150% UI scaling. Check wrapped legends, long date/FC names, sparse histories, large gil values, and long Lifetime ranges.
- Check that titles, axes, bars, zero markers, hatching, incomplete markers, and full-slot tooltips are readable without overlaps or horizontal scrolling.
- Check Income scope/role/period interactions and unavailable-history notices while forecasts are progressive or partial.
- Confirm collapse/expand saves without a forecast refresh, survives reload, and survives discarding unrelated global/FC drafts.
- Compare Plugin Statistics under equivalent conditions with the chart displayed and collapsed. Automated cache tests are not a substitute for in-game frame-time verification.

No in-game visual or Plugin Statistics acceptance claim has been made before the user installs this version.
