---
id: ter-9hie
status: in_progress
deps: []
links: []
created: 2026-10-31T21:34:03Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: show each pass's time alone beside its busy time

Found by ter-0y0z, 2026-10-31. tools/gpu-trace.py reports a pass's busy time as the union of its own encoders' intervals (line 254). An interval that a pass shares with another pass counts in the busy time of both, and passes.md has no column that shows it. On ter-0y0z's above-alt export of mars-air, these are the medians of the time per frame that also lies in another pass: G-buffer 0.177 of 0.479 ms, bloom 0.107 of 0.571 ms, haze 0.141 of 0.164 ms, direct light 0.052 ms, and each SMAA pass about 0.015 ms. This class of misreading gave ter-6vhj its unexplained exposure row and cost ter-0y0z nine traces.

## Acceptance Criteria

passes.md has a column with each pass's time alone: the segments where open_segments (tools/gpu-trace.py) names only that pass. tools/README.md, 'GPU trace', says that the busy time minus the time alone is the time that the pass shares with other passes. A test or a fixture export makes sure that two overlapping passes give the expected time alone.


## Notes

**2026-10-31T21:36:09Z**

2026-10-31: the orchestrator had filed the same finding on main as ter-v4gw while ter-0y0z's fixer filed this ticket on its branch. ter-v4gw is removed in ter-0y0z's merge, because this ticket has the epic, the acceptance criteria and ter-e91p's dependency. Its one extra point: also report the frame's wall time between named passes, so that a with/without table can read the cost that the frame pays (ter-0y0z's 'window', from the end of the reflections to the start of the bloom).

**2026-10-31T23:22:23Z**

Workflow run 983c3b8a-245c-4b29-ad8b-fd6d09710d6d (version 869b0b07), branch ter-9hie-gpu-trace-time-alone from 960368e1; brief /tmp/briefs/9hie.txt.
