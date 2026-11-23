---
id: t1-hdtm
status: open
deps: []
links: []
created: 2026-11-23T05:39:44Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, shadows, local-lights]
---
# A lamp's light runs about 6 px down the face of the exit cabin's beam, below the beam's top edge

In the survey still exit-cabin, a lamp above the exit cabin's black beam lights the beam's face for about 6 px (at 1:1) below the beam's top edge. The top edge shades that face, so the light there is a leak. It is the lamps' form of the jamb leak of t1-gout: a shadow tile's texel holds a caster only where the caster covers the texel's center, so the tile's edge lies up to half a texel inside the real edge, and the symmetric 4 x 4 filter puts half of its blur on the shaded side.

## What was already measured (do not repeat it)
Design L of t1-gout starts a light's share at the lit fraction DoubtfulLitFraction = 0.5 + 0.5 / FilterTapsPerAxis = 5/8 in place of 1/2. The cascades have it since commit A of t1-gout. The same rule on the tiles ('commit B', one line in tileShadow of shadows.glsl: lightOutsideShadow(tileLitFraction(...))) was built and compared on stills on 2026-11-22 and 2026-11-23:
- It cuts this leak from about 6 px to about 2.5 px. It does not remove it.
- It makes every lamp shadow clearly sharper and darker: up to 194 of 255 on the exit cabin's floor (the shadows of the frame bars).
- It raises the tiles' acne: in ALitFloorUnderASpotLightHasNoAcne at 60 degrees with a 45 degree turn, 634 of 25,784 pixels differ at 4 m (main 4) and 5,505 of 135,815 at 12 m (main 2), about 2.3 percent darker, largest 8 of 255. The rule's slope 1 / (1 - 5/8) = 2.67 amplifies the ring pattern that ShadowBias.SlopeBias documents for a slope diagonal to the grid.
- Its cost was not measured. The cascades' part cost +0.24 ms in the sunlit orbit cabin through the picture.

The orchestrator decided on 2026-11-23: the tiles keep the symmetric filter, because the user's rule names hard-edged shadows as not acceptable. So the fix of this leak is not design L's constant on the tiles.

## Evidence
The lead's crops, main left and commit B right: exit-cabin-beam4x.png (this leak), exit-cabin-crop2x.png (the floor's shadows), spot-box-base5x.png (no leak at the spot lamp's box). They lie in /Users/alexispurslane/.t3/worktrees/tertius/t3-2b720d96-b40221aeff8f27f7-evidence/step3/tiles/ and the lead of t1-gout copies the first two into docs/research/shadow-grid-phase/ with commit A.

## Open
No fix is designed. A fix must keep the lamps' soft shadow edges. Directions that nobody measured: a tile's resolution at this distance, and a caster's coverage rule in the tile pass. Lost light at the edge is acceptable, a hard edge is not.


## Notes

**2026-11-23T08:09:47Z**

The still, from t1-gout step 3: scripts/demo-exit.json at frame 200 (the survey case exit-cabin), 1920 x 1080, render scale 0.96, with the automatic exposure of commit A's build held for both builds (-0.4470315 stops; docs/research/shadow-grid-phase/tools/tilestills.sh). The crop is x 1100 to 1260, y 90 to 240 of the 1920 x 1080 frame, a ceiling lamp over the black beam. On main the lamp's light runs about 6 px down the beam's face below its top edge; with the tiles' lit-side rule (lightOutsideShadow(tileLitFraction(...)), refused) about 2.5 px. Both are read from the 1:1 crop, not measured by a script. The images: docs/research/shadow-grid-phase/remedy/tiles-beam-face.png (main on the left, 4x) and tiles-cabin-floor.png. LocalShadowRenderTests.AWallsSideFaceUnderARoofsEdgeTakesNoLightFromASpotLampsTile is this leak's red test, skipped: the symmetric filter gives a face 0.386 of the lamp 0.25 texel inside a roof's edge, 0.318 at 0.5 texel and 0.197 at 1 texel.
