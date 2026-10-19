---
id: ter-6azq
status: open
deps: [ter-xdt8]
links: []
created: 2026-10-19T17:19:52Z
type: task
priority: 1
assignee: Alexis Purslane
tags: [performance, frames]
---
# Win back the sync point time that the motion intent cost rails frames

Found by the fix stage of ter-xdt8 (2026-10-19). In KeplerFrameTickBenchmarks (1,000 Kepler frames) the sync point went from 109 us to 246 us on one thread and from 159 us to 302 us on all threads; the tick went from 399 us to 603 us. Most of it is the FollowOwners loop over the step records, 24 us to 127 us, of which about 49 us is the proper acceleration rule (two passes: values as the step found them, then the writes). Step 4 went from almost nothing to 26 us. Profile with the SYNC_PROFILE laps (docs from ter-sn28), find what the new path does per frame that the sample table did not, and bring the sync point back to within 10 % of 109 us without changing behavior (state hashes equal). The parallel paths of ter-sn28 are a model.

