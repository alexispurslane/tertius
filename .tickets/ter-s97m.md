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
# Blue-noise draws for the bounce light's cosine direction: fewer daubs at the same ray count?

The user asked on 2026-11-12 about blue noise. Today every random number of the tracer is white noise: pcg3d of the packed pixel, the draw index and a constant per kind (drawOf in screen_trace.glsl). A blur of white noise keeps its low-frequency part, which looks like soft blotches. The diffuse filter is such a blur, so white-noise samples are one plausible source of the bounce light's daubs. Spatiotemporal blue noise (NVIDIA, 2022, docs/research/temporal-renderer/signals.md reference 13) was made for this situation: at most one sample per pixel, real-time GPUs, a spatial filter and temporal accumulation. Tertius traces one diffuse ray per 2x2 block in most pixels.

Test: replace only the two random numbers u of the cosine draw of a diffuse ray (traceFrom in screen_trace.glsl) with a spatiotemporal blue-noise mask indexed by pixel and draw index. Also order the block's chosen pixel (TurnDraw) by the same mask, or say why not. Keep the guided draw (descent and target point) on pcg3d, because blue noise helps little in many dimensions. Same ray count. Measure the daub score (/private/tmp/t7-3e22/daub.py; clouds stop showing at about 0.010), the grain, and the mean error against the true reference in the plate, the deck, the cabin and exit-cabin, at 60 draws and settled. Judge stills beside the reference by eye first.

Known risks: (1) blue noise moves the error to high frequencies, it does not remove bias, so it cannot fix the clamp's darkening or the rare bright hits of a small light (Design A and the guided rays own those). (2) The reflection study tiunr-m0uo (2026-11-07) spread reflection samples over pixels and frames: noise fell 11.6 % on smooth and 2.8 % on rough reflections, but the error of the average on rough reflections rose 11 %. Find out whether a mask whose period (for example 64 frames) interacts with the 32-draw history causes such an error before trusting a gain. (3) Masks: generate our own offline (void and cluster) or check the license of NVIDIA's before adding an asset. (4) Determinism: a fixed mask keeps runs on one machine identical.

Coordinate with the mottle agent (t7-3e22, Design A): measure against main as a clean A/B, but merge only after measuring again on top of Design A.

