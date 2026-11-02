---
id: ter-8fz2
status: open
deps: [ter-k96l]
links: []
created: 2026-11-01T18:27:21Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, environment-map]
---
# Environment map: the diffuse light D from an irradiance cube convolved from the specular map, in place of band-2 spherical harmonics

User decision, 2026-11-01, after ter-xgra. The map's diffuse light D is stored as band-2 spherical harmonics, and the specular map holds the map's light per direction. Under a small bright light the two disagree far more than the 'few percent' that Known limit 2 of docs/plans/screen-space-illumination.md states. Evidence: captures/xgra/sheets/deck-late-artifact2-candidates.png, panels off and off-bruteD. With the screen-space light off, a brute-force D (256 cosine-weighted reads of the specular map at level 0) makes the cabin's base face on deck-late clearly lighter. The error is in every pixel's ambient light, not only in creases. The fix: D comes from an irradiance cube, convolved from the same specular map that the reflections and the screen-space light read, and read at the pixel's full-resolution normal so that normal maps keep their detail. ter-xgra's probe patch p-bruteD.patch (captures/xgra/probes) is the reference. Cost: a new convolution pass for each map, estimated near the 0.28 ms of today's prefilter, not measured. Comes after ter-k96l, whose share form D(n) · S uses D. The design follows docs/plans/environment-map.md, and its decisions on the spherical harmonics change with it.

## Acceptance Criteria

D comes from the irradiance cube, and the spherical harmonics are gone or kept only where the ticket states a reason. On deck-late with the screen-space light off, D lies within a stated tolerance of the brute-force D (p-bruteD.patch) on the cabin's base face and the floor band. A render test pins that a surface under a map with one small bright light gets the cube's irradiance within that tolerance. The cost is measured with interleaved runs and a control. The convolution keeps the map's compute pipelines within 4 storage images per stage. ARCHITECTURE.md, environment-map.md and Known limit 2 of screen-space-illumination.md match the code.


## Notes

**2026-11-02T02:33:32Z**

The reference (builder, 2026-11-01). Base c2e6399e, Release, headless, 1920 x 1080, Apple M1 Max, every run under the GPU lock. Files in this worktree's captures/8fz2/ (git ignores captures/), the probe tools in /tmp/8fz2.
- The reference is ter-xgra's p-bruteD.patch, which applies on this base unchanged, with its read count raised: D(n) is the mean of N cosine-weighted reads of the specular map at roughness 0.2, a fixed Hammersley set. The runs use deck-late (captures/xgra/scripts/deck-late.json, ter-k96l's late.json at 1920 x 1080) with --screen-space-light off and --bloom off. A probe patch p-fixexp fixes the exposure at 0.75 stops, because the automatic exposure moved by up to 0.04 stops between builds (0.676, 0.713 and 0.708 stops at 256, 1024 and 4096 reads), and that moved the box means more than the read count did.
- The boxes: the cabin's base face is x 1200-1899, from y 560 down to 3 px above the crease (52,407 px). The floor band is 1 to 20 px below the crease in the same columns (9,220 px). The crease comes from the base's light-off capture, as in ter-xgra's masks.py. Each mean is of display values decoded from sRGB (tool: /tmp/8fz2/tools/boxes.py, kept with the captures as captures/8fz2/tools/boxes.py).
- Convergence, linear means (R, G, B):

| Reads | Face | Band |
|---|---|---|
| 4096 | 0.02137, 0.02185, 0.03011 | 0.11957, 0.16626, 0.33685 |
| 8192 | 0.02137, 0.02185, 0.03011 | 0.11957, 0.16627, 0.33686 |
| 16384 | 0.02137, 0.02185, 0.03011 | 0.11962, 0.16636, 0.33699 |
| base, the harmonics | 0.01387, 0.01104, 0.01478 | 0.11461, 0.15963, 0.32960 |

- The reference count is 4096 reads. Doubling it to 8192 moves each box mean by at most 0.01 % (the band's green), and 8192 to 16384 by at most 0.05 %. Both are far below a tenth of the tolerance, 0.3 %.
- The tolerance is 3 %: on deck-late, each box's mean within 3 % of the reference's, per channel. In the tests, D within 3 % of the map's largest D, the light toward its one small bright light, at every normal.
- The harmonics against the reference: the face is 35 %, 49 % and 51 % too dark (R, G, B), and the band 4.1 %, 4.0 % and 2.2 % too dark.

**2026-11-02T03:24:12Z**

The corrected reference (builder, 2026-11-01). The reference of the note above reads the specular map at roughness 0.2. The GGX lobe at that roughness has long tails, so the reference adds light from the bright parts of the map to the face's dark hemisphere. The cube convolves the radiance map itself. Against that reference, the cube makes the face 6.3 %, 9.8 % and 17.1 % too dark (R, G, B), at every cube size and source size tried, which shows that the cube's resolution is not the cause.
- The corrected reference reads the specular map at roughness 0, which is the radiance itself. Each pixel turns the Hammersley set by its own random shift (a Cranley-Patterson rotation, seeded by a hash of the view direction), so the reads' error averages out over each box. Patches: /tmp/8fz2/patches/p-rot4096.patch and the same at 8192 and 16384 reads.
- Without the per-pixel shift, every pixel of a flat box reads the same directions, and the band's mean still moved by 4 % between 16,384 and 65,536 reads. Runs of 65,536 shifted reads and of 262,144 plain reads lost the device (a frame too long for the GPU).
- Convergence, linear means (R, G, B), deck-late with --screen-space-light off and --bloom off, exposure fixed at 0.75 stops:

| Reads | Face | Band |
|---|---|---|
| 4096 | 0.02000, 0.01970, 0.02493 | 0.12064, 0.16817, 0.34172 |
| 8192 | 0.02001, 0.01971, 0.02495 | 0.12074, 0.16841, 0.34210 |
| 16384 | 0.02002, 0.01972, 0.02496 | 0.12077, 0.16849, 0.34223 |

- The reference count is 16,384 reads. Each doubling moves each box mean by at most 0.15 %, half of a tenth of the tolerance.
- The cube's size by measurement, error per channel (R, G, B) against the reference:

| Cube face / source face, texels | Face | Band |
|---|---|---|
| 16 / 32, the choice | 0.0, -0.1, 0.0 % | 0.0, -0.1, -0.1 % |
| 32 / 32 | 0.0, -0.1, +0.1 % | +0.2, +0.2, +0.1 % |
| 16 / 64 | -0.1, -0.2, -0.1 % | -0.4, -0.3, -0.3 % |
| base, the harmonics | -30.7, -44.0, -40.8 % | -5.1, -5.3, -3.7 % |

- On deck-late, the three sizes agree within the reference's own noise. The CPU measurement of the earlier note decides the size: 16 / 32 keeps a light of 2 degrees within 2.23 % of the peak D, and 16 / 16 does not. 32 / 32 costs four times the texels and gives 1.01 %, which the tolerance does not need.

**2026-11-02T03:58:20Z**

The result (builder, 2026-11-01).
- The cube: RGBA16F, 16 texels a side, 4 levels down to 2 x 2, one cube per map in a cube array. It convolves the radiance map's level of 32 texels a side, or level 0 of a smaller map. Alpha holds the cosine-weighted open-sky share, which no reader uses yet. The CPU reads back the last level, 24 texels, 192 B per map. The plan records the choice in docs/plans/environment-map.md, "The irradiance", and decision 24.
- The tolerance: 3 % of the largest D under one light of 2 degrees, at every normal (IrradianceCubeTests on the CPU, EnvironmentLightingTests on the GPU). The CPU measurement gives 2.23 %. A light of 0.5 degrees reaches 4.05 %, worst on the ring of normals at right angles to it: a known limit, in screen-space-illumination.md, Known limit 2.
- The error per box on deck-late, screen-space light off, against the corrected reference of 16,384 reads (R, G, B): the face 0.0, -0.1, 0.0 % (the harmonics -30.7, -44.0, -40.8 %), the band 0.0, -0.1, -0.1 % (the harmonics -5.1, -5.3, -3.7 %).
- The cost: interleaved runs of time-deck, six rounds of base, a control copy of base, and the cube, 1-minute load 7.7 to 9.1 on 10 cores. The renderer's timers gave the prefilter 0.460 ms against 0.337 ms (base) and 0.339 ms (control), and the frame span 13.98 against 14.10 ms. The timers' pass times are not the pass's cost on this Mac (tools/README.md), so the fix stage's note below gives the cost from traces: about +0.185 ms. Two traces of all of demo-exit (gpu-trace.sh --no-counters --frames 0, 3,889 frames, 1-minute loads 14.57 and 15.04 at their starts): the prefilter's median 0.404 and 0.406 ms. Lanes per texel: 4, 8, 16, 32 and 64 gave 0.530, 0.460, 0.481, 0.68 and 0.98 ms, so 8. Rounds of 256 source texels gave 0.577 ms.
- The captures, 1920 x 1080, Release, headless, base (harmonics) against cube, in captures/8fz2/after (sheets in after/sheets and after/sheets-off; tool captures/8fz2/tools/sheets.py). Changed views: deck-late with the light on and off (37 % and 39 % of pixels change by more than 8 of 255), region by region in the fix stage's note below: the cabin's base face is lighter and slightly bluer and matches the reference, the cabin's upper frames are a little lighter and bluer, the left wall and the blue post are less blue, and the floor keeps its light. The chute film (6.7 %): the crate's top and front are lighter by 7 to 11 of 255, and the walls by about 2. The other films and the five review scenes change by about 1 of 255 on average, within the automatic exposure's noise.
- No snapshot reference changed: the snapshot fixtures run with the map off, and the full Checked suite passed with the references as they were. So no reference needed a new approval.
- The throw slice's test changed on purpose: with bloom off, the crates' fronts and the chamber's wall show black in the reference and in the cube, so the test checks the copy's lit top face.
- The device no longer requires the subgroup ballot operations, which only the harmonics' read used.

**2026-11-02T05:01:23Z**

The fix stage after the review (builder, 2026-11-02). Each finding and its resolution.
- Finding 1, the cost. The base traces of ter-k96l are no longer on disk, so I took new ones under the GPU lock, interleaved: base, cube, base, all of demo-exit (tools/gpu-trace.sh --no-counters --frames 0, 3,889 frames, 1920 x 1080, Release), at 1-minute loads of 6.43, 10.24 and 6.81 at their starts. The prefilter's median: 0.219, 0.404 and 0.219 ms. With the two earlier cube traces (0.404 and 0.406 ms), the convolution adds about 0.185 ms, not 0.12 ms. The ticket's estimate of 0.28 ms was the timer's median on the deck, and the traces give today's prefilter 0.219 ms. The ambient light pass fell from 0.582 and 0.584 ms to 0.494 ms, about -0.09 ms, from the removed subgroup loop. The frame span's median: 13.563 and 13.541 ms (base) against 13.543 ms (cube). Tables: captures/8fz2/trace-cmp/{base1,cube1,base2}/passes.md, run.trace deleted. ARCHITECTURE.md ("Cost") and environment-map.md ("Why a cube") now state the trace delta, and keep the timers' frame span as the frame budget.
- Finding 2, the changed view of deck-late. Linear means (sRGB decoded), base against cube, screen-space light on (off): the cabin's base face (x 1200-1899, y 560-639) from (0.0179, 0.0153, 0.0205) to (0.0225, 0.0224, 0.0290), off (0.0178, 0.0154, 0.0215) to (0.0225, 0.0228, 0.0302): 26 % to 46 % lighter and slightly bluer, as the reference. The cabin's upper frames (x 1300-1899, y 150-449) 3 % to 10 % lighter, B/R 0.88 to 0.94 (0.90 to 0.96). The beige wall left of the light (x 0-499, y 250-499) darker and less blue, B/R 1.01 to 0.75. The blue post (x 975-1024, y 500-749) less blue, B/R 4.11 to 3.46 (3.88 to 3.34). The floor (y 800-1079) 1 % to 3 % darker. Tool: /tmp/8fz2/tools/regions.py.
- Finding 3, the smallest map. A map of 16 texels a side has no level of 32, so its cube convolves level 0 of 16. Measured by the new CPU sweep: 3.53 % of the largest D under a light of 2 degrees, 4.22 % under 0.5 degrees, over the tolerance. Known limit 2 and environment-map.md, "The irradiance", state it, and the test OnAMapOf16TexelsASmallBrightLightStaysWithinTheKnownLimit keeps it within 4 %. A larger source for such a map would need a level that it does not have.
- Gap, the CPU table. IrradianceCubeTests now holds the sweep: a bilinear read of the cube (a texel past a face's edge from the neighboring face), 10 lights in random directions, the normals of 37 rings of 24 around each light. Worst error of the choice, 16 over 32: 3.15, 2.90, 2.07 and 1.56 % at 0.5, 1, 2 and 4 degrees. A source of 16: 4.22, 3.88, 3.53 and 1.80 %. The test of the choice asserts 3 % at 2 degrees, which the source of 16 fails, so the test separates the two. The plan's table now gives these numbers and keeps the first, scratch numbers as such.
- Finding 4, stale docs. environment-maps-per-frame.md: decisions 23 and 25, the table of changes, the flowchart, "The prefilter of each map", the sketch of environment.glsl and its bullet, the growth (bindings 12 and 14 rewritten), the descriptor table (binding 14 a sampled cube array, vertex, fragment and compute) and two rows of the safety table. environment-map.md: the sampler's stages at binding 13. ARCHITECTURE.md: the stages of bindings 12 to 14, and the row of environmentMeanRadiance, which now argues from six finite half texels. The barrier test's summary counts one barrier before each map's downsample.
- Finding 5, the exact D of the matte sphere. Measured first: the exact D from the map of the white render and from the map of the black render agree to within 0.001 % of the peak at every tested normal, because a face lights a surface only by the lights of the list (decision 17), and the glowing ball is emission, so the sphere is black in the map in both renders. So the finding's premise does not hold, and no test can fail on it. The test now reads the exact D right after the white renders anyway, and its summary states the reason.
- Finding 6, the signature's weight. The map's part now weighs pi / 24 per texel: the irradiance that the map sends to a surface, in the unit of the direct light. A uniform map of radiance L weighs pi |L|, against 11.1 |L| with the harmonics and 24 |L| unweighted. Red first: TheMapsLightWeighsAgainstTheSunsAsTheIrradianceThatItSendsToTheObject failed (a 5 % change of the sun under a uniform map of radiance 1 did not recapture), then passed. AMapLightThatMovesToAnotherSideOfTheObjectMakesACaptureStale pins a change of direction with the same total. Three tests' numbers moved with the weight. The plan's "Impostors" and ARCHITECTURE.md's "Impostors" record the weight.
- Finding 7, the readback's layout. EnvironmentMap.RecordReadback now owns every layout change of the cube after the prefilter: it reads the cube's recorded state (the prefilter sets StorageCompute after its writes), moves the last level to TransferSrcOptimal and the others to SampledVertexFragment, copies, moves the last level back, and records the state. The pass only calls it. So the no-face path is no longer a branch of its own, and the barrier count stays 5M + 2.
- Gap, the CPU cost of the signature. ImpostorManagerBenchmarks gained a Map parameter. With impostors: 1.360 ms with the map against 1.372 ms without, error about 0.06 ms, 0 B allocated (load 9.0 to 7.1). The 24 texels cost nothing measurable.
- Gaps that stay: no Vulkan validation run on a Linux driver (every run here is on MoltenVK). The red phase of the GPU small-light test against the harmonics needs the base's shaders in a second checkout, which this stage may not make; the captures show the harmonics 31 % to 44 % off on the base face, far over the 3 % that the test allows.

**2026-11-02T06:17:13Z**

The hardening (test stage, 2026-11-02). Coverage of the changed lines in src, Debug and Release together: 208 of 212 lines (98.1 %) and 33 of 34 branches. The 4 uncovered lines were the refusal of a source of the wrong size in IrradianceCube.Convolve, which the new ASourceThatIsNotSixFacesOfItsSizeIsRefused now covers. Hand mutations: 49 mutants, 29 in C# and 20 in GLSL. 34 were killed by the existing tests. 11 survived and were then killed by 9 of the 10 new test methods: the frame readback of each map (C13, C14), the hue change of a map texel in the impostor signature (C19), the map's light in the total that a lamp's change is measured against (C25), the refusal (C29), the division by the weights' sum (G3), the lanes that wrote past their map's layers (G6), the source level of 32 under a small bright square (G7), the map axes of the diffuse read (G14), and the puffs' mean over the last level of every face (G17, G19). 4 stay: C4 equivalent (a texel at cosine 0 adds 0), C21 not observable on MoltenVK (after a growth a slot that skips the rewrite of binding 14 reads a view of the destroyed cube, which Vulkan leaves undefined, and MoltenVK serves the right texels), G12 not observable (a race on shared memory between rounds), C27 a judgment call outside this ticket's change (the larger total against the first total decide differently only within about 2 % of the 2 / h threshold). The tenth, AfterAGrowthOfTheMapsAMatteSurfaceTakesTheDiffuseLightOfTheNewMaps, checks both frame slots after a growth and kills no mutant here (see C21). The commit e358ca6ce holds the table. Full Checked run: 9833 tests, 0 failed, 9819 succeeded, 14 skipped.
