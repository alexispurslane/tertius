---
id: ter-1wcb
status: open
deps: []
links: []
created: 2026-10-20T08:55:34Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics]
---
# Give a thrusting body's instant velocity and a thrusting ship's record one rule

Found by ter-vja8 (2026-10-20). JoltPhysicsBackend.InstantVelocity adds half a step of only the field and the damping to a body's chord, while TryHullRecord adds half a step of the measured proper acceleration, which holds an intent's thrust. So a thrusting body's velocity read and hand-off, and a thrusting ship's frame record, use two rules for one meaning (the instant velocity at the end of the step), also in frames that do not turn. No test covers a thrusting body's instant velocity. Decide the one rule (the design's 'Instant velocities and chords' in ARCHITECTURE.md says the instant velocity is the exact inverse of the chord rule) and pin it with a test of a thrusting crate's velocity read and of a thrusting ship's record.

