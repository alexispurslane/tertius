---
id: ter-fij5
status: closed
deps: []
links: []
created: 2026-10-17T00:59:16Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# The gravity pass allocates Jolt body wrappers again after each garbage collection

From ter-pnco: the backend's gravity pass (ApplyGravity in src/Tertius.Physics/JoltPhysicsBackend.Step.cs) reads BodyLockWrite.Body for every awake body in every step. That goes through JoltPhysicsSharp's locked wrapper table, and after each garbage collection the binding allocates the wrappers again: about 10 KB per collection for 100 bodies, on the physics thread. So the allocation test of ter-pnco cannot force collections. Use the body pointer of the lock with direct JoltNative calls there, as the contact callbacks and ray casts do, and then let StepsWithManyPersistingContactsAndAPendingPairAllocateNothingOnAnyThread force collections.


## Notes

**2026-10-27T11:56:07Z**

2026-10-27: merged (fd19d99a); full Checked on the merged main: 9,062 total, 0 failed. Coverage of the changed lines 84/84 (100%), branches 26/26.
