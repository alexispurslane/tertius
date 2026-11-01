---
id: ter-fu8n
status: open
deps: []
links: [ter-kn1d]
created: 2026-11-01T20:34:31Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [rendering, shadows]
---
# Shadows: the slope bias of 2.5 moves a thin wall's sun shadow off its base where the cascade texels are coarse

Found by ter-kn1d's fix stage on 2026-11-01, as known limit 3 of the one bias model (ARCHITECTURE.md, 'Shadow bias'). In demo-exit's wheel room, map 1's +Z face keeps a sun term E(+Z) of 0.13 to 0.17 under the fix (0.006 with the sun left out of the map). Most of it (0.116 to 0.158) comes from 12 to 21 texels on one row of the +Z face, whose rays meet the floor 13 to 50 m along the ring, 0.1 to 0.45 m from the near wall's inner face. There the floor faces the sun at a cosine of 0.25 to 0.49, and the sun is beyond the near wall, so the wall should hide that floor. The term halves with a slope bias of 1.5 (0.057 to 0.075), so the slope-scaled caster bias moves the thin wall back where the texels are coarse. Nobody measured which cascade holds those points. It cannot turn the room's light over by itself: its rays lie from 5 degrees below to 12 degrees above the eye's horizon, so it adds 0.018 to 0.022 to E(up) and at most 0.0007 to E(down). No main view showed a shadow that starts farther from its caster after ter-kn1d; this shows only in the map, far from the eye. Start by finding the cascade and its texel size at those points. Evidence: probe-a.log, probe-bias15.log and probe-base.log in captures/map-flip-2026-11-01/kn1d-fix/ (main checkout, git-ignored).

