---
id: ter-84iw
status: in_progress
deps: []
links: []
created: 2026-10-19T01:22:33Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, engine]
---
# Frame outlines in the render snapshot

Core and Simulation: FrameOutline (a union like RenderLight), RenderSnapshot.Outlines, World.ShowFrameOutlines, the builder's rule for each kind of frame (no outline for a root frame, the bubble ellipsoid for a semantic frame, the contents sphere for every numeric frame), and its benchmark. Design: docs/plans/blue-danube-demo.md, section 'Engine changes for the demo' (decided with the user 2026-10-18: video chosen on the command line, outlines as a view setting that a step changes, a numeric frame's outline is the sphere around its contents, one outline record per frame, the image test helper taken out of ter-rtxu now, colors and a legend card instead of labels). The last step updates ARCHITECTURE.md.

