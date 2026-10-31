---
id: ter-uu2y
status: open
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
