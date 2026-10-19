---
id: ter-9hm3
status: open
deps: [ter-imi5]
links: []
created: 2026-10-19T15:15:21Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# Find the static bodies near each child bubble without a pass over every static body

Follow-up of ter-imi5 (2026-10-19). Since the complete ghosts, the ghost pass tests every static plain body of a system against each child bubble, every step. The design's Cost section measured it and accepted it for today's scenes, and named two fixes for later: cache the places of the static members of each system (they change only when a static body moves or is made), or query Jolt's broad phase for static bodies near each child bubble. Build one of them, with the benchmark of 2,000 static props from docs/plans/complete-ghosts.md, and link this ticket from its Cost section.

