---
id: ter-s97m
status: closed
deps: []
links: []
created: 2026-11-12T13:01:35Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [quality, research]
---
# Spatiotemporal blue-noise draws for the bounce and reflection rays: fewer daubs and less grain at the same ray count?

The user asked on 2026-11-12 about blue noise, and then asked to test it on the reflection rays too. Today every random number of the tracer is white noise: pcg3d of the packed pixel, the draw index and a constant per kind (drawOf in screen_trace.glsl). A blur of white noise keeps its low-frequency part, which looks like soft blotches. The diffuse filter and the history's spatial pass are such blurs, so white-noise samples are one plausible source of the daubs. Spatiotemporal blue noise (STBN; NVIDIA, 2022, docs/research/temporal-renderer/signals.md reference 13) gives two things at once: each frame's mask is blue over space, so a blur of its error is nearly flat, and each pixel's values over the frames are spread evenly, so a temporal average converges faster. It was made for this situation: at most one sample per pixel, real-time GPUs, a spatial filter and temporal accumulation. Tertius traces one ray of each kind per 2x2 block in most pixels.

What was tried before, and why this differs: the reflection study tiunr-m0uo (2026-11-07) used a digitally shifted Sobol sequence per pixel over the frames, with a random shift per pixel. That spreads each pixel's samples evenly over time, but across space it is still white noise, so a blur keeps blotches. Its own record says so: 'Its digitally shifted Sobol sequence is not spatiotemporal blue noise; the interaction with rotating 2x2 ray reuse limits per-pixel coverage claims' (docs/plans/reflection-reconstruction-campaign.md, line 57). Its result: noise fell 11.6 % on smooth and 2.8 % on rough reflections, and the error of the average on rough reflections rose 11 %.

Test, one kind at a time, same ray count:
1. Bounce light: replace only the two random numbers u of the cosine draw of a diffuse ray (traceFrom in screen_trace.glsl) with an STBN mask. Keep the guided draw (descent and target point) on pcg3d, because blue noise helps little in many dimensions.
2. Reflections: replace the two random numbers u of the GGX draw (reflectionDirection) with an STBN mask. Report smooth, glossy and rough surfaces separately; at the roughness floor the lobe is tiny and little can change.
3. The 2x2 sharing breaks a per-pixel sequence, because a pixel's sample often comes from its block's chosen pixel, which rotates (TurnDraw). Index the mask for a block's shared ray by the block (a mask over block coordinates), and for a pixel's own ray by the pixel, or give a measured reason for another choice. Say how the chosen pixel's rotation should follow the mask.
4. Measure against main, beside the true reference first by eye, then by numbers: the daub score (/private/tmp/t7-3e22/daub.py; clouds stop showing at about 0.010), the grain, and the error of the average, in the plate, the deck, the cabin, exit-cabin and the reflections scene's low view, at 60 draws and settled.

Known risks: (1) STBN moves the error to fine detail; it does not remove bias, so it cannot fix the clamp's darkening or the rare bright hits of a small light (Design A and the guided rays own those). (2) Find out whether the mask's period (for example 64 frames) interacts with a history of 32 draws (bounce light) or with the uncapped slow reflection history, and whether that explains a rise in the error of the average like tiunr-m0uo's, before trusting a gain. (3) Masks: generate our own offline (void and cluster) or check the license of NVIDIA's before adding an asset. (4) Determinism: a fixed mask keeps runs on one machine identical. (5) The cost is one texture read per ray; time it.

Coordinate with the mottle agent (t7-3e22, Design A) and the own-density builder (ter-xo83, reflection resolve): measure against main as a clean A/B, but merge only after measuring again on top of what has merged by then.

## Notes

**2026-11-12T16:30:22Z**

Research result (agent s97m, 2026-11-12). Prototype branch: ter-s97m-blue-noise-draws (not for merge). Recommendation: build variant B-shift for the reflection rays only. Keep the bounce rays on pcg3d.

The mask. tools/stbn/stbn.c (on the branch) makes our own STBN mask of 128 x 128 pixels over 64 frames, two bytes per texel: a radial number and an angle. Each pixel's 64 values are an Owen-scrambled 64-point 2D Sobol set, so every pixel covers [0, 1)^2 evenly over one period. Greedy swaps of two frames at one pixel (200 million, 3 minutes) then lower the energy of Georgiev and Fajardo (2016) with the cross-shaped kernel of the STBN paper: pairs in one frame by spatial distance (sigma 2.1 px), pairs at one pixel by time distance (sigma 1.9 frames). Spectrum check (tools/stbn/spectra.py): each frame keeps 2 to 7 % of white noise's power below 0.1 cycles per pixel. After a Gaussian blur of 2 px, it keeps 3 % (radial) and 7 % (a direction component) of white noise's variance. A pixel's mean over 8 frames keeps 20 to 32 % of white noise's variance. No NVIDIA file was used. The mask is 2 MB, an R8G8_UINT image in the frame set. A byte gives the cell of 1/256, and pcg3d places the number inside it.

Variants, at the same ray count. A: the two numbers of the cosine draw of a diffuse ray come from the mask. B: the same for the GGX draw of a reflection ray. C: both. The guided draw and its choice stay on pcg3d. A block's ray reads the mask at the block, and a pixel's own ray at the pixel. The reflection rays read the mask half a tile and half a period away from the diffuse rays. "-shift": each period of 64 draws reads the mask at its own toroidal shift, from pcg3d of the period number.

How it was measured. One job per scene on the GPU workbench: the survey's probe, 1920 x 1080 under FSR 3.1, the history kept through captures 33 draws apart. The truth is main's mean over draws 1023 to 1980. A run's error in one signal is put into the whole image's truth, which then goes through the ACES display curve, with the truth's log-average at 0.18. Daub = daub.py of t7-3e22 (clouds stop showing at about 0.010). Grain = the 90th percentile of the relative error below 3 px. Error of the average = the 90th percentile of the error blurred by 3 px, of the mean of draws 363 to 990. For main, that window and the truth are disjoint, so main's value is the floor of the measure. "66 draws" stands for 60 draws: the captures fall 33 draws apart. Every job checks that the branch with the mask off gives main's bits. All tables below pass that check.

Reflections, B-shift against main, at 66 draws (daub, grain) and the error of the average:

| Scene and surface | Daub | Grain | Error of the average |
|---|---|---|---|
| mirror-plate, smooth | 0.0009 to 0.0005 (-40 %) | 0.0036 to 0.0030 (-17 %) | +1 % |
| exit-deck, rough | 0.0042 to 0.0029 (-31 %) | 0.0091 to 0.0056 (-38 %) | -15 % |
| cabin-light-cabin, rough | 0.0150 to 0.0136 (-10 %) | 0.0370 to 0.0306 (-17 %) | -5 % |
| exit-cabin, rough | 0.0121 to 0.0119 (-2 %) | 0.0342 to 0.0297 (-13 %) | +2 % |
| reflections-low, smooth | 0.0037 to 0.0028 (-22 %) | 0.1176 to 0.1072 (-9 %) | -1 % |
| reflections-low, glossy | 0.0132 to 0.0123 (-7 %) | 0.0316 to 0.0239 (-24 %) | +15 % |
| reflections-low, rough | 0.0035 to 0.0037 (+4 %) | 0.0513 to 0.0471 (-8 %) | -1 % |

The settled draw (330) gives the same picture: grain -4 to -37 %. No scene has a glossy surface outside reflections-low (roughness 0.15 to 0.35), and only reflections-low and mirror-plate have smooth ones.

Bounce light, A against main: worse in every scene with bounce light. Daub at 66 draws: exit-cabin 0.0190 to 0.0329 (+73 %), exit-deck +31 %, cabin-light-cabin +3 %. Grain +3 to +11 %. Error of the average +51 to +107 % (A-shift: +13 to +100 %). The picture shows new clouds on exit-cabin's back wall. A control with white bytes in the same structure (W1: same cells, same 64-draw period) keeps main's daub (0.0202 against 0.0190), so the blue spectrum itself causes the clouds, not the indexing. Why is not known. A guess to test: the diffuse filter and the history's clamp rely on the spread of neighboring samples, and STBN makes neighbors anti-correlated. C carries A's clouds into the whole image (exit-cabin daub +56 %), so C loses too.

The mask's period. Plain B raises the error of the average by 22 to 68 % on reflections (the uncapped slow history), and plain A by 51 to 107 % on the bounce light (the 32-draw history). This is the effect that tiunr-m0uo saw (+11 % on rough reflections). The controls find the cause: white bytes that repeat every 64 draws (W1) raise it as much or more (exit-cabin rough reflections 0.0101 to 0.0225), and white bytes that never repeat (W2) give main's value. So the repetition of each pixel's 64 values causes the rise, not the blue spectrum. The shift per period removes it for the reflections (-15 to +15 %) and keeps the gains at 66 draws.

The block's turn (TurnDraw). The turn rotates the chosen corner with period 4, and 4 divides the mask's period of 64. So each corner of a block receives the same 16 of the block's 64 values in every period. A reflection pixel lives mostly on rays from its own corner, so without the shift its long mean holds a fixed 16-sample error. The shift per period gives each corner new values in each period. Recommendation: keep the turn as it is, index a block's ray by the block, and shift the mask per period. Indexing by the chosen pixel with the turn count as time loses the spatial spectrum: the chosen corners are a random subsample of a pixel mask.

Cost. TERTIUS_STBN=both against main, 5 runs each on the workbench: frame span +0.000 ms (8.908 against 8.908 ms, within the noise). The diffuse rays pass +0.002 ms, the reflection rays pass less than 0.001 ms.

The Mac. Plain stills (33 draws) of reflections.json and cabin-light.json under the shared GPU lock show the same direction: a finer grain in the low view's glossy reflection with B, and no visible change in the cabin.

Pitfalls of the study. Twice, main moved between the jobs (ter-xo83 and ter-w1o3). A job with --tree main=main then compared the branch with a newer main, and its A/A check failed. Pin main to the branch's merge base in such a study. Six renderers at once ran out of the card's 16 GB, and three fit. Total spending: $2.03.

What a builder needs. B-shift only: the mask, its binding and the reflection kind's two numbers. It is about 40 lines and a 2 MB asset. Measure again on top of Design A (t7-3e22) before the merge, because Design A changes the bounce history's clamp. Design A does not touch the reflections, so B-shift's result is expected to hold. The mask's spectrum check, the A/A identity with the mask off, and a determinism test belong in its tests.

**2026-11-12T19:09:31Z**

Build result (agent s97m, 2026-11-12). Branch ter-s97m-reflection-blue-noise: B-shift only. A reflection ray's two GGX numbers come from the STBN mask (a block's ray at its block, a pixel's own ray at its pixel, each period of 64 draws at its own toroidal shift from pcg3d); 16 bits of pcg3d place each number inside its byte's step. The bounce rays, the guided draw and TurnDraw are unchanged. Design A (t7-3e22) had not merged, so the A/B ran on main at the merge base 65cec7161.

Why a Nix derivation for the mask. Decision 21 keeps the 2 MiB mask out of git. native/stbn-mask.nix compiles native/stbn.c and runs it once (about 44 s); the dev shell names the file in TERTIUS_STBN_MASK, and Tertius.Rendering embeds it (a build without it fails with a message). An MSBuild step with a cache would run the generator again in every fresh worktree and in both trees of every workbench job, about 45 s each. The Nix store keeps one copy per machine and per workbench image, keyed by the source's hash. The generator uses integer arithmetic over rounded tables and a fixed seed, so every machine writes the same bytes. The workbench image takes only flake.nix, flake.lock and native/, so the generator lives in native/, not tools/ (the first image build failed on that).

A/B against main on the GPU workbench (same probe and measures as the study; main in the new dev shell, commit 745ac30c4/e10bd9137, not for merge). Change of the branch against main, at 66 draws (daub, grain) and the error of the long mean (draws 363 to 990):

| Scene and surface | Daub | Grain | Error of the long mean |
|---|---|---|---|
| mirror-plate, smooth | -44 % | -19 % | -14 % |
| exit-deck, rough | -42 % | -40 % | -15 % |
| cabin-light-cabin, rough | -17 % | -20 % | -9 % |
| reflections-low, smooth | -30 % | -9 % | -1 % |
| reflections-low, glossy | -22 % | -28 % | +16 % |
| reflections-low, rough | -4 % | -10 % | -2 % |
| whole image: reflections-low / exit-deck / mirror-plate / cabin | -21 / -20 / -44 / -4 % | -25 / -29 / -3 / -13 % | +18 / -3 / -1 / -1 % |

The glossy floor's long mean, checked for the orchestrator: daub 0.0044 (main) and 0.0037 (branch), both below the about 0.010 at which clouds show. The crops of the long mean (enlarged 2x) show a faint mottle under the box's reflection in main and a smooth floor in the branch, close to the truth. A second job on the same commits gave the same scores to the last digit.

Cost: frame span 8.681 against 8.698 ms on the workbench (-0.017 ms, 5 runs each, within the noise), 20.141 against 20.139 ms on the Mac under the exclusive GPU lock (+0.003 ms, within the noise). Cloud spending of the build: $0.31 (6 jobs).

Tests. CPU: BlueNoiseMaskTests (strata per cell, 5x5 blurred variance per frame, 8-frame mean). GPU probes: ReflectionBlueNoiseTests (a block's numbers fill the 64 strata in periods 0 and 1, the next period reads other bytes, one draw is blue over the blocks for the radial number and the angle on the circle, a reflection ray takes the mask's direction and not pcg3d's, a block's ray reads the mask at the block and not at its chosen pixel). ScreenTraceRenderTests: two renderers give the same bits after 6 and after 70 frames. DescriptorLayoutsTests: the screen tracer's compute stage sees 17 sampled images, and the shared pool grows per frame in flight by what a frame set and a deform set take.

The pool. That last test found that the frame set takes 7 sampled images and the pool held 6 per frame (on main: 6 against 5). The constant 20 of the G-buffer, post and atlas sets hid it. Fixed to 7; the rest is ter-byim (note added there).

Coverage of the changed C# lines (Rendering, Debug): 56/56 lines, 7/8 branches. The partial branch is BlueNoiseMask.Load's throw for an assembly without the mask, which the build's RequireBlueNoiseMask target already prevents.

Mutants (tools/mutate.py; no Stryker module changed):

| id | place | change | result |
|---|---|---|---|
| M01 | screen_trace.glsl:279 | maskFrame -> 0 | killed |
| M02 | screen_trace.glsl:280 | shift -> 0 | killed |
| M03 | screen_trace.glsl:281 | cell dropped from the index | killed |
| M04 | screen_trace.glsl:282 | tile column % -> / | killed |
| M05 | screen_trace.glsl:292 | bytes.yx -> bytes.xy | killed |
| M06 | screen_trace.glsl:292 | draw >> 16 -> >> 8 | killed |
| M07 | screen_trace.glsl:293 | 1/2^24 -> 1/(2^24-1) | survived: judgment call. Each number moves up by under 2^-24 of itself and reaches 1 at one value in 2^24, which no test can draw. |
| M08 | screen_trace.glsl:704 | reflection kind takes pcg3d | killed (wiring test) |
| M09 | screen_trace_block_rays.comp:22 | cell = chosen pixel | survived, then killed by ABlocksReflectionRayReadsTheMaskAtTheBlockAndNotAtItsChosenPixel |
| M10 | DescriptorLayouts.cs | mask binding also in the fragment stage | killed |
| M11 | DescriptorLayouts.cs | pool sampled images per frame -1 | survived on MoltenVK, then killed by EachFrameInFlightGrowsTheSharedPoolByWhatItsFrameSetAndDeformSetTake |
| M12 | BrdfTableImages.cs:53 | R8G8Uint -> R8G8Unorm | killed |
| M13 | BrdfTableImages.cs:95 | mask upload removed | killed |
| M14 | FrameResources.cs:346 | mask descriptor write removed | killed |
| M15 | BlueNoiseMask.cs:39 | read of the resource removed | killed |

native/stbn.c has no mutants: its output is the mask, which BlueNoiseMaskTests check, and a mutant of it would need a new Nix build per mutant.
