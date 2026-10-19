---
id: ter-vbrx
status: closed
deps: []
links: []
created: 2026-10-19T01:22:33Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, engine]
---
# The capture sink: the renderer hands captured pixels to a sink

Core, Rendering, Host: ICaptureSink, CapturedImage, PngCaptureSink (today's SaveCapture, moved) and CaptureNextFrame(sink), used by the headless and the window capture. Design: docs/plans/blue-danube-demo.md, section 'Engine changes for the demo' (decided with the user 2026-10-18: video chosen on the command line, outlines as a view setting that a step changes, a numeric frame's outline is the sphere around its contents, one outline record per frame, the image test helper taken out of ter-rtxu now, colors and a legend card instead of labels). The last step updates ARCHITECTURE.md.

