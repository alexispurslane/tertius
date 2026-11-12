---
id: t2-3dut
status: open
deps: [t2-bne2]
links: []
created: 2026-11-11T23:10:43Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, performance, lights]
---
# Count the deck's screen tiles that need no rectangle edge sum, then trace a direct pass with a shader per tile kind

Design A of docs/plans/light-pairs.md (t2-mlvy). On the deck, the rectangle lamps' two edge sums cost 1.66 ms of the direct pass's 4.19 ms and 2.2 ms of the frame: with a point light's response for every rectangle lamp the pass takes 2.53 ms (variant x18, exposure fixed). Change 1 of t6-h5e3-six-frame-cuts already shades a far rectangle as a point, and wins 0.1 ms, because the edge sums' code stays in the loop body and unused code costs time on this GPU. A loop of its own for the point pairs wins 0.2 ms more in the direct pass and loses 0.5 ms in the transparent pass (variant x19). So the next step is a shader per screen tile: a tile whose every rectangle pair shades as a point runs a pipeline without the edge sums. First experiment, a picture: the share of the deck's 16-pixel tiles of each kind under the rule of t6-h5e3 change 1 (it depends on the materials' roughness). Second, only if that share is large: sort the tiles in a pass, draw the direct pass with two pipelines, and trace it against main with the exposure fixed.

## Acceptance Criteria

The share of tiles without edge sums on the deck, the cabin and the bay is measured. If it is above a half on the deck, a two-pipeline direct pass is traced against main and against t6-h5e3 change 1.

