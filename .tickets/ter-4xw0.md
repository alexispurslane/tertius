---
id: ter-4xw0
status: in_progress
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


## Notes

**2026-11-01T00:00:59Z**

2026-10-31, the user's choice of two designs: a value margin that spans tiers. A holder (a light whose entity holds layers in ShadowTiles.records, the store of the last submitted frame) ranks for shadow tiles as if its light were twice as strong: its rank irradiance times 2, and its main-view tier computed at its reach times sqrt(2), because reach = sqrt(peak / cutoff), so twice the light gives sqrt(2) the reach. So a challenger takes a holder's tiles only when it has more than twice the holder's irradiance in the same tier, or stands in a higher tier than the holder reaches even at sqrt(2) its reach. The grown tier is an input of the shadow ranking only: PreparedLight.Tier, which decides the direct pass's light list, does not change. The holders come from records, so no new stored state. Rejected: a time margin of N drawn frames (a count in each record, every handover waits N frames, and a swing slower than N frames still swaps each time), and a margin inside one tier only (a tier change, which follows the exposure through each light's reach, would still hand the tiles over at once, the cause that the ticket names). This replaces the deferral of settled fork 2 in docs/plans/local-light-shadows.md.
