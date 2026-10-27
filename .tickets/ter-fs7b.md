---
id: ter-fs7b
status: closed
deps: [ter-a24f]
links: []
created: 2026-10-25T20:07:19Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering]
---
# Bounce light: read the environment map in each open direction

Known limit of docs/plans/screen-space-illumination.md: with the environment map on, the open share of the one estimate takes the map's mean light (its irradiance). The map is captured at the camera, so it already holds the nearby walls, and up to a quarter of a wall's light counts twice (b(1 - b) for a blocked share b), while as much far light is lost. Read the map in each open direction instead, so that the open share takes the light of the directions that it holds.


## Notes

**2026-10-27T14:58:14Z**

2026-10-27: merged; the branch took main (the hair fix) in 736fcb3d, tested there: Checked 9,306 total, 0 failed; Release 9,293, 0 failed; the merged main is the same tree. Coverage of the changed lines 72/72 (100%), branches 9/10. Cost +0.10 ms on demo-exit's deck and +0.14 ms in the surface cabin at 1080p. A hair receiver's own surface blocks nothing and reads no map light.
