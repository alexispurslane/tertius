---
id: ter-fs7b
status: in_progress
deps: [ter-a24f]
links: []
created: 2026-10-25T20:07:19Z
type: feature
priority: 3
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering]
---
# Bounce light: read the environment map in each open direction

Known limit of docs/plans/screen-space-illumination.md: with the environment map on, the open share of the one estimate takes the map's mean light (its irradiance). The map is captured at the camera, so it already holds the nearby walls, and up to a quarter of a wall's light counts twice (b(1 - b) for a blocked share b), while as much far light is lost. Read the map in each open direction instead, so that the open share takes the light of the directions that it holds.

