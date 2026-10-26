---
id: ter-mgap
status: open
deps: []
links: []
created: 2026-10-26T20:22:28Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [rendering, lights]
---
# Lights that only an environment face keeps can take the shadow budgets of lights in the main view

Found by the review of the merge of main into the lights epic (2026-10-26). FarShadows.Choose and ShadowTiles.Update rank every light of the frame's list by LightUnits.EyeIrradiance, whichever view keeps it. A light that only a drawn face of an environment map keeps can therefore take a cascade set or atlas tiles that a main-view light would have had, and the main view's pixels change. ARCHITECTURE.md now says so; docs/plans/environment-map.md decision 20 still claims that such a light adds exactly nothing to the main view's pixels. Decide whether the main view's lights rank first (a design question for the user if it changes what is shadowed), and fix decision 20's text either way.

