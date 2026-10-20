---
id: ter-04lz
status: open
deps: []
links: []
created: 2026-10-20T02:48:42Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-s9m0
tags: [physics, performance]
---
# Cache the reach of the wake test of a turning frame whose rate changes

Found by the review of stage 2 of ter-1bnq (2026-10-19). The wake test of a turning frame bounds the change of the centrifugal and Euler acceleration by the reach of the system: the largest distance from the semantic origin to a point of a dynamic member, which JoltPhysicsBackend.Reach walks over every member. It is measured only when the push alone does not wake the system and the rate changed, but in a frame with angular damping that is every step. DampedSpinningShipBenchmarks (1,000 awake crates in a spinning ship) measured 911 us per tick without angular damping, 976 us with damping 0.05/s, and 926 us with the walk skipped: about 50 ns per dynamic member per step. The damped case also allocates about 101 B more per tick, which goes away when the walk is skipped, though the walk's code allocates nothing; find its source. A cached reach needs a sound bound on how fast it can grow between walks, which the review did not find.

