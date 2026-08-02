# Submarine ETA Planner

[![Build](https://github.com/AlexValliere/submarineEtaPlanner/actions/workflows/build.yml/badge.svg)](https://github.com/AlexValliere/submarineEtaPlanner/actions/workflows/build.yml)

Submarine ETA Planner is a Dalamud plugin that only forecasts how long tracked Free Company submarines need to reach a target rank you choose.

The plugin reads local SubmarineTracker data, simulates future voyages, and presents ETA plans grouped by FC. Choose the target rank on the Simulation page, apply the change, and the complete dashboard forecast updates to that rank. Recommended leveling applies a ready-to-use preset that follows main sector progression and selects the highest expected-EXP-per-hour route while coordinating unlock attempts across the FC. Active voyages are shown separately from conditional next-route forecasts. Custom strategy exposes advanced EXP, route-goal, duration, and build-profile controls.

## Features

- Forecast every tracked FC submarine to a target rank you choose.
- Distinguish the active voyage from the next route after return.
- Coordinate shared unlock progression and voyage timing across each FC fleet.
- Model sector-discovery RNG with median ETAs and P10-P90 likely ranges.
- Show concurrent FC unlock attempts and conditional routes without presenting locked sectors as guaranteed.
- Use Recommended leveling for expected-EXP-per-hour routing and main leveling-route unlocks.
- Use Custom strategy for advanced EXP, route-goal, duration, and build-profile controls.
- Search and filter FCs, review readiness and warnings, and expand complete voyage forecasts.
- Keep existing results visible during background refreshes.

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

1. Open the planner with `/seta`.
2. Select **Simulation** and choose your target rank.
3. Keep **Recommended leveling** selected unless you need custom routing controls.
4. Select **Apply & refresh**.
5. Return to the dashboard and expand an FC or submarine to inspect its forecast.

## Probabilistic unlock forecasts

Sector discovery is not guaranteed. The planner runs up to 256 deterministic, repeatable simulations using the FC-wide unlocked-sector state and every known active voyage. It requires at least 64 completed samples for a complete result; otherwise the existing calculation deadline produces an explicit partial forecast. It reports:

- **P50 / Median**: half of modeled outcomes finish by this time.
- **P10-P90**: the likely range containing the middle 80% of modeled outcomes.
- **Unlocks in progress**: the submarines currently visiting an unlock source and their combined modeled chance.
- **Conditional routes**: routes that become available only in simulation outcomes where the required sector was discovered.

The default discovery chance is **33% per eligible source visit**. This is a community-informed forecasting assumption, not an official game value, and can be changed under **Routes → Unlock chance per visit**. Square Enix confirms that discovering sectors can require repeated voyages in the [official Patch 4.2 notes](https://fr.finalfantasyxiv.com/lodestone/topics/detail/75c691f90f4a7da3907f0671ac33e139e9792abf); the [FFXIV Submarine Builders guidance](https://ffxivarchive.neocities.org/submarine) describes sector unlocking as flat RNG unaffected by submarine stats.

The plugin performs no runtime web requests. It does not learn from, upload, or otherwise transmit SubmarineTracker loot history.

## Chat commands

- `/seta` toggles the dashboard.
- `/seta settings` opens simulation settings.
- `/seta refresh` opens the dashboard and refreshes the forecast.
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

Dalamud API 15 currently targets .NET 10. If your machine only has the .NET 9 SDK, validate the planner core with:

```powershell
dotnet test .\tests\SubmarineEtaPlanner.Tests\SubmarineEtaPlanner.Tests.csproj
```
