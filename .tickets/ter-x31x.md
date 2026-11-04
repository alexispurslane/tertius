---
id: ter-x31x
status: open
deps: [ter-nmbm]
links: []
created: 2026-11-04T00:13:46Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# ReSTIR's spatial result fed back into the temporal reuse darkens a lamp's hot spot

On demo-exit's deck at frame 120, a small panel next to a lamp holds a bright hot spot, about 17 % of the deck's direct light, at rows 0 to 48 and columns 784 to 864 of the 1280 x 720 image. Under ResampledLights(4, 2) its samples measure 21 % below EveryLight (median ratio 0.79 over its 490 brightest pixels). The rest of the deck measures -0.0009. With SpatialNeighbors = 0 the hot spot measures +0.009. The deficit does not depend on the combine: the biased combine of ter-nmbm's first build, the unbiased target with 1 / M, and the unbiased combine all measure -0.20 to -0.21 there, and a temporal reuse weighted by the visibility target too. A direct pass that shades with the spatial result but writes the pixel's own R0 to R1 measures -0.026 there (mean ratio 0.973, within the noise of 490 pixels). So the loss grows where R1, the spatial result, returns through the next frame's temporal reuse, which keeps 80 of 84 parts of the history: a loss of about 1 % per frame in the spatial step compounds to about 20 %. The math of each step is unbiased (ARCHITECTURE.md, 'ReSTIR DI'), so the cause is still unknown. Candidates to examine: a per-step loss in the near field of a lamp, where p-hat falls steeply across the 16-pixel disk; the M weights of R1 (the sum of 3 reservoirs, capped at 80); the previous texel of the temporal reuse. The diagnostics, their exports and the per-pixel dumps are in /private/tmp/nmbm3 (builds.txt, bias/, analysis/; 2026-11-03).


## Notes

**2026-11-04T02:13:20Z**

2026-11-03, the final build of ter-nmbm (b20714c0c, the unbiased combine with hashed random seeds): the hot spot measures -0.18 (mean ratio 0.80 over its 490 brightest pixels), the rest of the deck +0.0001, zero neighbors +0.023 there. The deficit does not come from the random numbers: far-apart stream constants measured -0.041 on the deck and the hashed seed -0.031. But correlated streams can swing it: streams 0x2 and 0x3 in the low bits of one seed word measured +2.6 in the hot spot (+0.46 on the deck), which the hashed seed removed. So the feedback loop at this hot spot amplifies any per-frame error of the spatial step. Exports and dumps: /private/tmp/nmbm3 (builds.txt lists diagA, diagB, diagF, diagG, diagH, diagI and diagJ).

**2026-11-04T02:58:15Z**

2026-11-03, from the review of ter-nmbm's continuation: one run gives one result per pixel, and the spatial reuse links the pixels' samples, so the per-frame results cannot yet tell a true bias from a heavy tail. A cheap test decides: average the hot spot's samples over many independent runs (an offset of the draw index or a salt in the seed), or over a long still of about 1,000 frames. If the mean ratio goes to 1, the cause is variance. If it stays at 0.8, the bias is real. The fix stage also counted the deck's sparkle: 0.21 % of the pixels have a sample above 4 times their exact light under the unbiased combine (1.85 % of the light, brightest 437 times), against 0.07 % with the biased combine and 0.13 % without neighbors. A heavy tail of this kind fits the variance reading.
