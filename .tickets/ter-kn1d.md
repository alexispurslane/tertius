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

**2026-11-01T19:54:29Z**

The decision (the user, 2026-11-01): design A, one bias model. The cascades lose the receiver-plane term of sampleCascade, `reference + max(dot(slope, offset), 0.0)`, and take the tiles' slope-scaled caster bias of 2.5 (decision 33 of docs/plans/local-light-shadows.md) in place of 1.5. The normal offset and the constant bias stay.

The reasons.
- One model for the cascades and the tiles: every tap compares against the receiver's own depth, and one caster bias, ShadowPass.SlopeBias, covers the receiver's slope over the filter's reach of 2.5 texels. The tiles already worked this way.
- The receiver-plane term is the cause of the leak. At a grazing light, the plane extrapolated toward the light passes under a wall that stands on the receiver, so the taps see the light past the wall. Without the term, the leak cannot happen, at any bound of the slope.
- The user rejected design B, the receiver plane with rejection of taps that are not the receiver's own surface: it keeps two bias mechanisms and adds a third rule.
- The user rejected the bound of MinimumReceiverCosine at 0.3: it shrinks the leak about tenfold but leaves the flip (E(up)/E(down) 0.091/0.020 to 0.046/0.069).

Implementation and tests (the first commit of the fix).
- ShadowTests.AShadowReachesTheBaseOfAWallOnAFloorThatTheSunGrazes, at the sun cosines 0.134 (the floor's crease) and 0.025 (the ceiling's crease). A wall 0.3 m thick and 5 m tall, as the wheel's side walls, stands on a floor slab 1 m thick that ends at the wall's far face, with the sun behind the wall. It compares the floor's light 0.02 to 0.4 m in front of the wall's base with the floor's light without the wall. On the old code, it failed at both cosines: a share of 0.25 of the sun everywhere up to 0.25 m (0.5 at 2 cm for 0.025), which is one row of four taps of the 4 x 4 filter. On the fix, the share is 0 at every sample.
- A first version had a floor of no thickness. With the fix, it still let 0.15 of the sun through at the cosine 0.025, independent of the wall's thickness and height and of whether the floor casts. The cause: the filter reaches 2.5 texels and the normal offset lifts the lookup 1 texel, so the lowest taps look below the floor's top, and under the edge of a floor of no thickness they see the sun. A slab's end face stops them, as the wheel's floor does. With the slab, the share is 0 at the cosines 0.025, 0.05, 0.08 and 0.134. The note in the test's scene says so. The plane floor's leak is not this ticket's mechanism: it needs open geometry, and the old code leaked there too.
- The acne tests (LitGroundHasNoAcne at 8 and 20 degrees) and every other ShadowTests, LocalShadowRenderTests and HairRenderTests test pass.
- HairRenderTests.AHairCardLitFromBehindKeepsTheShadowOfACasterCloseBehindIt: the arithmetic derived again. Every tap now compares against the lookup's own depth, so the need is the caster's downhill drop 0.81, the footprint 0.54, the constant bias 0.5 and the slope bias 2.5 x 0.39 = 0.98: 2.82 texels. The gap rises from 3 to 3.5 texels, so that half a texel stays spare. The second premise, about the floored receiver cosine, has no mechanism left, so it is gone.
- The hair card's turn of the normal toward the light keeps a reason. Without it, AHairCardLitFromBehindDoesNotShadowItself fails: the normal offset then moves the lookup away from the light, into the card's own shadow.
- Removed with the term: ShadowPass.MinimumReceiverCosine, its frame parameter (shadowParams.w is now unused), and the GPU fields AxisX and AxisY of GpuCascadeSet, which only the term read (the struct is 256 bytes, the frame block 2,192). LocalShadowPass.SlopeBias is gone: both shadow passes take ShadowPass.SlopeBias.

**2026-11-01T19:54:39Z**

puffs-lit.png re-approved (LitPuffRenderTests.LitPuffsAreBrightInTheSunDimInTheShadowOfABoxAndWarmUnderALamp). The test failed with 237 pixels above a FLIP of 0.1, the largest 0.138, the mean 0.00222, as the diagnosis measured. I opened the reference, the actual image and the FLIP heat map.
- The reference and the actual image look the same at a glance: the gray floor, the roof's shadow on it, the warm glow of the lamp under the roof, and the three puffs (dim in the shadow, warm under the lamp, bright in the sun).
- The heat map shows one bright line along the roof's sunlit front face, and faint contour rings on the floor.
- The roof's front face, 0.3 m tall, darkens by up to 9 of 255 levels over its lower two thirds (rows 82 to 89, 1,655 pixels, 96 to 88 at worst). The sun reaches that face at a cosine of 0.30, so the whole face covers about 1.5 cascade texels seen from the sun. The filter's taps across the roof's front edge compare the face's depth with the roof's top, which lies nearer the light, and the top's small slope bias does not cover the difference. The old receiver-plane term followed the face's plane over the edge. This is the cost of design A that the diagnosis named.
- The lit floor darkens by 1 of 255 levels in 7,165 pixels (the contour rings). With every tap at the receiver's own depth, the bias of 2.5 times the steepest depth change per texel leaves the corner taps of the filter a little short where the floor's slope runs diagonally across the map, so a small part of their bilinear weight shadows. This is under the acne tests' tolerance of 2.5 levels.
- The puffs, the roof's shadow on the floor and the lamp's light do not change: every pixel outside the roof's front face differs by at most 1 level.
Against the scene's description (lit puffs bright in the sun, dim in the box's shadow, warm under a lamp), the new image is correct. I approved it.

**2026-11-01T20:24:59Z**

The fix in the captures, the second sun term, and the cost (2026-11-01). The captures are in captures/kn1d-fix/ of the ter-kn1d worktree (git ignores them): before/ is the base d9e028ba, after/ is the fix 89d1e53c, both Release, headless, 1920 x 1080, from the three scripts in captures/kn1d-fix/scripts/ (copies of the main checkout's scripts at 1080p). Sheets: spinward-sheet.png, spinward-2x2.png, films-sheet.png, and crop-*.png and heat-*.png for each changed view.

spinward-flip.json: no frame turns over. All ten frames show one state, the state that the map's rules give a room lit only by its ceiling lamps: the lamps hang from the ceiling, so a face that looks up sees them and E(up) is larger than E(down), and the ceiling, which sees only the lamp-lit floor, stays dark. Before the fix, ticks 3885 to 3905 showed a near-white floor, tick 3910 a mix, and ticks 3915 to 3930 a near-white ceiling with dark tops. After it, the mean display level is 96.5 at ticks 3885 to 3905, 84.1 to 84.6 at ticks 3910 to 3925, and 91.0 at tick 3930, at a steady exposure of 3.354 stops. The step at tick 3910 is the end of the ceiling cracks' light (below). The rise at tick 3930 is the +Z face's redraw with a brighter floor sliver (below). Neither swaps the ceiling and the floor. The speck band along the far crease (ter-k96l, artifact 1) is gone too.

The second sun term, traced with a probe of map 1 that was never committed (captures/kn1d-fix/probe-a.log and its face dumps, on exp-a.json).
- Under the fix, map 1's E(+Z) is 0.129, 0.171 and 0.149 at ticks 3885, 3930 and 3960. Of that, 0.116, 0.158 and 0.138 come from 21, 16 and 12 texels of the +Z face above a radiance of 1 (the lamp-lit walls are 0.003 to 0.04). On the base it was 0.31 to 0.72 from 104 to 428 such texels.
- Those texels lie on one thin row. The rays from the eye through them meet the floor 13 to 50 m along the ring in +Z, 0.1 to 0.45 m from the near wall's inner face. The sun is at (x, -0.668, -0.73) in station axes, beyond the near wall, and the floor there faces it at a cosine of 0.25 to 0.49, because the ring turns the floor by 7 to 29 degrees over that distance. The near wall hides that floor from the sun, so it is a leak at the wall's base.
- It is not the receiver-plane term, which is gone. With the term gone and the old slope bias of 1.5, the bright part is 0.057 to 0.075 from 10 to 14 texels: it doubles with the bias. So it is the slope-scaled bias moving the wall back where the texels are coarse, a shadow that starts away from its caster. I did not measure which cascade holds those points.
- It cannot turn the room over by itself. The rays lie from 5 degrees below to 12 degrees above the eye's horizon, so the sliver adds 0.018 to 0.022 to E(up) and at most 0.0007 to E(down) at all three ticks. It does not move to the other side as the sun crosses the floor's plane at the eye, because the cosine at that far floor stays 0.25 to 0.49 through the crossing.
- A second leak, in the +X face at tick 3885 only: two diagonal stripes on the ceiling, 3.4 m from the eye, from the near wall's crease to 1.3 m from it, 1.4 degrees of the ring apart, at a sun cosine of 0.01 to 0.025 from below. The ceiling bands turn by half a segment against the side walls (StationExit.CeilingTurn, pi / 256), and both rings have 256 flat segments. So at each wall vertex the ceiling's face lies 95 (1 - cos(pi/256)) = 7.2 mm inside the wall's top edge: a real crack in the geometry every 2.33 m, at both walls. With CeilingTurn set to 0 for one run, the step at tick 3910 is gone (96.8 at every tick to 3925). This is a defect of the demo's geometry, not of the shadows.
Both are for new tickets; this ticket fixes neither.

The other views (films at 1080p, deck-late).
- unbelt, deck: unchanged, pixel for pixel.
- brake-run: a bright sun sliver along the floor's crease with the side wall, with its bloom, is gone; the exposure rises from 3.577 to 4.011 stops, because the meter no longer sees it.
- chute-top and chute: a dotted row of sun specks along the upper crease is gone (exposure +0.15 and +0.09 stops).
- floor: a dotted line of sun specks along the far crease of the side wall and the ceiling is gone. The lamp's glow there stays.
- spinward: the room's lamp state instead of the near-white floor (exposure 1.167 to 3.351 stops).
- antispinward: the same layout, 0.2 stops more exposure; the side wall's slope shades a little differently with the new map.
- stack: 19 pixels change by 16 levels or more; nothing visible.
- deck-late (the hub, where the sun enters by the slot): 168 pixels change by 16 levels or more. The speck band of the screen-space light along the base of the glass gallery is a little sparser, and the exposure rises by 0.09 stops.
I looked for shadows that now start farther from their casters: a pixel that got brighter by 8 levels or more at a caster's base. The views have none past the exposure change: deck-late 149 such pixels, on the gallery's glass and frame, floor 701 on the far side wall, chute 31, chute-top 66, stack 7. The sliver in map 1's +Z face above is the one such case, and it shows only in the map.

Cost: the direct pass, from four interleaved pairs (before, after, after, before, twice) on a measure script of 40 frames of spinward at 1080p, with separate before and after builds, under the GPU lock, at a 1-minute load of 8.4 to 9.9 (other builders ran). Direct light mean 6.20, 6.16, 6.05, 6.05 ms before against 5.70, 5.70, 5.75, 5.70 ms after: 6.11 to 5.71 ms, -0.40 ms (-6.5 %). Medians 5.94 to 5.74 ms. The control, the G-buffer pass, stays at 0.605 to 0.611 ms in every run. The environment faces' median falls from 0.536-0.551 to 0.469-0.504 ms. No rise.
