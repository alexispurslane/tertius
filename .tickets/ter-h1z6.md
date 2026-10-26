---
id: ter-h1z6
status: open
deps: []
links: []
created: 2026-10-26T17:09:53Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering, impostors]
---
# Impostors write the quad's facing as their geometric normal, which their depth contradicts

Found by the bounce light's convex-edge fix (ter-y1s7) on 2026-10-26. impostor.frag writes outNormal = vec4(octEncode(toward), octEncode(toward)): the quad's facing vector as both the shading normal and the geometric normal. Its distance target holds the real shape of the captured object. So every screen-space reader that builds a plane from a texel's geometric normal and position sees a plane that the depth image contradicts: the bounce light's near steps (they now fall back to the texel's own point when the plane disagrees with the depths around the step), the reflections' origin offset along the geometric normal and the upsample's bilateral weights, and the occlusion upsample's plane weights. Fix: give an impostor texel a real geometric normal, either captured into the atlas beside the color and depth, or derived from the screen derivatives of the rebuilt position (in uniform control flow, by the portable rules). Measure the effect on AmbientOcclusionRenderTests.TheGroundBesideAnImpostorDarkensAsBesideItsGeometry and on the reflections of an impostor.

