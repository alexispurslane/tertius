---
id: ter-13rm
status: open
deps: [ter-vges]
links: []
created: 2026-11-03T12:30:05Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# World-space radiance cache fed from the screen

The user's choice of 2026-11-03: a later step of the temporal renderer. Visible surfaces write their light into a sparse world-space cache each frame, and a screen ray that misses reads the cache before the environment map, so that light behind the camera stays known for a while. It needs a design of its own.

