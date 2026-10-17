---
id: ter-uu1x
status: open
deps: []
links: [ter-kugq, ter-jwzd, ter-dhkf]
created: 2026-10-17T14:34:17Z
type: epic
priority: 1
assignee: Alexis Purslane
tags: [physics, frames]
---
# Numeric frames: fewer of them, and more reliable

Design: docs/plans/numeric-frames.md. This epic adds two sections to it: "Thresholds from measured precision" and "Ghosts as infinitely heavy".

Goal: make numeric frames only where float precision needs them, so there are fewer of them and the merge test runs less often. Then the merge test can afford more inputs: everything that a group cannot move counts as infinitely heavy, so a numeric frame holds no ghosts.

The precision measurements (ter-kugq, merged 2026-10-17, tests in tests/Tertius.Physics.Tests/Precision, explicit) give the numbers. Distance from the origin added no error up to 9,000 km. Speed adds error only along an axis where a velocity changes. Relative errors between bodies that move together stay small: at 1 km/s the gap error is 1.3 mm per minute and the relative velocity error 0.024 mm/s. A body that thrusts drifts fast relative to its neighbors: 2.1 mm/s per minute at 50 m/s, 85 mm/s per minute at 1 km/s.

User decisions (2026-10-17):
- A group with a body that thrusts (characters included) keeps a create speed of 50 m/s. The user accepted the drift of about 2 mm/s per minute of thrust at that speed, against the doc's target of 1 mm/s.
- A group of free bodies only (coasting, colliding, pushing each other, falling together) gets a create speed of about 1 km/s.
- A moving platform that carries things is always a semantic frame (see ter-jwzd).
- Change 1 of the three general changes: make falling frames only where float precision needs them, with one rule that no frame inside a turning frame falls.
- Change 2: everything that a group cannot move counts as infinitely heavy: anchors, interiors and ghosts. The anchors and interiors are ter-dhkf. The ghosts are this epic.

