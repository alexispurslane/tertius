---
id: ter-3att
status: closed
deps: []
links: []
created: 2026-10-25T16:58:26Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-s9cv
tags: [rendering, lights]
---
# Lights: the C# light math

Part 1 of docs/plans/local-light-shadows.md on branch ter-s9cv-light-shadows, "Order of the work". LightUnits, FarShadows, PreparedLight, LightSpread, LightShadow, OccluderRange and ShadowCaster, all new files.

## Acceptance Criteria

The part's tests pass in Checked and Release, with hand mutations of each new test, as the part's "Done" says.


## Notes

**2026-10-25T21:03:19Z**

Built, reviewed and fixed on the epic branch ter-s9cv-light-shadows on 2026-10-25: a8533208 (part 1), with the review's fixes in 434b0131 (Cone.Of with a fade that scales below 0.014 rad, PreparedLight.RadiantIntensity in doubles, FarShadows ranking over every far light). The epic branch merges into main with Part 7; coverage runs after Parts 4 and 6, as the design says.
