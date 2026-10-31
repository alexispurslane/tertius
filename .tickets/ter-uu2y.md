---
id: ter-uu2y
status: in_progress
deps: [ter-6vhj]
links: []
created: 2026-10-31T17:37:40Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, frames]
---
# A planet's air shades the view from outside the bubble of its frame

Review finding 1 (high) of ter-6vhj, 2026-10-31, left as a fork. The atmosphere record comes only from the camera's chain of frames (RenderSnapshotBuilder.BuildAtmosphere: the nearest chain frame with a PlanetMedium, settled rule 5 of docs/plans/atmosphere.md). So when the camera leaves the bubble of the air's frame, the ground loses its whole-column haze and its sun tint in one frame. For mars-air the transmittance at its 300 km bubble edge is about 0.48, so the jump is large (ter-6vhj screenshots edge-inside.png and edge-outside.png). In the demo's Earth pattern the air lies on the turning ground frame, whose bubble ends at the stratopause (about 50 km), so the ground seen from orbit never shows haze or tint. A taller bubble does not hide the edge, and the turning ground frame cannot have a bubble that holds the orbits. ter-6vhj merges first with this limit documented (ARCHITECTURE.md and docs/plans/atmosphere.md, 'Not in this ticket'); this ticket removes it. Design: two designs in chat, then the user's choice, before anyone builds it.


## Notes

**2026-10-31T17:46:55Z**

2026-10-31, the user's choice of two designs: (1) The rule that names the air: the camera's chain first, as settled rule 5 of docs/plans/atmosphere.md says now; only when no chain frame has a PlanetMedium, the planet-medium frame that is densest at the eye: the smallest eye height in scale heights, h_e / H with h_e = max(0, |c| - R), among the frames whose poses the snapshot places. A tie goes to the lower frame slot, so every run picks the same air. (2) One air per view: the snapshot keeps one atmosphere record, and no shader changes; a second airy planet in the same view gets no haze. The haze, the tint, the sky and the day factor stay as ter-6vhj built them: they already apply only along the part of a ray that crosses the air, and the sky takes the density at the eye, so space stays black. The rejected options: the densest air always (one rule, replacing rule 5), every air in view up to 4 (a list of records), and moving the air to a frame with a larger bubble (the Earth pattern's air turns with the ground, whose bubble must end below orbit). Acceptance: mars-air's edge-inside and edge-outside views (295 km and 305 km) differ by no more than the change of height explains; the Earth pattern's ground seen from parking orbit shows its haze; the docs ('Not in this ticket' in atmosphere.md, ARCHITECTURE.md) lose the limit and state the rule.

**2026-10-31T18:09:03Z**

2026-10-31: launched through the workflow MCP (ultracode-ticket-completion, version 869b0b07-3413-4b06-8707-a88a91bf6e91), run 85be4f8d-5943-4e17-a7d4-ed28bfe29f28, branch ter-uu2y-air-past-bubble from f9805d42.

**2026-10-31T18:16:36Z**

2026-10-31, implementation choices. (1) The candidates and their poses: PrepareFramePoses already places every frame of the tree (relCurrent by dense index), so the fallback reads those poses and computes no new ones. It loops over the dense frames with frames.Medium; a numeric frame has no medium. (2) The new private RenderSnapshotBuilder.AtmosphereFrame returns a nullable value tuple (frame, medium): the chain walk first, then the smallest h_e / H in doubles from relCurrent less the render origin, ties to the lower FrameHandle.Index (the frame slot, not the dense index). It allocates nothing. (3) The old test ACameraWhoseChainHoldsNoPlanetMediumSeesNoAir asserted the old rule (Mars off the chain gave no air); AWorldWithoutAPlanetMediumGivesNoAir replaces it. (4) The tie test frees a frame slot and reuses it, so the lower slot is the later frame, and asserts that precondition. (5) The Earth-pattern test builds its own turning ground with a SpinMotion in a test world; the demo's Earth is unchanged.

**2026-10-31T18:16:36Z**

2026-10-31, visual acceptance (headless mars-air at 1280 x 720, base f9805d42 against this branch). day, dusk, above, terminator and edge-inside are identical byte for byte. edge-outside changed: it keeps the haze and the tint. With the script's 60 ticks per edge view, the pair still differs by a mean of about 17/255 per channel, because the auto exposure has not settled (-7.75 against -8.11 stops). With 600 ticks each, both settle at -8.317 stops and differ by a mean of 0.2/255 per channel; the only pixels that differ by more than 12% lie along the limb, which drops a few pixels because the eye is 10 km higher.

**2026-10-31T18:47:12Z**

Review fix stage, 2026-10-31. (1) The mars-air scene comment no longer says that the record goes past the bubble's edge; it names rule 5's fallback. (2) The densest-air test now puts the nearer surface in meters at more scale heights (Earth-like 80 km = 9.4 H, Mars 100 km = 9.0 H, Mars in the higher slot). Two new tests: both surfaces 1e9 m away, where e^(-h/H) is 0 in a double, and an eye below both surfaces, where the lower slot wins. Each was watched failing on its wrong rule (meters, densities, no clamp) and restored. A new allocation test runs 100 ticks whose build chooses an air off the chain and allocates 0 bytes. (3) scripts/mars-air.json runs each edge view 600 ticks, so the exposure settles; the first four captures do not change. (4) The haze pass now runs in every view of a world with a hazed planet. ARCHITECTURE and the plan state this, and ticket ter-bgpv (under ter-pq5y) measures it and can skip the pass. (5) PlanetMedium.HeightAt(distance) = max(0, distance - R) is the one C# copy of h; DensityAt, PaintedAtmosphere.Prepare and AtmosphereFrame call it, and the IMPORTANT note in atmosphere.glsl names it. (6) The tie test's comment says that a frame without air frees the slot.

**2026-10-31T19:13:29Z**

2026-10-31, integration: run 85be4f8d ended ready for integration (build 8136a3c0, one review with 6 findings, fix d77e4f9d). Hand mutations of the selection rule (RenderSnapshotBuilder.AtmosphereFrame): the chain skipped, the tie to the higher slot, and the most scale heights winning were killed by their tests; a rule that measured each air from the origin of the camera's frame instead of the eye survived, because every test put the eye at that origin. Commit 8845797b adds TheDensestAirIsMeasuredFromTheEyeNotFromTheOriginOfTheCamerasFrame, which kills it. Coverage of the changed lines (Checked, against f9805d42): 29/29 lines and 16/16 branches.
