---
id: ter-cvqr
status: open
deps: []
links: []
created: 2026-10-20T14:50:37Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, characters]
---
# Measure the press of a walking person on a dynamic curved floor, which WeightInput says gains m v^2 / r

The comment of WeightInput in src/Tertius.Physics/JoltPhysicsBackend.Step.cs says that on a station's curved floor the turns of its faces add m v^2 / r to the press of a walking person. No test measures it. The weight input itself is the effective gravity plus the Coriolis term, so that part can come only from Jolt's collision of the character with each next face. The spin station scene measured the person's path, not the press, because Jolt presses a standing character only onto a dynamic body, and the station's floor is kinematic (ter-n44b note of 2026-10-20). Measure the press on a dynamic faceted floor (for example a curved plank of the ring's facets, as the plank test of SpinStationCharacterTests does for a flat one) against m (omega r +- v)^2 / r, and fix the comment or the press. At 3 m/s on a floor 100 m from the axis, the part is 0.091 m/s², 0.8 percent of the weight.

