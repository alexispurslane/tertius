---
id: ter-7yit
status: closed
deps: [ter-of1p, ter-4vzc]
links: []
created: 2026-10-27T01:21:05Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# The shadow casters share the one interpolation of each instance per frame

Found in the merge of main into the prefilter profile (ter-of1p, 2026-10-26). SceneBatcher.CollectVisible interpolates each instance once per frame for the main view and every face of every map. CollectCasters (from the lights epic) still lerps and slerps every CastsShadow instance a second time for the shadow views, including instances that are not Visible. Make one placement per instance per frame that does not depend on Visible, and let the casters read it. It keeps the image. The benchmark's map-off case (about 1,045 us on main and on the branch) holds this second interpolation.


## Notes

**2026-10-27T03:27:13Z**

2026-10-26: merged into main (78369d36). Coverage of the changed lines: 52/52 lines, 34/34 branches. The full Checked suite passed on the merged main: 8,793 total, 0 failed. The batcher saves 146 to 313 us per Prepare of 20,000 instances (about 7.5 ns per caster for the second interpolation).
