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

