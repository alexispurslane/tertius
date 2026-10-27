---
id: ter-izsu
status: closed
deps: [ter-y1s7, ter-nydu]
links: []
created: 2026-10-25T18:34:36Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Profile the four occlusion passes and bring them under 1 ms at 1080p

ter-b3h5 measured the four occlusion passes at about 1.7 ms together at 1920x1080 (depth 0.13, horizons 0.70, denoise 0.16, upsample 0.78 ms), over the design's limit of 1 ms. The user chose on 2026-10-25 to merge and then profile. Profile on a quiet machine, the upsample first, because it costs the most and the depth chain (open fork 2 B of docs/plans/ambient-occlusion.md) does not touch it. Then measure the depth chain if the horizon search stays the largest pass.

## Acceptance Criteria

A profile of each pass, the changes that it justifies, and the cost measured again on a quiet machine, with the numbers in ARCHITECTURE.md.


## Notes

**2026-10-25T20:07:18Z**

2026-10-25: the user chose 'build, then cut' for the bounce light, whose passes replace and extend these four (4.0 to 8.4 ms by arithmetic at 1080p). So this ticket now profiles the screen-space light passes after the bounce light's Part 6, and tries the three levers of docs/plans/screen-space-illumination.md, 'Cost' (fewer far rays, a half-resolution copy of the direct light, and what the profile finds, the upsample first), each with captures, before Part 7 picks the defaults.

**2026-10-25T23:20:19Z**

2026-10-25: the user allows every technique of docs/plans/rendering-performance.md, "The techniques", without asking, half-resolution reflections included, and forbids every temporal technique (decision 8): no accumulation over frames, no TAA, no temporal upscaling. Denoise spatially only.

**2026-10-26T20:54:42Z**

2026-10-26: the profile (demo-exit at 1080p, Release, M1 Max, Metal System Trace) found the far rays' trace loop the largest pass, 6.2 ms and bound by arithmetic, and the five occlusion passes at 9.1 ms. The half-resolution copy of the direct light and the geometric normal keeps the image and saves 0.28 ms in the sectors pass. FarRays.OnePerTexel (fork 2's option B) is a setting with OnePerSlice as the default: the five passes take 4.9 ms with both. The upsample takes 0.32 ms and needs no change. ARCHITECTURE.md, 'Ambient occlusion', 'Cost', has the numbers.

**2026-10-27T00:09:52Z**

2026-10-26, review fixes: the measurements again, each build in its own runs (ARCHITECTURE.md, 'Ambient occlusion', 'Cost'). Before the changes the five occlusion passes took 8.9 to 9.2 ms, with the copy 8.1 to 8.5 ms, and with the copy and FarRays.OnePerTexel 4.7 to 4.8 ms, after the review made option B's pick find its direction once (about 0.2 ms less in the far rays). The split of the lighting costs about nothing against main's traced 2.96 ms. On MoltenVK the GPU timer's per-pass times move work into the pass before (ter-okrp). New tickets: ter-abun (flaky empty-pass timing test), ter-ed76 (xctrace hangs), ter-uhd8 (a headless option for option B, which Part 7 needs).
