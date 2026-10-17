---
id: ter-dq92
status: closed
deps: []
links: []
created: 2026-10-17T05:07:23Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# A crate in a falling numeric frame passes into a static wall whose shape is a mesh

From the fixes of the contact review (2026-10-17), seen and not fixed: the ghost of a static mesh anchor in a falling numeric frame is a static Jolt body, because Jolt simulates mesh shapes only as static or kinematic and the engine makes mesh ghosts static. The ghost pass moves it by teleport, about 34 cm into a crate in each step, and Jolt does not stop the crate. Without numeric frames the crate stops at the wall. Jolt also sees that ghost as at rest, so a body in the falling frame cannot bounce off a mesh anchor, though it does without numeric frames. Decide whether a mesh ghost in a moving frame becomes kinematic with its real velocity, and test both scenes beside the same scenes without numeric frames.


## Notes

**2026-10-17T08:27:57Z**

Merged. A mesh ghost is kinematic with 1 kg while it moves and static while it holds still; staticGhostMotion is gone. Follow-up: ter-ykqs (no linear cast for ghosts).
