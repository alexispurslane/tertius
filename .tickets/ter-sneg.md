---
id: ter-sneg
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T13:52:12Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# Decide which frame tree changes go through World methods

World.Frames gives the game the whole mutable FrameTree, so mirrored rows can disagree with the tree until the next tick.

