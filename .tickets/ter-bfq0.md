---
id: ter-bfq0
status: open
deps: []
links: []
created: 2026-10-18T18:02:11Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [frames, numeric]
---
# Add the numeric home's acceleration when a step record converts into the semantic parent

Found by the final integration of ter-6r8c (2026-10-18, worktree 19). MotionSampleTable.ConvertInto only rotates a record's Acceleration into the semantic parent. For a dynamic hull under a numeric home, the converted Acceleration lacks the home's own acceleration. Nothing reads it today; spin gravity and the following frames may. Add the term, with a test.

