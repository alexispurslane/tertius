---
id: ter-zk4g
status: open
deps: [ter-xshi]
links: []
created: 2026-10-27T09:14:32Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, determinism]
---
# Jolt's float step against the tick: one meaning for a body's Jolt velocity

Found by the review of ter-xshi (2026-10-27). Since ter-xshi, the tick is exactly 1/TickRate in doubles, but Jolt steps by the float nearest it: at 60 Hz Jolt's step is 5.2e-8 longer than the tick (at 50 Hz, 2.2e-8 shorter). ter-xshi keeps physics exact where it matters: the two-turn update in a turning frame gives Jolt the chord over the tick times tick / Jolt's step and reads it back by the inverse, the dynamic steer velocities and MoveKinematic divide by Jolt's step, and the hull prediction moves over Jolt's step. Elsewhere a body keeps Jolt's velocity as its chord (ARCHITECTURE.md, 'Threads and the tick'). So Jolt's velocity has two meanings, and at the boundary (SetVelocity and impulses in a turning frame, the character's turning update, the damping half step) the world sees 5.2e-8 relative inconsistencies.

Options, waiting for the user:
(A) Jolt runs in its own time unit. The seam scales every velocity, angular velocity and impulse by tick / Jolt's step going in and by the inverse coming out, every acceleration, force and gravity by its square, and damping and sleep thresholds to match. No exception remains. It changes every body's motion by 5.2e-8 relative and moves the pinned hashes and generated scripts. Engine-wide.
(B) Keep today's rule: exact in turning frames and at the seam sites that ter-xshi lists, with the documented 5.2e-8 inconsistencies elsewhere. No work.
(C) Make the default tick rate 64, which a float holds exactly. Jolt's step is then the tick, no conversion exists anywhere, and 64 ticks are exactly one second. The simulation runs 6.7% more steps per second, scripts that count ticks are regenerated, and a 60 Hz display shows 64 steps a second.

## Acceptance Criteria

The user picks an option. Under A or C, a body's Jolt velocity has one meaning, and SetVelocity in a turning frame reads back exactly.


## Notes

**2026-10-27T11:52:15Z**

2026-10-27: the user chose option C: the default tick rate is 64, which a float holds exactly, so Jolt's step is the tick. A tick rate must be a power of two, so the conversion code goes away everywhere (the two-turn update's tick / Jolt's step factor, the divisions by Jolt's step in the steer velocities and MoveKinematic, the hull prediction over Jolt's step): one meaning for a body's Jolt velocity. WorldOptions refuses another rate with an always-on guard. The scripts that count ticks and the pinned hashes are regenerated. The renderer interpolates between the last two snapshots, so a 60 Hz display stays smooth.
