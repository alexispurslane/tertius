---
id: ter-uyjw
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T13:52:12Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design]
---
# Let a frame owner have a character body, for walking vehicles with an interior

The hull code calls Jolt rigid-body functions on the owner's body ID. Give it one interface for the owner's pose, velocity and acceleration, then remove the refusal.

