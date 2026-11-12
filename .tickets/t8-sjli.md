---
id: t8-sjli
status: open
deps: []
links: [t8-pkag]
created: 2026-11-12T21:48:40Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: t8-pkag
tags: [rendering, linux]
---
# The tube light standing on the middle of the square shows level 225 against the band 223 to 224 on NVIDIA

TubeLightRenderTests.ATubeLightLightsAMatteSquareAsTheCSharpTubeDoes(standing-on-the-middle) fails on the RTX 2000 Ada: the center shows display level 225, and the test allows the levels of 0.98 to 1.02 times the C# tube's answer, 223 to 224.

