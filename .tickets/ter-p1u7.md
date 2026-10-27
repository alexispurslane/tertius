---
id: ter-p1u7
status: in_progress
deps: []
links: []
created: 2026-10-26T18:23:33Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-sglu
tags: [rendering, lights, performance]
---
# A per-pixel bound on the lights, so that a pixel shades only the lights whose share can show

Decision 45 of docs/plans/local-light-shadows.md: no clustered or tiled light list until a measurement shows a need. Part 7's cost (ARCHITECTURE.md, "The scenes and the cost of the shadows") shows one: in lamp-hall-16, 16 shadowed point lamps of 2.5 W cost the lighting pass about 4.3 ms at 1920x1080 on the M1 Max, against about 2.0 ms for the same hall under --local-shadows off, so the shadows of 16 lamps add about 2.3 ms, above the plan's bound of 2 ms. 64 shadowed point lamps cost it about 11.8 ms. Each shading pass loops over every kept light, and each shadowed near light takes 16 PCF taps where it shines. The reach of a 2.5 W lamp is about 730 m at the default exposure clamp, so in a hall 16 m across every lamp reaches every pixel: a clustered list by reach alone culls nothing there. So the design must first say what bounds the lights per pixel: a list by reach (which helps scenes larger than the reaches), a per-light cutoff of the shadow taps where a light's share of the pixel's light is small, or both. Design it twice, then build it with its cost measured against Part 7's halls (scripts/lamp-hall-*.json, TERTIUS_LOCAL_SHADOW_COUNTS).

## Acceptance Criteria

lamp-hall-16 costs the lighting pass at most 2 ms more than under --local-shadows off at 1080p, measured by the recipe of docs/plans/rendering-upgrade.md, with no visible change in the captures of the halls; tests pass in Checked and Release; ARCHITECTURE.md describes the list.


## Notes

**2026-10-26T18:31:42Z**

The demo run scripts/demo-exit.json at 1080p shows the same cost (Part 7, 2026-10-26): the lighting pass takes 7.2 and 7.3 ms on the branch against 1.9 and 2.1 ms on the base 9546afbe, and the frame span 17.1 and 17.7 ms against 11.4 and 12.0 ms, under a 1-minute load of 15 to 27.

**2026-10-26T20:35:11Z**

2026-10-26, the user's decisions on the light list (after a talk on deferred shading and light volumes):
1. The bound on the lights per pixel is decided per pixel, in the shared light loop of lighting.glsl, with no tile or cluster list and no light volumes. Light volumes were ruled out: every reach sphere covers the whole screen in the station (a 10 W lamp reaches 1.46 km), so 21 full-screen volumes would move about 1.9 GB per frame.
2. Two steps per pixel. Step 1 sums every kept light's irradiance without shadow. Step 2 shades only the lights whose share (their unshadowed irradiance over the sum) is at least 1/256, the analogue of FarShadows.Share, which cannot show; and samples a light's shadow only above a shadow share picked from captures, with a fade band so that no contour shows. Below it, a dim lamp lights without its faint shadow. The user had no preference here and the orchestrator settled it by its recommendation; the user sees the captures of the halls and demo-exit before the change merges.
3. The ticket's first step is a profile of the lighting pass on lamp-hall-16 and demo-exit, and every shading pass that shares the loop gains (lighting, transparent, environment faces, impostor captures, puffs).

**2026-10-27T02:23:15Z**

2026-10-26: built on ter-p1u7-light-share (52f53dee, 79b1b31c, e0cc3820), held for the user. The bound is slower than main: the lighting pass takes 4.58 ms in lamp-hall-16 at S = 1/4 against 4.19 ms, and 8.49 ms in demo-exit against 7.44 ms. The acceptance criterion (shadows at most 2 ms over --local-shadows off) is not met. Cause: U may sum only the lights whose shadow the view does not sample (the literal rule, every light, darkened the hub and spoke outside discs around the lamps), and where most lamps cast shadows U is small, so step 2 skips almost nothing while step 1's second pass over the lights costs 0.4 to 1 ms. The profile of main: about 3.6 ms of demo-exit's lighting pass is the lamps' reach (every lamp at every pixel), about 1.3 ms the lamps' shadows, limited by PCF sample rate. Captures in the scratchpad p1u7-captures: S = 1/4 changes 0.023 % of the hall's pixels by more than 2 levels. Open for the user: drop the bound, or a different design.
