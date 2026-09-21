# Current-farming income projection verification

Release version: **1.1.2** (assembly and repository version **1.1.2.0**), with compact FC projection rows and previous-rank approximations.

## Local checks — 2026-09-21–22

- Release test suite with previous-rank fallback: **628 passed, 0 failed, 0 skipped**.
- Release plugin build with the installed Dalamud development libraries: **0 warnings, 0 errors**.
- Release 1.1.2 revalidation: project, compiled assembly, generated manifest, repository manifest, and packaged `latest.zip` report **1.1.2.0**. Packaged plugin/Core assemblies, manifest, and route data are present; generated and repository changelogs match.
- Bundled route data verified with SHA-256 `24996254FAB3FFC4A74F1AFA2C9212732888A0C6387DAB026B75EA566B6D67FF`.
- `git diff --check`: passed.

Commands:

```powershell
dotnet test tests/SubmarineEtaPlanner.Tests/SubmarineEtaPlanner.Tests.csproj --configuration Release --no-restore
dotnet build src/SubmarineEtaPlanner/SubmarineEtaPlanner.csproj --configuration Release --no-restore
pwsh -NoProfile -File ./tools/Verify-RouteData.ps1
git diff --check
```

## Automated coverage

- 30/90/365-day arithmetic, including the four-submarine 73-million-gil annual example and fractional values preserved through aggregation.
- 0/9/10 observations, recorded zero-gil returns, own-history precedence, pooled fallback without duplicate counting, FC/submarine identity separation, and hidden FC exclusion.
- Pool eligibility independent of current donor role; exact sector-set and surveillance/retrieval/favor matching; inclusive history boundaries and future-return exclusion.
- Manual/automatic roles, per-FC targets, pinned-route order, collection-delay overrides, missing setup, valid zero estimates, partial totals, and scoped FCs without farmers.
- Scope/favorite changes preserve learned rates and cache reuse; history-only changes invalidate even with unchanged leveling fingerprints. Saved route/role/delay/target/visibility changes, rolling-window expiry, future entries, and clock rollback invalidate correctly.
- Charts preserve exact summary totals, including fractional boundaries, unequal calendar months, leap days, 23/25-hour DST days, zero/unavailable results, large finite axes, and collapsed-chart caching.
- The actual runtime Configuration source is linked into the core-only tests with a minimal host interface. Legacy settings retain History preferences and receive projection defaults; invalid enum values normalize; saved projection preferences round-trip.
- Previous-rank matching uses current parts resolved at exactly one earlier rank. The four selection priorities are covered, including exact pooled history ahead of sufficient previous-rank own history. Exact/previous-rank shortages remain separate and are never combined to reach ten.
- Previous-rank 9/10 thresholds, zero-gil returns, duplicate suppression, hidden FC exclusion, paused/leveling donors, mismatched stats/sectors/ranks, unresolved prior builds, changed parts, and the rank-1 lower bound are covered. Current setup validation still gates estimates.
- Approximation arithmetic uses current speed, pinned ordered route, and collection delay. Synthetic regressions model the Meow/Cute 203/252/222 to 203/254/223 transition with full coverage including one approximation each, and GLOSS's insufficient four-return new route.
- Approximation preference defaults on and persists both values. Toggle, visibility, history-only updates, expiry, future observations, and rollback invalidate correctly; display scope/favorites reuse matching indexes. Identical parts/rank resolve the previous build once per calculation.
- Exact-history promotion updates provenance and chart state even when gil/day is unchanged. Mixed exact/approximate/unavailable totals include supported zero approximations and preserve chart/summary parity.

## UI verification status

The plugin compiles against the installed ImGui/Dalamud bindings. Source review confirms projection details use the existing responsive table allocator with horizontal scrolling, compact FC headings, wrapping controls/text, and scaled chart drawing. In 1.1.2, the standalone FC exact/approximate line is removed entirely: collapsed rows return immediately after their header/tooltip, and expanded rows proceed to the existing detail content without reserving space for that line. The Basis column, header figures/colors and tooltips, overall summary, and chart information remain available. History calculations and its chart renderer are unchanged; projection settings are top-level automatic preferences, outside FC/global drafts.

**In-game visual and interaction acceptance is pending.** The automated test host does not render ImGui, and these checks do not certify appearance or input behavior in FFXIV.

Check the installed candidate at **1040 × 700** and **780 × 520**, each at **100% and 150% UI scale**:

- Switch History/Projection and all three horizons; check summary wrapping and saved selections after reopening/reloading.
- Toggle **Allow previous-rank approximations** and reload the plugin; verify its saved value, immediate recalculation, and independence from staged FC/global edits.
- Inspect long FC/submarine names, large amounts, mixed fleets, partial totals, all-unavailable results, supported zero estimates, and a scoped FC with no farmers.
- Check collapsed and expanded FC rows contain no standalone exact/approximate breakdown line or leftover space. Check exact-only, approximate, partial, and unavailable fleets, and confirm expanding/collapsing restores compact list spacing.
- Inspect **Approximate** submarine Basis cells and exact/approximate breakdowns in the overall summary, chart, and FC header tooltips, including totals that are both Partial and include approximations. Confirm disabled/unresolvable fallback and both history shortages have readable explanations.
- Expand FCs and scroll the detail table horizontally; verify complete rows, readable columns, setup shortcuts, and provenance/reason tooltips.
- Check rank-transition tooltips for current/sample stats, reference rank, selected sample count, and own/pooled source. After enough exact returns arrive, confirm the approximation label disappears even when the monetary rate stays the same.
- Collapse/expand charts; inspect daily/monthly bars, partial boundaries, tooltips, and chart-total parity.
- Confirm the chart's approximation label and breakdown agree with FC scoping and disappear when exact matching replaces the last approximation.
- Change favorites and FC scope; confirm estimates stay stable. Hide/show a donor FC and refresh; confirm matching coverage updates.
- Save/discard unrelated staged FC/global edits; confirm projection display preferences remain saved. Compare History filters, totals, chart, and layout with the existing behavior.
- Check Plugin Statistics with the chart expanded/collapsed and after a history refresh. Automated cache tests do not substitute for frame-time measurements in game.
