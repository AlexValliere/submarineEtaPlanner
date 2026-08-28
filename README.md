# Submarine ETA Planner

[![Build](https://github.com/AlexValliere/submarineEtaPlanner/actions/workflows/build.yml/badge.svg)](https://github.com/AlexValliere/submarineEtaPlanner/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-teal.svg)](LICENSE)

Submarine ETA Planner turns local SubmarineTracker data into a Free Company fleet-operations workspace. It highlights returns and next actions, forecasts leveling and sector unlock progress, reports recorded salvage income, and projects recurring farming cycles and ceruleum fuel runway.

The plugin is read-only with respect to the game and SubmarineTracker: it does not collect submarines, resend voyages, buy fuel, or modify tracker data. Its Operations, Leveling, Unlocks, Income, and FC Setup views organize every tracked FC while Settings provides global simulation and display controls.

## Features

- **Operations:** prioritize fleets that need attention, follow current returns, review recommended next actions, and filter mixed leveling and farming fleets.
- **Leveling:** forecast every assigned leveling submarine to an FC-specific target rank, with route, EXP, rank, and likely completion details.
- **Unlocks:** inspect FC-specific unlocked, explored, discoverable, locked, and actively attempted sectors on schematic maps with complete discovery paths.
- **Income:** compare recorded gross NPC salvage value across FCs, submarines, routes, and 7-, 30-, 90-, 365-day, or lifetime periods.
- **FC Setup:** save favorites, target ranks, leveling strategies, submarine roles, pinned farming routes, fuel-stock sources, safety stock, and collection delays.
- **Settings:** tune global simulation, route, data-source, build-profile, calculation-limit, and display preferences.
- Distinguish active voyages from conditional next routes, and coordinate shared unlock attempts across the whole FC fleet.
- Model sector-discovery RNG with median ETAs and P10-P90 likely ranges without presenting locked sectors as guaranteed.
- Project recurring farming dispatch cycles, fuel per voyage, remaining full-fleet sends, approximate runway, and refill deadlines.
- List every tracked FC immediately, publish forecasts progressively, and reuse unchanged results when only part of SubmarineTracker's data changes.

## Installation

Submarine ETA Planner requires [XIVLauncher](https://goatcorp.github.io/) and Dalamud.

[Submarine Tracker](https://github.com/Infiziert90/SubmarineTracker) must also be installed and enabled. If it is unavailable, the planner keeps existing results visible, blocks refreshes, and provides a shortcut to its Dalamud installer page.

1. Type `/xlsettings` in the FFXIV chat box.
2. Open the **Experimental** tab and scroll to **Custom Plugin Repositories**.
3. Paste the repository URL below into an empty field, press the **+** button, and ensure it is enabled.
4. Select **Save and Close**.
5. Type `/xlplugins`, search for **Submarine ETA Planner**, and select **Install**.
6. Install and enable **Submarine Tracker** from the plugin installer if needed.
7. Type `/seta` to open the planner.

### Repository URL

`https://alexvalliere.github.io/submarineEtaPlanner/repo.json`

## Quick start

1. Open the planner with `/seta`; it starts on **Operations**, where fleets needing attention appear first.
2. Open **FC Setup** to choose favorites, set FC-specific target ranks and strategies, and assign each submarine to Leveling, Farming, or Paused.
3. For farming submarines, optionally pin a route, adjust collection delay, select the FC's local fuel-stock source, and set its safety stock.
4. Use **Leveling** for progression forecasts and **Unlocks** for FC-specific sector status and remaining discovery paths.
5. Use **Income** for historical SubmarineTracker salvage results. Farming-cycle and fuel forecasts remain projections, not recorded earnings or game actions.
6. Use **Settings** when you need global simulation, route, data-source, build-profile, calculation-limit, or display controls, then select **Apply & refresh**.

Fresh installations start with target rank 90, Recommended leveling, FC-wide fleet simulation, a 120-minute collection delay, no voyage-duration cap, a 33% unlock chance, and a 20-second per-FC calculation limit. Existing saved settings are never replaced during an update.

**Reset defaults** asks for confirmation before loading defaults across every settings category. The reset remains staged so you can inspect each tab, adjust values, select **Apply & refresh**, or use **Revert** without changing the saved configuration.

## Progressive calculations

Forecasts run one FC at a time so a difficult fleet cannot consume the entire refresh deadline. Forecast-backed views list all tracked FCs immediately, mark each one as queued or calculating, and publish completed results without waiting for the remaining FCs. FCs already at the target rank are handled first, followed by leveling FCs closest to the target.

The **Limits → Per-FC time limit** setting bounds each FC independently. If an FC reaches that limit, its partial or previous result remains visible and calculation continues with the next FC. Probability sampling stops early after at least 64 trials when the P10, P50, and P90 estimates have stabilized; uncertain forecasts may continue up to 256 trials.

When SubmarineTracker's database changes, the planner compares a semantic fingerprint for each FC and recalculates only changed fleets. Unchanged complete forecasts appear immediately as **Up to date**. An FC with a voyage that has just returned is held as **Waiting for SubmarineTracker** until the tracker records its new rank and unlock outcome. The cache is memory-only, so reloading the plugin starts a full forecast. The header **Refresh** action and `/seta refresh` also intentionally perform a full recalculation.

## Recorded income

The Income view reads valid primary and additional loot entries from SubmarineTracker's local history and attributes them by FC, submarine, route, and voyage return. It reports gross gil, recorded gil per day, observed run rate, gil per voyage, voyage count, and history coverage. Voyage counts and coverage begin only with returns containing at least one of the tracked salvage items, so earlier leveling voyages do not dilute farming income. It totals only the eight market-prohibited salvage accessories used for direct NPC gil farming:

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

## Transparency

The project is licensed under the [MIT License](LICENSE). SubmarineTracker attribution, its complete MIT notice, and the exact provenance of the bundled route data are recorded in [Third Party Notices](THIRD_PARTY_NOTICES.md) and [Route Data Provenance](docs/ROUTE_DATA_PROVENANCE.md).

Development used substantial AI assistance under human direction and in-game validation. The installer icon was generated with AI image tooling. See [AI Usage Disclosure](AI_USAGE.md) for the complete declaration.

## Chat commands

- `/seta` toggles the planner on the Operations view.
- `/seta settings` opens Settings.
- `/seta refresh` opens Operations and refreshes the forecasts.
- `/seta help` lists the available commands.

## Data source

The plugin looks for Submarine Tracker data at the standard XIVLauncher config path:

`pluginConfigs\SubmarineTracker\submarine-sqlite.db`

You can override the database path in the plugin's Data Source settings.

## Support

Report bugs or request features through [GitHub Issues](https://github.com/AlexValliere/submarineEtaPlanner/issues). Please include the plugin version, any warning shown by the planner, and whether Submarine Tracker is installed and enabled.

## Acknowledgements

Submarine ETA Planner uses data and calculation concepts adapted from [Submarine Tracker](https://github.com/Infiziert90/SubmarineTracker). See [Third Party Notices](THIRD_PARTY_NOTICES.md) for attribution and licensing details.

## Development

```powershell
dotnet restore
dotnet test
dotnet build -c Release
```

Public release verification is documented in [Public Release Checklist](docs/PUBLIC_RELEASE_CHECKLIST.md). The official Dalamud testing-track manifest and disclosure text are prepared in [D17 Submission Template](docs/D17_SUBMISSION.md).

Release history is available in the [Changelog](CHANGELOG.md).

Dalamud API 15 currently targets .NET 10. If your machine only has the .NET 9 SDK, validate the planner core with:

```powershell
dotnet test .\tests\SubmarineEtaPlanner.Tests\SubmarineEtaPlanner.Tests.csproj
```
