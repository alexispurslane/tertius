---
id: ter-e91p
status: open
deps: [ter-9hie]
links: []
created: 2026-10-31T21:34:03Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: a paired mode for frames that alternate with and without a pass

Found by ter-0y0z, 2026-10-31. The paired-trace analysis of ter-bgpv was rebuilt as temporary scripts in ter-0y0z (/tmp/ter-0y0z/tools/paired.py, split.py and window.py). Each rebuild can choose a different statistic, and ter-0y0z's first doc compared the window with a haze column that held an interval outside the window. This is the second real use, so the analysis goes into the tool. The renderer's probe that drops a pass on every other frame stays out of the commits.

## Acceptance Criteria

tools/gpu-trace.py has a paired mode. It marks each frame by whether a named pass has GPU work, pairs each frame without it with the mean of its two neighbors with it, and reports per pass the paired medians of the busy time and of the time alone, with bootstrap standard errors of the median. It reports a window statistic between two named passes, and a control mode that pairs odd frames with their even neighbors. tools/README.md gives the method.

