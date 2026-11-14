---
id: t0-adau
status: open
deps: []
links: []
created: 2026-11-14T15:56:48Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# The rect-lamps ceiling shows single bright pixels near the far lamps; find their cause

Filed 2026-11-14 at the orchestrator's request. The long reference (clamps off, a long run) shows the same specks, so they are not a fault of the history or its clamp. Hypothesis to test first, stated as a hypothesis: a ceiling point's bounce ray that hits the glossy floor reads the floor's shaded light as the camera sees it, including the lamps' bright reflections in the floor. That light depends on the view direction, and the ceiling point does not receive it from that direction. Cheap test: count specks in a rect-lamps still with the hit light's specular part left out, against main and against the unchanged branch, on one machine. No fix this week unless the test is clear and the fix is small.


## Notes

**2026-11-14T16:19:02Z**

2026-11-14, first test (cloud, files in /tmp/t7-3e22/preview): the floor-reflection hypothesis does not hold. Bounce rays already leave the hit's specular part out (the diffuse layer of leavingLightOf); leaving it out of the reflection rays too removed about 10 % of the specks, and the rest stay at the same pixels (specks per 10,000 pixels at 132 draws: 6.15 with the knob, 6.74 without, main 7.29); the ceiling daub moved by less than 0.001. The long reference (clamps off) shows a far denser field of fireflies over the bright band than any shown still, so the specks are rare bright samples in the light itself that 20 captures do not average. Next hypothesis to test, as a hypothesis: which hits make them (for example a bounce ray that reaches a lamp's own glow on screen, glowAt in leavingLightOf), by a speck map of the hit kind.

**2026-11-14T16:45:21Z**

2026-11-14, robust reference of rect-lamps (median of 24 batch means of 20 captures from one clamps-off run of 16,467 draws; /tmp/t7-3e22/robust): with the fireflies gone, the true ceiling shows a broad bright glow column around the lamps. Every shown column (main, Design X, the stand-ins) is much darker there and shows specks in its place. So the specks look like the few surviving samples of a real, rarely sampled light that the clamps and filters otherwise remove: the fault is likely the sampling of that light, not a speck to suppress. Next: measure the glow column's level in each column against the reference, and find which path carries that light.

**2026-11-14T17:32:03Z**

2026-11-14, cause found (files in /tmp/t7-3e22/robust; $0.53 in cloud jobs): the lost glow column on rect-lamps' ceiling is not lamp light. A ceiling point gets no direct light from the lamps (one-sided rectangle lights, the ceiling 0.1 m behind their faces), and a bounce ray that hits a lamp reads 0 (glowSent gives no glow for an entity that holds a light), so the lamps' glowing sides count nowhere, but they carry no measurable share here. The glow is Sun light from the far end window, found by the glossy ceiling's reflection rays in about 1 sample in 500 at about 1,000 times the mean: in the glow box 0.21 % of the specular samples carry 86 % of the specular light, 95 % of it from hits at the end window, 100 % of the bright light from block rays, and none with the Sun off. The clamps throw it away: every clamped column keeps only 55 to 60 % of the glow column's light (main 55 %, Design X today 58 %), the quiet ceiling 94 to 109 %. Open: is it real light through the window opening (the environment's sunlit outside at the hit's edge fade) or a leak through the end wall (the walk's thickness test reading the environment)? A probe of the walk's true hit pixel and fade decides it (about $0.04). Candidate cures, unmeasured: (1) clamp each specular sample before accumulation but keep the clipped excess per 8x8 tile in a small blurred image and add it back (about 115 KB, 0.02 to 0.05 ms; glints become a soft glow); (2) guide the rough reflection rays toward bright openings as the diffuse rays are guided (no memory, a few % of the pass; larger change). If it is a leak, the cure is to stop miss light from passing through thickness hits.

**2026-11-14T17:52:37Z**

2026-11-14, the deciding probe ($0.11): the glow column's bright reflection light is mostly a bug. About 74 % of it comes from reflection rays that passed the end window's sill edge into the opening but still hit the sill's thickness slab (ticket ter-ukw6 on main); about 26 % is real front hits on the sub-pixel sunlit sill. The clamps-off reference holds the false light too, so much of the '40 to 45 % lost glow' is light that should not be there. Order: fix ter-ukw6, then re-measure the reference, the glow levels and the specks; only then decide on guiding the rough reflection rays (with or without the clipped-excess redistribution).
