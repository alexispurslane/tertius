---
id: t8-ol7x
status: open
deps: []
links: [t8-pkag]
created: 2026-11-12T21:48:41Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: t8-pkag
tags: [rendering, linux]
---
# The light-age diagnostic finds receivers with a hit distance of 0 and a hit on NVIDIA

LightAgeHistoryTests.TheDiagnosticGivesEachReceiverTheFramesSeenOfTheUnchangedSurfaceThatItsRaysHit fails on the RTX 2000 Ada: about 30 receivers, in pairs of one block, have a specular sample with alpha (the hit distance) 0, but their rays hit a surface seen for 31 frames.

