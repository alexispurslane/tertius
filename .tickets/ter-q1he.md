---
id: ter-q1he
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T14:29:38Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# IPhysicsBackend.ShapeProblem caches one verdict per shape description forever

The cache is unbounded, like the radii cache. Its first question about a new description waits for the native gate, so a spawn during a tick can wait for the running physics step.

