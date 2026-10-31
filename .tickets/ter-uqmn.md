---
id: ter-uqmn
status: in_progress
deps: []
links: []
created: 2026-10-27T12:22:51Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Bring the shading models' cost in the direct and transparent passes down

ter-3dp5 measured the skin and hair epic on demo-exit's deck window (120 frames from 399, 1920x1080, Release, traces with --no-counters, 2026-10-27): with no skin and no hair on screen, the direct pass grows by 0.4 to 0.9 ms under SeparableSubsurface (base 5.37 to 5.75, branch 6.13 and 6.28 ms) and by 0.8 to 1.3 ms under NoSubsurface (6.59 and 6.69 ms, two runs that agree within 0.1 ms), and the transparent pass by 0.4 to 0.7 ms (1.62 to 1.71 against 2.10 to 2.32 ms). The design estimated 0.02 ms for the shading target's read. That the direct pass is 0.3 to 0.6 ms slower with the scatter off, where it writes one attachment fewer, is not explained; the off runs' other passes are also 0.05 to 0.2 ms slower, so part of it may be the machine's state. No run isolates the cause. Candidates: the per-light choice by model in respond and takesLight inside the light loop, and the two parts of DirectTerms kept apart per light; the shading target's fetch; the larger shader's occupancy; for the transparent pass, forwardShading and the runs by sides. First isolate the cause (for example a build whose respond is brdf alone), then: a loop specialized per model, or the standard path first with hair and skin as rare branches. Measure on demo-exit and skin-and-hair.json, and keep the pixels bit for bit. See docs/plans/skin-and-hair.md, Cost.


## Notes

**2026-10-31T16:25:28Z**

Cause isolated (2026-10-31, demo-exit deck, 120 frames from 399, 1920x1080, Release, gpu-trace --no-counters, medians of each pass's own time in ms, load 4 to 24). Direct pass: pre-merge bf812296 5.22 to 5.24, base cd8b233c 6.07 to 6.10. Diagnostic builds from the base: the hair branch out of respond and takesLight 5.57 to 5.60; also no shadow-normal flip 5.59 to 5.61; also no shading-target fetch 5.47 to 5.49; also one accumulator instead of DirectTerms' two 5.42. So the hair code inside the light loop is about 0.5 of the 0.85 ms gap, the fetch about 0.1, the two sums about 0.05, and about 0.19 remains against pre-merge with all of these gone: the diffuse image, the second attachment (ter-vtc7 and the scatter), which no shading-model change removes. Transparent pass: pre 1.56 to 1.57, base 1.93, loop per model 1.77, the stripped build 1.65 to 1.66.

Implementation choice, measured: directLight chooses the model once per pixel and runs modelLight(fiber) with a constant, so each loop copy holds one model's code; the surface loop passes its geometric normal unturned (identity: takesLight demands geometricNdotL > 0). Branch: direct 5.55 and 5.62, transparent 1.77 and 1.78. skin-and-hair, 2 runs each, base then branch, direct pass: head 1.70, 1.59 against 1.38, 1.29; fringed fan 1.41, 1.58 against 1.12, 1.23; cutout fan 1.67, 1.78 against 1.40, 1.38. No pass slower.

Fork, open: the loop per model changes pixels. Base captures are deterministic (two runs, 486 of 486 equal). Branch against base: demo-exit 209 of 3,888 frames differ, 230 pixels, each by 1 of 255 in one channel; skin-and-hair 81 of 486 frames, 102 pixels, 99 by 1 and 3 adjacent pixels by up to 7 in one glass-fan frame. Not the flip: the flip change alone keeps every byte. The Metal source from SPIRV-Cross is the same text in both loops, so Metal's fast math rounds the separate surface loop differently. A dead merge point in the surface loop did not restore the bytes. GLSL precise becomes optnone helpers in MSL, which cost a call per operation.
