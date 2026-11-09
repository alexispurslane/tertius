---
id: tiunr-e8xk
status: closed
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


## Notes

**2026-11-09T00:32:46Z**

Done 2026-11-08. Shares the branch t3/tiunr-e8xk-tube-light-stryker with t9-al5u, whose two tool commits come first.

- Base da70cd8c2 (the parent of the tube light commit) with head main at f3fee2c12. For Core, Simulation and Physics, that diff is identical to the diff of 972d42f16 alone, so the run mutated exactly the tube light's lines and none of main's other commits.
- Run1, the official run with perTest: valid, 6,509 s (Core 2,674 s with 0 mutants, Simulation 3,813 s with 10 mutants), 1-minute load mean 12.8 and max 45.9, no timeout, no Dubious test. 8 killed, 2 survived: the sign of the render origin in the tube light's Position and PreviousPosition.
- tools/mutate.py, 16 listed mutants for the changed lines without a Stryker mutant: before the new tests 8 killed and 6 survived (entity index, entity generation, color, power, shadow flag, chain depth).
- New test cases, red on each survivor (mutate2): a tube case in ALightOnAMovingEntityCarriesItsPreviousPlace (a moving camera, so the origin is not zero), a tube in ALightCarriesItsPowerRadiusAndShadowFlagIntoTheSnapshot and in ALightCarriesTheIndexOfItsEntity, and a tube light in the sun's frame in RenderSnapshotChainTests.ALightTakesTheDepthOfTheEntityThatCarriesIt.
- Not run: a second full official run to show the kills of the two Stryker survivors. Run2 (perTest) was stopped for t9-al5u, and run3 (coverage off) was stopped because it was slower. The parent decided that M15 and M16 in tools/mutate.py show the kills.
- Table: the tiunr-5jqs note of 2026-11-08.
- Deviation: my first start of run1 ran under a 2-hour background limit, so I stopped it after seconds and removed its stale lock (holder pid 75251, dead, label of this worktree) before the real run1.
