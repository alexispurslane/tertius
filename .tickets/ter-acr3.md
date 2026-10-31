---
id: ter-acr3
status: open
deps: []
links: []
created: 2026-10-31T18:37:24Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, reflections]
---
# Reflections: a grazing ray of a normal-mapped standard surface can hit its own surface inside the surface fade

Found by the review of ter-nwhv (2026-10-31). Inside the surface fade's range (a cosine of the dominant direction with the geometric normal from 0 to SurfaceFadeCosine, 0.05), a ray grazes its own surface and can hit that surface a few pixels from its start. On hair this ended the screen's reflection at a ragged, speckled edge, and ter-nwhv cut it for hair only: a hair pixel below SurfaceFadeCosine starts no ray (docs/plans/skin-and-hair.md, decision 35). A pixel of any other model keeps the fade, so the same mechanism stays open for every normal-mapped surface of the standard or skin model. No demo asset has such a normal map today, but imported glTF assets will. The work: capture a normal-mapped standard surface in the grazing band, for example the standard floor tilted 10 degrees of AStandardPixelWhoseRayGrazesItsSurfaceTakesTheScreensReflectionInTheShareOfTheSurfaceFade (HairReflectionRenderTests), or the tilted rug of hair-rugs in the standard model. If the ragged edge shows, decide whether design 1 of ter-nwhv, an own-surface skip generalized from the card number of ter-3yy5 to any surface, replaces the surface fade for every model, or whether the hair cut extends to every model.

