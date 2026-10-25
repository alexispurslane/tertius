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


## Notes

**2026-10-25T21:32:45Z**

2026-10-25: the builder found that the host alone cannot match readings to shown frames (the renderer collects a frame's exposure only when its slot is reused, and a metering draw's reading overwrites the shown one). The orchestrator settled its fork as design A: every fence wait collects, so after a capture RenderStats describes the captured frame, by auto-exposure.md's 'after the slot's fence wait', one store for the exposure, and stored state describing the last finished step. Design B (the capture carries its own reading) would give the engine two readings that can disagree. The readings go to exposure.json beside gpu-timings.json.
