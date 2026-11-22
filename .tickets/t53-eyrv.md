---
id: t53-eyrv
status: in_progress
deps: []
links: [ter-186q]
created: 2026-11-21T21:17:26Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering]
---
# The walk can miss a slab of no depth where a descent enters it at its own w: 8 of a million random rays

Found by ticket t1-7b4x. Under the layer as the slab, a sheet that faces the eye or lies edge-on holds a slab of no depth at the finest level: its front w equals its back w (an edge-on plane, or a plane with no change over its square). The walk (walkScreen in screen_walk.glsl, ScreenRay.Walk in C#) descends into such a pixel at the entry of its parent's slab. When the pixel's w is the parent's front w, that entry lies exactly at the pixel's w. The ray's w at the entry, startW + stepW * s, can round one unit in the last place behind it. The pixel's test then calls the ray behind the slab, and the walk passes the pixel.

Evidence (C# copy): ScreenWalkTests' million random rays over RoomScene with every rectangle a sheet (ScreenTesting.SheetShares). The march over the flat slabs hits a slab of no depth on 4,559 rays. The walk misses 8 of them, and every one of the 8 disagreements is of this kind. Ray 6763 of seed 20261104: the march hits pixel (67, 34), slab (0.21049231, 0.21049231), an edge-on box face. The walk passes it and hits (67, 31). With solid slabs (a back share of 0.75 everywhere) the walk and the march agree on every ray.

The rounding of the entry's w scales with the ray's start w, not with the slab's w, so a tolerance in the pyramid's build cannot bound it. A fix belongs in the walk: for example, a pixel that a solved descent entered counts the entry's w as inside the parent's slab. The GLSL walk and its C# copy change together. The skipped test ScreenWalkTests.RandomRays.OnSheetSlabsTheWalkFindsTheHitOfTheMarchOnAMillionRandomRays turns on when the fix lands. Effect on the image: a ray in about 125,000 that should meet a facing or edge-on sheet passes behind it. No still showed it.


## Notes

**2026-11-22T13:53:36Z**

2026-11-22, the line orchestrator. This bug and ter-186q are one class: the walk derives the ray's inverse depth at a solved entry again from s, with a rounding larger than a thin slab. One ticket lead fixes both in the walk. The design is the note of 2026-11-22 in ter-186q.
