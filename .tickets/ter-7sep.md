---
id: ter-7sep
status: open
deps: []
links: []
created: 2026-10-25T17:00:53Z
type: bug
priority: 1
assignee: Alexis Purslane
tags: [physics, jolt, tests]
---
# Lock joltc's process-wide map of physics systems, which hangs parallel tests

On 2026-10-25 a full Release run of the Physics tests (in the environment map's worktree) hung for 26 minutes at about 900 % CPU, and the machine's load reached 127. A sample of the test process showed several .NET thread-pool threads, each in all 1,245 samples, inside JPH_PhysicsSystem_Create -> JPH::HashTable<JPH::PhysicsSystem*, ... JPH_PhysicsSystem*>::InsertKey. joltc keeps one process-wide UnorderedMap from each JPH::PhysicsSystem to its JPH_PhysicsSystem wrapper, with no lock. Tests that create physics systems on different threads at the same time corrupt it, and InsertKey then probes forever. It is the same kind of bug as the temp allocator that native/joltc-thread-temp-allocator.patch fixed (ter-a4s2). The game hits it too whenever two backends are created at once.

## Design

Fix it at the source, as the temp allocator patch did: a second patch in native/joltc.nix (and native/build-joltc.sh) that guards every read and write of that map, in create, destroy and lookup, with one std::mutex. Find every other process-wide container in joltc's sources while there, and list each with its verdict in the ticket. Test first: a test that creates and disposes physics backends on many threads at once, in a loop, under a time limit, which hangs or crashes on today's library often enough to be red. Add the finding to the Jolt pitfalls memory.

## Acceptance Criteria

The parallel create test passes 100 runs in a row. The full Physics suite passes in Checked and Release. No process-wide container of joltc is left without a lock or a reason.


## Notes

**2026-10-25T17:25:05Z**

Red: `ConcurrentSystemCreationTests` (32 threads, 3,000 frame systems each, created and disposed at once, 30 s limit) failed on the unpatched library in 9 of 15 runs: hangs in the map's insert, SIGSEGV (exit 139) and SIGABRT (exit 134). 16 threads of 2,000 failed 3 of 10; 8 threads of 3,000 and 32 of 1,000 failed none of 10. Green: `native/joltc-system-map-lock.patch` (one `std::mutex` around the insert in create, the erase in destroy and the lookup in the step listener) passed 100 runs in a row, about 1 s each.

The process-wide state of joltc 886e088 (src/joltc.cpp; joltc.h and joltc_assert.cpp hold none), each with its verdict:

- `s_PhysicsSystems`, the map from each Jolt physics system to its wrapper: guarded by the new patch.
- `s_TempAllocator`: replaced by a `thread_local` allocator per calling thread (joltc-thread-temp-allocator.patch). Safe.
- `s_initialized` and what `JPH_Init` sets in Jolt (`Factory::sInstance`, `RegisterTypes` tables, `JPH::Trace`, `JPH::AssertFailed`, the default allocator hooks): not thread-safe in joltc, but written once, by `Foundation.Init` inside `JoltRuntime.EnsureInitialized`, under its `InitGate`, and only read after. Tertius never calls `Foundation.Shutdown`. Safe.
- `s_TraceFunc`, `s_AssertFailureFunc`: written only by `JPH_SetTraceHandler` and `JPH_SetAssertFailureHandler`, which neither Tertius nor the binding calls. The assert hook does not exist in the Distribution build. Safe.
- The 12 `s_Procs` callback tables (broad-phase layer, object layer, body, shape and sim shape filters, step listener, contact listener, body activation listener, character contact listener, character-vs-character collision, body draw filter, debug renderer): each written by its `*_SetProcs`. An IL scan of JoltPhysicsSharp 2.23.0 shows every call to them in a static constructor, which .NET runs once, before the type has an instance. Tertius writes the contact listener table once more, under `InitGate` (`ContactListener.InstallCallbacks`, pitfall 8). Written once before use, then read only. Safe.
- The function-local `static const` default filters (`g_defaultBroadPhaseLayerFilter`, `g_defaultObjectLayerFilter`, two `g_defaultBodyFilter`): const, and C++11 initializes a function-local static once, thread-safe. Safe.

No other risk found, so the patch holds only the one lock.
