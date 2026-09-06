# UI visual refresh verification

Version: **0.5.51.0**. Implementation date: **6 September 2026**.

Source implementation and local release validation are complete. In-game acceptance of the new build is pending. The user requested publication through the normal repository before installation and will perform the visual checks. Computer control was stopped at the user's request.

## Baseline

- Base revision: `87182489ecd6424f96dce917459da69d14db75c5`, version 0.5.50.0.
- Implementation branch: `codex/ui-visual-refresh`.
- Release tests: 483 passed, 0 failed, 0 skipped.
- Release plugin build: 0 warnings, 0 errors.
- Route-data verification passed.
- The running 0.5.50.0 Operations, Leveling, Unlocks, Income, FC Setup and Simulation settings pages were observed before installation of the redesign. These observations were at the user's existing window size; they do not certify the size/scale matrix. The saved Dalamud configuration specified 150% scale.
- The existing outer window inherited Dalamud's title color and background while the inner panels used the plugin palette. The new draw scope addresses this lifecycle mismatch.
- The baseline Release output is retained locally in ignored `artifacts/ui-refresh/baseline/` for comparison. No configuration reset or migration is required for rollback.
- No comparable Plugin Statistics samples were obtained. No performance acceptance claim is made from game FPS.

## Implemented

- `PlannerTheme` owns the graphite/teal palette and native text, title, popup, frame, button, header, tab, scrollbar, table, selection and focus styles. Cached definitions supply the push/pop counts. The disposable scope surrounds this plugin's entire `WindowSystem.Draw()` call, including window Begin, and is removed after it returns or throws.
- `PlannerTypography` owns two managed font handles, at 1.25 and 1.5 times the user's default font size. Dalamud's font atlas supplies the user font, additional language glyphs, rebuilds, scaling and unavailable-font fallback. Handles are created once and disposed after detaching the draw event and removing the windows. Body and table text retain the current font.
- Header and metric heights use the same fonts for measurement and drawing. Native navigation keeps its six entries and compact breakpoint, with neutral inactive surfaces and an active accent. Existing attention counts receive emphasis inside the same native buttons. Selected filters, primary Save buttons, status pills and callouts share the palette.
- Current-voyage progress uses the existing fraction, with a lighter background and a thin progress stroke. Parent clipping is intersected. Fleet headers, submarine rows, fuel panels and forecasts retain their existing values and table behavior.
- Settings and FC preference rows align labels/help with controls when space permits and stack below the existing groups at narrower widths. These groups do not add ImGui ID scopes. The existing page and staged/automatic save handlers are retained.
- Route-picker destination actions align at the right, wrap when necessary, and keep their existing order and IDs. The initial modal size is constrained to the active viewport; its ordinary default is retained. Validation explanations wrap and Use route receives primary styling. Route selection still updates only the outer FC draft.
- Income retains its four metrics, all periods/filters/scopes, nine detail columns and the approved content-sized FC identity plus three equal metric groups. The chart uses the shared colors, retaining zero/missing/partial marks, aggregation, time boundaries and cache behavior.
- Unlocks retains map coordinates, hit testing, prerequisites, search, remaining context and selection. Node fills are quieter and selected paths/rings remain visible. Long map/route/chart tooltips use a viewport-clamped preferred width and explicit wrapping.
- Save bars measure the state text, wrapped action rows and hint. Existing save/discard/reset and FC-navigation action order, popup IDs and consequences remain unchanged.

## Local candidate validation

Commands executed from the repository, using the installed SDK 15 development libraries and existing restored dependencies:

```powershell
dotnet test tests/SubmarineEtaPlanner.Tests/SubmarineEtaPlanner.Tests.csproj --configuration Release --no-restore
dotnet build src/SubmarineEtaPlanner/SubmarineEtaPlanner.csproj --configuration Release --no-restore
pwsh -NoProfile -File ./tools/Verify-RouteData.ps1
git diff --check
```

- Final Release tests: **483 passed, 0 failed, 0 skipped**. The manifest regression assertion was updated for 0.5.51.0 and now also checks that the source/repository changelogs match. The initial run after the version change caught the previous version assertion; the corrected suite passed.
- Final Release plugin build: **0 warnings, 0 errors**.
- Bundled route-data SHA-256: `24996254FAB3FFC4A74F1AFA2C9212732888A0C6387DAB026B75EA566B6D67FF` — unchanged and verified.
- Core assembly SHA-256, both baseline and candidate: `ADDECE6BC11BE926008910E1A2C8636F42F719055B6A23FAC7C18FE4383A8E9F`.
- Explicit ImGui ID string inventory matched across every modified existing UI source file. Manual diff review checked the retained parent ID scopes, native control calls, table flags, conditional fields and draft mutation/save paths. This is source evidence, not a click-test result.
- No changes to Core algorithms, tracker/fuel readers, route data, configuration schema/defaults, or persistence methods.
- Candidate packaging follows the existing CI fallback: manifest, plugin/Core assemblies, runtime dependencies, dependency manifest and route data. The local candidate is `artifacts/ui-refresh/SubmarineEtaPlanner-0.5.51.0.zip`.
- Version 0.5.51.0 is consistent in the project, generated package manifest, repository manifest and existing manifest regression test. Publishing to `main` runs the repository's normal build/Pages workflow.

The test project does not render ImGui. These results establish compilation, logic regression coverage and source/package consistency; they do not certify appearance or pointer/keyboard interaction.

## User acceptance still to perform on the installed update

Check every page at 1040 × 700 and 780 × 520 logical units, each at 100% and 150% Dalamud scale. Also check just above/below the existing sidebar and compact Operations breakpoints. Record screenshots and any clipping, wrapping or alignment issues against the actual dimensions and scale.

- Operations: role/sort/search and all four attention filters; clear actions; independent favorites; collapsed/expanded FCs; all submarine details and companions; current/conditional/pinned routes; low/critical/unavailable fuel and its full details.
- Leveling: every column, expanded voyage forecasts, diagnostics, milestones, alternatives and partial/uncertain results. Confirm useful density and complete names at small widths.
- Income: all periods, scope and Show all FCs; centered metric groups and right-aligned values; all nine detail columns with horizontal overflow. Inspect chart open/collapsed, exact tooltips, zero/missing days and incomplete periods.
- Unlocks: all maps/states, active attempts, cross-map search, persistent selection, Remaining only, prerequisites and FC/map changes. In particular, review Map 7 A's long hover preview and persistent details.
- FC Setup: favorite auto-save, inherited/overridden target and strategy, assignments, collection delays, fuel sources/observations/reserve and preview. In the route picker, exercise Up/Down/Remove, search, validation, Use route and Cancel.
- Drafts/dialogs: preserve an FC draft across ordinary page changes; exercise another FC's setup shortcut with Save/Discard/Cancel; verify favorite auto-save survives discard; verify route edits persist only after outer Save. Load defaults for review and inspect/discard the staged settings, including Display. Exercise the observation-forgetting confirmation without losing wanted observations.
- Settings: every section and conditional custom-strategy field, all help, build-profile actions, primary/disabled Save, Discard and Reset. Check automatic Display preferences and chart visibility alongside unrelated drafts.
- Fonts/theme: accented and non-Latin names, large values, bright/dark game scenes, native title controls, hover/disabled/focus states, popup readability and no style leakage to another Dalamud window.
- Runtime: refresh/cancel/close/unload/reload, missing/stale tracker and incomplete/queued data. Compare repeated Plugin Statistics samples in equivalent scenes with the window closed, fleet details expanded, chart open/closed and Unlocks visible; check font/texture handle lifetime.

These checks remain pending until the user reports results. Prior pending checks in the compact-interface and Income-chart verification records are not implicitly marked passed by this release.
