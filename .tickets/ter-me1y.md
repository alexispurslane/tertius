---
id: ter-me1y
status: open
deps: []
links: [ter-4psc, ter-tenw]
created: 2026-10-17T07:05:17Z
type: chore
priority: 4
assignee: Alexis Purslane
tags: [physics, frames, performance]
---
# Small lags and costs of numeric frame ghosts

From ter-2gg0 (2026-10-17): (1) A body that moves into a numeric frame at a sync point gets its new ghosts only at the end of the next step. (2) The stick-out test has no split hysteresis, so a member at the edge of the reach can make a ghost appear and vanish at each update; no test showed harm. (3) The existing merge test calls PoseTree.Relative once per pair of bodies, about 70 ns a call and about 350 us per update for 100 falling bodies; the coarse first test in the semantic frame's coordinates that ter-2gg0 added (PlaceSlack) can cut that too. Measure with benchmarks/Tertius.Benchmarks/NumericFrameUpdateBenchmarks.cs.


## Notes

**2026-10-18T01:24:54Z**

Review of ter-4psc (2026-10-17): item 1 is fixed for the ghosts of the bodies that stick out of a frame whose hull a numeric update moves. The move takes those ghosts from the hull's old home into its new one at once (JoltPhysicsBackend.MoveGhostsThatStickOut). Item 1 still holds for a home that an update names for the first time to keep a ghost of an anchor, and for the ghosts beside it: they appear at the end of the next step.
