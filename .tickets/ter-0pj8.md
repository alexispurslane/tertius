---
id: ter-0pj8
status: in_progress
deps: []
links: []
created: 2026-11-02T12:51:56Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, environment-map]
---
# The map's ray search in C#: MapSearch.Hit against an inverse distance field

Ticket 1 of ter-jw7g's design (docs/plans/environment-map-distances.md, 'Ticket 1' and 'The search'). MapSearch.Hit and IInverseDistanceField: the march of 8 samples, the 3 regula falsi steps, the cone's level and the reach, against analytic test fields (the sky, a plane, a box before a wall, a pole before a wall, a far planet, a sphere room). Owns src/Tertius.Rendering/Shading/MapSearch.cs and tests/Tertius.Rendering.Tests/MapSearchTests.cs. Acceptance: tests 1 to 12 of the design pass in Checked and Release, each red first; Samples, SecantSteps and ConeWidthPerAlpha are named once.


## Notes

**2026-11-02T12:54:15Z**

2026-11-02: launched as workflow run 06debe04-2619-49f9-a711-de3c1095d8dc, version 869b0b07-3413-4b06-8707-a88a91bf6e91 (build, review, fix), branch ter-0pj8-map-search, base 6c40880fa.

**2026-11-02T13:39:51Z**

2026-11-02: run 06debe04 built MapSearch (72a011cd9, 17 tests green) and stopped at a fork: the march starts at t = 1/8, so a hit nearer than about |x|/7 is lost, against the design's decision C and 'The cone at each sample'. The orchestrator settled it inside the chosen design: one more sample at t0 = 1/64 before the uniform march (the nearest place where a level-0 texel at W = 128 can see a separate surface). It changes no hit that the uniform march finds and adds the bracket [t0, 1/8]. Reads per pixel: 12. Rejected: quadratic spacing (coarse far end) and a test at t = 0 (a real root on the shaded point's own surface). Continuation: workflow run 81bbe30d-fbbe-4df5-862e-8dd96173cd40, same worktree.
