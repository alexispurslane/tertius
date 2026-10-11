---
id: ter-ziae
status: open
deps: [ter-dtw2]
links: []
created: 2026-10-11T23:45:16Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-1yed
tags: [design, physics, simulation]
---
# Design queries, sensors, collision masks and entity flags with the user

Write docs/plans/queries-and-sensors.md for the epic ter-1yed. Design each open decision twice in chat, and let the user choose. Each child ticket of the epic lists its own open questions. The decisions that span them come first:

- Where and when a physics query runs. Gameplay runs beside the physics step, and the step holds the native Jolt lock (JoltRuntime.NativeGate) for its whole length. So a component cannot call Jolt during its Handle call. Three places can answer a query:
  1. The physics thread, at the start of the next step, before it moves anything. Jolt then holds the end of the previous tick, which matches the front snapshot. The answer to a question asked in tick N arrives in tick N+2.
  2. The sync point, after the step. This is safe and serial. Answers arrive in tick N+1, but their cost lands on the serial part of the tick.
  3. A copy of the previous tick's shapes on the gameplay side. Answers come at once. But Simulation has no shapes and cannot reference Physics, so this needs its own intersection code.
- Whether a query is a call that returns at once, or a request message whose answer arrives as an event.
- Determinism. Jolt returns narrow phase hits in a varying order, so every result needs a fixed order, for example by distance and then by entity index.
- Which of these features mods can use. The mod API list in docs/plans/content-packages.md must name them.

## Acceptance Criteria

docs/plans/queries-and-sensors.md exists, the user approved each of its decisions, and each child ticket of ter-1yed names its section.

