---
id: ter-74nr
status: closed
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


## Notes

**2026-10-27T06:26:20Z**

Merged. 209 callers pass TestChains.CamerasFrame; the scenes that set a deeper chain (BatcherTests.Box, EnvironmentScenes.Square, Facing and AddEnclosure, EnvironmentPrefilterTests.Colorful, the per-frame room scenes) take a required chainDepth, and RoomScenes.In is gone. Lights and puffs still set their depth with a with expression, outside Gpu.Instance.
