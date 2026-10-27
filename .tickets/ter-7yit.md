---
id: ter-7yit
status: open
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

