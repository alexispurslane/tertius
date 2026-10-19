---
id: ter-dz7g
status: open
deps: []
links: []
created: 2026-10-19T23:36:33Z
type: feature
priority: 3
assignee: Alexis Purslane
tags: [engine, camera]
---
# An attachment that follows only its parent's position, in the axes of the parent's frame

Found by ter-2n31 (2026-10-19). AttachTo applies the offset in the subject's axes after the step that runs beside the camera's tick, so a sudden move of the person (about 5 cm at a walking turn, 0.3 m at a stair) and the engine's instant turn of a character's up (up to 90 degrees, at the ignition and at the ends of the ramp) each reach the demo's chase camera for one tick. An attachment mode that follows only the parent's position, in the axes of the parent's frame, would let the chase camera smooth those moments too. Not needed for the demo, whose sections film those moments with fixed cameras. Engine change: goes to the user first.

