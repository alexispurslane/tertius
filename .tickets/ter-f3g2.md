---
id: ter-f3g2
status: in_progress
deps: [ter-y8z0]
links: [ter-eqtb, ter-rnjk]
created: 2026-11-02T00:33:34Z
type: task
parent: ter-b2da
priority: 1
assignee: Alexis Purslane
tags: [rendering, reflections]
---
# Soft rough reflections: probe stochastic rays with a fixed noise and ray reuse against the reference, before the design choice

The user's main concern after ter-y8z0 (2026-11-01): the blockiness of the screen-space reflections on rough surfaces (ter-y8z0's artifact 3). Cause, from ter-y8z0: one ray along the dominant direction decides which surface a pixel reflects, and the cone only blurs that surface's color, so a reflected outline stays hard and the reflection breaks into blocks. The converged lobe (64 GGX rays per pixel) shows a soft glow instead. The user asked whether temporal SSR, which breaks decision 8 of docs/plans/rendering-performance.md, is worth it. Two designs go to the user with images: (1) temporal SSR: one stochastic ray per pixel per frame, ray reuse, and accumulation over frames with reprojection of the reflected point (needs motion vectors, which the engine does not have); (2) frame-pure stochastic SSR: rays jittered by a fixed noise per pixel, as the screen-space light does (decisions 39 to 43 of docs/plans/screen-space-illumination.md), and a resolve that reuses the hits of neighbors weighted by BRDF over pdf (Stachowiak 2015), then a blur. This ticket probes design 2 on the views of artifact 3 against ter-y8z0's corrected (normalized) lobe reference, measures its cost, and shows the converged lobe as the best case of design 1 for a still camera. It changes no default. Its place in or out of epic ter-b2da waits for the user's answer on the FSR order (ter-htg7).


## Notes

**2026-11-02T00:51:05Z**

2026-11-01: workflow run 9f91a243-94ad-40db-98ed-dd28a3636b8f (version 869b0b07), branch ter-f3g2-soft-rough-reflections from eeac4083. A probe ticket: a timed prototype of design 2 (GGX rays by a fixed noise, a resolve that reuses neighbors' hits weighted by BRDF over pdf, a blur), captured on every view of artifacts 1, 2, 3 and 5 and on the mirrors, against ter-y8z0's normalized lobe reference and MaxRoughness 0.37.

**2026-11-02T00:54:34Z**

The user's decision (2026-11-01): FSR waits for the reflections. So this probe, the design choice that follows it and the build of the chosen design stay in epic ter-b2da, and ter-htg7 merges FSR after them.

**2026-11-02T01:00:44Z**

The user's choice (2026-11-01): design 2, frame-pure stochastic SSR (GGX rays drawn by a fixed noise per pixel, a resolve that reuses the hits of neighbors weighted by BRDF over pdf, a blur; nothing kept between frames). It reopens decisions 3 and 12 of docs/plans/screen-space-reflections.md ('no noise') in the way that the screen-space light already accepts a fixed noise; decision 8 of rendering-performance.md holds. This probe still runs: its prototype, settings, measures against the normalized lobe reference and cost set the build ticket's starting point. If the probe shows that design 2 at roughness 0.38 looks like MaxRoughness 0.37 for much more cost, the orchestrator brings that back to the user before the build.

**2026-11-02T02:11:20Z**

The prototype of design 2, frame-pure stochastic reflections (builder, 2026-11-01). Base eeac4083 (main with ter-kn1d's merge). All paths are relative to captures/f3g2/, whose README.txt lists every script, patch and gallery. No file under src, tests, shaders or tools changed in the commits.
- Patches: probes/p-design2.patch holds the design with its constants in one place (src/Tertius.Rendering/Shaders/reflection_stochastic.glsl). probes/p-design2-p1.patch and p-design2-p2.patch are the two kept settings. probes/variant.sh writes and builds any other setting. probes/probe-build.sh is ter-y8z0's script, and it also deletes the files that a patch adds.
- What it does, in the default's half-resolution passes:
  1. The trace. Each texel's representative pixel 2t traces one ray along a GGX sample of its lobe: the half vector from D(h) (n . h), the sampling of the reference lobenorm. The noise u is the screen-space light's noise (ambient-occlusion.md, "The noise"): the Hilbert index of a 64 x 64 tile drives the R2 sequence. Why: it is a fixed function of the texel, so a still camera sees the same image in every frame and decision 8 holds. Neighboring texels along the Hilbert curve get well-spread values, so a small disk of neighbors covers the lobe evenly, which reuse needs. A sample below the geometric or shading surface is dropped, as lobenorm drops it.
  2. What the trace stores. The radiance and coverage read at the hit (the default's RGBA16F image). The hit image becomes RGBA32F: the hit's view-space point and the pdf, or for a miss the sample's direction and -pdf, or 0 for a dropped sample.
  3. A new pass, "reflection resolve", at half resolution, into a new RGBA16F image (binding 7). The receiver is the texel's representative pixel. It reads its own texel and N neighbor texels on a Vogel disk, turned by a third R2 value of the same index. The disk's radius in pixels is the length of the receiver's own ray times 1.7602 alpha (the cone of decision 19), over the pixel's width, at most 48 px. A miss takes the receiver's distance to the eye as its length. Each sample weighs the receiver's target over the pdf that drew the sample, times the default upsample's plane and normal weights. The target is the receiver's pdf of l times n . l, and 0 below the surface: the weight of lobenorm's mean, so the estimate converges to the reference. l points from the receiver to the neighbor's hit, or along the neighbor's direction for a miss, which counts as 0 with its weight. The result is the weighted mean of (radiance, coverage).
  4. The "reflections" pass, rewritten: the upsample and the blur in one. A tent over the resolved texels, half width 1 texel (exactly the default's bilinear weights) up to 2 texels, by half the disk's radius in texels, times the default's plane and normal weights. Where the disk is smaller than 4 px and the raw samples of the pixel's bilinear texels disagree (a hit beside a miss, or depths past the slab), the pixel traces its own sample, as the default traces its own ray. The delta is the default's reflectionDelta.
- Two reads at the hit were tried. Sharp: the cone covers only the chord to the next texel's ray, as lobenorm reads each ray. Filtered: the cone covers the sample's solid angle among the reused samples, 1 / ((N + 1) pdf), at most the lobe's cone (filtered importance sampling, Krivanek and Colbert 2008).
- Rules: nothing accumulates over frames. The new formats are core Vulkan storage formats (RGBA32F and RGBA16F), and the probe needs no new device feature. One run of mirror-balls under VK_LAYER_KHRONOS_validation (core checks) gave no error.
- Settings tried, on five boxes of the films (analysis/settings.txt, columns as in the measures note):
  - The blur halves the noise: 8 neighbors without it 348, with it 172 (deck box).
  - The noise and the distance from the reference fall with N: deck |-ref5| 4.47, 3.75, 3.05, 2.32 for 4, 8, 16, 32 neighbors with filtered reads.
  - Filtered reads are smooth but too bright where the screen around the hit is brighter than the hit. The floor wedge (artifact 5): 116.0 with 32 neighbors, 119.2 with the own sample alone, against the reference's 106.1. The own sample alone with sharp reads gives 106.9. So this bias lies in the cone's wide read itself, not in the reuse. That is also a finding about the default: part of artifact 5's bright wedge comes from the cone's read at the hit, not only from the one ray.
  - Sharp reads match the reference's mean in every box, but they show the reference's fireflies as soft bright blobs.
  - Karis's weights 1 / (1 + luma) remove the blobs but also most of the reflection: brake-run teeth 122.7, reflections off 123.0, reference 141.2. Rejected.
  - A disk of at most 16 px changes no box by more than 0.4 levels.
- Kept: P1 = 32 neighbors, sharp reads, blur (gallery g/d2n32s): the closest to the reference, with bright blobs. P2 = 32 neighbors, filtered reads, blur (g/d2n32): the smoothest, with the cone's brightness bias. The cost note also times 8 neighbors with sharp reads (d2n8s).
- Mirrors (analysis/mirror-plate.txt, mirrors.txt). The engine floors the roughness at 0.045 (MinRoughness in brdf.glsl), so no lobe is one direction, and the result is not bit-identical. Stated tolerance:
  - The flat mirror of roughness 0, mirror-balls/plate: P1 differs from the default in 0.005 % of the pixels, by at most 1 level.
  - The ball of roughness 0: P1 changes 5.5 % of the ball's pixels by more than 4 levels, P2 5.5 %, the reference 7.1 %. The reflection of the neighbor ball near the rim and the edge of the lamp's highlight are softer than in the default (sheets/m-mirror-ball-r0-3x.png). The own sample alone, without reuse or blur, shows the same softening. So its cause is the GGX sample of the floored lobe on a curved mirror, not the resolve. A fix ticket must look at it.
  - The reflections scene's floor keeps the box's sharp reflection (sheets/m-reflections-front-1x.png).
- Frames: with a still camera, two frames one tick apart differ as little as the default's (analysis/framediff-proto.txt: unbelt 0.10 %, deck 0.05 % of the pixels above 4 levels). Under a moving camera the noise stays fixed on the screen while the surfaces move under it, as the screen-space light's noise does.

**2026-11-02T02:11:48Z**

Measures and images per artifact (builder, 2026-11-01). Every view of ter-y8z0's artifacts 1, 2, 3 and 5, mirror-balls and the reflections scene was captured in seven variants: the default (g/base), P1 (g/d2n32s), P2 (g/d2n32), ter-y8z0's reuse probe 3C (g/reuse), MaxRoughness 0.37, candidate D (g/maxr037), the reference lobenorm (g/lobenorm) and reflections off (g/refl-off). All are on this base, so the numbers differ from ter-y8z0's, which predate ter-kn1d's shadow change.
- The measures (analysis/measure.py, table.txt has every box): luma is the box's mean Rec. 709 luma of the 8-bit values. |-ref5| is the mean absolute difference of the 8-bit channels from the reference after a 5 x 5 box blur (PIL BoxBlur(2)), which removes most of the reference's own speckle. |-D| is the same difference from D. Noise is ter-xgra's noise.py: the mean absolute difference between luma and its 9 x 9 box blur, times 100.
- Each sheet (sheets/a<artifact>-<view>-<scale>.png) shows default, P1, P2, reference, D and reflections off, in that order.

Luma of each box. The second line gives |-ref5| for default, P1, P2, reuse and D.

| Artifact, view, box | default | P1 | P2 | reuse | D | reference | off | |-ref5| default, P1, P2, reuse, D |
|---|---|---|---|---|---|---|---|---|
| 3 deck fragments | 108.7 | 100.9 | 102.2 | 109.4 | 100.3 | 100.4 | 100.3 | 9.7, 2.2, 2.3, 9.6, 1.8 |
| 3 unbelt floor | 60.9 | 58.6 | 59.9 | 60.8 | 56.1 | 58.6 | 56.1 | 5.4, 1.7, 2.5, 3.8, 3.7 |
| 3 terminal pillar | 74.9 | 29.3 | 44.6 | 59.1 | 26.6 | 27.0 | 26.6 | 48.2, 2.6, 17.5, 31.9, 5.0 |
| 3 terminal door | 80.0 | 67.2 | 73.6 | 87.0 | 24.6 | 65.5 | 24.6 | 42.2, 7.8, 10.3, 34.4, 42.0 |
| 3 chute ceiling | 96.3 | 96.4 | 96.6 | 96.2 | 88.2 | 96.2 | 88.2 | 5.5, 2.9, 2.4, 4.4, 8.4 |
| 3 floor lamp cut | 132.5 | 130.8 | 132.0 | 133.1 | 114.9 | 130.9 | 114.9 | 5.1, 3.7, 2.3, 3.0, 15.7 |
| 3 brake-run left wall | 90.6 | 88.2 | 88.5 | 90.3 | 90.0 | 88.2 | 90.0 | 4.5, 1.0, 1.6, 3.7, 2.3 |
| 3 deck-late, lower half | 135.7 | 134.9 | 135.5 | 135.7 | 139.5 | 134.7 | 139.5 | 5.3, 1.6, 2.9, 5.2, 6.8 |
| 3 pbr-spheres floor | 88.3 | 87.7 | 87.8 | 87.8 | 85.9 | 88.0 | 85.7 | 1.7, 1.0, 1.0, 1.4, 2.7 |
| 3 reflections/low ellipse | 93.4 | 91.6 | 91.4 | 91.9 | 89.9 | 91.0 | 94.1 | 4.2, 3.5, 3.3, 3.8, 4.8 |
| 1 and 3 chute-top slide | 111.0 | 118.1 | 117.3 | 111.7 | 104.5 | 118.3 | 104.5 | 12.6, 5.8, 7.3, 12.0, 16.7 |
| 2 brake-run teeth | 131.3 | 141.1 | 142.0 | 131.8 | 123.0 | 141.2 | 123.0 | 18.3, 8.8, 3.4, 16.3, 18.0 |
| 2 antispinward edge | 112.3 | 121.2 | 122.0 | 112.4 | 112.1 | 120.7 | 112.1 | 10.6, 6.7, 3.7, 9.8, 9.1 |
| 2 terminal steps | 7.9 | 9.4 | 12.4 | 11.7 | 2.2 | 8.1 | 2.2 | 3.4, 1.4, 4.3, 3.6, 5.7 |
| 5 floor wedge | 123.9 | 106.8 | 116.0 | 122.8 | 107.2 | 106.1 | 107.2 | 20.3, 2.5, 9.8, 17.0, 1.6 |
| 5 stack curve | 93.2 | 82.0 | 88.3 | 93.3 | 80.6 | 82.5 | 80.6 | 12.2, 2.8, 6.2, 11.3, 3.2 |
| 1 floor steps | 113.9 | 104.5 | 108.5 | 113.6 | 101.0 | 104.5 | 101.0 | 10.7, 3.4, 5.0, 10.1, 4.1 |

- Artifact 3, the blocks (sheets/a3-deck-2x.png, a3-terminal-1x.png, a3-unbelt-half.png, a3-chute-ceiling-1x.png, a3-brake-run-leftwall-1x.png, a3-antispinward-pillar-2x.png, a3-deck-late-half.png, a3-boarding-half.png, a3-pbr-floor-1x.png, a3-reflections-low-3x.png, a3-floor-lampcut-3x.png). Both prototypes remove every hard outline and every fragment. What remains is a soft glow with grain, as in the reference. P1 is the closer to the reference in brightness: the terminal's pillar 29.3 against 27.0, where P2 keeps 44.6, and it brings the door's reflection back (67.2 against 65.5). P2's filtered reads keep part of the default's extra brightness. The reuse probe 3C softens the outlines but keeps the default's brightness (deck 109.4).
- Artifact 2, the teeth (sheets/a2-brake-run-teeth-3x.png, a2-antispinward-edge-2x.png, a2-terminal-steps-3x.png). Both prototypes remove the teeth and give the reference's soft reflection with its brightness: brake-run 141.1 and 142.0 against 141.2.
- Artifact 5, the wedge (sheets/a5-floor-wedge-2x.png, a5-stack-2x.png). Both remove the hard curved edge. P1 also removes the wedge's extra brightness (106.8 against 106.1). P2 keeps half of it (116.0), the bias of its filtered reads.
- Artifact 1, the steps (sheets/a1-floor-steps-1x.png, a1-chute-top-slide-2x.png). The bands are gone on the floor film's floor and the chute-top slide in both prototypes. Each sample keeps its entry-share ramp, but neighboring samples hit different rows, so the bands turn into grain, and the box's mean equals the reference's (P1 104.5). This holds on the demo's rough paint only. A narrow lobe sends its samples along almost one direction, so the steps can stay on smooth floors. That case was not measured, and ter-eqtb's fix stays needed for it.
- Noise. The prototypes add grain. P1 has more than the default (brake-run teeth 539 against 106, deck 176 against 165), less than the reference (1043, 278), and shows bright blobs where the reference shows fireflies. P2 has about the default's grain (deck 129, teeth 147).
- The prototype against D. D matches reflections off on every painted view: on the demo's paint (roughness 0.38) it traces no ray. Two cases:
  - Where the reference itself shows almost no reflection, D is as close to the reference as P1 and has no grain. Deck: reference 100.4, off 100.3, |-ref5| D 1.8 against P1 2.2. Also the floor wedge (D 1.6 against 2.5), the stack, unbelt and the floor steps. On the deck, the user's first complaint, D gives the reference's look for less.
  - Where the reference shows a clear rough reflection, D loses it and the prototype keeps it, and the difference shows. Chute-top slide: reference 118.3, P1 118.1, D 104.5. Brake-run teeth: 141.2, 141.1, 123.0. Antispinward edge: 120.7, 121.2, 112.1. Terminal door: 65.5, 67.2, 24.6. Floor lamp cut: 130.9, 130.8, 114.9. Chute ceiling: 96.2, 96.4, 88.2. sheets/a1-chute-top-slide-2x.png and a3-terminal-1x.png show it.
- Mirrors: see the prototype note. On mirror-balls the whole view changes about as much in the prototypes as in the reference (analysis/mirrors.txt).

**2026-11-02T02:12:03Z**

Cost (builder, 2026-11-01). Measured as ter-y8z0 measured: probes/timing.sh runs the builds in interleaved rounds in a rotated order, each a locked run of the 120-frame window with --video none. The cost is the summed mean of the reflection passes by the renderer's timer (depth pyramid, radiance pyramid, reflection trace, the probe's reflection resolve, reflections). base2, a byte copy of base, is the control. Five rounds on the deck window and five on the floor window. Every run records its 1-minute load (timing/runs.txt, summary in timing/summary.txt). The load was 8 to 22 on the deck rounds and 7 to 165 on the floor rounds: another agent's builds ran during floor rounds 3 to 5. The medians hold: the control stays within 0.06 ms of base on both windows.

| Build | Deck, median sum (range) | Floor, median sum (range) | Against the default |
|---|---|---|---|
| base, the default | 3.59 ms (3.59-4.28) | 3.69 ms (3.35-3.83) | |
| base2, the control | 3.60 ms (3.60-3.64) | 3.75 ms (3.35-4.19) | |
| D, MaxRoughness 0.37 | 1.58 ms | 2.04 ms | -2.01 ms deck, -1.65 ms floor |
| 8 neighbors, sharp (d2n8s) | 6.97 ms | 6.82 ms | +3.38 ms deck, +3.13 ms floor |
| P1, 32 neighbors, sharp | 9.78 ms | 9.97 ms | +6.19 ms deck, +6.28 ms floor |
| P2, 32 neighbors, filtered | 9.75 ms | 9.98 ms | the same as P1 |

- Per pass on the deck, medians: the trace 1.19 ms in the default against 2.55 ms in the prototype. The resolve 1.04 ms with 8 neighbors, 3.85 ms with 32. The upsample ("reflections", with its own rays) 1.29 ms against 2.24 ms. The pyramids do not change (0.53 and 0.60 ms). The floor window gives the same picture.
- What is the design's own cost, and what is the probe's overhead:
  - The trace's rise, about +1.0 to +1.4 ms, belongs to the design. Its rays point in scattered directions, so the lanes of a wave walk different cells. Ray binning, a known remedy, is not probed.
  - The resolve costs about 0.12 ms per neighbor at 1080p. The probe reads four images per neighbor: the radiance, the hit, and the receiver-side distance and normal of the G-buffer. A packed record of one or two reads per neighbor can about halve it: an estimate of 0.5 ms for 8 neighbors and 2 ms for 32, not measured.
  - The upsample's rise, about +0.9 ms, is mostly overhead: the probe reads a fixed 5 x 5 texels per pixel from the images, without the default's shared-memory tile. A tile of 9 x 9 texels can bring it near the default's 1.29 ms.
  - An optimized design 2 then costs about +2 ms against the default with 8 neighbors and about +3.5 ms with 32. These are estimates from the measured passes, not measurements.
- With 8 neighbors and sharp reads, the image is noisier than P1 (settings.txt: deck noise 207 against 176, brake-run teeth 819 against 539), and its mean stays on the reference's.
- The frame budget: decision 1 of docs/plans/rendering-performance.md asks for 12 ms of GPU time at 1080p. The default's frame span is 13.2 ms on the deck window, and P1's is 19.4 ms.

**2026-11-02T02:12:26Z**

Summary for the user's choice (builder, 2026-11-01). The probe changes no default and no code. Screenshots: sheets/shot-deck.png, shot-chute-top-slide.png, shot-terminal.png and shot-floor-wedge.png. Each shows default, P1, P2, reference, D and reflections off.

| | Design 1: temporal SSR | Design 2: frame-pure stochastic SSR (this probe) | Candidate D: MaxRoughness 0.37 |
|---|---|---|---|
| What the user gets | With a still camera, its best case is the converged lobe, the reference lobenorm: a soft glow with no outlines, no blocks, no teeth and no wedge, and no grain once it settles. Under motion: ghosting, and the grain comes back while the history rebuilds. | No outlines, blocks, teeth or wedge, and the reference's brightness (P1). Grain that never settles: soft bright blobs where the reference has fireflies (P1), or finer grain with part of the default's extra brightness (P2). Mirrors stay sharp (the flat plate within 1 level). The ball of roughness 0 softens near its rim. | No screen-space reflection on the demo's paint (roughness 0.38): its look is reflections off. On the deck it matches the reference's look, which is almost no reflection. It loses the rough reflections that the reference shows on the chute-top slide, brake-run, antispinward, the terminal's door, the floor's lamp and the chute's ceiling. Smooth surfaces keep their reflections. |
| Cost | Literature only, no probe. The trace and a resolve as in design 2, usually with fewer neighbors. Motion vectors, which the engine does not have: a velocity target written by the G-buffer pass, with last frame's transforms for every mesh, skinned and deformed ones too. A history image and a temporal pass that reprojects the reflected point by the hit's depth, not the surface's (Stachowiak 2015), with clamping against ghosting. In the turning ring everything moves on the screen all the time, so the history is often rejected, and a still image depends on the frames before it. | Measured: +6.2 ms on the deck window and +6.3 ms on the floor window with 32 neighbors (P1, P2). +3.4 and +3.1 ms with 8 neighbors. An optimized version: about +2 ms with 8 neighbors and +3.5 ms with 32 (estimates). | Measured: saves 2.0 ms on the deck window and 1.65 ms on the floor window. |
| User decisions that it reopens | Decision 8 of docs/plans/rendering-performance.md (no accumulation over frames), and decisions 3 and 12 of docs/plans/screen-space-reflections.md (a cone with no noise, no noise) | Decisions 3 and 12 of screen-space-reflections.md. Decision 8 holds. | The user's choice of MaxRoughness 0.6 (2026-10-25, open fork 2, decision 31 of screen-space-reflections.md) |

- What the probe found beyond the brief:
  - Part of artifact 5's bright wedge comes from the cone's wide read at the hit, not only from the one ray. The filtered reads of P2 keep half of it, and sharp reads remove it (measures note).
  - On the demo's rough paint, design 2 also removes artifact 1's steps (they turn into grain). ter-eqtb stays needed for smooth floors. That case was not measured.
  - The curved mirror of roughness 0 softens near its rim (prototype note).
- A fix ticket for design 2 can start from probes/p-design2-p1.patch. Its open work: the blobs of P1 (a firefly treatment that keeps the reflection's energy, for example a clamp relative to the neighbors), the curved mirror's rim, the packed resolve, the shared-memory upsample, and a check of artifact 1 on a smooth floor.
- A fix ticket for D changes one constant (probes/p-maxr037.patch).

**2026-11-02T02:28:35Z**

The user's decision of 2026-11-01, after ter-f3g2's probe: SSR's roughness limit goes to 0.37 now, inside epic ter-b2da; below it, today's method (one ray with a cone) stays; stochastic SSR (ter-rnjk) is not built; ter-gf0t moves into the epic to give back part of the lamp glow on rough surfaces that lose SSR. Reason: the probe showed that stochastic SSR helps only between roughness 0.37 and 0.6, which the 0.37 limit removes from SSR anyway, at +3 to +6 ms measured and with grain that never settles, while the limit saves 1.65 to 2.0 ms and matches the 64-ray reference on the deck paint.
