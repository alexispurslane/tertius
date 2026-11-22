---
id: t2b-xeo7
status: open
deps: []
links: []
created: 2026-11-22T20:08:08Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering]
---
# A lamp that lights only faces the camera does not see sends no bounce: behind the block, the lit-back room is 9 to 71 times darker than the path trace

Found by t1-r9vj (docs/research/back-face-depth-layer/stage-2/fade-lit-front.md), on main 906d1c260 and in every column of the probe. Lost light, which the user's bar accepts in a small amount; here it is most of the room's light. The user decides whether it is a bug.

The scene: lit-block-back of refs/scratch/t1-r9vj-lit-block. A spot lamp lights the block's face away from the camera. The camera sees only the block's dark face.

Measurements (frame 239, mean luminance over the floor patch of the lit-front frame, the same floor lamp): the back wall above the block reads 0.0055 in main and 0.0033 to 0.0057 in every rule. The path trace reads 0.195 with all bounces and 0.085 with one bounce. The floor in the gap behind the block reads 0.0030 against 0.213 and 0.064. Sheet: docs/research/back-face-depth-layer/stage-2/lit-back-sheet.png (the path trace's tile is near white at the matched exposure).

Reading, not measured: the screen tracer and the environment map see only what the eye sees. The lit face is hidden from both, so its light reaches no surface. A camera that turns until it sees the lit face would brighten the whole room. That change over time was not filmed.

