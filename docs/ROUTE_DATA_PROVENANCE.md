# Route Data Provenance

`src/SubmarineEtaPlanner/CalculatedData.msgpack` contains precomputed submarine route combinations. Keeping this data local lets the planner search routes without runtime web requests.

## Current source

- Upstream project: [Infiziert90/SubmarineTracker](https://github.com/Infiziert90/SubmarineTracker)
- Upstream plugin version: `2.0.5.6`
- Source revision: [`aa3b40ce3e7eb9c2db9b5ad4ce2cb489755d7a5a`](https://github.com/Infiziert90/SubmarineTracker/commit/aa3b40ce3e7eb9c2db9b5ad4ce2cb489755d7a5a)
- Upstream path: `SubmarineTracker/CalculatedData.msgpack`
- Last upstream change to the blob: [`35d4463ffc09ab2496d93a5f1a5a01977b00e7ce`](https://github.com/Infiziert90/SubmarineTracker/commit/35d4463ffc09ab2496d93a5f1a5a01977b00e7ce)
- SHA-256: `24996254FAB3FFC4A74F1AFA2C9212732888A0C6387DAB026B75EA566B6D67FF`

Run `tools/Verify-RouteData.ps1` to verify the bundled file. Pass an extracted or checked-out upstream file with `-UpstreamFile` to confirm byte-for-byte equality.

## Updating

1. Check out the intended public SubmarineTracker revision.
2. Use its committed `SubmarineTracker/CalculatedData.msgpack`, or regenerate it in a DEBUG build through SubmarineTracker's `Importer.Export()` route builder.
3. Replace the local file only after reviewing the upstream changes and license.
4. Update the version, revision, last-change revision, and SHA-256 in this document, `THIRD_PARTY_NOTICES.md`, and `tools/Verify-RouteData.ps1`.
5. Run the complete test suite and a real in-game forecast after a game-data update.

The plugin also compares the bundled route and unlock coverage with the live Lumina submarine-sector sheet. If the game contains destinations absent from the bundled data, the dashboard reports an explicit compatibility warning instead of silently treating the cache as current.
