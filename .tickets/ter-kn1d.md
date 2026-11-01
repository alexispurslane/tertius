---
id: ter-kn1d
status: open
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

**2026-11-01T19:31:16Z**

Diagnosis (2026-11-01), stopped at a design fork. No code changed.

Cause. The sun leaks into the closed wheel room through the cascade shadow filter, at the creases where the near side wall (y = -10 m) meets the floor and the ceiling, about 1 m from spinward-camera's eye. The leak comes from the receiver-plane term of sampleCascade in shadows.glsl: a tap compares against the receiver's plane extrapolated uphill, `reference + max(dot(slope, offset), 0.0)`, with the slope floored at a receiver cosine of 0.1 (ShadowPass.MinimumReceiverCosine). At a grazing sun, the plane extrapolated 1.5 texels passes under the wall that stands on it, so the taps see past the wall. The station turns the sun across the floor's plane near tick 3910. The sun's x in station axes is -2.2e10 m at tick 3885 and +4.1e9 m at tick 3930, and +X is close to the room's up. Before the crossing, the leak lights the ceiling's crease (NdotL 0.025). After it, the leak lights the floor's crease (NdotL 0.134). The sliver fills about 1,000 texels of a face, and its light is about 100 times the lamps' light in the map. So the room's ambient light turns over.

Evidence (captures/kn1d-diagnosis/ in the ter-kn1d worktree, git-ignored).
- Map 1 (the station's map, which lights the room) holds E(up)/E(down) = 0.651/0.036 after the refresh at tick 3885. It holds 0.224/1.262 after the single-face redraws of +X and -X at ticks 3910 and 3915 (probe2.log). The capture frame numbers are one higher than the capture index, because frame 0 is the metering refresh.
- A refresh at tick 3800 or 3885 gives the before state. A refresh at tick 3930 gives the after state, also as the first drawn frame (exp-a, exp-b and exp-c). So the cause is the world's tick, not the first frame or an old face.
- With the sun left out of the faces, map 1 holds 0.009/0.011 at both ticks (exp-a4.log).
- In a dump of the faces, the bright strip lies at the near wall's base. Its cascade-0 visibility rises from 0.04 to 0.29 over the last 25 cm before the wall. With the receiver-plane term removed, the lit texels fall from 1196 to 92, from 1315 to 0 and from 368 to 3. Without the normal offset or the constant bias, they barely change (face-dumps-bias-variants).
- The main view shows the same leak when it looks there. Aimed at the wall's base below the eye with the map off, it shows a bright sliver at tick 3930 and none at tick 3885 (look2-off.png). The face's holding-box rule and the main view's depth rule give the same visibility at the strip's texels.

The leads that were ruled out.
1. Frames and axes: the chain (station, root), the served chain and the camera's frame stay the same in every drawn frame. Map 1's axes are the identity. No refresh happens at the flip.
2. The draw rule: map 1 draws all 86 instances and all 22 lights in every frame. The light list differs only in the sun's position.
3. A basis that turns over: map 1's basis is the table of CubeFaces at the identity. The direction that crosses a threshold is the sun's, which crosses the floor's plane.
The green box's move in the same ticks is a coincidence: with the sun left out, map 1 does not change.

Two designs, measured with temporary changes.
- One bias model, the tiles' model. Remove the cascades' receiver-plane term, and give the cascades the tiles' caster slope bias of 2.5. The 40 ShadowTests pass, the acne test at 8 and 20 degrees included. On spinward-flip.json, map 1 holds E(down) 0.016 to 0.017 in every frame and E(up) 0.061 then 0.031, so no turnover. The speck band along the far crease (ter-k96l) is also gone (designB-compare.png). Cost: puffs-lit.png changes (237 pixels, largest FLIP 0.138). HairRenderTests.AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt computes with SlopeBias and needs its arithmetic derived again. A larger slope bias moves steep casters back.
- Bound the receiver-plane term. Raise MinimumReceiverCosine from 0.1 to 0.3. The 40 ShadowTests pass. The leak shrinks about tenfold, but the map still turns over: E(up)/E(down) goes from 0.091/0.020 to 0.046/0.069. Cost: the leak is bounded, not removed, and two bias mechanisms stay. The hair test was not run.
Both designs leave a smaller sun term in the +Z face of map 1 (E(+Z) 0.13 to 0.19, against 0.006 without the sun). Nobody traced it yet.
