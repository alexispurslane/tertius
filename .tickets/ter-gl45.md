---
id: ter-gl45
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T13:52:12Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# JoltPhysicsSharp keeps a wrapper for every body that it ever created

About 3,025 entries after 3,000 bodies. Measure the leak and release the wrappers.

