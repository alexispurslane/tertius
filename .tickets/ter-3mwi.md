---
id: ter-3mwi
status: open
deps: []
links: []
created: 2026-10-17T02:58:53Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, contacts]
---
# A sensor and a body that touch only through two ghosts are not reported, and interiors carry no contacts

From ter-ejx9: a pair of two ghosts is not reported, because the real bodies' own systems report it. But a sensor and a body that stick out of sibling frames and overlap only outside both bubbles touch only through two ghosts, so the engine misses that touch. Interiors also still do not carry a contact across a hand-off. Decide both with the contact ledger in src/Tertius.Physics/JoltPhysicsBackend.Contacts.cs.


## Notes

**2026-10-17T05:07:23Z**

From the review of the contact sweep: the hand-over from a hull to an interior is never carried either. A fast body that crosses a ship's bubble in one step and touches the hull in the parent gets Collision, then CollisionEnded at the hand-off, then Collision again on the interior. A ship that enters a station while its hull's ghost touches a crate there gives the same sequence. This is older than the contact sweep.
