# Submarine ETA Planner

Submarine ETA Planner is a read-only Dalamud plugin that estimates how long tracked Free Company submarines need to reach a target rank you choose.

The plugin reads local SubmarineTracker data, simulates future voyages, and presents ETA plans grouped by FC. Choose the target rank on the Simulation page, apply the change, and the complete dashboard forecast updates to that rank. Practical leveling follows sector progression before selecting the highest-EXP available route, while the exact route-search model remains available for diagnostics. It never deploys submarines, clicks UI, or automates workshop actions.

## Requirement

[Submarine Tracker](https://github.com/Infiziert90/SubmarineTracker) must be installed and enabled. If it is missing or disabled, the planner keeps any existing results visible, blocks refreshes, and offers a shortcut to the relevant Dalamud installer page.

The ocean-themed command dashboard groups tracked fleets by FC, summarizes readiness and warnings, and provides search, readiness filters, and expandable voyage forecasts. A responsive navigation rail separates the dashboard from focused Simulation, Routes, Limits, Data Source, Build Profile, and Display pages. Calculation changes are applied together with **Apply & refresh**, so editing settings does not repeatedly restart the planner.

## Chat command

- `/seta` toggles the dashboard.
- `/seta settings` opens simulation settings.
- `/seta refresh` opens the dashboard and refreshes the forecast.
- `/seta help` lists the available commands.

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

The plugin starts by looking for Submarine Tracker data in the standard XIVLauncher config path:

`pluginConfigs\SubmarineTracker\submarine-sqlite.db`

You can override the database path in plugin settings.

## Dalamud Custom Repository

After GitHub Pages is enabled, add this custom repository URL in Dalamud:

`https://alexvalliere.github.io/submarineEtaPlanner/repo.json`

## Safety

This plugin is an estimator only. It does not call AutoRetainer, deploy submarines, collect rewards, change routes, or interact with FFXIV UI automation.
