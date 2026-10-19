---
id: ter-p51r
status: closed
deps: [ter-721k, ter-84iw]
links: []
created: 2026-10-19T01:22:33Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, engine]
---
# The overlay pass that draws frame outlines

Rendering: the Overlay stage, OutlinePass and its shaders (2-pixel rings drawn in the vertex shader, dimmed to 30% behind geometry), RenderStats.Outlines, the reviewed snapshot test, and the measured cost. Design: docs/plans/blue-danube-demo.md, section 'Engine changes for the demo' (decided with the user 2026-10-18: video chosen on the command line, outlines as a view setting that a step changes, a numeric frame's outline is the sphere around its contents, one outline record per frame, the image test helper taken out of ter-rtxu now, colors and a legend card instead of labels). The last step updates ARCHITECTURE.md.

