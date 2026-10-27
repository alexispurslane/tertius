---
id: ter-t87o
status: closed
deps: []
links: []
created: 2026-10-27T03:43:35Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering, lights]
---
# A face-only light's tiles can overwrite a tile record that a main-view light would reuse

Found while building ter-mgap (2026-10-26). The shadow budgets now rank the main view's lights first, so a light that only a face of the environment map keeps never takes a tile that a main-view light would have had in the same frame. The tile cache still carries history: a layer that such a light takes loses the record that it held. When a main-view light that lost its tiles earlier gets them back, ShadowTiles.Take gives it the lowest free layers; without the face-only light, one of them could still hold its own old record and be reused (ShadowTiles.IsStale). With it, the main-view light draws its tile again. A fresh tile and a reused one differ by under a quarter of a texel (ShadowTiles.StaleTexels), so the main view's pixels can differ slightly from a run without the map, against docs/plans/environment-map.md decision 20. Allot also gives new layers in list order, so a face-only light earlier in the list takes lower layers first. Decide whether this matters; a fix could allot the main view's lights first and keep face-only lights off layers that hold a main-view light's record.


## Notes

**2026-10-27T04:21:39Z**

Closed as not a bug by the review of ter-mgap (2026-10-27). ShadowTiles.Drew clears the record of every layer that no light of the submitted frame holds, so a light that lost its tiles holds no record afterwards and never reuses one by chance. Allot runs Keep for every light before any Take, so a light that only a face keeps can take only layers whose record belongs to a light with no tiles this frame, records that are lost with or without it. A light whose tile count changes changes its aim kind (ConeAim against FaceAim), so IsStale finds every old record stale whichever layers it gets. So whether a main-view light reuses or redraws a tile depends only on its own grants, which the ranking keeps.
