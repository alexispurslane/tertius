---
id: ter-uqmn
status: in_progress
deps: []
links: []
created: 2026-10-27T12:22:51Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance]
---
# Bring the shading models' cost in the direct and transparent passes down

ter-3dp5 measured the skin and hair epic on demo-exit's deck window (120 frames from 399, 1920x1080, Release, traces with --no-counters, 2026-10-27): with no skin and no hair on screen, the direct pass grows by 0.4 to 0.9 ms under SeparableSubsurface (base 5.37 to 5.75, branch 6.13 and 6.28 ms) and by 0.8 to 1.3 ms under NoSubsurface (6.59 and 6.69 ms, two runs that agree within 0.1 ms), and the transparent pass by 0.4 to 0.7 ms (1.62 to 1.71 against 2.10 to 2.32 ms). The design estimated 0.02 ms for the shading target's read. That the direct pass is 0.3 to 0.6 ms slower with the scatter off, where it writes one attachment fewer, is not explained; the off runs' other passes are also 0.05 to 0.2 ms slower, so part of it may be the machine's state. No run isolates the cause. Candidates: the per-light choice by model in respond and takesLight inside the light loop, and the two parts of DirectTerms kept apart per light; the shading target's fetch; the larger shader's occupancy; for the transparent pass, forwardShading and the runs by sides. First isolate the cause (for example a build whose respond is brdf alone), then: a loop specialized per model, or the standard path first with hair and skin as rare branches. Measure on demo-exit and skin-and-hair.json, and keep the pixels bit for bit. See docs/plans/skin-and-hair.md, Cost.

