---
id: ter-5eaq
status: open
deps: []
links: []
created: 2026-10-19T15:02:06Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics]
---
# Edge cleanup misses a dynamic compound with the lower body ID

Found in the review of ter-fb3w (2026-10-19). Jolt 5.6 orders the bodies of a pair by motion type, then by body ID (PhysicsSystem.cpp:1072-1077), and its InternalEdgeRemovingCollector removes only the inner edges of the pair's second body. So a ball that lands 10 mm from a joint of a dynamic raft of sharp planks moves 14.7 mm when the raft has the lower body ID, and 0 mm when it has the higher one. Two tests in tests/Tertius.Physics.Tests pin both cases. Static and kinematic floors are not affected, because a dynamic body always comes first against them. Find a fix for dynamic compounds of sharp pieces that carry bodies, for example a ship's deck that is a dynamic compound.

