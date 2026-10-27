---
id: ter-k1ne
status: open
deps: [ter-u6nc, ter-zw26, ter-a24f]
links: []
created: 2026-10-26T19:32:56Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, materials]
---
# Skin and hair Part 4: the shading target and hair shading

Part 4 of docs/plans/skin-and-hair.md: the sixth G-buffer target (R8G8B8A8_UINT) and hair's fiber shading. Waits for Parts 2 and 3 and the bounce light epic's merge.


## Notes

**2026-10-27T06:51:34Z**

Open fork from the Part 4 review (finding 1): the merged bounce light's own-surface rule blocks the sectors below the receiver's geometric plane, and for hair that plane is the card. Under VisibilityBitmaskLight.Default, TwoCardsWithOneStrandAndDifferentNormalsShadeAlike shows the 40° card at (0.224, 0.095, 0.049) against (0.240, 0.101, 0.052) facing, and TheHairsAmbientLightIsTheSourcesAtTheFiberNormal(false) shows (0.154, 0.046, 0.013) against (0.167, 0.050, 0.014). Option A: the sectors pass reads the shading target and gives a hair pixel no own-surface sectors, or n_f's plane. Option B: keep the card's plane as the hair sheet's self-occlusion and say so in the docs. Waiting for the user.
