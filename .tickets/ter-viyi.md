---
id: ter-viyi
status: open
deps: [ter-mu7k]
links: []
created: 2026-10-11T17:33:21Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [host, rendering]
---
# Encode captured images off the render thread, so a capture in the window does not freeze it

A capture step in the window pauses the simulation while the render thread draws the tick and saves the PNG. Measured on the drop-box scene at 1280 by 720: 109 to 154 ms per capture, mostly PNG encoding in VulkanRenderer.SaveCapture, which runs inside Render. The window's pacing slips after the stall (ter-mu7k), so the motion goes on smoothly, but the window freezes for that time at each capture.

Design to decide: copy the read-back pixels and encode and write the PNG on a worker, so that Render returns after the copy. Today Render returns after the PNG is on disk, and the renderer tests rely on that (Gpu.RenderAndCapture), so the change needs either an option or a way to wait for the file, for example a task that CaptureNextFrame returns.

## Acceptance Criteria

A capture in the window holds the simulation thread for less than one tick at 1280 by 720, and the captured PNG is the same as before. The renderer tests still find the file when they need it.


## Notes

**2026-10-11T17:40:12Z**

Second change, found while answering the user (2026-10-11): the simulation thread waits longer than it needs to. WindowPlayback.Capture calls captures.RequestAndWait, which returns only after the PNG is on disk. The simulation needs to wait only until the render thread has taken this tick's snapshot with TryAcquireLatest. The triple buffer never gives the reader's buffer back to the writer, so after that handover the simulation can tick and publish newer snapshots while the render thread draws and saves the old one. So CaptureMailbox gets a signal at the handover, and Capture waits for that instead of Done. That cuts the simulation's pause from 110 to 150 ms to under one display frame. Alone, it does not remove the freeze from the screen: the render thread still draws nothing while it encodes, and then shows a jump. Both changes together remove the pause: the simulation waits only for the handover, and a worker encodes the PNG.

**2026-10-19T11:39:20Z**

The capture sink of ter-vbrx (2026-10-19) answers the open question. A sink that copies the pixels in Take and encodes the PNG on a worker lets Render return after the copy. SaveCapture is now PngCaptureSink. The renderer tests capture into an in-memory sink and no longer need the file.
