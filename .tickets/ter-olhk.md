---
id: ter-olhk
status: closed
deps: []
links: []
created: 2026-11-02T08:33:55Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, reflections, performance]
---
# The reflection passes skip a frame in which no pixel reflects

Found by ter-8bda's measurement (2026-11-02). With the default SSR roughness limit of 0.37, no pixel of demo-exit reflects: the probe p-vizray, which marks every pixel that starts a ray, marks none in 363 frames (every 12th tick of the nine films), none in deck-late and none in demo-surface's seven views. Every view of those scripts is byte-identical to the same view with --reflections off. At the old limit of 0.6, every one of the 363 frames has rays, about 95 to 100 % of the screen in the mean of each film. The positive control mirror-balls marks 6.4 % (balls), 0.5 % (belly) and 15.9 % (plate) of its pixels. Yet the four reflection passes still run in every frame: 1.79 ms on the deck window and 2.02 ms on the floor window (median of 7 interleaved rounds, renderer timer, 1-minute load 7 to 12). Of that, the depth and radiance pyramids take 1.33 and 1.50 ms, the trace 0.05 ms, and the upsample ('reflections') 0.41 and 0.47 ms. A skip of the whole chain in such a frame removes these passes' busy time, the pass sum, of 1.79 ms on the deck window and 2.02 ms on the floor window, against the epic's 12 ms frame. The pass sum is an upper bound of the frame saving, not the saving itself. In the same runs, the change to 0.37 cut the pass sum by 2.01 ms on the deck window and by 1.43 ms on the floor window, but the median frame span, from the start of the frame's first pass to the end of its last pass, fell by 1.70 ms (14.17 to 12.47 ms) and by 0.61 ms (13.17 to 12.56 ms). So the frame span of a skip can fall by less than the pass sum, above all on the floor window. The runs do not show why. On MoltenVK, a pass's GPU time can hold part of the next pass's work, and the run log says that only the span and the sums of neighbor passes hold. Only a whole-frame skip is sound: a ray from any reflecting pixel can hit any pixel of the screen, so the pyramids must cover the whole screen as soon as one pixel reflects, and a region skip of the pyramids is not possible. The design picks the source of the fact 'no pixel reflects' (for example a bound from the drawn materials' roughness and their roughness maps on the CPU, or a GPU count with an indirect dispatch), and keeps the image of every frame unchanged. Evidence: captures/8bda/analysis/rays.txt, rays-controls.txt and timing/summary.txt (ter-8bda's worktree until the orchestrator copies captures/8bda to the main checkout).

## Acceptance Criteria

In a frame with no reflecting pixel, the reflection passes record no work, and the image is unchanged (a test of a scene of rough floors only). In a frame with one reflecting pixel, every pass runs. On the demo's deck and floor windows, by interleaved runs with a control, the reflection passes' sum falls by about the measured 1.79 and 2.02 ms, and the report gives the drop of the median frame span beside it (ter-8bda measured 1.70 and 0.61 ms for a similar removal). Checked and Release tests pass; ARCHITECTURE.md describes the skip.


## Notes

**2026-11-03T12:35:13Z**

Closed on 2026-11-03 at the user's request, without its work merged: it belongs to the pre-temporal renderer (spatial reuse, the 12 ms performance push, or the old reflection and bounce pipeline), and epic ter-89xq, the temporal renderer, supersedes it. Its notes and any branch or worktree stay for reference.
