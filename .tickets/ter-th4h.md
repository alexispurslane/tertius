---
id: ter-th4h
status: closed
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

**2026-10-25T22:12:01Z**

Merged into main on 2026-10-25 (28c8fce0, with main merged in at b710cf2b). Design A: every fence wait collects, so after a capture RenderStats describes the captured frame, and ExposureResources.Collect keeps only a newer frame. A headless run writes exposure.json beside gpu-timings.json: one entry per shown frame, {frame, capture, stops}. Coverage of the changed C# lines: 91 of 94, branches 15 of 18. The uncovered lines are the two UnreachableException guards (WriteExposures' count check and FrameReadings.Add's missing reading) and the video folder's IOException filter, which moved with the RunHeadless split. The three Physics allocation tests fail under coverage, as always. No screenshots: a captured frame's pixels do not change.
