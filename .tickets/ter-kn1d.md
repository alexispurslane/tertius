---
id: ter-kn1d
status: in_progress
deps: []
links: []
created: 2026-11-01T18:54:33Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, environment-map]
---
# Environment map: the room's light flips between the ceiling and the floor within 0.17 s on spinward

Found on 2026-11-01 from the user's question about dark tops in a still of the FSR experiment (the still is in fact its native render, /tmp/fsr1/stills/native/exit/exit-spinward.png, tick 3930).

What shows: on demo-exit's spinward camera, with the environment map on, the room's light turns over between tick 3905 and tick 3915 (60 ticks per second). Before: the ceiling is black and the floor near white. After: the ceiling is near white, the floor dark gray, and every face that looks up (the floor, the green plank's top, the boxes' tops) is dark, while the sides stay lit. With --environment-map off, the same ticks show no change. The exposure stays about the same (1.17 stops at tick 3885, 1.39 at tick 3930). The flip also shows on long-step captures, where every face of the map refreshes (ter-xgra's gallery at tick 3885 and the FSR thread's still at tick 3930), so it is not only old faces that wait for their turn. Tick 3910 is a mix, which fits a few faces per frame at a step of 5 ticks (0.083 s, under the long step of 0.09 s). The green box goes from the floor to the top of the blue column in the same 10 ticks; this may be a coincidence.

Why it is a defect: the room and its lamps do not change, so the light that reaches a face that looks up cannot turn from bright to dark in 0.17 s. The darkness of the tops is D(n) of the map's irradiance, not the screen-space light's occlusion: the open floor, where nothing can occlude, darkens by the same amount. The screen-space light's speck band along the wall crease (ter-xgra, artifact 1) is strong only in the state after the flip.

Evidence (git ignores captures/, so use the main checkout's absolute paths): captures/map-flip-2026-11-01/ holds spinward-flip.json (demo-exit to the spinward camera, then a capture every 5 ticks from 3885 to 3930, at 1920 x 1080), map-on/ and map-off/ (Release, headless, default settings and --environment-map off), sheet.png (all 20 frames), flip-2x2.png (ticks 3905 and 3915, map on and off), spinward-pair.png (ter-xgra's tick 3885 beside the FSR thread's tick 3930) and compare.png (crops of the plank and the boxes).

Leads, not yet checked: (1) the camera's frame, the map's frame (RenderSnapshot.OriginFrameId) or the served chain changes near tick 3910, and the faces are drawn in one frame's axes but read in another's (docs/plans/environment-map.md, decision 23 and 'The faces and their axes'); (2) the draw rule of the chain leaves out a different subtree, with its lights, after a change of the chain; (3) a basis that the code builds from a direction, for example the ring's up, turns over when that direction crosses a threshold.


## Notes

**2026-11-01T18:55:46Z**

Workflow run a84b9182-9905-4d23-8200-18d47d2abb85, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-kn1d-environment-map-flip, base d9e028ba.

**2026-11-01T19:37:40Z**

The diagnosis (run a84b9182, branch head 65629c74) found a sun leak under the near side wall through the cascade filter's receiver-plane term, a design fork. The user chose design A, one bias model, on 2026-11-01: remove the receiver-plane term and give the cascades the tiles' slope bias of 2.5. Rejected: design B (the plane with rejection of taps that are not the receiver's own surface: unmeasured, two bias models, a thin leak left at the base) and MinimumReceiverCosine 0.3 (the map still turns over). Continuation run 26a0f03a-c115-440c-b3c1-c4474eb08861, same version, branch and base, in the preserved worktree.
