---
id: ter-th4h
status: in_progress
deps: []
links: []
created: 2026-10-25T15:53:10Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [rendering, tools]
---
# Write the exposure of each frame in a headless run

A headless run writes no exposure readings: gpu-timings.json holds only the device, the size and the passes. So the observer of the exposure films had to guess the adaptation from pixel brightness. Write RenderStats.Exposure.Stops per drawn frame, for example as a column beside the GPU timings, so that a film's adaptation shows as numbers.

## Acceptance Criteria

A headless film writes the exposure in stops of every shown frame, and a test reads it back.

