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

Timed runs of the shot scripts (241 frames, medians of the GPU frame span, the same runs as the shots): cabin 12.11 (bulbs), 15.99 (before), 13.00 (after); bay 12.53, 15.50, 12.98; rect-lights 9.86 (before), 8.93 (after). The timed runs read about 2 ms more than the traces of their last 121 frames in every version; the differences between versions agree. Before the review's fixes, both views stayed within 0.9 ms of the bulbs; the final numbers are below.

Accuracy of the fit:
- theta / sin theta: within 1.5e-6 relative at every angle up to theta = pi - 1e-9 (TheEdgeSumsFitOfThetaOverSineStaysWithinTwoMillionthsOfItAtEveryAngle, bound 2e-6). Past pi / 2 the fit takes sin theta from the length of the cross product (the review's F2, below), so no floor takes over near pi.
- The cut edge sum against the exact (arctangent) sum, 200,000 random quads above and across the horizon, in doubles: at most 9.2e-7 sr apart, and within 0.047 % wherever the integral exceeds 1e-3 sr (scratch script fiterr.py).
- RectLightTests pass unchanged: diffuse within 0.1 % of the numerical integral, the LTC shine within 3 % + 0.006 and 15 % + 0.02, the mirror and the rough floor energy as before.

Images (240 still draws then the capture, before = main at c69914f66, after = this branch; /private/tmp/tertius-rect-cost-20261108/shots): cabin 6 of 921,600 pixels differ by more than 1 %, the largest a single floor speck of the screen tracer (212 before, 157 after; the specks are on main too); bay 26 pixels, at most 8.6 %; rect-lights none beyond 1 %, at most 2 %. RMSE 0.17 %, 0.26 % and 0.06 %.

New GPU tests, each green on main's code first (they pin the paths that this ticket changed): ALitPuffScattersARectangleLightsFaceRadianceTimesTheSolidAngleOfItsFace, ALitPuffBehindARectangleLightTakesNoneOfItsLight, ASquareWhoseHorizonHidesARectangleLightTakesNoneOfItsLight, AHairPixelTakesARectangleLightAsHairFiberTakesALightAlongTheFacesVectorIrradiance (the fiber branch had no test before).

Left as is: the lit puffs' loop keeps every shape in one loop (a vertex shader, one loop per puff, not per pixel).

Review (one reviewer subagent, 2026-11-09). Findings and what was done:
- F1, tools/gpu-trace.py: a run restarted only on overflow, so the second command buffer of a frame whose first the trace lost joined the next frame's first (3 + 25 stamps), and every later pass took a wrong name. Fixed test first: the frame's shape is the most common run of stamp counts (25, 3), a frame of the encoder list must follow it, and the frames of the trace come from the table of GPU intervals, each run of as many command buffers with stamps as the shape has, in the order of their first GPU start. So a frame whose rows the trace lost keeps its index again (the reviewer's Q1). New tests: test_the_second_command_buffer_of_a_frame_whose_first_the_trace_lost_does_not_join_the_next_frame, test_the_passes_of_the_second_command_buffer_of_a_frame_count_in_that_frame, test_a_frame_whose_encoder_rows_the_trace_lost_keeps_its_place_among_frames_of_two_command_buffers. A report of the recorded cabin trace gives the same table as before the change.
- F2, the fit near theta = pi: sin theta came from 1 - cos^2 theta with a floor of 1e-7, so an edge that passes close to the shaded point lost its term. Fixed test first: APointBesideTheLineOfAnEdgeTakesTheLightOfTheArctangentEdgeSum (a floor point 0.05 mm in front of a wall panel's lower edge took 0.99 of the exact 1.57 sr, red; now within 1e-4). Past pi / 2 the fit takes sin theta from the length of the cross product, in both copies, and the fit test now covers every angle up to pi - 1e-9.
- F3, normalize of a zero vector: a corner or a crossing that rounds to the shaded point gave NaN, where the old edge sum gave 0. Fixed: normalizedOr(v, vec3(0.0)) for the corners, the crossings and the fiber's corners, and NormalizedOr in C#; edgeVector of a zero vector is 0. No test: no placement makes a corner round to the point on purpose (judgment call).
- F4, the cap: IrradianceLimit now caps a rectangle's face radiance, which can pass the float range before min. Not a NaN: min(+inf, cap) is the cap. The docs said otherwise; fixed in lighting.glsl, ShaderInput.IrradianceLimit and ARCHITECTURE.md's Portable Vulkan table.
- Q2, names: the fit's value is now thetaOverSineOverTwoPi in both copies.
- Q3, stale texts: lighting.glsl's header and ARCHITECTURE.md's "Shading" now say that a rectangle's E is its face radiance, the floor sentence is gone, and the normalizations are up to twelve.
- Q4: the 189-column comment line is rewrapped.
- Q5, the coupled rect argument: kept, as the reviewer accepted: a compile-time constant is the one way to keep the other shapes' code out of a loop, as fiber and tinted do.
- The reviewer could not run anything; the timings, images and tests above are this agent's runs.

Final measurements, after the review's fixes (the F2 fix adds one length per edge past pi / 2):
- Traces (last 121 frames, medians, ms): cabin direct light 1.09, frame span 10.82 (bulbs 0.79 and 10.31); bay direct light 1.67, frame span 11.41 (bulbs 1.25 and 10.71); lamp-hall-64 direct light 3.46, frame span 11.21 (bulbs 3.31 and 10.56).
- Timed cabin runs (241 frames, median GPU frame span, bulbs and this branch alternated): bulbs 12.86 and 12.63, this branch 13.82 and 13.39. A single run of this branch read 13.77 at a low load. Timed bay: 12.96 (bulbs 12.53). Timed rect-lights: 8.93 (before 9.86).
- So the cabin stays within 0.8 to 1.0 ms of the bulbs and the bay within 0.4 to 0.7 ms. In the cabin trace, the direct pass gives 0.30 ms of the 0.51 ms, and the transparent pass and the screen tracer's ray hits (the same lightIrradiance for every panel) give the rest.

Mutation testing (tools/mutate.py; no Core, Simulation or Physics line changed, so no Stryker run):

| id | place | change | result | answer |
|---|---|---|---|---|
| S01 | lighting.glsl edgeVector | no fit past pi / 2 | killed | AMirrorReflectsARectangleLightAtItsSize |
| S02 | lighting.glsl edgeVector | edge vector halved | killed | ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight |
| S03 | lighting.glsl cutCosine | corners not normalized | killed | ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight |
| S04 | lighting.glsl cutCosine | crossing not normalized | killed | ARectangleLightLightsAMatteSquareAsTheCSharpRectangleDoes |
| S05 | lighting.glsl lightIrradiance | a point in the face's plane takes light (> for >=) | survived | Equivalent: rectAbove rejects a surface point in the face's plane, and rectSolidAngle gives 0 to a puff there. |
| S06 | lighting.glsl lightIrradiance | no test behind the face | survived, then killed | AHairPixelBehindARectangleLightTakesNoneOfItsLight (new, red on the mutant) |
| S07 | lighting.glsl lightIrradiance | face area halved | killed | ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight |
| S08 | lighting.glsl scatteredLight | puff without the solid angle | killed | ALitPuffScattersARectangleLightsFaceRadianceTimesTheSolidAngleOfItsFace |
| S09 | lighting.glsl rectResponse | fiber share 1 | killed | AHairPixelTakesARectangleLightAsHairFiberTakesALightAlongTheFacesVectorIrradiance |
| S10 | lighting.glsl rectResponse | fiber corners not normalized | killed | AHairPixelTakesARectangleLightAsHairFiberTakesALightAlongTheFacesVectorIrradiance |
| S11 | lighting.glsl shapeLight | rectangle loop never runs | killed | ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight |
| S12 | lighting.glsl shapeLight | both loops take every light | killed | ARoughSquareSeenAtASlantShowsTheCSharpLtcShineOfARectangleLight |
| S13 | lighting.glsl edgeVector | sine from 1 - cos^2 (the code before F2) | survived | Judgment call: only a pixel within about 0.05 mm of an edge's line tells it apart, which no GPU test can place; the C# copy (C07b) carries that test. |
| C01 | RectView.CutCosine | corners not normalized | killed | ARectangleLightsAPointAsTheIntegralOverItsFaceDoes |
| C02 | RectView.CutCosine | crossing not normalized | killed | ARectangleLightsAPointAsTheIntegralOverItsFaceDoes |
| C03 | RectView.ThetaOverSine | no fit past pi / 2 | killed | the fit test |
| C04 | RectView.ThetaOverSine | floor of sin^2 1e-6 (before F2) | killed | the fit test |
| C05 | RectView.ThetaOverSine | theta over sine halved | killed | the fit test |
| C06 | SceneBatcher.RectCount | count the other lights | killed (the first form, W: 0, did not build: the unused method is an error; rewritten) | TheFrameCountsTheRectangleLightsOfItsListForTheShadersLoopOverThem |
| C07b | RectView.ThetaOverSine | sine from 1 - cos^2 | killed (C07 did not build: the unused sine parameter is an error; rewritten with 0 * sine) | APointBesideTheLineOfAnEdgeTakesTheLightOfTheArctangentEdgeSum |

20 valid mutants: 18 killed, 1 equivalent (S05), 1 judgment call (S13).

Coverage on the changed lines (tools/coverage, Debug and Release, after the merge of main with the diffuse filter): 23 of 23 changed C# lines and 6 of 6 branches covered (SceneBatcher.RectCount, RectView). The GLSL lines have no coverage tool; the mutants above cover each changed path.
