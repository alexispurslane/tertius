---
id: ter-4xw0
status: open
deps: [ter-19fm]
links: []
created: 2026-10-31T17:13:54Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, shadows]
---
# Shadow tile hysteresis: a tile changes owner only past a margin

The user chose on 2026-10-31 to build the hysteresis that docs/plans/local-light-shadows.md deferred (settled fork 2, option B: a light that held tiles keeps them until a light without tiles has twice its irradiance at the eye). Since ter-ox1a, the main-view flag that ranks the tiles follows the exposure, so a shadow can switch lamps when the exposure changes, as well as when the camera turns. Hysteresis stops back-and-forth flicker at the budget's edge; it does not stop a single handover to a much stronger lamp. Design first: two designs in chat for the user (for example a margin on the ranking value against a margin in time), then build. Comes after the cone ticket, so that every remaining handover involves lamps that light the view.

