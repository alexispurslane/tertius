---
id: t3-5jws
status: open
deps: []
links: []
created: 2026-11-10T16:55:26Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# FSR 3.1's history smears the direct light of moving lamps and figures: blotches and flicker in four films

Found by the split by light signal of ter-6k3k (docs/plans/renderer-finish.md, "The split by light signal"). In the films lamp-hall-4-spot-moving, lamp-hall-16-moving, lamp-hall-64-moving and rigs-crowd, the direct light carries the error against a still of the same state: blotch scores 0.058, 0.025, 0.038 and 0.029 at the middle frame, and flicker 0.0059, 0.0075 and 0.0189 in the three lamp halls. The renderer's direct light has no history. A split of lamp-hall-4-spot-moving without FSR 3.1 (split --native, 1280 x 720, --upscaling off) gives a direct blotch of 0.000 and a direct flicker of 0.0003. So FSR's own history causes the error. Guided diffuse rays, the history cut and the thinner filter do not touch it. Next: find what FSR receives for a moving light's change (reactive mask, transparency and composition mask, motion of the lit pattern), as AMD's docs for FSR 3.1 describe, and measure the fix with the split. Results: /private/tmp/tertius-split-20261109/results/split/motion-lamp-hall-4-spot-moving*.json.

## Acceptance Criteria

The split of the four films gives a direct-light blotch of at most 0.04 and a direct-light flicker of at most 0.006 with FSR 3.1 on.

