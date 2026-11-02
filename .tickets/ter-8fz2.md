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
- The cost: interleaved runs of time-deck, six rounds of base, a control copy of base, and the cube, 1-minute load 7.7 to 9.1 on 10 cores. The prefilter took 0.460 ms against 0.337 ms (base) and 0.339 ms (control), so the convolution adds 0.12 ms, under the estimate of 0.28 ms. The frame span fell from 14.10 to 13.98 ms. Two traces of all of demo-exit (gpu-trace.sh --no-counters --frames 0, 3,889 frames, loads 15.1 and 6.8): the prefilter's median 0.404 and 0.406 ms. Lanes per texel: 4, 8, 16, 32 and 64 gave 0.530, 0.460, 0.481, 0.68 and 0.98 ms, so 8. Rounds of 256 source texels gave 0.577 ms.
- The captures, 1920 x 1080, Release, headless, base (harmonics) against cube, in captures/8fz2/after (sheets in after/sheets and after/sheets-off; tool captures/8fz2/tools/sheets.py). Changed views: deck-late with the light on and off (37 % and 39 % of pixels change by more than 8 of 255): the cabin's frames, walls and the blue post lose the blue cast that the harmonics' ringing added, and the floor keeps its light. The chute film (6.7 %): the crate's top and front are lighter by 7 to 11 of 255, and the walls by about 2. The other films and the five review scenes change by about 1 of 255 on average, within the automatic exposure's noise.
- No snapshot reference changed: the snapshot fixtures run with the map off, and the full Checked suite passed with the references as they were. So no reference needed a new approval.
- The throw slice's test changed on purpose: with bloom off, the crates' fronts and the chamber's wall show black in the reference and in the cube, so the test checks the copy's lit top face.
- The device no longer requires the subgroup ballot operations, which only the harmonics' read used.
