---
id: ter-a9hh
status: in_progress
deps: []
links: [ter-acr3]
created: 2026-10-31T20:54:22Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, reflections, hair]
---
# Reflections: find why a grazing ray of a tilted hair card hits its own card despite the origin offset

Found by the review of ter-acr3 (2026-10-31). On the tilted hair rug of hair-rugs, with the hair cut of decision 35 (docs/plans/skin-and-hair.md) removed, rays whose cosine with the card's geometric normal lies between 0 and SurfaceFadeCosine (0.05) hit their own card a few pixels from their start: the first reflecting row spreads over rows 413 to 436, with 22 in-column downward steps and 15 isolated bright pixels (ter-acr3's note). A standard copy of the same rug, with the same 10-degree normal map, shows none of this: rows 412 to 414, 0 steps, 0 isolated pixels. Section 2 of docs/plans/screen-space-reflections.md argues that the origin offset of two pixel footprints along n_g keeps a ray that rises from a flat surface in front of it, and the standard capture agrees. The hair card is flat too, so that argument does not explain the hair hits. Candidates to test: the hair ray's direction and cone come from the fiber normal n_f, not from the shading normal, so its cone can reach below the card while its dominant direction stays above it; the card's depth or geometric normal in the G-buffer can differ from the plane that the trace intersects (alpha-tested strands, the card's thickness); the half-resolution trace and its upsample. The work: find the cause with a capture or a test, and state it in section 7 'The fades' of screen-space-reflections.md. The cause decides whether the cut of decision 35 or design 1 (an own-surface skip through ter-3yy5's card number) targets the real mechanism. Not required now: the cut hides the edge on every shipped scene.

