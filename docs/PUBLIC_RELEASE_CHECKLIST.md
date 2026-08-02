# Public Release Checklist

## Automated

- [ ] Release tests pass.
- [ ] Release plugin build succeeds.
- [ ] Source, packaged, and repository manifest versions match.
- [ ] `repo.json` and the icon URLs resolve from GitHub Pages.
- [ ] `tools/Verify-RouteData.ps1` passes.
- [ ] The packaged archive contains the manifest, assemblies, dependencies, and route data.

## In game

- [ ] Install and update through the public repository rather than a development install.
- [ ] Run a complete multi-FC refresh and inspect `/xldev` → Plugin Statistics for frame-time or memory regressions.
- [ ] Cancel during route search, close the window, unload, and reload the plugin.
- [ ] Test missing, disabled, and newly updated SubmarineTracker installations.
- [ ] Test a stale database, active voyages, unknown routes, incomplete unlock data, per-FC timeout, and no-deadline mode.
- [ ] Verify 100% and 150% UI scaling at the minimum window size.
- [ ] Confirm the displayed percentile and unlock assumptions remain understandable without reading the README.

## Publishing

- [ ] Use a genuinely human-created icon, or retain the current icon disclosure in the manifest.
- [ ] Capture privacy-safe screenshots without character names, FC identifiers, or unrelated game chat.
- [ ] Tag the exact release commit using `v<version>`.
- [ ] Include concise release notes or a manifest changelog.
- [ ] For official Dalamud submission, disclose **Copilot** AI usage and the AI-generated icon.
- [ ] Submit new plugins to `testing/live` before requesting stable promotion.
