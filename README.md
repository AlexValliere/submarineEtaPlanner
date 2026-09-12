# Submarine ETA Planner

[![Build](https://github.com/AlexValliere/submarineEtaPlanner/actions/workflows/build.yml/badge.svg)](https://github.com/AlexValliere/submarineEtaPlanner/actions/workflows/build.yml)
[![Version: 1.0.0](https://img.shields.io/badge/version-1.0.0-teal.svg)](CHANGELOG.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-teal.svg)](LICENSE)

Plan your Free Company submarine fleets from one place. See what is ready to collect, what returns next, when leveling fleets should reach their target, and how long your farming fuel will last.

**1.0.0 is the first stable release**, shaped by player use and in-game validation. Six connected views bring daily operations, progression, income, and fleet setup into a compact graphite-and-teal interface that adapts to your window size. Existing users keep their saved settings and fleet preferences when updating.

The planner uses local [Submarine Tracker](https://github.com/Infiziert90/SubmarineTracker) data. It does not collect submarines, send voyages, buy fuel, or modify tracker data; you perform workshop actions in game. The plugin makes no runtime web requests, and your fleet data stays local.

## Your fleet at a glance

| View | What you can do |
| --- | --- |
| **Operations** | Prioritize ready submarines, upcoming returns, low fuel, and fleets needing setup. Inspect current voyages and recommended next actions. |
| **Leveling** | Forecast progress toward each FC's target rank, with routes, EXP, expected completion dates, and likely ranges. |
| **Unlocks** | Explore sector maps, follow discovery prerequisites, and see unlock attempts shared across your FC fleet. |
| **Income** | Compare recorded gross NPC salvage value by FC, submarine, route, and period, with a history chart. |
| **FC Setup** | Choose visible FCs and favorites; set targets, strategies, submarine roles, farming routes, collection delays, and fuel reserves. |
| **Settings** | Adjust global simulation, route, data-source, build-profile, calculation-limit, and display preferences. |

Forecasts appear progressively as each FC finishes calculating. Unchanged fleets reuse their results, while missing data and uncertain unlocks remain clearly identified.

## Installation

Requires **XIVLauncher with Dalamud** and **Submarine Tracker installed and enabled**.

1. Type `/xlsettings` in the FFXIV chat box.
2. Open **Experimental** and scroll to **Custom Plugin Repositories**.
3. Paste the repository URL below into an empty field, press **+**, and ensure it is enabled.
4. Select **Save and Close**.
5. Type `/xlplugins`, search for **Submarine ETA Planner**, and select **Install**.
6. Install and enable **Submarine Tracker** from the plugin installer if needed.
7. Type `/seta` to open the planner.

```text
https://alexvalliere.github.io/submarineEtaPlanner/repo.json
```

If Submarine Tracker is unavailable, the planner keeps existing results visible, blocks refreshes, and provides a shortcut to its installer page.

## Quick start

1. Open `/seta` and review **Operations**. Use **Ready to collect**, **Returning within 4h**, **Low fuel**, and **Needs setup** to focus the fleet list. The return-window dropdown offers 1, 2, 4, 8, or 24 hours.
2. Open **FC Setup** and choose the FCs you want to see. Set each fleet's target and assign submarine roles. Visibility and favorites save automatically; target, strategy, and assignment edits use **Save changes**.
3. For farming submarines, optionally pin a route, adjust collection delay, choose a fuel-stock source, and set safety stock.
4. Check **Leveling** for readiness forecasts, **Unlocks** for discovery paths, and **Income** for recorded salvage returns.
5. Use **Settings** for global preferences. Select **Save changes** to apply staged edits or **Discard changes** to abandon them.

New installations start with target rank **90**, **Recommended** leveling, FC-wide simulation, a **120-minute** collection delay, and a **20-second per-FC** calculation limit. Updates preserve existing settings. **Reset defaults** opens a confirmed, staged preview that you can review before saving.

## Understanding the numbers

- **Forecasts are estimates.** Unlocks use a configurable 33% discovery chance per eligible visit by default. Expected dates and likely ranges describe modeled outcomes; locked sectors are never guaranteed.
- **Income is recorded gross salvage value.** It uses Submarine Tracker's local loot history and NPC sale prices. It is not net profit or proof of a sale, and missing history cannot be reconstructed.
- **Fuel runway is a projection.** It depends on your farming routes, collection delays, reserves, and last known stock. Fuel observations come from the character currently being played and remain available locally after switching characters.

See the [user guide](docs/USER_GUIDE.md) for detailed filters and save behavior, income-chart coverage, calculation limits, unlock probabilities, fuel observations, and data handling.

## Chat commands

| Command | Action |
| --- | --- |
| `/seta` | Toggle the planner on Operations. |
| `/seta settings` | Open Settings. |
| `/seta refresh` | Open Operations and fully recalculate forecasts. |
| `/seta help` | List available commands. |

## Data and support

The planner reads Submarine Tracker's database from the standard XIVLauncher configuration path:

```text
pluginConfigs\SubmarineTracker\submarine-sqlite.db
```

You can override the database path in **Settings → Data Source**.

Report bugs or request features through [GitHub Issues](https://github.com/AlexValliere/submarineEtaPlanner/issues). Include the plugin version, any warning shown by the planner, and whether Submarine Tracker is installed and enabled.

## Credits and transparency

Submarine ETA Planner uses data and calculation concepts adapted from [Submarine Tracker](https://github.com/Infiziert90/SubmarineTracker). Thank you to the players who use the planner and help improve it through feedback and in-game validation.

The project uses the [MIT License](LICENSE). See [Third Party Notices](THIRD_PARTY_NOTICES.md) and [Route Data Provenance](docs/ROUTE_DATA_PROVENANCE.md) for attribution, the complete SubmarineTracker MIT notice, and bundled-data provenance.

Development used substantial AI assistance under human direction and in-game validation. The installer icon was generated with AI image tooling. See [AI Usage Disclosure](AI_USAGE.md) for the complete declaration.

## Development

The projects target **.NET 10**; the plugin uses **Dalamud API 15**. Building the plugin also requires the matching Dalamud development libraries. See the [build workflow](.github/workflows/build.yml) for the development-library setup used in CI.

```powershell
dotnet restore SubmarineEtaPlanner.sln
dotnet test tests/SubmarineEtaPlanner.Tests/SubmarineEtaPlanner.Tests.csproj --configuration Release --no-restore
dotnet build src/SubmarineEtaPlanner/SubmarineEtaPlanner.csproj --configuration Release --no-restore
pwsh -NoProfile -File ./tools/Verify-RouteData.ps1
```

For core-only validation with the .NET 10 SDK:

```powershell
dotnet test tests/SubmarineEtaPlanner.Tests/SubmarineEtaPlanner.Tests.csproj --configuration Release
```

Release preparation is documented in the [Public Release Checklist](docs/PUBLIC_RELEASE_CHECKLIST.md); official Dalamud submission guidance is in the [D17 Submission Template](docs/D17_SUBMISSION.md). The public release is named **1.0.0**, with **1.0.0.0** used in the plugin's four-part assembly and repository version fields.

See the [Changelog](CHANGELOG.md) for release history.
