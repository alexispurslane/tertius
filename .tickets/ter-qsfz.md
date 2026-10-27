---
id: ter-qsfz
status: closed
deps: []
links: []
created: 2026-10-27T08:09:42Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-n7lw
tags: [rendering, hair]
---
# Hair under the screen-space light: a tilted card's own texels darken it

Found by Part 4's builder (2026-10-27). Under VisibilityBitmaskLight.Default, a hair card turned 40 degrees shows (0.2228, 0.0947, 0.0490) against (0.2395, 0.1013, 0.0524) for a card that faces the camera: 7% darker, so the card planes show. The settled first answer (a hair pixel's plane in every screen-space reader is its fiber normal's plane) does not fix it: half of a tilted card's own texels lie above the fiber plane, so they block the same sectors that the card's plane blocked. With RadiusMeters 0.01 (no near steps) every case passes. TheHairsAmbientLightIsTheSourcesAtTheFiberNormal is 1.5% off with the patch, 8% without, against 1%.

The patch with the fiber-plane readers (planeNormalCodeAt in gbuffer_inputs.glsl, six readers, and the test TheOcclusionPassesTakeAHairPixelsFiberNormalAsItsPlane) is stored as a blob: git cat-file -p refs/wip/n7lw-fiber-plane > fiber-plane.patch, against ter-n7lw-skin-hair at 7df62c24.

Options, waiting for the user:
(A) For a hair receiver, a step that lies on the receiver's own card plane blocks nothing. An exception with a named misbehavior; one more plane test per step; needs the card normal beside the fiber normal.
(B) A hair pixel takes no screen-space estimate (V = 1, G = 0), as the fringe already does. Hair loses the crease shadow and the bounce light from the head behind it.
(C) Keep the card's self-occlusion as the hair's own shadowing, and run the planes-do-not-show acceptance without the screen-space light.
(D) A hair step never blocks a hair receiver, but blocks every other surface. Hair loses the occlusion of one card by another; each step needs the shading model.

## Acceptance Criteria

The user picks an option. Two hair cards with one strand and different normals shade alike under the default screen-space light, within the tolerance of the option.


## Notes

**2026-10-27T11:48:41Z**

2026-10-27: the user chose option A: for a hair receiver, a step that lies on the receiver's own card plane blocks nothing. The receiver needs its card normal beside its fiber normal. Start from the fiber-plane patch (refs/wip/n7lw-fiber-plane) only where the tests need it.

**2026-10-27T14:00:02Z**

2026-10-27: merged (89555b40); full Checked on the merged main: 9,283 total, 0 failed. Coverage of the changed lines 32/34 (94.1%): the misses are the empty-union guard arms of OwnSurface. Option A covers flat cards; curved or smooth-shaded cards are ter-3yy5 (five options for the user).
