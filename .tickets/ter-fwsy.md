---
id: ter-fwsy
status: in_progress
deps: [ter-4zt6]
links: []
created: 2026-10-21T16:10:45Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-4zt6
---
# Puffs on the workers: the parallel pieces of the puff step, the births and the snapshot's placement

Task 8 of docs/plans/particles.md (section 'Task 8: puffs on the workers' and 'The parallel pieces'). Split from the particle epic's build on 2026-10-21, because the demo's plumes do not wait for it and its thresholds need measurements on a quiet machine. It takes the thresholds from the measurements of tasks 3 and 4, and tests one thread against four, bit for bit, with each threshold forced down to 1. If the measurements find no useful threshold, it records that and closes.

