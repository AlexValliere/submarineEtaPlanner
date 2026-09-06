# UI visual refresh implementation plan

Status: source implementation complete for **0.5.51.0**; local release checks pass and in-game acceptance is pending with the user. Originally prepared on 6 September 2026 against version **0.5.50.0**, commit **87182489ecd6424f96dce917459da69d14db75c5**. After requesting implementation, the user authorized pushing the update before installation and will perform the in-game review. See [verification record](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/docs/UI_VISUAL_REFRESH_VERIFICATION.md) for actual evidence and remaining checks.

The objective is to give Submarine ETA Planner a consistent graphite and teal interface while retaining every existing feature, value, control, explanation, and workflow. The work covers all six pages, shared panels, tooltips, and dialogs. Completion requires an in-game check; the browser design study is only a visual reference.

**Decisions for this implementation**

- Use the graphite and teal direction from the accepted preview as the fixed plugin treatment. The preview's alternate palettes are design exploration controls, not proposed plugin settings.
- Keep Dalamud SDK 15, the existing ImGui bindings, native interaction controls, and existing Font Awesome icons. Build on `PlannerUi`.
- Preserve the current page order, default window size, minimum size, sidebar adaptation, and table overflow behavior. Use the actual plugin's geometry and wording when the illustrative preview differs.
- Retain the user's body font and readable text size. Use managed fonts selectively for titles and summary values.
- Complete the refresh without adding animated backgrounds, particles, new blur behavior, a theme editor, density preferences, new navigation, or new collapsible sections. Existing Dalamud window behavior remains intact.
- Keep calculation, tracker, configuration, and persistence logic unchanged. No configuration migration or dependency upgrade is required by the visual work.
- Implement in small steps that each compile, with visual verification recorded as it becomes available. Preparing this plan does not build, install, commit, push, or publish a new plugin version.

**Functionality that must survive unchanged**

| Area | Required behavior |
| --- | --- |
| Shared window | Operations, Leveling, Unlocks, Income, FC Setup, Settings; native window controls; `/seta` and its existing subcommands; Refresh/Cancel; snapshot and calculation notices; unsaved-change indicator. |
| Operations | Search, all existing role/sort options, four attention filters, clear actions, favorite ordering, FC/submarine expansion, shared companions, FC shortcuts, current voyage progress, current and next routes, recommendations, rank/build/EXP/ETA/ranges/reasons. |
| Leveling | All filters/sorts, target/strategy context, fleet completion and bottleneck, all submarine columns, complete expanded forecast tables, diagnostics, route alternatives, milestones, uncertainty and incomplete-data explanations. |
| Unlocks | FC/map selectors, summary values, all destination states, cross-map search, Remaining only, stable map positions, hover previews, persistent selection, prerequisites, cross-map links, active attempts, blocking reasons and the remaining-sector table. |
| Income | All five periods, role and sort choices, FC scope and Show all FCs, four summary metrics, FC summaries, all nine submarine columns, coverage and source caveats, chart visibility and exact tooltips. |
| Income chart | Existing totals and time boundaries; daily/weekly/monthly buckets; recorded-zero markers; missing-return hatching; incomplete-period markers; local timezone handling; current visibility persistence and cache invalidation. |
| Fuel | Healthy/low/critical/unavailable distinctions, collapsed warnings, stock source/age, reserve, projected consumption, sends, runway, refill deadline, explanatory warnings, setup shortcut and active-farming-only visibility. |
| FC Setup | FC selection, automatic favorite saving, inherited/overridden target and strategy, assignments, collection delays, pinned-route controls, fuel source/holder/manual stock, reserve, observation management and runway preview. |
| Route picker | Current route, ordered selection, Up/Down/Remove, destination search/grouping, duplicate/rank/unlock/build/route validation, fuel/duration preview, Use route and Cancel; outer FC changes remain staged. |
| Settings | Simulation, Routes, Limits, Data source, Build profile and Display; every current field, conditional field, bound, help text, and build-profile action. |
| Saving and dialogs | Same saved-automatically preferences, staged edits, Save/Discard, global Reset defaults confirmation and staged preview, FC Use global target and strategy, FC-switch Save/Discard/Cancel, observation-forgetting confirmation. |

This inventory includes controls inside expanded details and conditional states. Removing information from the default view, moving essential explanations into hover-only text, changing what a filter counts, or resetting stored view preferences would violate the scope.

**Visual specifications**

Use these starting values as internal design constants; adjust only where actual game rendering demonstrates a readability problem.

| Role | Starting value / rule |
| --- | --- |
| Window surface | Graphite `#12171B`, nearly opaque for dependable contrast over the game. |
| Sidebar | `#0E1317`, separated by a quiet border. |
| Panels | `#1A2228`; use `#202A31` for input and hover surfaces. |
| Text / secondary text | `#E7EEF2` / `#A4B5BF`; check against actual rendered backgrounds and disabled-state alpha. |
| Borders | `#303D45`; fine horizontal separators and light section boundaries. |
| Selection / primary action | Teal `#66D9CA` with a muted teal selected surface. |
| State colors | Amber for attention, red for critical/error, existing positive/route/unlock distinctions retained. Always keep the text or icon that identifies the state. |
| Typography | Body follows the user. Start headings around 1.25 times body size and summary values around 1.5 times, using a small managed font set. Table data stays at body size. |
| Spacing | 4/8/12/16 logical units; measure text and scale constants exactly once. |
| Rounding | Controls about 5 logical units, panels about 7; keep dense table edges simple. |
| Geometry | Content-driven height and measured wrapping. Do not enlarge metric/filter cards at the expense of usable fleet density. |

Selected filters should be distinguishable from ordinary buttons. Status labels should remain distinguishable from actions. Primary Save buttons receive emphasis; Discard, Refresh and navigation shortcuts use restrained secondary treatments. Existing button labels and action order remain intact.

**Execution sequence**

| Step | Deliverable | Depends on | Main acceptance condition |
| --- | --- | --- | --- |
| 0 | Baseline and behavior inventory | None | Current behavior, visual issues and performance are recorded. |
| 1 | Correct theme lifecycle and shared palette | 0 | The outer window and controls share the theme without leaking it. |
| 2 | Typography, controls and common layout helpers | 1 | Shared components render at both scales and preserve interaction semantics. |
| 3 | Operations, Leveling and fuel refresh | 2 | Compact fleet workflows and all details remain complete. |
| 4 | FC Setup, Settings and dialog refresh | 2–3 | Draft/save/navigation behavior is unchanged. |
| 5 | Income/chart and Unlocks refresh | 2–4 | Existing column geometry, data semantics and selection behavior remain intact. |
| 6 | Complete regression and visual acceptance | 1–5 | Every required state is verified, with evidence and open issues recorded. |
| 7 | Release candidate and handoff | 6 | Package and notes accurately describe a visual-only update. |

Each step below is a proposed implementation checkpoint, not a claim that work has already passed.

**0. Establish a reproducible baseline**

1. Recheck the working tree and the current version before implementation. If work has advanced since this plan, preserve those changes and reconcile the inventory against the actual starting revision.
2. Start implementation on a branch such as `codex/ui-visual-refresh`. Record the base revision and retain the previous working package for comparison.
3. Use the current test fixtures and an identical snapshot/time/timezone for repeatable value comparisons. Do not use separate stochastic live forecasts or changing countdowns as equality evidence.
4. Capture all six pages at the required sizes/scales, including expanded FC/submarine/fuel views, route picker and save dialogs. Record existing problems separately so the redesign does not claim to have caused or fixed unverified issues.
5. Enumerate controls, displayed fields, stable ImGui IDs, relevant parent ID scopes, automatic/staged save paths, and persistent table flags. Follow the live call paths; some shared forecast rendering remains in `PlannerWindow.Results.cs` despite that filename.
6. Run the normal baseline Release tests, plugin build and route-data check. The previous verification reports 483 passing tests; establish the current result instead of treating that count as a new run.
7. Capture representative Plugin Statistics with the window closed, Operations collapsed/expanded, Income chart collapsed/expanded, and Unlocks visible. Use the same scene, fleets, window dimensions and refresh activity for later comparisons.

Completion evidence: baseline screenshots, version/revision, command outcomes, feature inventory, and observed performance range. Store results in a new `docs/UI_VISUAL_REFRESH_VERIFICATION.md` when implementation starts. Current pending checks in the existing verification documents remain pending until actually exercised.

**1. Correct the theme lifecycle and establish the palette**

Primary files: [Plugin.cs](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Plugin.cs), [PlannerWindow.cs](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.cs), [PlannerUi.cs](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerUi.cs). Proposed new file: `src/SubmarineEtaPlanner/Ui/PlannerTheme.cs`.

1. Move the shared palette, spacing/rounding constants and theme scope into `PlannerTheme`, keeping `PlannerUi` focused on drawing helpers. Retain forwarding color names where that avoids unrelated churn in existing renderers.
2. Push the theme in `Plugin.Draw()` immediately around this plugin's `windowSystem.Draw()` call, inside the existing exception handling. Dispose the scope even when drawing exits early or throws.
3. Remove the duplicate theme push from `PlannerWindow.Draw()`. This allows Begin-time window styling to use the intended palette and padding.
4. Cover normal/disabled text, title bars, window/child/popup backgrounds, borders, button/frame/header states, tabs, checkmarks, sliders, scrollbars, table colors, selection and available focus/navigation colors.
5. Use explicit native style scopes for component overrides. Keep push/pop accounting centralized and avoid allocating style definition arrays every frame.
6. Keep the existing window identity, constraints, navigation, pinning/click-through/global window behavior, and Refresh/Cancel handlers.

Acceptance: themed outer window and popups; readable native title controls; no style leakage to Dalamud or other plugins; no double scaling or added padding that breaks minimum-width content. Check that theme push/pop remains balanced on the early-return paths.

**2. Build the shared visual components**

Primary files: [PlannerUi.cs](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerUi.cs), [PlannerWindow.Navigation.cs](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Navigation.cs), [PlannerWindow.TableLayout.cs](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.TableLayout.cs). Proposed new file: `src/SubmarineEtaPlanner/Ui/PlannerTypography.cs`.

1. Create a small disposable typography owner for managed heading/value font handles. Let `Plugin` own its lifetime and pass it to `PlannerWindow`; pass the needed handles into shared drawing helpers. Keep ordinary body text on the current user font.
2. Initialize the handles once, use their supported rebuild/scale lifecycle, provide a normal-font fallback, and dispose them after detaching drawing and removing windows. Verify accented and non-Latin names. Do not rasterize fonts or create textures during frame rendering.
3. Measure headings and values with the same font used to draw them. Treat ImGui text measurements as pixels and scale only logical constants.
4. Refine the existing header, navigation button, segmented button, metric card, pill, callout and icon helpers. Introduce reusable primary/secondary button styles and settings-row layout only where they reduce actual repetition.
5. Use native `Button`, `Selectable`, checkbox, combo and input interactions. Decoration can use draw lists, but must preserve focus, disabled behavior and hit regions.
6. Apply quieter navigation with one active accent marker and aligned icon columns. Retain all six entries and the current automatic compact sidebar.
7. Standardize tooltip sizing with a preferred width around 380 logical units, clamped to the active viewport. Scope text wrapping and preserve every line. Use the same treatment for Unlocks, chart tooltips and long shared explanations where appropriate.
8. Keep table padding consistent with `CalculateResponsiveTableLayout` and its existing overhead calculation. Preserve each table's own resizable/no-saved-settings/frozen-column/scroll settings; those are deliberately different between views.
9. Recalculate metric and save-bar heights from measured content. Long labels and larger fonts must wrap without clipping or covering the page beneath them.

Acceptance: every shared component has distinct normal, hover, pressed/selected, focused and disabled states; headings and icons align; long values fit; minimum-width tooltips remain readable. Adding a child/table must not silently change a control's effective ImGui ID or break a popup scope.

**3. Apply the design to Operations, Leveling and fuel**

Primary files: [Operations](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Operations.cs), [Leveling](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Leveling.cs), [FleetHeaders](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.FleetHeaders.cs), [FleetShared](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.FleetShared.cs), [Fuel](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Fuel.cs), [shared forecast rendering](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Results.cs).

1. Align the existing search, role choices and sort controls. Keep their option labels, ordering, stored selections and filtering calls.
2. Restyle the four existing attention controls, preserving their compact footprint. Emphasize counts and selected state; keep `subs` versus `FCs` explicit. A second click still clears the selected attention filter.
3. Give FC headers a unified surface, quiet current-voyage progress and a clear expansion affordance. Keep FC tag and World separate, the favorite independent, and warnings visible in collapsed headers.
4. Retain the exact current-voyage progress source and tooltip meaning. A visual progress treatment must never imply target-rank completion or confidence in a forecast.
5. Refine the submarine rows and existing detail indentation. Preserve the narrow-width flow that puts routes/actions beneath the main cells. Keep current, pinned-next and conditional-next labels from the actual formatter; the preview's sample wording is not authoritative.
6. Apply the same type and separator rules to Leveling tables and complete expanded voyage forecasts, including diagnostics and explanations rendered by shared code.
7. Restyle the existing fuel summary and details. Keep low/critical/unavailable status visible when an FC is collapsed, and keep the full stock, freshness, reserve, consumption, runway and refill information in the current details section.
8. Retain all existing notices and clearing actions for loading, errors, empty filters, missing tracker, stale data and partial forecasts. Make their severity readable without hiding the reason.

Acceptance: FC membership, submarine companions, counts, sort order and displayed values match the baseline for identical inputs. Favorite clicks do not collapse groups. Existing shortcuts still target the right FC. Paused/unknown states never acquire a new send recommendation. Useful row density and full long names are maintained.

**4. Apply the design to FC Setup, Settings and dialogs**

Primary files: [FC Setup](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.FcSetup.cs), [Settings](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Settings.cs), [Navigation](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Navigation.cs), [Window](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.cs).

1. Apply aligned label/control rows within the existing settings groups. Keep help below its label, and stack controls below when width is insufficient. Preserve the page/section structure and the order of related fields.
2. Style Simulation, Routes, Limits, Data source, Build profile and Display with the same section controls. Preserve Recommended/Custom conditional settings and every existing bound and edit action.
3. Style FC selection, target/strategy, assignments, route controls, stock observations, reserve and collection-delay controls without changing draft mutation or validation functions.
4. Polish the existing route-picker modal: readable selected destinations, aligned Up/Down/Remove controls, unclipped search/results, visible validation reasons, fuel/duration preview and reachable Use route/Cancel actions. Keep limits and enabled/disabled behavior unchanged.
5. Refine the existing bottom save bars. Show the current saved/unsaved state, a primary Save changes button and secondary actions. Measure the bar height after text wrapping so every button and explanatory line remains reachable.
6. Preserve the Display exception: ordinary display preferences save automatically; during Reset defaults preview they remain staged. Preserve chart visibility as a separate automatic preference.
7. Give the FC-switch, Reset defaults and forget-observation dialogs matching surfaces, typography and spacing. Keep their existing action order, default focus, scope and exact consequences.

Required interaction walkthroughs:

- Edit an FC draft, visit another ordinary page, and return: draft survives.
- Open another FC's setup through each applicable shortcut: Save, Discard and Cancel each do exactly what they currently do.
- Toggle a favorite, then discard an unrelated FC draft: the automatically saved favorite survives.
- Change a route inside the picker: Use route updates the FC draft; only outer Save changes persists it. Cancel does not apply the picker edit.
- Use global target and strategy: only those overrides change in the draft.
- Load global defaults: all categories are staged; inspect, Save and Discard follow the existing model, including Display.
- Change diagnostics, timeout display behavior and chart visibility: preserve their current saving and refresh behavior, including interactions with unrelated drafts.

Acceptance: all fields/actions are reachable at minimum size, help is complete, and serialized configuration changes match only the actions intentionally exercised. Visual changes introduce no new configuration properties or reset of existing preferences.

**5. Finish Income, its chart, and Unlocks**

Primary files: [Income](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Income.cs), [IncomeChart](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.IncomeChart.cs), [FleetHeaders](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.FleetHeaders.cs), [Unlocks](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/src/SubmarineEtaPlanner/Ui/PlannerWindow.Unlocks.cs).

Income work:

1. Retain content-sized FC tag/World and the three equal-width metric groups. Keep headings/groups centered and numbers right-aligned inside those groups. Refine type and colors without reopening the approved proportion design.
2. Keep the four summary cards, nine submarine-detail columns, period/role/sort choices, coverage details, FC scope and shortcuts.
3. Apply shared colors and spacing to the existing chart heading, bars, ticks, hover surface, legend and tooltips. Keep chart aggregation and data selection untouched.
4. Preserve distinct zero markers, missing-return hatching and partial-period indicators, including grouped periods. Exact gil/returns/FC/day details remain available in the tooltip.
5. Keep chart collapse persistence, no-work-while-collapsed behavior and all existing cache keys/boundaries. A style change must not trigger history reconstruction or forecast work every frame.

Unlocks work:

1. Refine node fills, labels, selected rings and path strokes using the shared state colors. Preserve map geometry, stable positions and current selection/hit detection.
2. Keep every state distinguishable by existing text/icon/shape information as well as color. Active attempts must remain identifiable alongside selection and unlock state.
3. Apply the shared tooltip width treatment to long hover previews, especially the previously noted Map 7 A case. Keep the persistent details below the map.
4. Preserve cross-map search, prerequisite links, FC/map selection resets, Remaining only context and the full remaining-sector table.

Acceptance: Income values/buckets match the baseline; the accepted columns remain aligned; all chart marks remain distinguishable. Unlock selections, prerequisites and active attempts lead to the same destinations and display the same information. Both views remain readable at minimum width and 150% scale.

**6. Verify behavior, rendering and performance**

The test project references Core and selected fuel runtime files; it does not render the ImGui UI. Passing those tests establishes important logic coverage, not visual or click-target correctness. Keep the existing tests and add new tests only for genuinely nontrivial new layout logic if it is introduced. Do not create tests that merely assert palette constants or repeat drawing statements.

| Check | Evidence / existing coverage |
| --- | --- |
| Fleet filters, counts, routes and companions | `CompactInterfaceTests`, `FleetReworkTests`, `SubmarineRoleTests`, relevant presentation tests, plus in-game filter walkthrough. |
| Column allocation and header alignment | `TableColumnLayoutTests`, `DashboardFcHeaderPresentationTests`, plus actual rendered long names and overflow. |
| Voyage/target distinctions | `CurrentVoyageProgressFormatterTests`, `VoyageProgressFormatterTests`, ETA and recommendation tests, plus live headers/details. |
| Drafts and automatic preferences | `FcSetupDraftTests`, applicable compact-interface tests, plus the complete save/navigation walkthrough from step 4. |
| Fuel | Existing stock/presentation/runway tests, plus healthy/low/critical/unavailable/stale UI cases. |
| Income | `RecordedIncomeCalculatorTests`, `IncomeChartTests`, plus scope/period interactions and sparse/zero/missing/partial chart rendering. |
| Unlocks | `UnlockMapPresentationTests`, `UnlockMapSelectionTests`, plus map selection/search/prerequisite walkthroughs. |
| Progressive computation | Existing progress/incremental ETA tests, plus queued/calculating/waiting/partial/cancelled UI cases. |

Run relevant existing tests after an implementation step when its changes could affect those behaviors. Complete one full Release validation on the final candidate, then rerun affected checks only when further changes or failures warrant it.

Commands, matching the existing CI workflow, run from `C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner` with the normal Dalamud development environment configured:

```powershell
dotnet restore SubmarineEtaPlanner.sln
dotnet test tests/SubmarineEtaPlanner.Tests/SubmarineEtaPlanner.Tests.csproj --configuration Release --no-restore
pwsh -NoProfile -File ./tools/Verify-RouteData.ps1
dotnet build src/SubmarineEtaPlanner/SubmarineEtaPlanner.csproj --configuration Release --no-restore
```

Mandatory geometry matrix:

| Logical window size | Dalamud scale | Expected scaled size |
| --- | --- | --- |
| 1040 × 700 | 100% | 1040 × 700 |
| 780 × 520 | 100% | 780 × 520 |
| 1040 × 700 | 150% | 1560 × 1050 |
| 780 × 520 | 150% | 1170 × 780 |

Dalamud scales the window's logical size; do not apply that scale twice. Also inspect just above and below the existing available-content breakpoints for the sidebar and compact Operations layout. Keep the current minimum size; the preview's mobile layout is not a new plugin requirement.

For each page, inspect its ordinary view at all four combinations. Add targeted checks for:

- Long FC/world/submarine names, accented/non-Latin text, long sector paths, large gil values and short counts.
- Collapsed and expanded FCs, submarine forecasts, fuel details, chart, route picker and confirmation dialogs.
- No data, unavailable/disabled tracker, stale snapshot, empty filters, missing history, unknown routes/unlocks/fuel, queued/calculating/waiting/partial results and refresh cancellation.
- Hover, selected, disabled and existing keyboard focus/navigation behavior. Native window controls and scrolling must remain usable.
- Readability against both bright and dark game scenes; no clipping, overlap, hover-only loss of essential information, or unnecessary new horizontal scrolling.
- Table-specific resizing, frozen columns, automatic sizing and full access to the last columns.

Compare repeated Plugin Statistics samples before/after under equivalent conditions. Investigate a repeatable increase beyond the observed measurement variation. Check chart collapsed/open, expanded fleets, map rendering, window closed and unload/reload. There must be no growing font/texture handle count, repeated atlas creation, unintended forecast refresh, or loss of the existing chart/fuel caching. Record the measured results, not an inference from the game's FPS counter.

Complete a final diff audit: no unintended changes to Core algorithms, tracker/fuel readers, route data, configuration schema/defaults, saved-value meanings or persistence handlers. If a visual change cannot preserve a workflow, revise the visual implementation rather than redefine the workflow.

Completion evidence: actual command results, screenshots with dimensions/scale, tested state checklist, frame-time/memory observations, and any remaining issue. In-game checks that have not been possible stay explicitly pending; the refresh is not certified complete while required acceptance checks remain.

**7. Prepare the candidate and handoff**

1. Finish `docs/UI_VISUAL_REFRESH_VERIFICATION.md` with evidence and resolved issues. Update user-facing documentation only where appearance needs new screenshots or descriptions; functional instructions should continue to match the preserved workflows.
2. Record the change as a visual refresh in the changelog. Confirm the version is current before selecting the next patch; do not overwrite a version advanced by another change.
3. Prepare the package through the existing build process. Verify version consistency, DLLs, manifest, dependencies and unchanged bundled route data using the existing release checklist.
4. Keep implementation checkpoints separately reviewable, for example: theme scope; shared typography/controls; fleet views; forms/dialogs; Income/Unlocks; final fixes and evidence.
5. Retain the previous package and the source baseline for rollback. The redesign requires no configuration migration, so a visual rollback should not require resetting user preferences.
6. Distinguish candidate preparation from publication. The current workflow deploys GitHub Pages on `main`/`master`; pushing there is a release action. Publish only within the user's release authorization after the candidate and acceptance evidence are ready.

**Definition of done**

The visual refresh is finished when the six pages and all existing dialogs share the agreed styling; every original control, detail and explanation remains accessible; identical inputs retain identical data semantics; saved preferences and draft workflows survive; the four size/scale combinations pass; the normal Release checks pass; and measured UI performance shows no unexplained regression. A completed handoff includes the working candidate and verification record.

The sections above retain the agreed implementation and acceptance requirements. Consult the verification record for completed work; the new build's in-game acceptance and performance checks remain pending.

**Reference material**

- [Initial source review](C:/Users/Alex/.codex/visualizations/2026/09/06/01a0773e-428b-7922-931a-29f92d6fd652/ui-review.md).
- [Current compact-interface verification](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/docs/COMPACT_UI_VERIFICATION.md).
- [Current Income chart verification](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/docs/INCOME_CHART_VERIFICATION.md).
- [Public release checklist](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/docs/PUBLIC_RELEASE_CHECKLIST.md).
- [Existing build/release workflow](C:/Users/Alex/AppData/Roaming/XIVLauncher/SubmarineEtaPlanner/.github/workflows/build.yml).
- [Lightless theme source examined at the review revision](https://git.lightless-sync.org/Lightless-Sync/LightlessClient/src/commit/1689e35fc5a9375fa480098062ca3379364c1706/LightlessSync/UI/Style/MainStyle.cs).
- [Lightless UI components examined at the review revision](https://git.lightless-sync.org/Lightless-Sync/LightlessClient/src/commit/1689e35fc5a9375fa480098062ca3379364c1706/LightlessSync/UI/FreyjaUI).
- [Lightless managed font use examined at the review revision](https://git.lightless-sync.org/Lightless-Sync/LightlessClient/src/commit/1689e35fc5a9375fa480098062ca3379364c1706/LightlessSync/UI/UiSharedService.cs).
- [Dalamud window lifecycle and sizing reference used during the review](https://dalamud.dev/api/Dalamud.Interface.Windowing/Classes/Window/).
