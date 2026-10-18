---
id: ter-nw4p
status: in_progress
deps: []
links: []
created: 2026-10-18T18:02:11Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# Turn BodyRecord.AngularAcceleration into the new home's axes after a hand-off

Found by the final integration of ter-6r8c (2026-10-18, worktree 19). After a hand-off, BodyRecord.AngularAcceleration stays in the old home's axes. Nothing reads it today (the BodyRecord doc says so), but the spin gravity epic reads the angular acceleration of turning frames. Turn it into the new home's axes at the hand-off, with a test.

