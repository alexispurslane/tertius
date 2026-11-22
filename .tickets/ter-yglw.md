---
id: ter-yglw
status: open
deps: []
links: []
created: 2026-11-22T21:13:04Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-6fka
tags: [rendering, experiment]
---
# Cost of one small environment face on the Mac's deck: 32 x 32 with small objects culled against today's 128 x 128

Experiment E3 of docs/research/lighting-system-review-2026-11-22/README.md, section 12. Time the existing environment face pass on the deck at a face size of 32 with a cull of objects below one texel, against today's face of 128. Design P needs about 216 faces for a cabin, so the face cost decides whether it fits.

## Acceptance Criteria

GPU time per face on the Mac and on the RTX 2000 Ada at both sizes, at least two runs each at a quiet load. Reading: above about 0.2 ms per face on the Mac with the cull, a refresh within 60 draws costs over 1.5 ms per draw, and design P does not fit as sketched.

