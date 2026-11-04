---
id: ter-rgg9
status: open
deps: [ter-gayt]
links: []
created: 2026-11-04T20:36:02Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, trace]
---
# The screen tracer's walk takes the hill-climb's portable changes

From ter-dzxf (docs/research/temporal-renderer/walk-hill-climb.md, 'Reproduction and handoff'): after ter-gayt merges, change walkScreen in screen_walk.glsl to the two-image combination bounds-predicate+loop-predicate+facing: the vector Boolean bounds test, the vector Boolean loop condition without short-circuit control flow, and the sign-only facing test (no normalize of the decoded normal, local to the facing test). Keep the original arithmetic order, the two-image depth layout (plan decision 46), reflection cap 256 and diffuse cap 62, and the words form of the per-ray inputs (tools/sol-walk/loop-reads.py counts 0). The harness cut the extracted walk by 21.8 to 23.6 % at cap 62 with every hit unchanged on 3,038,431 saved rays; the projected whole trace on the deck is about 3.14 ms against 3.90. Acceptance: the C# copy (ScreenRay) and the GPU tests that pin the shader to it pass unchanged; the real busy frame on demo-exit's deck at the top clock is measured and reported against the 12 ms budget, before and after. The user chose portable only on 2026-11-04: no Mac-only Metal path.

