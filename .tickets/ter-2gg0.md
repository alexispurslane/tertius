---
id: ter-2gg0
status: open
deps: []
links: []
created: 2026-10-17T02:58:53Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, frames]
---
# A body in another numeric frame of a ship's semantic parent misses the ghost of a body that sticks out of the ship

From ter-wllb: the second ghost goes into the system of the ship's semantic parent, but a body that sits in another numeric frame of that parent (a slow body far out in its own resting frame, or one that moves with the ship in its own falling frame) never meets it when the numeric merge test does not link it to the hull. Without numeric frames it would. Fixing it needs a spatial test against the members of each numeric frame of the semantic parent.


## Notes

**2026-10-17T03:35:29Z**

The review of the contact sweep (2026-10-17) confirmed a second case of the same gap with a test on the real World and Jolt: a station G hangs directly in space Q, a crate in G sticks 1 m out of G's bubble, so Q's system holds its ghost. A rock at 60 m/s falls in a numeric frame N of Q, and passes through the part that sticks out, with no Collision. Without numeric frames, it hits the ghost. The merge test runs per semantic frame, so it never compares the rock (in Q) with the crate (in G). Suggested fix: in FrameTree.UpdateNumericFrames, test the members of each numeric frame N of a semantic frame S against the bubble of each semantic child C of S, with the merge reach, and record (C, N) beside AnchorGhosts, so that UpdateGhosts puts the ghost of a body that sticks out of C into N too.
