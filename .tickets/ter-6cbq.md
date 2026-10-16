---
id: ter-6cbq
status: open
deps: []
links: []
created: 2026-10-16T22:57:50Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics]
---
# A body that a frame's death rehomes loses its collision groups from a SetCollision intent

Spotted by the ter-gj55 agent, not checked: Rehome re-creates a body from the BodyDesc of its spawn, so a body that moves when its frame dies probably loses the groups and mask that an earlier SetCollision intent gave it. Test it, and re-create the body from its current groups and mask.

