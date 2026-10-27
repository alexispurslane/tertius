---
id: ter-uqmn
status: open
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

ter-3dp5 measured the skin and hair epic on demo-exit's deck window (120 frames from 399, 1920x1080, Release, traces with --no-counters, 2026-10-27): with no skin and no hair on screen, the direct pass grows by 0.4 to 1.0 ms (base 5.37 to 5.75, branch 6.13 to 6.59 ms) also under NoSubsurface, and the transparent pass by 0.4 to 0.6 ms (1.62 to 1.71 against 2.10 to 2.28 ms). The design estimated 0.02 ms for the shading target's read. Candidates: the per-light choice by model in respond and takesLight inside the light loop, and the two parts of DirectTerms kept apart per light; a loop specialized per model, or the standard path first with hair and skin as rare branches. Measure on demo-exit and skin-and-hair.json, and keep the pixels bit for bit. See docs/plans/skin-and-hair.md, Cost.

