# Submarine ETA Planner

Submarine ETA Planner is a read-only Dalamud plugin that estimates how long tracked Free Company submarines need to reach rank 114.

The plugin reads local SubmarineTracker data, simulates future voyages, and presents ETA plans grouped by FC. It never deploys submarines, clicks UI, or automates workshop actions.

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

The plugin starts by looking for SubmarineTracker data in the standard XIVLauncher config path:

`pluginConfigs\SubmarineTracker\submarine-sqlite.db`

You can override the database path in plugin settings.

## Dalamud Custom Repository

After GitHub Pages is enabled, add this custom repository URL in Dalamud:

`https://alexvalliere.github.io/submarineEtaPlanner/repo.json`

## Safety

This plugin is an estimator only. It does not call AutoRetainer, deploy submarines, collect rewards, change routes, or interact with FFXIV UI automation.
