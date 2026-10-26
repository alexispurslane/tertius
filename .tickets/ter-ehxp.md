---
id: ter-ehxp
status: open
deps: []
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

