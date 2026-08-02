# Dalamud D17 Submission Template

New plugins must be submitted under `testing/live/SubmarineEtaPlanner` in a fork of `goatcorp/DalamudPluginsD17`.

## `manifest.toml`

```toml
[plugin]
repository = "https://github.com/AlexValliere/submarineEtaPlanner.git"
commit = "<exact-public-release-commit>"
owners = ["AlexValliere"]
project_path = "src/SubmarineEtaPlanner"
changelog = "Public beta with configurable target ranks, FC-wide probabilistic unlock forecasting, and progressive per-FC results."
```

Copy `images/icon.png` to the D17 submission's `images/icon.png`. The current icon is 512×512 and AI-generated; either replace it with a genuinely human-created icon or keep the disclosure below.

## Pull request disclosure

> AI usage level: **Copilot**. I defined the requirements and safety boundaries, reviewed the implementation, tested the plugin against my live SubmarineTracker data, and remain responsible for understanding and maintaining the code. OpenAI Codex produced a substantial portion of the implementation, tests, and documentation under my direction. The submitted installer icon was generated with OpenAI image-generation tooling and is disclosed in the plugin description.

Before submission, replace the commit placeholder with the immutable public commit and complete `docs/PUBLIC_RELEASE_CHECKLIST.md`.
