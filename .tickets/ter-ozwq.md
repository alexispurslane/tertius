---
id: ter-ozwq
status: open
deps: []
links: []
created: 2026-10-27T04:21:29Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering, lights, shadows]
---
# A cascade set that draws no caster counts as having maps while another set draws one

Found by the review of ter-mgap (2026-10-27); not reproduced. frame.counts.z is one switch for the whole frame (shadows.glsl, beyondShadowMaps and cascadeShadow): ShadowPass.Prepare leaves it at 0 when no caster touches any set, and sets it to 4 when one caster touches any set. A far light's mapped occluders (SceneBatcher.AddMapped, by ShadowCascades.TakesCaster) need not be drawn in its own set (ShadowPass.CascadeMask). So when set 0 of light A draws no caster, A's mapped occluders count fully as far shadows while no other set draws one, and are weighted by beyondShadowMaps, near 0 inside the maps, against an empty set 0 as soon as another set draws a caster. That other set can belong to a light that only a face of the environment map keeps, so a main-view pixel can depend on the faces, against docs/plans/environment-map.md decision 20. A pixel changes only where a mapped occluder that misses every box of set 0 and its extension toward the light still dims it, through the light's penumbra: about where the frustum meets the coarsest box, near the end of the maps' fade, so the change is at most (1 - beyondShadowMaps) times a penumbra share. The reviewer's fix: count casters per set, and treat a set that draws no caster as having no maps. The deeper mismatch, a mapped occluder that a set with other casters does not draw, exists without the map too. Decide whether either needs a fix; a test needs a scene that shows the pixel change first.

