---
id: t2-gfq5
status: in_progress
deps: []
links: []
created: 2026-11-12T06:39:03Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# The local shadow filter with nine bilinear comparison reads in place of nine gathers: 0.40 ms less in the deck's direct pass

The pair research of t2-mlvy (docs/plans/light-pairs.md, 'The nine-read shadow filter', and docs/research/light-pairs/pair/notes.md) found that the local shadow filter of shadows.glsl can read its 5 x 5 texels with nine bilinear comparison reads (textureGrad on the shadow sampler) in place of nine gathers of four comparisons each. The weights are the same in exact arithmetic. Measured on the deck on 2026-11-11, busy medians: the direct pass 4.093 ms on main and 3.696 ms with the filter, the transparent pass 2.085 ms and 2.125 ms, the frame span's median 24.382 ms and 24.206 ms, its 95th percentile 25.003 ms and 25.562 ms. The deck still differs from main by at most 8 steps of 255, with 99 % of the pixels within 1 step and 6.96 % of the pixels changed. The offline compile gives r112 as the highest register against r116 on main, which is one step up in the Asahi driver's table of shader copies at once; that fits the gain and does not prove its cause. The patch is docs/research/light-pairs/pair/c9-shadows.patch.

## Acceptance Criteria

The filter is in shadows.glsl with tests that state its weights against the exact 5 x 5 box filter. A film under motion shows no new flicker along shadow edges. A run on a Linux driver or lavapipe shows the shadow edges, because the hardware's rounding of the bilinear weights differs by driver (up to 2.3 % of full light in a constructed worst case at 4 bits). A trace of the deck and of the hall with the other frame-time cuts gives the frame span's median and 95th percentile. The reference images that change get a real review.

