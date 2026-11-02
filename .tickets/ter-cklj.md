---
id: ter-cklj
status: open
deps: []
links: []
created: 2026-11-02T22:40:17Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections]
---
# A rough curved mirror keeps a sharp reflection where its reflected direction turns fast along one screen axis only

Found at the fix stage of ter-nw4a (2026-11-02). captures/nw4a/sheets-fix/fix-mirror-ball.png (main | fix | 64-ray reference, mirror-balls balls, x2): on the ball of roughness 0.2, the reflection of the glowing neighbor near the right rim stays as sharp after ter-nw4a as on main, while the reference shows a wide soft glow. The fix reports that the footprint stays below one texel near the rim, so these pixels keep their own ray and get no blur.

Design A sizes the footprint with f = max(alongX, alongY) from reflectionFootprintAt (gbuffer_surface.glsl): how far the reflected direction turns across one pixel, the larger of the two screen axes. Near a ball's rim the direction turns fast along the radius and slowly along the rim. The max makes the footprint round and as small as the fast axis allows. Hypothesis to test first: an elliptical footprint, with one radius per screen axis from alongX and alongY, blurs along the slow axis as the lobe does. A second possible cause: the neighbor lies near the ball, so rho(t) is small for its hits.

To decide: a probe of the elliptical footprint on mirror-balls balls against the 64-ray reference, with its cost on the step view and on reflections low, or a recorded limit with the reason.

