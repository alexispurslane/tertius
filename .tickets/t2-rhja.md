---
id: t2-rhja
status: in_progress
deps: []
links: []
created: 2026-11-09T01:15:12Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# Make the rectangle light cheaper per pixel

The rectangle light (ter-e436) adds about 3.0 to 3.6 ms to the frame span of the orbit cabin's view along its five ceiling panels on the M1 Max at 1280 x 720 (median of 241 frames, two runs each: 15.8 ms against 12.1 to 12.7 ms with the bulbs), and 3.3 ms to the demo surface's bay view. Per pixel and light it takes the solid angle (two arctangents) and two cut edge sums (up to five arctangents each) and a table sample.

Candidates, each to check against RectLightTests' tolerances and the GPU tests:
- Heitz's rational fit of theta / sin(theta) in the edge integral (the LTC code of Heitz and Hill 2017), which needs one normalization per corner and no arctangent. It is not exact; measure its error against the integral before adopting it.
- Leave the solid angle out on surfaces, where lightIrradiance multiplies by it and rectResponse divides by it. Only lit puffs and fibers need it. This changes what LightPath.irradiance means for a rectangle, so every reader of it must agree.
- Measure each with a GPU trace (tools/README.md, "GPU trace"), not the per-pass JSON, which MoltenVK smears across neighboring passes.


## Notes

**2026-11-09**

Measurement (GPU traces, tools/gpu-trace.sh --no-counters, 1280 x 720, the last 121 frames of each script, medians; logs and traces in /private/tmp/tertius-rect-cost-20261108). The trace report first failed with "No command buffer of Tertius.Demo holds 28 timestamp encoders": since the FSR 3.1 integration a frame takes two command buffers (QueueSubmission.Submit), 25 stamps and 3. Fixed test first in tools/gpu-trace.py: consecutive command buffers whose stamps add up to one more than the passes make one frame (tools/test_gpu_trace.py, four new tests).

Where the time went, before this ticket (ms, median):

| View | Pass | Bulbs | Rectangle before |
|---|---|---:|---:|
| Cabin | direct light | 0.79 | 2.53 |
| Cabin | transparent | 0.16 | 0.38 |
| Cabin | frame span | 10.31 | 12.13 |
| Bay | direct light | 1.25 | 3.42 |
| Bay | frame span | 10.71 | 12.48 |
| lamp-hall-64 (no rectangle) | direct light | 3.31 | 4.99 |
| lamp-hall-64 (no rectangle) | frame span | 10.56 | 11.93 |

- Every pixel evaluates every panel. The cabin's list holds its five ceiling panels, and each reaches at least 91 m (10 W, P / pi over the cutoff irradiance at 0 stops) along a cabin of 48 m. The bay's list holds its four wall panels of 100 W. The reach cutoff and the test behind the face already existed. In the direct pass, the transparent pass and the screen tracer's ray hits, each pixel ran up to 12 arctangents per panel: two for the solid angle, before the horizon test, and up to five per cut edge sum.
- The larger part was not the panels' math: lamp-hall-64 holds no rectangle, and its direct pass still grew by 1.7 ms. The rectangle's code in the one light loop made every light slower.

What changed:
1. The edge sum takes Hill and Heitz's rational fit of theta / sin theta on the unit corner directions, with no arctangent (edgeVector, RectView.ThetaOverSine). The crossings at the horizon are normalized too, so the cut stays on the arc.
2. A surface and a fiber take no solid angle: lightIrradiance gives a rectangle its face radiance, and rectResponse integrates it over the face, as MaterialSample.RectTerms already did. Only the lit puff takes the solid angle (scatteredLight). The test behind the face is now one dot product, before any sum.
3. modelLight runs the rectangles in a loop of their own (shapeLight, with the constant rect), after the other lights. frame.counts.w counts the rectangles, and their loop does not run without one (SceneBatcher.RectCount, red first: TheFrameCountsTheRectangleLightsOfItsListForTheShadersLoopOverThem). Lights now add in the order of the list within each loop.

Each step, traces as above (ms, median):

| Step | Cabin direct | Cabin frame | Bay direct | Bay frame | Hall direct | Hall frame |
|---|---:|---:|---:|---:|---:|---:|
| Bulbs | 0.79 | 10.31 | 1.25 | 10.71 | 3.31 | 10.56 |
| Rectangle before | 2.53 | 12.12 | 3.42 | 12.48 | 4.99 | 11.93 |
| 1 and 2 | 1.42 | 11.36 | 2.09 | 11.64 | 4.41 | 11.64 |
| and 3 without counts.w | 1.04 | 10.94 | 1.56 | 11.41 | 3.92 | 11.33 |
| and counts.w | 1.06 | 10.93 | 1.51 | 11.27 | 3.48 | 10.91 |

Timed runs of the shot scripts (241 frames, medians of the GPU frame span, the same runs as the shots): cabin 12.11 (bulbs), 15.99 (before), 13.00 (after); bay 12.53, 15.50, 12.98; rect-lights 9.86 (before), 8.93 (after). The timed runs read about 2 ms more than the traces of their last 121 frames in every version; the differences between versions agree. Target met: both views within 0.9 ms of the bulbs.

Accuracy of the fit:
- theta / sin theta: within 1.5e-6 relative up to theta = pi - 3.2e-4 (TheEdgeSumsFitOfThetaOverSineStaysWithinTwoMillionthsOfItUpToNearlyAHalfTurn, bound 2e-6). Past that the floor of sin^2 at 1e-7 takes over, where an edge passes within 3.2e-4 rad of the point.
- The cut edge sum against the exact (arctangent) sum, 200,000 random quads above and across the horizon, in doubles: at most 9.2e-7 sr apart, and within 0.047 % wherever the integral exceeds 1e-3 sr (scratch script fiterr.py).
- RectLightTests pass unchanged: diffuse within 0.1 % of the numerical integral, the LTC shine within 3 % + 0.006 and 15 % + 0.02, the mirror and the rough floor energy as before.

Images (240 still draws then the capture, before = main at c69914f66, after = this branch; /private/tmp/tertius-rect-cost-20261108/shots): cabin 6 of 921,600 pixels differ by more than 1 %, the largest a single floor speck of the screen tracer (212 before, 157 after; the specks are on main too); bay 26 pixels, at most 8.6 %; rect-lights none beyond 1 %, at most 2 %. RMSE 0.17 %, 0.26 % and 0.06 %.

New GPU tests, each green on main's code first (they pin the paths that this ticket changed): ALitPuffScattersARectangleLightsFaceRadianceTimesTheSolidAngleOfItsFace, ALitPuffBehindARectangleLightTakesNoneOfItsLight, ASquareWhoseHorizonHidesARectangleLightTakesNoneOfItsLight, AHairPixelTakesARectangleLightAsHairFiberTakesALightAlongTheFacesVectorIrradiance (the fiber branch had no test before).

Left as is: the lit puffs' loop keeps every shape in one loop (a vertex shader, one loop per puff, not per pixel).
