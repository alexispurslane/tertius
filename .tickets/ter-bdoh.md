---
id: ter-bdoh
status: closed
deps: []
links: [ter-12xp, ter-fe63]
created: 2026-11-14T16:20:01Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# rect-lamps' glossy floor sparkles in every shown still but not in the long reference

**2026-11-25 · Superseded by ter-xzj7.** Use the old observations as controls after isolating which pass produces the filmed berth grain. A result on the old test scene alone would not settle the film defect.

Seen 2026-11-14 in the rect-lamps preview (/tmp/t7-3e22/preview/full-264.png): the floor's reflections of the lamps show bright single-pixel sparkle in main, Design X and every variant, at 132 and 264 draws, but not in the long reference (clamps off, captures 9 to 28). So the sparkle comes from the shown still's reflections, not from the light itself. Related: ter-gi1v (grain of the lobe fit's shared reflection rays on glossy floors at a low angle, open, blocked) and the reflection-noise research of tiunr-9b7v / tiunr-lmmx. First step: measure where the sparkle comes from (reflection rays per block, the reflection history's length and clamp, the specular filter) with a speck map of the reflections alone (probe signal 3).

