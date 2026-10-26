---
id: ter-y1s7
status: closed
deps: [ter-9xoi, ter-rxjr]
links: []
created: 2026-10-25T20:07:17Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-a24f
tags: [rendering]
---
# Bounce light: the acceptance tests and the wiring

Part 6 of docs/plans/screen-space-illumination.md.


## Notes

**2026-10-26T16:26:16Z**

2026-10-26: Part 6 committed (557fc379) with its review fixes (6fa90930) on ter-a24f-ssgi. Open: the reviewer's convex-edge finding. Beside a convex edge whose side face is seen nearly edge-on, the plane extension of the floor fix puts a step's surface point in the air: 31 of 8,460 texels block 1 to 6 of 32 sectors, up to 0.18 m from the edge, so V drops in a thin band. Settled by the orchestrator, by the rule 'add an exception only when the pure model misbehaves and you can name it': option A, reject a surface point whose view depth lies outside the depths of the texels that straddle the step's exact place, in the GLSL and the C# copy (SectorSearch.SliceSteps). The red test is in the scratchpad as y1s7-ConvexEdgeSectorsTests.cs.

**2026-10-26T17:48:43Z**

2026-10-26: done on ter-a24f-ssgi. Part 6 557fc379, review fixes 6fa90930, convex-edge fix aa0c747e: a step's surface point must lie between the depths of three texels around its exact place, else the step stands at its texel's own point P(t). Impostors' false geometric normal filed as ter-h1z6.
