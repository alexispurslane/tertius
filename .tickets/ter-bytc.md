---
id: ter-bytc
status: open
deps: []
links: [ter-s6zr]
created: 2026-11-02T14:10:07Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-b2da
tags: [rendering, reflections, design]
---
# One owner for the specular lobe's cone, or a recorded reason for two widths

Found in the review of ter-0pj8 (2026-11-02). Two types own the cone that stands for the specular lobe around a reflected ray, with different widths. ReflectionCone (src/Tertius.Rendering/Shading/ReflectionCone.cs) takes tan of the half angle = TangentPerAlpha · α, TangentPerAlpha = 1.7602 from Uludag's share, so a full width of about 3.52 α. MapSearch (src/Tertius.Rendering/Shading/MapSearch.cs) takes the full width at half maximum, ConeWidthPerAlpha = 2.57 α. For one lobe the two differ by a factor of about 1.37, about 0.45 of a level. docs/plans/environment-map-distances.md, 'The cone at each sample' and decision C ('One rule with SSR's cone: Yes'), says that one rule chooses every level. Ticket ter-s6zr's GLSL adds a third copy beside reflection.glsl's ConeTangentPerAlpha. Work: either record in 'The cone at each sample' why the search's cone (it smooths a distance field) takes a different width from SSR's (it blurs radiance), or give the lobe's cone one owner that both read, such as GgxLobe or ReflectionCone. This is a design decision: design it twice.


## Notes

**2026-11-02T15:19:23Z**

2026-11-02, the user's choice between two designs: one cone, SSR's width. ReflectionCone owns the specular lobe's cone (tan of the half angle = TangentPerAlpha · α, TangentPerAlpha = 1.7602 from Uludag's share), and the map search reads it, with SSR's level rule: the level whose texel side equals the cone's radius as the eye sees it. MapSearch.ConeWidthPerAlpha (2.57, the full width at half maximum) goes away. No SSR image changes; the search's distance reads become about half a level sharper, and ter-g6af measures the effect. Rejected: two cones with a recorded reason. Built inside ter-s6zr, which writes the GLSL twin of the search, so that the C# and GLSL searches change together. This ticket closes when ter-s6zr merges.
