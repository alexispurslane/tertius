---
id: ter-qi9z
status: open
deps: [ter-0saa]
links: []
created: 2026-11-01T18:41:22Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, direct-light, design]
---
# Unshadowed lamps light through walls: a visibility model for lamps that works in a dynamic, physics-driven world

The user, 2026-11-01: to stop unshadowed lamps from shining through walls, the engine can eventually need something like ter-ww0h's design A (rooms and doorways), but it is not clear how that can work in a world as dynamic and physics-driven as this game. ter-ww0h measured that unshadowed lamps light 12 M lamp-pixel pairs per frame on demo-exit at a cost of 1.65 to 2.04 ms, and that some of that light crosses walls; the share that crosses walls is not yet measured. A lead to examine, not a decision: interiors live in their own semantic frames, so a ship's or a station's rooms and doorways stay rigid in that frame while the physics moves the frame. A room graph could then belong to each interior's frame and move with it, with door state as the only live part, the way the environment maps reuse the frame tree instead of a second hierarchy. Loose bodies, broken walls and hand-built structures need an answer of their own. Other directions to compare: a shadow for every lamp at a low resolution, and a coarse occupancy volume built from the physics shapes. Needs a design with two options for the user before any code.

## Acceptance Criteria

A design doc in docs/plans/ that measures the share of unshadowed light that crosses walls, compares two visibility models on cost and on the dynamic cases (moving interiors, doors, loose bodies, broken or built walls), and records the user's choice.

