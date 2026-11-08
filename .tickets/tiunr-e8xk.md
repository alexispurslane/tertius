---
id: tiunr-e8xk
status: open
deps: []
links: []
created: 2026-11-08T19:49:01Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# Run the official Stryker run on the tube light's Core and Simulation lines

The tube light's hardening (tiunr-5jqs) sent its changed Core and Simulation lines (RenderLight.cs, LightData.SurfaceRadiance, the tube case of RenderSnapshotBuilder) to tools/mutate.py, not to the official Stryker run that CLAUDE.md requires for those modules. The review of the merge on 2026-11-08 found it (finding F1). It was not run at the merge, because a Simulation run takes hours and no Checked build or commit may happen in the worktree during it. Run nix develop --command python3 -I tools/stryker/changed.py <base> <out> over those lines, and answer each survivor in the tube light's mutation table.

