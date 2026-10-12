---
id: ter-jtyn
status: open
deps: []
links: []
created: 2026-10-11T22:00:39Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# Stop stepping a frame's physics system on every tick for a character that stands still

The walk step of the vertical slice (ter-ai0t) put one person in the orbiting ship. The full headless run of scripts/orbit.json then went from 24 to 27 s to 37 to 39 s in a Release build, about 40 µs more per tick over 332,692 ticks. Before the person, the ship's own Jolt system held only the static interior, so physics skipped it: JoltPhysicsBackend.Step skips a system only when it has no character and no active body. A Jolt CharacterVirtual never sleeps, so now the system runs PhysicsSystem.Update and CharacterVirtual.ExtendedUpdate on every tick, even while the person stands still.

First profile the run to confirm where the 40 µs go. If it is the system step, let a character that stands on something static, with no desired velocity, no pending jump or impulse, and a ground that does not move, skip its update and its system's step, as a sleeping body does, and wake it on any intent, contact or change of gravity.

## Acceptance Criteria

The cost per tick of a character that stands still is measured. If it is cut, the full orbit run with the person is within a few seconds of the run without it, and the character tests and the walk slice tests still pass.

