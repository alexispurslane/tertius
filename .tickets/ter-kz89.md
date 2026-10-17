---
id: ter-kz89
status: closed
deps: []
links: []
created: 2026-10-17T02:58:53Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# A body in a numeric frame inside a ship may never leave the ship's bubble

From ter-wllb, read in the code and not run: BubbleParentOf returns null for a numeric frame, so a body whose home is a numeric frame inside a ship gets no ghost in the ship's parent and no sibling ghosts, and the hand-off may never move it out of the ship's bubble. Example: a crate thrown at more than 50 m/s inside a ship gets a falling numeric frame there, then flies out through an open door. Write the test first. If the body leaves the bubble correctly, record why and close this ticket. Otherwise apply the rule that numeric frames do not change which frames a body meets (UpdateGhosts and HandOffBodies in src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs).


## Notes

**2026-10-17T08:27:57Z**

Merged. The bug was real: a crate thrown at 60 m/s inside a ship never left it. BubbleParentOf became BubbleOf(frame) = the semantic frame of the home, and the hand-off and the ghosts test that bubble at the body's place there. After the review, a contact that no system takes over after a hand-off waits one step when one of its bodies is in a numeric frame.
