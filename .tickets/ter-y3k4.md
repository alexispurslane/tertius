---
id: ter-y3k4
status: open
deps: [ter-rgg9]
links: []
created: 2026-11-04T20:36:02Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, trace]
---
# One-image depth pyramid for the walk: measure it in the engine

From ter-dzxf: one RG32F pyramid whose mip 0 holds the exact centre inverse depth in both channels saved a further 0.13 to 0.19 ms per frame in the harness at cap 62 (deck-10: 2.534 against 2.674 ms), but needs about 12.48 MiB more allocation, doubles level 0's texel payload and reverses plan decision 46. Measure it in the engine before adopting: its construction cost, the real frame at the top clock, the other readers of the pyramid and their mip meanings, and the memory traffic on a Linux driver (with ter-cgif's Linux runs). Adopt only if the real frame gains on both platforms.

