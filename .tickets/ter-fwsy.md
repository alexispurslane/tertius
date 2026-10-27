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


## Notes

**2026-10-27T04:30:00Z**

Built: the step on the workers from 3,072 live puffs (medians), which pays at the default capacity of 4,096. Not built, settled by the rule to leave out what is not needed yet: above 4,096 puffs with deaths, the join's row move on the sync thread costs about 110 µs at 8,192 and 220 to 270 µs at 16,384, so the workers lose by about 5 percent at 8,192. The fix for a scene that needs more puffs: a second Parallel.For copies each slice's survivors into its own stretch of spare, at offsets summed in slice order, then swaps rows and spare (about 2 KB more garbage per tick). Births and the snapshot placement stay on the sync thread: they pay only past the default capacity.
