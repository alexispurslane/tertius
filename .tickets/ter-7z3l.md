---
id: ter-7z3l
status: closed
deps: [ter-jxv9]
links: []
created: 2026-10-25T20:07:17Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-a24f
tags: [rendering]
---
# Bounce light: the lighting split

Part 2 of docs/plans/screen-space-illumination.md: the direct pass, the ambient pass, the direct image and the RGBA estimate target.


## Notes

**2026-10-26T01:01:11Z**

Done on the epic branch ter-a24f-ssgi on 2026-10-25: the merge of main (55c43cae), Part 2 (b36a8945) and the fix of the review (aee6da8b). The lighting pass is a direct pass (direct.frag, the direct image at binding 9) and an ambient pass (ambient.frag), and the RGBA16F estimate target at binding 7 replaces the R8 occlusion target. With the crease shading on, the image differs by at most 1 of 255: the half-float estimate keeps V = 0.99951 where R8 rounded it to 1. Reference images within 0.1 FLIP (largest 0.0492, puffs-lit). Checked 7,894 and Release 7,882 passed, 0 failed.
