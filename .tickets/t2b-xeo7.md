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


## Notes

**2026-11-22T20:29:37Z**

2026-11-22, the line orchestrator, from the orchestrator. The user's bar accepts lost light. It does not accept a visible change over time, and the user judges such a change from a video. So this ticket needs one film before anyone judges it: the same room, the camera turns from the view that hides the lit face to a view that shows it, and back. Real engine frames at the game's speed, one fixed exposure, the fixed render scale 2/3 stated on the label, H.264 at CRF 12 or lower. With it, the room's level per frame in two or three crops: how much it rises, in how many draws, and whether it falls again when the camera turns back. The research take's lead makes it in its worktree after step 1 of t1-gout. No remedy and no design before the user saw the film.

**2026-11-22T20:37:58Z**

2026-11-22, the line orchestrator, from the orchestrator: a correction of the film. A turn of the camera changes neither the eye's place nor the environment map, and the lit face points away from the eye, so no direction from the eye shows it. The film is a move: the eye moves around the block, from the place that hides the lit face to a place that shows it, and back. A second take beside it has the eye at rest and the camera turning toward the block's side, to show that a turn alone changes only what the screen's rays add. Both give the room's level per frame in two or three crops. The user asked whether the dim room is an artifact of a render that had not finished. It is not: the kept frame is frame 239 of a film of 240 frames from the run's start, the level changes by at most 0.9 % from frame 231 to frame 239, and by the code's rule the scene's one map draws all six faces on its first draw (EnvironmentFaces.RefreshesAll) and then one face per draw in turn (SceneEnvironmentMap.Default), so every face was drawn about 40 times. The run did not log the faces per draw: the film's run does, if the probe allows it.

**2026-11-22T22:05:41Z**

The film of the lit back in motion, as ordered: docs/research/back-face-depth-layer/stage-2/lit-back-film/README.md, video lit-back-walk-and-turn.mp4 (840 real draws at 60 a second, 1280x720, render scale 2/3, 6.79 stops fixed, H.264 CRF 12). Left: the eye walks round the block to behind the lit face and back. Right: the eye rests and the camera turns.
- The same floor point P = (1.95, 0, -1) behind the block, lit by bounce alone, reads 0.078 at rest and 0.507 at the walk's hold, where the eye sees the lit face: 6.5 times brighter. In the turn it stays within 8 % (0.077 to 0.083). After the return it falls to 0.098 at draw 720, 0.081 at draw 760 and 0.079 at draw 780.
- At the start pose the room is the same before and after the walk: three screen crops within 0.1 to 0.8 % (walk) and 0.5 to 1.5 % (turn).
- The face log measures the map's refresh: all six faces at draw 0, then one face per draw in turn (145 or 146 draws per face over 872 draws). So the dim room is not an unfinished render.
- Reading: the floor's bounce light depends on what the eye sees, so walking round the block visibly brightens and then darkens the room behind it. I did not check whether the screen's rays or the environment map carry it.
