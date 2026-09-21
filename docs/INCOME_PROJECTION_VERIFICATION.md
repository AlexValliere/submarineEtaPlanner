# Current-farming income projection verification

Release version: **1.1.0** (assembly and repository version **1.1.0.0**).

## Local checks — 2026-09-21–22

- Release test suite: **593 passed, 0 failed, 0 skipped**.
- Release plugin build with the installed Dalamud development libraries: **0 warnings, 0 errors**.
- Release 1.1.0 revalidation: project, compiled assembly, generated manifest, repository manifest, and packaged `latest.zip` all report **1.1.0.0**. Packaged plugin/Core assemblies, manifest, and route data are present; generated and repository changelogs match.
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

## UI verification status

The plugin compiles against the installed ImGui/Dalamud bindings. Source review confirms projection details use the existing responsive table allocator with horizontal scrolling, compact FC headings, wrapping controls/text, and scaled chart drawing. History calculations and its chart renderer are unchanged; projection settings are top-level automatic preferences, outside FC/global drafts.

**In-game visual and interaction acceptance is pending.** The automated test host does not render ImGui, and these checks do not certify appearance or input behavior in FFXIV.

Check the installed candidate at **1040 × 700** and **780 × 520**, each at **100% and 150% UI scale**:

- Switch History/Projection and all three horizons; check summary wrapping and saved selections after reopening/reloading.
- Inspect long FC/submarine names, large amounts, mixed fleets, partial totals, all-unavailable results, supported zero estimates, and a scoped FC with no farmers.
- Expand FCs and scroll the detail table horizontally; verify complete rows, readable columns, setup shortcuts, and provenance/reason tooltips.
- Collapse/expand charts; inspect daily/monthly bars, partial boundaries, tooltips, and chart-total parity.
- Change favorites and FC scope; confirm estimates stay stable. Hide/show a donor FC and refresh; confirm matching coverage updates.
- Save/discard unrelated staged FC/global edits; confirm projection display preferences remain saved. Compare History filters, totals, chart, and layout with the existing behavior.
- Check Plugin Statistics with the chart expanded/collapsed and after a history refresh. Automated cache tests do not substitute for frame-time measurements in game.
