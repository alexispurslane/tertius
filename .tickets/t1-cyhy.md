---
id: t1-cyhy
status: in_progress
deps: []
links: [t1-nyjl]
created: 2026-11-23T18:47:35Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, screen-trace]
---
# Most guided diffuse draws do not arrive at their target: 1.4 to 32 % arrive in the bay, and the rest bring nothing

A finding of the guide census (t1-nyjl), 2026-11-23, measured by the surface share's lead from the census's log of bay-sunbeam (2/3, 240 settling and 400 counted draws). No work is ordered yet: the stills of the separated signals (t1-nyjl) decide first whether the diffuse signal's variance shows in the bay's picture at all.

## Terms

- **Guided draw**: a diffuse ray that aims at a pixel of the screen, chosen in proportion to its guide light (guidedTargetOf, guidedPointOf in screen_trace.glsl).
- **Target**: the pixel that a guided draw aims at.
- **Arrive**: the walk hits a pixel of the 3 x 3 around the target and the ray crosses the target's box (the flag counts of guidedDensityOf). A guided draw that does not arrive brings 0, because the guided density covers only the draws that arrive.

## The numbers

| Region | Guided draws that arrive | Hit another pixel | Hit nothing | Median distance of the other pixel from the target, render pixels (p90) |
|---|---|---|---|---|
| Bay sunlit patch | 1.4 % | 15.6 % | 83.0 % | 9.4 (27.8) |
| Bay back wall below the slot | 14.8 % | 62.8 % | 22.4 % | 5.8 (10.8) |
| Bay floor | 11.6 % | 59.5 % | 28.9 % | 6.4 (12.0) |
| Bay right wall | 32.4 % | 66.7 % | 0.9 % | 3.2 (5.4) |
| Nose ceiling box | 87.8 % | | | |

- The variance of X within the guided draws splits into the part from arrival, p (1 - p) mA^2, and the part from the spread among the arriving draws, p vA (p the arriving share, mA and vA the mean and variance of X over the arriving draws). Arrival carries 0.842 to 0.865 of it in the bay's four regions at a share of 0.25, and 0.125 in the nose's box.
- The bay is open: the draws that fail are not occluded. They land beside their target. On the right wall 63 % of the draws that hit another pixel are within 4 pixels of the target, outside the 3 x 3 window.
- On the sunlit patch, hit nothing is mostly targets on the patch's own plane, which trace nothing, or walks that end.
- Tables: arrival-bay.json, blocked-bay.json, misshit-bay.json in the lead's evidence folder t3-2b720d96-b40221aeff8f27f7-evidence/guide/.

## What it means

In the bay the guide spends a quarter of the diffuse rays, and 68 to 99 % of those bring nothing. The rays that arrive carry most of the walls' light: on the right wall, guided draws carry 80.8 % of the second moment.

## A hypothesis of the line orchestrator, not a measurement

The target point is a uniform point of a box that runs from the target's depth to TargetDepthShare (0.05) of that depth behind it, along the eye's view ray through the pixel (LightPyramid.TargetOf, decision 7 of docs/plans/back-face-depth-layer.md). A ray from another surface toward a point inside that box crosses the real surface first, beside the target's pixel, by up to 0.05 times the depth times the tangent of the angle between the ray and the view ray. At a depth of 5 m that is up to 0.25 m times that tangent, and a render pixel there is about 6 mm wide. So a ray lands several pixels off unless it runs nearly along the eye's view ray. The test from the log, ordered from the lead: the arriving share in 4 bins of the target point's depth share. If the arrivals sit in the first bin, the box's depth is the cause.

## If the hypothesis holds, the design question

The model of a target is then wrong for a ray from the side: the target is a piece of a surface, not a box behind a pixel. Candidates, to be designed twice when the ticket is taken: a target point on the pixel's own surface plane (from its normal), with the density of a surface patch (the patch's area and cosine over the squared distance), or a wider counting window whose density is summed over the pixels that a draw can reach. Both must keep the estimate unbiased.

## Out of scope until ordered

Any change of the guide. The guided share (t1-nyjl) and the reflection rays' mixture (ter-fe63).


## Notes

**2026-11-23T18:49:37Z**

2026-11-23, line orchestrator, after the orchestrator's word: this finding matters more for the reflection mixture (ter-fe63) than for the diffuse side. A reflection ray in the mixture takes the same guided draws, so in the bay 68 to 99 % of them would be lost, while 88 % arrive on the nose. The mixture's bay pictures are not fair before the arrival is understood. Two answers that the orchestrator wants here. (1) What a draw that does not arrive brings, from the code (traceFrom, screen_trace.glsl): 0. A guided draw whose walk hits nothing sets its light to 0, and a guided draw that hits a pixel outside the 3 x 3 around its target, or does not cross its target's box, sets its light to 0 too. It does not take the light of the pixel that it hit. Its density is still the mixture's, with the guided density of the direction and the hit pixel, so the record is consistent; the sample is a zero. The reason in the code's comment: the guided density covers only the draws that count, so a draw outside it has no density to be weighed by. (2) Whether the cosine draws lose hits the same way: not known, and the log tests it. Ordered from the surface share's lead, early and with no take: (a) the arriving share of the guided draws in 4 bins of the target point's depth share, which tests the box hypothesis; (b) for the guided draws that hit another pixel, the distance of the hit pixel's surface point from the ray's line in widths of that pixel's footprint: under about 1.5 widths means that the walk found the true first surface and the target point was not on it, and more means that the walk registers a hit at the wrong place; (c) the same share for the cosine draws that hit; (d) the ends of the draws that hit nothing, guided and cosine apart: left the screen, ran out of steps, rejected behind a surface, or traced nothing below the geometric plane. If (b) or (c) shows a fault of the walk, it also changes what a miss reads, and it becomes a bug ticket of the walk.

**2026-11-23T19:03:59Z**

2026-11-23, line orchestrator: the four tables are in. The depth box is the cause in the bay, the step cap is a second and smaller cause, and the walk has no fault. The log lacked three fields, so the lead added them to a take that it ran anyway (the bay's diffuse-mode still at a cap of 64, 2/3, -4.089 stops, 400 counted draws) and ran one nose take as the control. The arriving shares match the earlier log exactly (1.39, 14.8, 11.6 and 32.4 %). Tables and tools: cyhy-bay.json, cyhy-nose.json, slant-bay.json, slant-nose.json, cyhy.py, slant.py in t3-2b720d96-b40221aeff8f27f7-evidence/guide/.

(a) The share of guided draws that arrive, by the target point's depth share u (how far into the box the point lies, 0 at the pixel's depth and 1 at 5 % of the depth behind it), in the bins 0 to 0.25, to 0.5, to 0.75, to 1: sunlit patch 5.5 %, 0.08 %, 0, 0; back wall 56.9 %, 2.4 %, 0, 0; floor 44.2 %, 2.2 %, 0, 0; right wall 98.0 %, 31.3 %, 0.006 %, 0; the nose's box 92.4 %, 88.0 %, 87.2 %, 83.5 %. The hypothesis holds. The bay's guided draws aim at a surface that the eye sees nearly edge-on (|n.v| of 0.12 to 0.15), so a point up to 5 % of the depth behind it along the eye's ray lies many pixels beside it on the screen. On the nose the targets are seen at another angle and u does not matter.

(b) and (c) The walk is right about where it hits. In widths of the hit pixel's footprint along its surface (the distance of the hit's surface point from the ray's line times |n.v| at the hit), the median and the 99th percentile: guided draws that hit another pixel 0.19 / 0.45 (patch), 0.40 / 0.55 (back wall), 0.40 / 0.58 (floor), 0.46 / 0.56 (right wall), 0.46 / 0.75 (nose), none to 0.1 % above 1.5; guided draws that arrive 0.22 / 0.46, 0.44 / 0.57, 0.42 / 0.60, 0.47 / 0.57, 0.23 / 0.39; cosine draws that hit 0.38 / 0.63, 0.57 / 0.80, 0.48 / 0.98, 0.48 / 0.63, 0.72 / 0.92, with 0.1 to 0.9 % above 1.5. My first measure, in widths across the eye's ray, counted the surface's slant and not the walk's error; the lead corrected it. So the cosine draws do not lose or misplace hits, and what a miss reads does not change by this finding. Open unit: the dump's neighbor spacing is 0.67 of frame.params.z in the bay's regions; the lead checks the ratio at the image's center, where it must be about 1.

(d) How the draws end, as shares of all draws of the kind (hit / direction below the geometric plane / out of the 62 steps / the ray's end with nothing passed behind / the ray's end after passing behind a surface). Guided: patch 17.0 / 78.5 / 4.5 / 0.003 / 0.000 %; back wall 77.6 / 0.4 / 22.0 / 0.001 / 0; floor 71.1 / 1.2 / 27.7 / 0.01 / 0.001; right wall 99.1 / 0.8 / 0.1 / 0.005 / 0.001; nose 98.1 / 0.9 / 0.6 / 0.4 / 0.001. Cosine: patch 45.8 / 0 / 1.9 / 52.2 / 0.03 %; back wall 66.5 / 0 / 6.8 / 26.7 / 0.006; floor 47.6 / 0 / 3.4 / 48.7 / 0.30; right wall 34.3 / 0 / 1.7 / 63.9 / 0.02; nose 85.3 / 0 / 1.1 / 13.5 / 0.10. So three causes lose guided draws: the depth box (most of the bay's losses); the step cap, which ends 22 and 28 % of the guided draws of the back wall and the floor, which aim across the room at the far edge-on patch; and targets on the receiver's own plane, 78.5 % of the sunlit patch's guided draws, which trace nothing. The walk rejects under 0.3 % of either kind behind a surface. Most cosine draws that hit nothing leave the screen.

My settlement for the pictures, for the orchestrator to reverse: no new model in a probe. One scratch constant, TargetDepthShare at 0.0125 in place of 0.05, gives the arriving shares of table (a)'s first column (98, 57 and 44 % on the right wall, the back wall and the floor) and stays unbiased, because the draw and the density read the same constant. The lead first checks the arriving share with it. It then goes into ter-fe63's bay pictures as a third column beside main and the mixture with today's box, and into one diffuse take here: the bay's diffuse signal alone at a share of 0.25 with the thin box, with the grain / blob table. That take says whether this ticket earns an engine build.

The design question for that build, not started: a thinner box is not the model. A box along the eye's ray has to be deep enough to hold a slanted surface inside its own pixel (at a slant of 0.13 the depth changes by about 0.8 % across one pixel), and every bit of depth sends rays from the side beside the target. The model that has no such conflict: a target is a patch of its surface, the target point is a uniform point of the pixel's footprint on the surface's plane, and the guided density is a surface patch's (the patch's light over its area, times the squared distance over the cosine at the patch). It needs the sender's normal at the 3 x 3 reads. Design it twice when the ticket is taken. The step cap of guided draws and the targets on the receiver's own plane are recorded here and are not part of the first build.
