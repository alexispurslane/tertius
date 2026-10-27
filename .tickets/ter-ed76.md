---
id: ter-ed76
status: open
deps: []
links: []
created: 2026-10-26T21:23:47Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, performance, tools]
---
# Make xctrace counter traces of 120 frames reliable, or document the limit

Found in ter-izsu (2026-10-26): tools/gpu-trace.sh with the GPU counters on 120 frames of demo-exit's deck kept only 3 of the 120 frames, or hung while xctrace saved the trace. A trace of 21 frames worked and gave the limiters. Traces with --no-counters of 120 frames work. Find the cause (trace size, the counter instrument's buffer, the shader timeline that it records too) and either fix the script, for example by tracing counters in shorter windows and merging them, or document the largest window that works in tools/README.md, 'GPU trace'.

## Acceptance Criteria

A counter trace of the frames that Part 3 of the performance epic needs completes in three runs in a row, or tools/README.md names the window that works and the script refuses or warns past it.


## Notes

**2026-10-26**

A trace with --no-counters hung the same way once (ter-izsu's review fixes, at a load of 10 to 22): the demo finished its 120 frames and wrote gpu-timings.json, and xctrace stayed at "Stopping recording..." for over 25 minutes with a run.trace of 40 KB, until it was killed. So the hang is not only the counters'.
