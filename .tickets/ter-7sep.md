---
id: ter-7sep
status: in_progress
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

