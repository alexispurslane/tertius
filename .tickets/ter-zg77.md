---
id: ter-zg77
status: open
deps: [ter-6r8c]
links: []
created: 2026-10-17T21:44:05Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-ozm8
tags: [frames, physics]
---
# A numeric frame follows every fast kinematic anchor, not only a Kepler hull

Part of the epic on frames that follow their owners. Read the epic and its design doc first. Rule 3 of 'Anchors' in docs/plans/numeric-frames.md gives only a Kepler hull a numeric frame where it rests (the orbit frame). With motion on components, any kinematic anchor that moves fast or far gets a numeric frame that follows its motion samples, and NumericBody.Fixed and NumericBody.Orbits become properties of the body's motion.

