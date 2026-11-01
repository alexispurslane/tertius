---
id: ter-r5hb
status: in_progress
deps: []
links: []
created: 2026-10-31T23:33:23Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, reflections, hair]
---
# Hair reflections: measure MaxSteps 256 on the review scenes, then remove the hair cut of decision 35 if the cost is small

ter-a9hh (2026-10-31) found that grazing hair rays inside the surface fade's range do not hit their own card: they run out of MaxSteps (128). Hair rays there need 65 to 256 steps, because their images are 200 to 400 px long. With the cut of decision 35 removed and MaxSteps 256, hair-rugs' tilted rug fades smoothly to a cosine of 0 (band.py: rows 413 to 419, 0 steps, 0 isolated pixels). Probes, diffs and captures: ter-a9hh's notes and /tmp/ter-a9hh. The user left the choice to the orchestrator, who chose to measure first and then remove the cut (2026-10-31).

## Design

1. Measure the GPU cost of MaxSteps 256 against 128 on the review scenes of the reflections (docs/plans/screen-space-reflections.md, Part 6) and on demo-exit's deck window, with a drift-cancelling method: paired traces or interleaved builds with a control, under the GPU lock. Report the reflection pass and the frame. 2. Recheck decision 31 (128 as the fewest steps that look clean) with the captures of those scenes at 256. 3. If the frame cost stays within the measurement's noise, or under a share that the ticket note justifies against ter-pq5y's 12 ms goal, remove the hair cut from reflectingPixel, let hair keep the surface fade like every other model, and raise MaxSteps to 256. Update decision 35 and decisions 31, the reflections plan and ARCHITECTURE.md. 4. If the cost is not small, keep the cut, record the numbers, and stop: that is a fork for the user.

## Acceptance Criteria

A ticket note gives the paired cost numbers and the method. Either the cut is gone with MaxSteps 256, tests updated red first (the hair cut's tests now pin the fade), snapshots reviewed with the reference, actual and FLIP images, and screenshots of hair-rugs; or the numbers show why the cut stays.


## Notes

**2026-11-01T00:53:55Z**

Measurement (builder, 2026-10-31). Result: on demo-exit's deck window, 256 steps without the hair cut cost the reflections 0.14 ms per frame against 128 steps with the cut, and the frame span cannot resolve that cost. On hair-rugs the cost is 0.30 ms, and on the three views of the reflections scene it is 0.

Method. One Release build with a temporary probe (kept as /tmp/ter-r5hb/probe-alternate.diff, not committed). ReflectionPass.Record pushed one of two configurations per frame, chosen by a probe file: A, 128 steps with the hair cut (the code at c25d6f21), or B, 256 steps without the cut (the candidate). The shaders read the cut from the top bit of the maxSteps push constant. An alternating trace ran A on the even frames and B on the odd frames. Each B frame was compared with the mean of its two A neighbors. The control trace ran A in every frame and paired the same frame slots. Each trace: tools/gpu-trace.sh --no-counters --keep-exports, 120 frames, 1920 x 1080, Release, M1 Max, under the GPU lock. A temporary script read the kept exports with the functions of tools/gpu-trace.py. The measured window is the wall-clock GPU time from the end of the radiance pyramid to the end of the reflections pass. It holds the half-resolution trace, the upsample and the idle time between them. The figures are paired medians in ms with bootstrap standard errors of the median. Every alternating trace that is counted holds all 121 frames that the probe recorded, so its frame parity is exact. Two control traces lost frames (the deck 5 of 121, the front view 1 of 121). That does not matter for a control, whose frames all run A.

Paired B minus A, the window / the frame span, with the 1-minute load at the start and at the end of each trace:
- demo-exit deck (frame 399), alternating, trace 1 (load 11.5 to 11.4): +0.139 (0.004) / +0.032 (0.056).
- demo-exit deck, alternating, trace 2 (load 4.4 to 3.7): +0.142 (0.004) / -0.023 (0.047).
- demo-exit deck, control (load 19.7 to 15.2): -0.002 (0.006) / -0.103 (0.035).
- hair-rugs, rug-camera, alternating (load 30.9 to 21.4): +0.302 (0.003) / +0.286 (0.017).
- hair-rugs, control (load 13.9 to 10.8): -0.005 (0.002) / -0.001 (0.011).
- reflections, front-camera, alternating (load 11.1 to 9.2): -0.010 (0.004) / -0.003 (0.036). Control (load 12.3 to 12.8): -0.002 (0.002) / -0.028 (0.012).
- reflections, low-camera, alternating (load 8.8 to 6.8): -0.001 (0.008) / +0.029 (0.045). Control (load 12.1 to 22.2): +0.002 (0.003) / -0.022 (0.019).
- reflections, edge-camera, alternating (load 4.6 to 33.1): +0.000 (0.002) / -0.010 (0.016). Control (load 9.3 to 17.0): +0.000 (0.005) / -0.018 (0.068).
The deck's frame span in these traces is about 17.3 ms (median), and its reflection trace and upsample take about 3.3 ms together.

Each part alone, against A, the window: the deck, 256 steps with the cut +0.210 (0.002) (load 4.6 to 4.4), 128 steps without the cut -0.074 (0.007) (load 4.7 to 6.1). hair-rugs, 256 steps with the cut -0.027 (0.002) (load 10.3 to 7.3), 128 steps without the cut +0.255 (0.013) (load 6.8 to 4.6). The two parts do not add up to the whole. Why is not studied: the shipped change is the whole, and the whole is what the traces above measure.

**2026-11-01T00:53:55Z**

Decision 31 rechecked (builder, 2026-10-31). Captures of the same probe build at 128 steps with the cut (A), 256 steps without it (B) and 1024 steps without it (the reference), as the largest difference of any pixel in levels of 255, and the pixels that differ by more than 10 levels. Repeated captures of one configuration are identical, so every difference comes from the steps or the cut.
- scripts/reflections.json at 1280 x 720: at 128 steps, front 16 (3 px), low 52 (33 px, a strip at the horizon), edge 1 (0 px). At 256 steps: 0, 0 and 0. So nothing that looked clean at 128 changes at 256, and the few pixels that differed at 128 now match the reference.
- scripts/hair-rugs.json, rugs: A differs from the reference by up to 58 levels (19,933 px), the band that the cut gives to the environment map. B differs by at most 6 levels (648 px above 2 levels, none above 10). The low view is identical in all three.
- demo-exit's deck at tick 459 (399 + 60), 1920 x 1080: A differs from the reference on 1,814 px by more than 10 levels (largest 48), B on 356 px (largest 29). The differences lie on the cabin's glass beside the door and on the floor's reflections of the cabin's posts. At 256 steps both look closer to the reference. So the deck's look changes a little, toward the reference.
- The shipped build (cut removed, default 256) gives the same pixels as B in rugs, low and the deck. band.py of ter-a9hh (columns 690 to 949, rows 380 to 511, --environment-map off, against --reflections off): rows 413 to 419, 0 downward steps, 0 isolated bright pixels, as ter-a9hh's probe at 256 steps.

Decision by the ticket's rule (builder, 2026-10-31): build the change. The frame span cannot resolve the deck's cost. Its paired differences, +0.032 (0.056) and -0.023 (0.047) ms, lie within the noise that the control shows, -0.103 (0.035) ms. The reflections' window resolves it: 0.14 ms, 1.2 % of the 12 ms goal of ter-pq5y. The share that this note sets: at most 0.2 ms on demo-exit's deck, a sixtieth of the goal. Reasons: (1) the goal is measured on demo-exit (docs/plans/rendering-performance.md, decision 1), and hair-rugs is a close-up review scene of hair, where the cost is 0.30 ms; (2) the user accepted 0.27 ms above the reflections' expected cost with no more work (2026-10-26); (3) the change replaces a cut that hides the mechanism with the real mechanism, enough steps, and the 256 steps also bring the deck's other reflections nearer the reference. The deck's frame is already above 12 ms in these traces (about 17.3 ms), so ter-pq5y's Part 3, which shares out the budget, must count these 0.14 ms.

**2026-11-01T01:01:23Z**

Implementation (builder, 2026-10-31). reflectingPixel in reflection_pixel.glsl drops the hair cut: a pixel of every model starts no ray at a cosine of 0 or less with its geometric normal and takes the surface fade above it. ConeTracedReflections.Default has MaxSteps 256; the validation range stays 1 to 1,024 (MaxStepsLimit, now 4 times the default). Tests red first: the hair case of the grazing test (now APixelWhoseRayGrazesItsSurfaceTakesTheScreensReflectionInTheShareOfTheSurfaceFade over both models and both traces) found 0 reflecting pixels of 5,978 needed with the cut, and TheDefaultTracesUpToARoughnessOf0Point6In256Steps failed at 128. AHairPixelWhoseRayPointsBelowItsCardKeepsTheEnvironmentMapsReflection now selects a cosine below 0. No snapshot test failed and no reference image changed. Full Checked and Release runs passed (9,715 and 9,702 passed, 14 skipped each). Screenshots: /tmp/ter-r5hb/shots (hair-rugs/rugs.png, hair-rugs/low.png, rugs-band-128cut-above-256nocut-4x.png, deck/deck.png); the zooms of the deck at 128, 256 and 1024 steps are /tmp/ter-r5hb/cap/deck-cabin-abc-3x.png and deck-floor-abc-2x.png.
