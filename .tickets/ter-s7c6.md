---
id: ter-s7c6
status: open
deps: [ter-k63u, ter-gayt, ter-7m38]
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, fsr]
---
# FSR 3.1 in the frame, with dynamic resolution

Build ticket 5 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, sections 4, 5 and 9, and decisions 2, 13, 15, 30, 31, 33, 34 and 37. Acceptance: section 14, item 5. It starts from branch experiment-fsr1 at 3be9aef99 (the closed ter-htg7), and the glow's width follows the output size (the closed ter-ch7z). SMAA goes.


## Notes

**2026-11-03T17:00:00Z**

From the review of ter-k63u:

- The exposure buffer gains previousStops here. Write it on the first frame too, as this frame's stops. AMD forces deltaPreExposure to 1 on its first execution, and the GPU test gets 1 only because it passes the same stops twice. FSR reads no history on frame 0, so the value has no effect there, but an uninitialized float can be NaN.
- The layout of the exposure buffer is declared in three shaders: frame.glsl, exposure_adapt.comp and fsr_exposure.comp. Only fsr_exposure.comp has previousStops. Change them together, or move the declaration into one shared GLSL file.
- No GPU test of ter-k63u has motion. The sign and the units of the motion vectors (MotionVectorScale = 1, in units of the render size) are first exercised here.
- FSR's GPU time of 2.79 ms was measured at a load of 5.5 with a largest render size of 1280 x 720. Measure it again on a quiet machine with the largest render size that this ticket picks.
