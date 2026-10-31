---
id: ter-nwhv
status: in_progress
deps: []
links: []
created: 2026-10-27T13:14:57Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, hair]
---
# Hair reflections: a normal map that tilts the strand off the card lets the surface fade cut the reflection

Found by the review of ter-qsfz (2026-10-27). A hair pixel's dominant reflection direction lies between its fiber normal and the eye's direction mirrored about the strand. Without a normal map the strand lies in the card, so both directions have a positive cosine with the card's normal and the reflections' surface fade (below a cosine of 0.05 with the card's normal) touches only cards within 3 degrees of edge-on. A normal map tilts the strand off the card (decision 28 of docs/plans/skin-and-hair.md): the mirrored direction's cosine with the card's normal is then V.n_c - 2 (V.T)(T.n_c) up to sign, which can fall below 0.05 or below 0 for cards far from edge-on, for example a tilt of 10 degrees with V.n_c = 0.3 and V.T = 0.7. The fade then cuts their reflection. Needs a test with a normal-mapped hair card and a decision on the rule.


## Notes

**2026-10-31T14:40:37Z**

2026-10-31: the user chose design 2, keep the fade. A hair ray that points below its card (cosine with the card's normal below SurfaceFadeCosine) traces nothing and keeps the reflection that the ambient pass gave it, the environment map's. The work: a test with a normal-mapped hair card that pins this fallback (the pixel keeps the ambient specular, never black), and docs/plans/skin-and-hair.md and ARCHITECTURE.md state the rule in place of the known limit. Declined: design 1, where the own card is no surface to SSR (rays below the card trace and skip hits on their own card through ter-3yy5's card number); it can come later if a normal-mapped hair asset looks patchy. Waits for a free builder slot.

**2026-10-31T17:36:20Z**

2026-10-31, builder: the code already kept the fade, so no shader change. A hair ray at a cosine of 0 or below with its card starts no ray and keeps the ambient pass's reflection; between 0 and SurfaceFadeCosine the fade blends. HairReflectionRenderTests pins this as lock-in tests (break-the-line checks: no fade, a fade against the fiber normal, a pixel fade without the surface fade, and hair that starts no ray each fail a test; dropping only the early return at fade 0 is equivalent, because the delta is multiplied by a fade of 0). The docs state the rule as decision 35 of docs/plans/skin-and-hair.md. Added the demo scene hair-rugs and scripts/hair-rugs.json. Finding, an open fork: in the capture, rays inside the fade's range graze the card and hit it a few pixels from their start, so the screen's reflection ends at a ragged edge near a cosine of 0.02 to 0.03 (measured on the test floor; the edge shows in hair-rugs). A trial in which a hair ray below SurfaceFadeCosine traces nothing (the note's literal wording) gives a clean straight edge at the cosine 0.05 instead. The user chooses: keep the fade with the ragged edge (now in Known limits), the clean cut at SurfaceFadeCosine for hair, or design 1.
