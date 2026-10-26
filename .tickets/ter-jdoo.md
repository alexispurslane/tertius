---
id: ter-jdoo
status: closed
deps: []
links: []
created: 2026-10-25T23:03:08Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [rendering]
---
# Find why the environment prefilter takes 1.2 ms, and cut it

Part 6 of ter-nl5u measured the environment prefilter pass at 1.16 to 1.35 ms per drawn frame on the M1 Max at the default SceneEnvironmentMap(128, 1), in every scene (demo-exit, demo-surface, mirror-balls). docs/plans/environment-map.md, "Cost", expected 0.1 to 0.2 ms for its work: the mips by solid angle, 2.1 million trilinear samples of the specular levels and the irradiance projection. ARCHITECTURE.md, "The environment map", "Cost", has the tables. The pass runs in every drawn frame, so it is most of the map's 1.6 ms. Profile the pass's dispatches (downsample, prefilter levels, irradiance) to find which one takes the time, before any change. Candidates to test, not decided: the single-workgroup irradiance dispatch reading 768 texels per invocation, the barriers between the mip levels, the prefilter levels that only change where a drawn face's lobes reach.

## Acceptance Criteria

A measurement names the dispatch that takes the time, and the pass's cost on demo-exit at 1080p falls toward the design's estimate, or the design doc records why it cannot.


## Notes

**2026-10-26T12:14:56Z**

Closed as a duplicate of ter-of1p, which profiles the environment map's prefilter (2026-10-26).
