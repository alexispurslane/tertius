---
id: ter-69s8
status: open
deps: [ter-9nrc]
links: []
created: 2026-11-04T13:42:25Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, lights]
---
# Tile light lists: each pixel loops only over the lights near it

The user's decision of 2026-11-04: with light sampling removed (ter-9nrc), every light of the main view is shaded exactly, and its cost grows with each light that a pixel loops over. Build light culling per screen tile now, so that the exact loop and the forward passes of the main view loop only over the lights whose reach touches the tile. Design first: the orchestrator presents two designs (for example 2D tiles with depth bounds from the G-buffer against 3D clusters) after a fact-finding read of every consumer of the light list, the depth layout and the exact loop's cost. Acceptance: lamp-hall-64 costs no more than the removed sampling did (1280 x 720, at the windowed clock), demo-exit's deck costs no more than the exact loop without tiles, and the images equal the exact loop's.

