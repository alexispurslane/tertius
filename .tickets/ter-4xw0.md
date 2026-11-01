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

**2026-11-01T00:02:39Z**

2026-10-31: launched through the workflow MCP (version 869b0b07-3413-4b06-8707-a88a91bf6e91), run 571f8633-d7b0-440e-b938-8f4c989b0c28, branch ter-4xw0-shadow-tile-hysteresis from e2a8a51a.

**2026-11-01T00:49:12Z**

2026-10-31: run 571f8633 stopped in its fix stage at a fork (findings 1 and 2 of the review). SceneBatcher.Reaches drops a light from the light list when its own reach misses the view and no drawn face keeps it, before ShadowTiles ranks it. So a point-lamp holder whose reach falls just short of the view loses its tiles at once, and the margin does not span the main-view edge for point lamps; and a holder that only a drawn face keeps can rank at its grown tier, which makes the main view's shadow slots depend on the face schedule (environment-map.md decision 20). Branch head 3927908c holds the build and the fixes of findings 3 to 6. Waiting for the user's choice between A (accept the limit, rank a FaceOnly holder at FaceOnly) and B (keep a holder in the light list while sqrt(2) its reach meets the view).

**2026-11-01T00:55:57Z**

2026-10-31, fork settled by the orchestrator: design A. The user left the choice to the orchestrator ('pick something reasonable'). A has less code and no new coupling, and no checked-in scene shows the pop at the view's edge, which is the project's rule for leaving a feature out ('leave out a feature that is not needed yet', and settled fork 2's original 'hysteresis waits until a scene shows the pop'). So: a holder whose own Tier is FaceOnly ranks at FaceOnly (environment-map.md decision 20 stays as it is); a holder that the batcher drops from the light list (its own reach misses the view and no drawn face keeps it) loses its tiles at once; the margin spans tiers only inside the list, which today means a spot lamp across AxisMissesView and InView. The docs say so. Design B, keeping a holder in the light list while sqrt(2) its reach meets the view, is the known follow-up if a scene shows point-lamp shadows popping at the view's edge.

**2026-11-01T00:56:19Z**

2026-10-31: continuation run 57867dac-c29a-4e0e-9bd7-b8a0047b81b1 (version 869b0b07) builds design A on the preserved worktree (HEAD 3927908c), then goes through review and fix.
