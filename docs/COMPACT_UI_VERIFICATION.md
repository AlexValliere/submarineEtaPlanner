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

The expanded Income submarine-name column was visibly squeezed in two fleets. Version 0.5.47.0 corrects padding allocation and applies automatic detail-column sizing. Its final in-game rendering still needs verification after updating.

## Local validation for 0.5.47.0

- Release test suite: 447 passed, none failed or skipped, including padding allocation at 100% and 150% scaling.
- Release plugin build: succeeded with no warnings or errors.
- Bundled route-data verification: passed.
- Release package manifest and required contents: verified.

## Remaining acceptance checks

- Exact 1040 x 700 and 780 x 520 sizes at 100% and 150% scaling, including compact Operations and long names.
- Unlock selection, cross-map search/prerequisites, and Remaining only interactions in game.
- Empty attention results, conflicting filters with an Income scope, mixed/paused fleets, missing tracker data, progressive calculations, and partial forecasts in game. Presentation logic also has focused automated coverage.
- Plugin Statistics frame-time comparison under equivalent conditions; no performance acceptance claim has been made from the game's FPS counter.
