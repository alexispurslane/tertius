---
id: ter-mjt5
status: in_progress
deps: []
links: []
created: 2026-10-18T18:02:11Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [frames, numeric]
---
# Give a frame that holds still in a numeric home its home's instant velocity

Found by the final integration of ter-6r8c (2026-10-18, worktree 19). Inside a numeric home, a frame that holds still, or a static hull that teleported, gets a tree velocity of zero relative to the home's chord. Its real velocity is the home's instant velocity, half a step of the home's acceleration further on. This is the same kind of error as the fixed finding for dynamic hulls (JoltPhysicsBackend.cs ~907), for the other kinds of frame. Make every frame's tree velocity under a numeric home relative to the same reference, with a test of a velocity read in the semantic parent.

