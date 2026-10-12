---
id: ter-f620
status: open
deps: []
links: []
created: 2026-10-12T11:31:11Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [simulation, frames]
---
# A Kepler frame jumps when its parent is destroyed and its grandparent has point gravity

Found in ter-7wbc. When a frame is destroyed, its children move up to the grandparent. A Kepler child keeps its orbit's elements, which now describe an orbit round the grandparent with the grandparent's G·M, so the frame jumps to that orbit's position at once. ter-7wbc made a Kepler child stop (Static driver) when the grandparent has no point gravity, and kept the jump in the other case. A test documents the jump.

Design to decide: keep the jump and document it, or turn the frame's state at that moment into new orbital elements round the grandparent, so its position and velocity carry over.

## Acceptance Criteria

A Kepler frame keeps its position and velocity when its parent is destroyed, or the design records why it jumps.

