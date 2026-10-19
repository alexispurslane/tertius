---
id: ter-g0vd
status: open
deps: []
links: []
created: 2026-10-19T15:56:06Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [frames, physics]
---
# Should a kinematic hull refuse a spawn velocity too?

Found by ter-xdt8 (2026-10-19). A spawn now refuses a velocity on every entity that physics does not integrate, except a kinematic hull, which keeps its spawn velocity as its frame's velocity (the design's fork 3 text, and the test AKinematicHullKeepsItsSpawnVelocityAsItsFramesVelocity); the numeric update reads that velocity to give a fast hull its following frame at spawn. Since a spawn can carry a first motion intent, the hull's start could come from that intent alone: the hull would refuse a velocity like every other kinematic body, and the numeric update would read the pending intent (the derived read of the step in flight). Ask the user whether to make that change; it reverses part of fork 3.

