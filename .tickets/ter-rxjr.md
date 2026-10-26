---
id: ter-rxjr
status: closed
deps: []
links: []
created: 2026-10-26T14:49:29Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-a24f
tags: [rendering]
---
# Bounce light: a flat floor's near steps block its own edge sectors and bounce its own light

Found while fixing the review findings of Part 5 (ter-9xoi). On a flat, sunlit white floor with nothing else in view (the scene of BounceLightRenderTests.ALitWallOffTheScreenGivesTheFloorNoBounce without the wall), the sectors pass alone, with the far rays, the denoise and the upsample skipped, leaves 4,525 of 26,000 floor texels at 640 x 360 with blocked sectors and a bounce. Near the left edge, 3.5 to 4.8 m away, the masks block 1 to 6 sectors at an edge of the slice (00000001, 80000000, 0000003F), V is 0.985 to 0.997 and the bounce is 0.2 to 1.2 percent of the floor's direct light. At the floor's far edge, 28 m away, they block up to 25 sectors. After the denoise and the upsample, the estimate keeps V of 0.998 and a bounce of about 0.15 percent of the floor's direct light. The far rays add nothing there: with the far pass returning early, the estimate is the same to the bit.

By decisions 12 and 39 a flat floor should block nothing and bounce nothing on itself. The likely cause: gtaoStepOffset rounds each step to a texel center, which lies off the slice plane, and sliceAngle takes the step's full 3D angle from V-hat. On a floor seen at a grazing angle, one texel row covers several texel widths of floor, so a snapped step's direction can lie well inside the hemisphere. Its light then counts, because CenterBias lifts the center a little above the floor, so the floor's own steps face it (Faces). Projecting the step's direction into the slice plane before the angle cut the largest residue only from 1.15e-3 to 0.89e-3, so the snapping is not the whole story yet.

BounceLightRenderTests.ALitWallOffTheScreenGivesTheFloorNoBounce bounds the bounce at the measured floor by 0.5 percent of the floor's direct light because of this. When this ticket is fixed, that bound becomes 0, as the design doc's criterion asks.

