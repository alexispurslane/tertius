---
id: ter-74nr
status: open
deps: []
links: []
created: 2026-10-26T20:22:28Z
type: chore
priority: 2
assignee: Alexis Purslane
tags: [tests]
---
# Gpu.Instance hides a default chain depth in the test helpers

tests/Tertius.Rendering.Tests/TestSupport.cs: Gpu.Instance uses chainDepth: TestChains.CamerasFrame as a hidden default, the pattern that the lights merge removed from Gpu.StarLight, Lamp and Spot (205494f0). Make the chain depth a required argument, as those helpers now have it, and pass it at every caller.

