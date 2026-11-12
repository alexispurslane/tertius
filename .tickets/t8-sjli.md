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


## Notes

**2026-11-12T22:13:32Z**

Kind (a). The card and the Mac show the same display level, 225, at the square's center (diag3 printed a ladder of glow squares from 0.94 to 1.06 times the C# answer on both). On the Mac's linear HDR target, the center is 1.0196 times the C# tube, so the band of 2 % left 0.04 % for the rounding of the displayed levels: the square of 1.02 shows 225 on the Mac and 224 on the card. Fix: this case alone gets a band of 3 %. On the axis the side gives no light and the far end little, so a tube without its near half sphere still fails by far more than 3 %. The other four cases keep 2 %.
