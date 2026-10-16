---
id: ter-2y6i
status: open
deps: []
links: []
created: 2026-10-16T12:38:20Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [host, tools]
---
# The state dump at tick 0 prints spawn velocities as floats

The orbit-cluster reader found that start.json shows the ship's velocity as a float32 (6016.291015625 against 6016.291064), while later dumps print doubles. Cosmetic, because the engine holds the exact value from tick 1, but a tick-0 dump is then not an exact initial state.

