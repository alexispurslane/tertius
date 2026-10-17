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

