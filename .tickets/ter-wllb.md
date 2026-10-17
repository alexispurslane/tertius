---
id: ter-wllb
status: closed
deps: []
links: []
created: 2026-10-17T01:25:16Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# A body that sticks out of a ship in a numeric frame has no ghost in the ship's semantic parent

From ter-ip1k: a body that sticks out of a ship whose tree parent is a numeric frame gets its parent ghost only in that numeric frame, not in the system of the ship's semantic parent (for example space). A body there that the numeric merge test does not link to the ship's hull never meets that ghost: the merge test reaches only the bounding spheres plus a 2 s lookahead, not the bubble. Examples are a static rock, or any body when the ship is in a resting frame, which keeps no anchor ghosts. Without numeric frames, the ghost in space would hit it. Apply the rule of ter-ip1k: numeric frames do not change which frames a body meets, so the parent ghost goes to the semantic parent's system too (UpdateGhosts in src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs).

