---
id: ter-5aak
status: closed
deps: [ter-k96l]
links: [ter-k96l]
created: 2026-11-01T23:38:29Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering, screen-space-light]
---
# The open share's ratio over 3 slices reads high in a crease where one blocked direction holds most of the map's light

Found by ter-k96l's build and review (2026-11-01), and recorded there as a Known limit in docs/plans/screen-space-illumination.md. S is a ratio of two sums over a texel's slices (the map's light along the open pieces over the map's light along all pieces), and the 6 denoise blurs average the ratios, not the two sums. Where one blocked direction holds most of the map's light, the mean of the 3-slice ratio is biased upward: 1.34, 1.27 and 1.13 times S at 64 slices per channel (+0.017 to +0.027 of the map's irradiance) in OpenShareCreaseTests' crease. The GPU crease test (S 0.80) shows almost no bias: 0.6618 against 0.6624. A candidate fix: blur the two sums and divide after the blurs, which costs more channels in the packed estimate (EstimatePacking, R32G32B32A32_UINT today). It needs a design with two options and the user's choice, a measure of the bias on demo-exit's creases, and its cost. Not part of epic ter-b2da: the acceptance views of ter-k96l are clean without it.


## Notes

**2026-11-03T12:35:13Z**

Closed on 2026-11-03 at the user's request, without its work merged: it belongs to the pre-temporal renderer (spatial reuse, the 12 ms performance push, or the old reflection and bounce pipeline), and epic ter-89xq, the temporal renderer, supersedes it. Its notes and any branch or worktree stay for reference.
