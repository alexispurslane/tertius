---
id: ter-s9m0
status: closed
deps: [ter-n44b, ter-ozm8]
links: []
created: 2026-10-16T23:00:45Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [physics, frames]
---
# Spin gravity: the forces of a turning frame act on its contents

Design: docs/plans/spin-gravity.md. The spike on branch spike-spin-gravity showed that Jolt handles a station that is a spinning semantic frame: free bodies fly on inertial lines to about 1 mm, a resting crate presses with m w^2 r, a person stands, walks and jumps correctly. Decisions (User, 2026-10-16): every turning frame gets the rotation terms; the engine corrects Jolt's velocity convention everywhere. This changes the scope rule that only a frame's linear proper acceleration acts on its contents.


## Notes

**2026-10-20T14:51:46Z**

2026-10-20: every ticket of the epic is merged, the last one the station scene (ter-n44b). ter-04lz, the reach cache of the wake test, stays open as an optimization that no work waits for.
