# Compact interface verification

## In-game checks on 2026-09-06, version 0.5.46.0

Checked using the user's live tracked fleets at 150% Dalamud UI scaling. The user resized the window manually; automated resize drags did not change it, so these checks do not certify the exact default/minimum size matrix.

- Operations fleet column headings are visible, with separately aligned FC tags and worlds.
- Income fleet headings and numeric values share column boundaries.
- Low fuel selects the critical farming fleet, highlights its submarines, and clearing it restores all 19 fleets.
- Critical fuel remains visible in the collapsed fleet. Expanded details show the observed stock and timestamp, reserve, daily consumption, sends remaining, deadline, and warning.
- Income's Setup shortcut opens the requested FC.
- Ordinary page changes retain the unsaved FC draft.
- A shortcut to another FC's setup prompts from both Operations and Income. Cancel retains the current page and draft; Discard opens the requested FC without saving the draft; Save saves the draft and opens the requested FC. Returning to the original FC confirms the saved value.
- An automatically saved favorite survives discarding an unrelated FC draft, without collapsing the group or refreshing the forecast.
- The Income shortcut scopes and expands the requested FC; removing the scope restores the previously selected Farming overview.
- Temporary target and favorite changes used for these checks were restored through the UI, and the temporary attention filter and Income scope were cleared.

The expanded Income submarine-name column was visibly squeezed in two fleets. Version 0.5.47.0 corrects padding allocation and applies automatic detail-column sizing; the follow-up checks below confirm the corrected rendering.

## In-game follow-up, version 0.5.47.0

Checked after the user installed the update, at the user's chosen window size and 150% Dalamud UI scaling.

- Operations headings, separate FC tags/worlds, and collapsed critical-fuel status remain readable and aligned.
- Expanded Income shows the full four submarine names in Meow-Spriggan with readable rank, build, and numeric columns. Horizontal overflow reaches the last recorded return while retaining the submarine-name column; the scrollbar was returned to its starting position.
- Income's Unlock map shortcut opens the requested FC.
- Clicking Map 6 sector T opens persistent details below the map: state, rank, discovery source, remaining path, blocking reason, and prerequisite links. The selected node and discovery path are highlighted; hover previews remain available.
- Remaining only preserves the selected sector and prerequisite context without moving node positions. An unrelated manual map change clears the sector selection.
- Map 7 sector A shows the cross-map path through Map 6. Clicking its Map 6 T prerequisite opens Map 6 and selects T.
- Searching for `iris` from Map 7 returns the map-qualified Map 6 L result. Selecting it opens Map 6 with L selected and clears the search.
- Needs setup with zero matching FCs shows an empty-state message and reachable Clear filters action; clearing restores all 19 fleets.
- Temporary attention/Remaining only filters and sector selection were cleared, and Operations was restored. No FC settings or favorites were changed during this follow-up.

One minor visual follow-up remains: the hover preview for Map 7 A wrapped into a narrow, tall column. Its persistent details below the map were readable and complete.

The user's subsequent Income screenshot showed that the collapsed row proportions still needed correction: World stretched too far and the numeric values clustered toward the right. The checks above establish readability and alignment, not balanced spacing. Version 0.5.48.0 keeps FC tag/World content-sized and gives the three metrics equal widths. The user's next screenshot confirmed improved spacing and prompted centered headings and number groups, with right-aligned values inside those groups, for 0.5.49.0. The centered rendering needs in-game review after updating, including short voyage counts and wrapped rows.

## Local validation for 0.5.49.0

- Release test suite: 447 passed, none failed or skipped.
- Release plugin build: succeeded with no warnings or errors.
- Bundled route-data verification: passed.
- Release package manifest and required contents: verified.

## Local validation for 0.5.48.0

- Release test suite: 447 passed, none failed or skipped.
- Release plugin build: succeeded with no warnings or errors.
- Bundled route-data verification: passed.
- Release package manifest and required contents: verified.

## Local validation for 0.5.47.0

- Release test suite: 447 passed, none failed or skipped, including padding allocation at 100% and 150% scaling.
- Release plugin build: succeeded with no warnings or errors.
- Bundled route-data verification: passed.
- Release package manifest and required contents: verified.

## Remaining acceptance checks

- Exact 1040 x 700 and 780 x 520 sizes at 100% and 150% scaling, including compact Operations and long names.
- Unlock selection clearing on FC changes, active-attempt details, and unavailable-data filtering in game; review hover-preview width for long details.
- Conflicting filters with an Income scope, mixed/paused fleets, missing tracker data, progressive calculations, and partial forecasts in game. Presentation logic also has focused automated coverage.
- Plugin Statistics frame-time comparison under equivalent conditions; no performance acceptance claim has been made from the game's FPS counter.
