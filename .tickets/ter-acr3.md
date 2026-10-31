---
id: ter-acr3
status: closed
deps: []
links: [ter-a9hh]
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

**2026-10-31T20:31:39Z**

2026-10-31: workflow run 7dd68b8d-19e8-46f0-a0de-0b42b5df4dfb (version 869b0b07-3413-4b06-8707-a88a91bf6e91), branch ter-acr3-standard-grazing-edge from b6b39af5. Capture first; if the ragged edge shows on a standard surface, the builder stops at a fork with trial captures of the cut for every model and of an own-surface skip.

**2026-10-31T20:46:51Z**

2026-10-31, builder: no ragged edge on a normal-mapped standard surface, so no code change. Capture: scripts/hair-rugs.json with both rugs in the standard model (a temporary change, restored), default view rug-camera and low view low-camera, environment map on and off, half and full trace. On the default view the tilted rug's fade band (cosine 0 to 0.05, rows about 412 to 450) ends at a smooth, straight edge. Measure (band.py) over columns 690 to 949 and rows 380 to 511 of the default view, map off, against the same run with --reflections off. Per pixel it takes the largest difference of the three 8-bit channels. In each column, the first reflecting row is the first row whose difference exceeds 2 codes. A downward step is a row whose difference falls more than 3 codes below the row above. An isolated bright pixel exceeds both vertical neighbors by more than 6 codes. Standard model, at both resolutions: the first reflecting row spans rows 412 to 414 (a spread of 2 rows, the perspective slope), with 0 downward steps and 0 isolated bright pixels. Positive control, the hair rug with the hair cut removed: rows 413 to 436 (a spread of 23 rows), 22 downward steps and 15 isolated bright pixels, the ragged, speckled edge of ter-nwhv. In the low view the whole tilted rug reflects below itself (cosine 0 or below), so it holds no band. The skin model answers the same: reflectingPixel computes the dominant direction from the shading normal for the standard and the skin model alike, and only hair replaces that normal with the fiber normal. The standard result agrees with section 2 of screen-space-reflections.md: the origin offset of two pixel footprints along n_g keeps a ray that rises from a flat surface in front of it. The hair card is flat too, so the cause of the hits on the card is not established (ter-a9hh). Captures, diffs and the measure script: /tmp/ter-acr3 (README.md). docs/plans/screen-space-reflections.md section 7 and skin-and-hair.md (the standard-model test's entry) state the result.

**2026-10-31T20:55:05Z**

2026-10-31, fix stage after review: the note's step counts were spreads of the first reflecting row. The note above now gives band.py's three quantities and their thresholds (2, 3 and 6 codes): standard 412 to 414, 0 steps, 0 isolated pixels. Hair control 413 to 436, 22 steps, 15 isolated pixels (rerun on the saved images). Section 7 of screen-space-reflections.md now says that the origin offset of section 2 explains the standard result, that skin takes its ray as standard does, and that the cause of the hits on the flat hair card is not established. Filed ter-a9hh to find that cause.
