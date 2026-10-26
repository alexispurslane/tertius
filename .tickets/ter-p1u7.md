---
id: ter-p1u7
status: open
deps: []
links: []
created: 2026-10-26T18:23:33Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, lights, performance]
---
# A clustered light list, so that a pixel loops only over the lights that reach it

Decision 45 of docs/plans/local-light-shadows.md: no clustered or tiled light list until a measurement shows a need. Part 7's cost (ARCHITECTURE.md, "The scenes and the cost of the shadows") shows one: in lamp-hall-16, 16 shadowed point lamps of 2.5 W cost the lighting pass about 4.3 ms at 1920x1080 on the M1 Max, against about 2.0 ms for the same hall under --local-shadows off, so the shadows of 16 lamps add about 2.3 ms, above the plan's bound of 2 ms. 64 shadowed point lamps cost it about 11.8 ms. Each shading pass loops over every kept light, and each shadowed near light takes 16 PCF taps where it shines. The reach of a 2.5 W lamp is about 730 m at the default exposure clamp, so in a hall 16 m across every lamp reaches every pixel: a clustered list by reach alone culls nothing there. So the design must first say what bounds the lights per pixel: a list by reach (which helps scenes larger than the reaches), a per-light cutoff of the shadow taps where a light's share of the pixel's light is small, or both. Design it twice, then build it with its cost measured against Part 7's halls (scripts/lamp-hall-*.json, TERTIUS_LOCAL_SHADOW_COUNTS).

## Acceptance Criteria

lamp-hall-16 costs the lighting pass at most 2 ms more than under --local-shadows off at 1080p, measured by the recipe of docs/plans/rendering-upgrade.md, with no visible change in the captures of the halls; tests pass in Checked and Release; ARCHITECTURE.md describes the list.


## Notes

**2026-10-26T18:31:42Z**

The demo run scripts/demo-exit.json at 1080p shows the same cost (Part 7, 2026-10-26): the lighting pass takes 7.2 and 7.3 ms on the branch against 1.9 and 2.1 ms on the base 9546afbe, and the frame span 17.1 and 17.7 ms against 11.4 and 12.0 ms, under a 1-minute load of 15 to 27.
