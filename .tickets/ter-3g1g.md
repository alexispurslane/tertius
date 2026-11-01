---
id: ter-3g1g
status: open
deps: [ter-s9cv, ter-a24f, ter-jxv9, ter-nl5u, ter-6vhj, ter-izsu, ter-ff6y, ter-of1p, ter-nydu, ter-p1u7, ter-fhlb, ter-uqmn, ter-ox1a, ter-0y0z, ter-19fm, ter-qsye, ter-4xw0, ter-mfyv, ter-rjts, ter-pjza, ter-prio, ter-ww0h, ter-b2da]
links: [ter-r5hb]
created: 2026-10-25T22:29:46Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# The whole frame in 12 ms of GPU time at 1080p

Part 3 of docs/plans/rendering-performance.md, "Order of the work". After every member of the rendering upgrade merges and the three profiles close: measure the whole frame on a quiet machine, rank the passes, optimize the ones over their share with changes that keep the image, then lower defaults where that is not enough, with the full quality kept as a setting.

## Acceptance Criteria

The GPU span of demo-exit at 1920x1080, Release, on the M1 Max, at most 12 ms in two runs on a quiet machine, and the budget table of each pass in ARCHITECTURE.md.


## Notes

**2026-10-25T23:20:19Z**

2026-10-25: the user allows every technique of docs/plans/rendering-performance.md, "The techniques", without asking, half-resolution reflections included, and forbids every temporal technique (decision 8): no accumulation over frames, no TAA, no temporal upscaling. Denoise spatially only.

**2026-10-31T21:27:38Z**

2026-10-31: the orchestrator added ter-0y0z, ter-19fm, ter-qsye and ter-4xw0 as dependencies, because this ticket's own text measures the frame after every member of the rendering upgrade merges, and these four still change the frame's cost.

**2026-11-01T01:22:22Z**

From ter-r5hb (2026-10-31): the reflections' default MaxSteps rose from 128 to 256, and the hair cut of decision 35 (docs/plans/skin-and-hair.md) is gone. On demo-exit's deck window (120 frames from 399, 1920 x 1080, Release), the reflection trace with its upsample takes about 0.14 ms more per frame, inside a 0.2 ms share that ter-r5hb's note justifies. A MaxSteps below 256 brings back a ragged, speckled band on the tilted rug of hair-rugs, because no cut hides those rays now. So a lower default needs one of two things: the hair cut restored, or that band accepted. HairReflectionRenderTests' grazing test fails at 128 steps, so the change cannot happen without notice.

**2026-11-01T02:14:30Z**

2026-10-31, steps 1 to 3 of Part 3 (orchestrator). Main 87ee9caf, Release, 1920 x 1080, Apple M1 Max. Two runs of demo-exit without a trace (--video none, 3,888 timed frames each), one after the other, load 2.3 to 3.3: GPU span mean 18.038 and 17.940 ms, median 17.934 and 17.883 ms, p95 22.022 and 22.128 ms. So the frame is 6 ms over the budget. A trace of every frame without counters (tools/gpu-trace.sh --no-counters --frames 0, 3,889 frames, 7.2 GB, load 2.3 at the start): span mean 17.555 ms. The timer's pass times on MoltenVK hold only for sums of neighbors, so the shares use the trace's own busy time of each pass per frame (its mean over the frames with work, times those frames / 3,889). Gaps between passes: the span minus the union of the passes' busy time, about 1.87 ms (the union counts the 0.69 ms that two passes share once). Each share is the own time times (12 - 1.87) / 16.033 = 0.632.

span 17.555 ms; sum of own busy times 16.033 ms (time shared by two passes counts twice); union about 15.689 ms; gaps about 1.866 ms; scale (12 - gaps) / sum = 0.6321
pass                      own ms   share   over  group
direct light               6.009   3.798  2.211  A direct light
reflections                1.920   1.214  0.707  B reflections
occlusion sectors          1.578   0.997  0.581  C screen-space light
reflection trace           1.390   0.879  0.511  B reflections
ambient light              0.647   0.409  0.238  C screen-space light
radiance pyramid           0.621   0.392  0.228  B reflections
transparent                0.601   0.380  0.221  D other
bloom                      0.411   0.260  0.151  D other
environment faces          0.407   0.257  0.150  D other
G-buffer                   0.372   0.235  0.137  D other
occlusion upsample         0.310   0.196  0.114  C screen-space light
environment prefilter      0.251   0.158  0.092  D other
local shadows              0.239   0.151  0.088  D other
shadow maps                0.189   0.120  0.070  D other
depth pyramid              0.159   0.100  0.058  B reflections
tone map                   0.150   0.095  0.055  D other
exposure                   0.145   0.092  0.053  D other
occlusion depth            0.122   0.077  0.045  C screen-space light
occlusion denoise          0.107   0.067  0.039  C screen-space light
SMAA blending              0.091   0.058  0.033  D other
SMAA weights               0.089   0.056  0.033  D other
scatter rows               0.072   0.046  0.027  D other
SMAA edges                 0.061   0.038  0.022  D other
scatter columns            0.041   0.026  0.015  D other
capture                    0.035   0.022  0.013  D other
(before the first pass)    0.010   0.006  0.004  D other
(after the last pass)      0.006   0.004  0.002  D other

A direct light         own  6.009  share  3.798  over  2.211
B reflections          own  4.090  share  2.585  over  1.505
C screen-space light   own  2.763  share  1.746  over  1.016
D other                own  3.171  share  2.004  over  1.167

Step 4 split into four tickets by group, so that the groups are cut in parallel: ter-mfyv (direct light), ter-rjts (depth pyramid and reflections), ter-pjza (occlusion passes and ambient light), ter-prio (the other passes and the gaps). This ticket keeps steps 5 and 6: measure again after they merge, lower a default only after the user sees the captures of the old and the new default, and write the budget table into ARCHITECTURE.md. The runs and the trace are in captures/gpu-3g1g on the main checkout, which git ignores.
