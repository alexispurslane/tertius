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


## Notes

**2026-10-31T20:46:51Z**

2026-10-31, builder: no ragged edge on a normal-mapped standard surface, so no code change. Capture: scripts/hair-rugs.json with both rugs in the standard model (a temporary change, restored), default view rug-camera and low view low-camera, environment map on and off, half and full trace. On the default view the tilted rug's fade band (cosine 0 to 0.05, rows about 412 to 450) ends at a smooth, straight edge. Measure over columns 690 to 949 against a run with --reflections off: the first reflecting row lies between 412 and 414 (the perspective slope), with 2 downward steps over 3 codes and 0 isolated bright pixels, at both resolutions. Positive control: the hair rug with the hair cut removed gives 413 to 436, 23 steps and 15 isolated bright pixels, the ragged, speckled edge of ter-nwhv. In the low view the whole tilted rug reflects below itself (cosine 0 or below), so it holds no band. The cause of the difference between hair and standard is not established: the standard ray starts on a flat plane and leaves it, and the hair ray's direction and cone come from the strand. Captures, diffs and the measure script: /tmp/ter-acr3 (README.md). docs/plans/screen-space-reflections.md section 7 and skin-and-hair.md (the standard-model test's entry) state the result.
