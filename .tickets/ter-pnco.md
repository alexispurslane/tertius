---
id: ter-pnco
status: closed
deps: [ter-gd22]
links: []
created: 2026-10-16T13:36:33Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [physics, performance]
---
# Cut the cost of Jolt's persisted-contact callback for contacts that wait for their touch

From ter-wu6v: a contact found at Jolt's speculative distance waits as pending, and Jolt's persisted callback detects its later touch. That callback costs about 15% of a tick for 300 awake boxes (1.79 ms against 1.54 ms), all of it in JoltPhysicsSharp's per-call transition, so the backend subscribes only while pending pairs exist. A cheaper way: after the step, run a narrow-phase query for the pending pairs only.


## Notes

**2026-10-16T23:41:21Z**

Decided (User, 2026-10-16): design A. Replace JoltPhysicsSharp's contact events with our own joltc contact listener (JPH_ContactListener_Create, JPH_ContactListener_SetProcs, JPH_PhysicsSystem_SetContactListener) whose procs are [UnmanagedCallersOnly] static C# functions, called through JoltNative with DllImport. Each call reads the two body IDs directly and leaves after one set lookup unless the pair is pending; the added and removed callbacks move over too. joltc keeps one procs table for the whole process, so no listener of the binding may stay alive beside ours: pin that with a test and add it to the Jolt pitfalls memory. Measure with the 300-box benchmark of ter-wu6v (1.54 ms without the persisted callback, 1.79 ms with it). If design A still costs too much, the next step is a patch in our joltc build that keeps the pending set in C++.

**2026-10-17T01:01:22Z**

Merged in de6a037. The extra cost of one pending pair fell from about 62 us (17.5 %) to about 3 us (0.7 %) per tick in ContactHeapBenchmarks (300 awake boxes, 585 persisting contacts). The joltc table trap is in the Jolt pitfalls memory. Follow-up: ter-fij5 (the gravity pass allocates body wrappers after each collection).
