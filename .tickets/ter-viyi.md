---
id: ter-viyi
status: open
deps: [ter-mu7k]
links: []
created: 2026-10-11T17:33:21Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-dtw2
tags: [host, rendering]
---
# Encode captured images off the render thread, so a capture in the window does not freeze it

A capture step in the window pauses the simulation while the render thread draws the tick and saves the PNG. Measured on the drop-box scene at 1280 by 720: 109 to 154 ms per capture, mostly PNG encoding in VulkanRenderer.SaveCapture, which runs inside Render. The window's pacing slips after the stall (ter-mu7k), so the motion goes on smoothly, but the window freezes for that time at each capture.

Design to decide: copy the read-back pixels and encode and write the PNG on a worker, so that Render returns after the copy. Today Render returns after the PNG is on disk, and the renderer tests rely on that (Gpu.RenderAndCapture), so the change needs either an option or a way to wait for the file, for example a task that CaptureNextFrame returns.

## Acceptance Criteria

A capture in the window holds the simulation thread for less than one tick at 1280 by 720, and the captured PNG is the same as before. The renderer tests still find the file when they need it.

