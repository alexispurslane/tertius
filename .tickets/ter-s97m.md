---
id: ter-s97m
status: open
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
