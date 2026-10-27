---
id: ter-pwnc
status: open
deps: []
links: []
created: 2026-10-27T11:12:56Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# The ghost pass allocates a Jolt body wrapper in every step for a ghost that moves

From the review of ter-fij5: FrameWorld.WakeAround (src/Tertius.Physics/Internal/FrameWorld.cs) reads the bounds and the object layer of the body through BodyLockRead.Body, JoltPhysicsSharp's managed wrapper, which the binding makes again after each garbage collection (Jolt pitfall 9). WantGhost in JoltPhysicsBackend.HandOff.cs calls WakeAround twice in every step for each ghost that IsAt does not find at its new pose. Jolt moves a kinematic ghost by the last step's velocity while a dynamic body moves by v + g dt, so a ghost of a falling or thrusting body misses IsAt's 1 um limit by about g dt^2 = 2.7 mm in every step, and its wrapper is made again after every collection. Measured on the ter-fij5 branch: a dynamic ball of radius 0.5 m that falls at 9.81 m/s^2 from x = 10.3 m, y = 2 m in its ground frame, beside a child bubble of radius 10 m at the origin, so that the bubble holds a ghost of it, allocates 96 bytes in every tick after a full collection; with the Body lookup in WakeAround removed it allocates 0. That breaks ARCHITECTURE.md's rule that hot paths allocate nothing after warm-up. Fix: read the bounds and the object layer in WakeAround through read.BodyHandle with JoltNative entries (JPH_Body_GetWorldSpaceBounds, JPH_Body_GetObjectLayer), as the gravity pass, the ray casts and the contact callbacks do. Test first: a sibling in ContactAllocationTests with that moving ghost and a collection before every counted tick, asserting 0 bytes per tick. Afterwards, update Jolt pitfall 9 in project memory, which names WakeAround as the remaining wrapper path in a per-step loop.

