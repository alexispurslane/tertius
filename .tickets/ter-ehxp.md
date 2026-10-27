---
id: ter-ehxp
status: closed
deps: [ter-izsu]
links: []
created: 2026-10-26T22:24:22Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-a24f
tags: [rendering, performance]
---
# Bounce light: only the near sectors, the far rays go

The user downscoped the bounce light on 2026-10-26 to the near part (see the note on ter-a24f). Remove the far rays pass (occlusion_far_rays.comp and its C# copy, the setting VisibilityBitmaskLight.FarRays and option B, the far rays' images, barriers, pass name and tests), and make an open sector beyond the radius take the ambient light again, as the design did before Part 5. Keep the half-resolution copy of the direct light and the geometric normal (ter-izsu). Update docs/plans/screen-space-illumination.md (a decision with the user's words; the far rays move to 'Later' with the commit that held them) and ARCHITECTURE.md. Measure the passes again on demo-exit at 1080p, and capture the lamp-lit cabin (cabin-light) and demo-surface's in-cabin still with the bounce on and off, to show whether the white speckle remains without the far rays.


## Notes

**2026-10-27T01:54:34Z**

2026-10-26: done on ter-ehxp-near-sectors (0daaa383), fast-forwarded into ter-a24f-ssgi. The far rays, option B, the sector masks buffer and their tests are gone; an open sector beyond the radius takes the ambient light; the depth pyramid is back with ReflectionTargets. The four occlusion passes take 2.1 to 2.7 ms against 8.1 to 8.2 ms for the five; the undisturbed frame span is 14.2 ms against 18.8 to 19.1 ms. The white speckle is gone from the lamp-lit cabin and demo-surface's in-cabin still. Above target: the sectors pass, 1.4 to 1.9 ms.
