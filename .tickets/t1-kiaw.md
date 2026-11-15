---
id: t1-kiaw
status: open
deps: []
links: []
created: 2026-11-15T01:24:22Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-kh4g
tags: [rendering, performance, design]
---
# The back-face pass repeats the vertex work of every one-sided solid mesh: 0.45 ms on the RTX 2000 Ada and 0.80 ms on the M1 Max in a crowd of 2.0 million skinned vertices

Found in stage 1b (docs/research/back-face-depth-layer/stage-1b/cost.md). In rooms the pass alone costs 0.032 to 0.062 ms on the card. In scripts/ter-kh4g-crowd-timing.json (rigs-crowd, 100 skinned cylinders of 20,202 vertices) it costs 0.453 ms of the card's frame and 0.801 ms of the Mac's. Its own time on the card is 0.485 ms, against 1.294 ms for the G-buffer pass. So the pass's cost follows the vertex count. The design of stage 2 must say what a view with many skinned figures pays, and whether the pass can share the vertex work of the G-buffer pass or skip small or far meshes. One person has about 20,000 vertices, so the crowd is far denser than a usual view.

## Acceptance Criteria

The design document states the pass's cost model by vertex count and its answer for views with many skinned figures.

