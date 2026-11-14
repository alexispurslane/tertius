---
id: ter-ukw6
status: open
deps: []
links: []
created: 2026-11-14T17:52:37Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# A screen ray that passes a thin sunlit sill's edge into an opening still hits the sill's thickness slab

Found 2026-11-14 by the t0-adau probe (/tmp/t7-3e22/robust, patches hitclass-final.patch then hittrue-final.patch after the survey's probe patch; main 557d825b1). In rect-lamps with the Sun on and clamps off, about 74 % of the glow box's bright reflection light on the ceiling (about 64 % of all its reflection light) comes from reflection rays that already left through the end window past the sill's outer edge, where the camera sees sky, yet count as hits on the sill: render row y 375, x 596-679, the sill a sub-pixel strip seen almost edge-on. Code: src/Tertius.Rendering/Shaders/screen_walk.glsl, walkScreen: at the finest level a pixel's slab reaches from its surface w back to w / (1 + ThicknessShare), ThicknessShare = 0.1 (DepthPyramid.cs); a ray inside it hits (meetsSlab && hitsPixel) with entryShare 0..1. src/Tertius.Rendering/Shaders/screen_trace.glsl, traceFrom ~766-767: fade = edgeFade(hit) * (1 - entryShare); light = mix(missLight, hitLightOf(hitPixel, kind), fade). The sunlit sill is about 1,000 times brighter than the miss light, so even a hit near the slab's back reads a huge value. About 26 % of the bright light is real front hits on the sunlit sill (t0-qwx8 territory). The clamps-off reference holds the false light too; after the fix, re-measure the robust reference, the glow levels and the specks (t0-adau), and only then decide on guiding the rough reflection rays.

