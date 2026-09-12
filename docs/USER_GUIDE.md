# Submarine ETA Planner user guide

Detailed behavior, forecasting assumptions, and data handling for version 1.0.0. For installation and first steps, see the [README](../README.md).

## Defaults and saved settings

Fresh installations start with target rank 90, Recommended leveling, FC-wide fleet simulation, a 120-minute collection delay, no voyage-duration cap, a 33% unlock chance, and a 20-second per-FC calculation limit. Existing saved settings are never replaced during an update.

**Reset defaults** asks for confirmation before loading defaults across every settings category. The reset remains staged so you can inspect each tab, adjust values, select **Save changes**, or use **Discard changes** without changing the saved configuration.

## Compact fleet workspace

Operations shows submarine, status, return time, route, and next action. Expand a submarine to inspect rank, build, expected EXP, target ETA, uncertainty, and missing-data explanations. At narrow widths, route and action move beneath the submarine row. Current voyages and proposed or pinned next routes are labeled separately; workshop actions must still be performed in game.

Attention counters apply after the FC search and role filter. Selecting one filters FC groups while retaining their submarine companions; selecting it again clears it. Voyage counters count submarines (including paused assignments with relevant voyage states). **Returning within 4h** counts known current-voyage returns after now and up to the selected number of elapsed hours ahead, including the exact upper boundary. Already collectible submarines appear only under **Ready to collect**. Choose **1h, 2h, 4h, 8h, or 24h** with the adjacent dropdown; the default is **4h**. The window advances automatically across midnight and clock changes. Its duration is **Saved automatically** for all fleets and remembered across plugin restarts; clearing filters preserves it. Changing the duration updates the counter, filtered fleets, and highlighted submarines together without refreshing forecasts or changing staged settings. Fuel/setup counters count FCs with active farming assignments; missing or stale information is not treated as zero stock.

FC headers include independent favorite stars. Expanded groups provide **Setup**, **Unlock map**, and **Income** shortcuts for that FC. An Income shortcut temporarily shows one FC regardless of the saved role filter; **Show all FCs** restores the overview. Ordinary navigation retains unsaved edits. Opening a different FC's setup offers **Save changes**, **Discard changes**, or **Cancel**.

Healthy fuel appears as a short expandable summary. Low, critical, and unavailable fuel remain visible in collapsed Operations headers. Expand **Fuel details** for stock source, observation age, reserve, consumption, and refill deadlines; **Fuel setup** opens the matching settings. FCs without active farming submarines do not show a farming-fuel panel.

On **Unlocks**, search sector codes or names across all maps, click a sector to keep its details open, and follow prerequisite links between maps. **Remaining only** keeps required path context and any explicitly selected sector visible. Unknown tracker unlock state disables this filter.

Operations and Income fleet headings keep **FC tag** and **World** in separate aligned columns. Income keeps those identity columns sized to their text, with gross gil, recorded average/day, and voyage count spread evenly across the remaining width. These three headings and number groups are centered within their columns; numbers align to a shared right edge within each group. Narrow windows wrap the figures below FC tag and World. Expand an FC for the full submarine table and history coverage. Detail columns size automatically to the contents and window, with horizontal scrolling when needed to keep submarine names readable. These remain recorded gross NPC salvage values, with the existing calculation rules.

Income also shows one collapsible chart below the summary cards. It follows the selected fleets, FC scope, and existing rolling period; its gross total matches the summary. Short periods use daily bars, 1 year uses calendar weeks starting Monday, and Lifetime switches from days to weeks to months as its span grows. Hover a bar or gap for exact gil, recorded returns, contributing FCs, and days with/without entries. Dates use your local timezone; today and partly included boundary periods are marked incomplete. Dots show recorded returns worth **0 gil**, while hatching marks days **without recorded returns**—absent entries are not assumed to be zero income or complete history. Chart return counts include zero-salvage returns; the existing summary voyage count and average coverage still begin with salvage returns. Click the chart heading to collapse it; visibility is **Saved automatically** and does not change staged settings or refresh forecasts.

FC visibility, favorites, and display preferences are labeled **Saved automatically**. Hidden FCs remain in FC Setup’s visibility list and selector, but are excluded from every other fleet page, total, chart, warning, and forecast. **Show all** restores every tracked FC, including when all are hidden. Other edits use **Save changes** and **Discard changes**. **Use global target and strategy** changes only those two FC overrides. Global **Reset defaults** remains a staged, confirmed preview.

## Progressive calculations

Forecasts run one visible FC at a time so a difficult fleet cannot consume the entire refresh deadline. Forecast-backed views list all visible tracked FCs immediately, mark each one as queued or calculating, and publish completed results without waiting for the remaining FCs. FCs already at the target rank are handled first, followed by leveling FCs closest to the target.

The **Limits → Per-FC time limit** setting bounds each FC independently. If an FC reaches that limit, its partial or previous result remains visible and calculation continues with the next FC. Probability sampling stops early after at least 64 trials when the P10, P50, and P90 estimates have stabilized; uncertain forecasts may continue up to 256 trials.

When SubmarineTracker's database changes, the planner compares a semantic fingerprint for each FC and recalculates only changed fleets. Unchanged complete forecasts appear immediately as **Up to date**. An FC with a voyage that has just returned is held as **Waiting for SubmarineTracker** until the tracker records its new rank and unlock outcome. The cache is memory-only, so reloading the plugin starts a full forecast. The header **Refresh** action and `/seta refresh` also intentionally perform a full recalculation.

## Recorded income

The Income view reads valid primary and additional loot entries from SubmarineTracker's local history and attributes them by FC, submarine, route, and voyage return. It reports gross gil, recorded gil per day, gil per voyage, voyage count, and history coverage. Voyage counts and coverage begin only with returns containing at least one of the tracked salvage items, so earlier leveling voyages do not dilute farming income. It totals only the eight market-prohibited salvage accessories used for direct NPC gil farming:

| Item | NPC sale price |
| --- | ---: |
| Salvaged Ring | 8,000 gil |
| Salvaged Bracelet | 9,000 gil |
| Salvaged Earring | 10,000 gil |
| Salvaged Necklace | 13,000 gil |
| Extravagant Salvaged Ring | 27,000 gil |
| Extravagant Salvaged Bracelet | 28,500 gil |
| Extravagant Salvaged Earring | 30,000 gil |
| Extravagant Salvaged Necklace | 34,500 gil |

Prices are read from the installed game's item data, with the table above used as an offline fallback. The displayed amount is gross NPC sale value, not proof that the items were sold and not net profit after repairs or other expenses. It covers only voyages present in SubmarineTracker history; voyages from before the tracker recorded loot cannot be reconstructed.

## Farming cycles and fuel runway

Submarines assigned the Farming role use their pinned farming route or their current ordered SubmarineTracker route for recurring-cycle projections. The planner validates the effective route, build, sectors, fuel cost, and duration before forecasting departures. Current voyages are treated as already paid; future sends are grouped around their configured collection delays.

FC Setup can resolve ceruleum stock automatically from one matching local observation, use a selected observed character, or use a manual value. Automatic safety stock reserves enough tanks for one complete resend of every active farming submarine; a fixed reserve can be used instead. Operations then shows tanks per full-fleet send, full-fleet sends remaining, approximate time above safety stock, and the estimated refill deadline. These are planning estimates based on the configured routes, timings, and last known stock, not automated workshop actions.

The planner reads only the inventory of the character currently being played. It keeps a local `workshop-fuel-observations.json` file in its plugin configuration directory so that character's last observed ceruleum tank count remains available after switching characters. The file stores the character content ID, character name and world, FC ID, observed tank count, and observation timestamp. Stored observations can be forgotten from FC Setup and are never uploaded.

## Probabilistic unlock forecasts

Sector discovery is not guaranteed. The planner runs 64 to 256 deterministic, repeatable simulations using the FC-wide unlocked-sector state and every known active voyage. It stops when the percentile estimates stabilize or the per-FC calculation deadline is reached; insufficient samples produce an explicit partial forecast. It reports:

- **P50 / Median**: half of modeled outcomes finish by this time.
- **P10-P90**: the likely range containing the middle 80% of modeled outcomes.
- **Unlocks in progress**: the submarines currently visiting an unlock source and their combined modeled chance.
- **Conditional routes**: routes that become available only in simulation outcomes where the required sector was discovered.

The default discovery chance is **33% per eligible source visit**. This is a community-informed forecasting assumption, not an official game value, and can be changed under **Routes → Unlock chance per visit**. Square Enix confirms that discovering sectors can require repeated voyages in the [official Patch 4.2 notes](https://fr.finalfantasyxiv.com/lodestone/topics/detail/75c691f90f4a7da3907f0671ac33e139e9792abf); the [FFXIV Submarine Builders guidance](https://ffxivarchive.neocities.org/submarine) describes sector unlocking as flat RNG unaffected by submarine stats.

The plugin performs no runtime web requests. Loot history and all calculated gil totals remain local; the plugin does not learn from, upload, or otherwise transmit them.
