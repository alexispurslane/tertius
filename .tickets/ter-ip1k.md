---
id: ter-ip1k
status: closed
deps: []
links: [ter-s02r]
created: 2026-10-16T13:45:02Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [physics, frames]
---
# A body in a numeric frame that touches a parked frame's bubble without entering it gets no ghost there

From ter-gsvv: bodies whose home is numeric now enter every semantic bubble that they would enter without numeric frames, but UpdateGhosts still gives such a body no ghost in a bubble that it touches without entering, for example a hull that does not fit. Apply the same rule as the entry test: numeric frames do not change which bubbles a body meets, so ghosts follow the same candidates (PrepareBubbleEntries, BubbleSitesOf in src/Tertius.Physics/JoltPhysicsBackend.HandOff.cs).

