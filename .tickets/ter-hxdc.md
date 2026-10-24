---
id: ter-hxdc
status: closed
deps: []
links: []
created: 2026-10-24T20:05:42Z
type: task
priority: 1
assignee: Alexis Purslane
---
# Faster Physics tests: table check at first use, test JIT, physics handoff spin, split long classes

The Physics test run took 35-70 s wall and about 150 CPU-seconds on 2026-10-24. Measured causes: (1) EntityTable.AssertInvariants scans every never-used slot of all 21 columns (2.1 MB at the default capacity 1024) twice per tick, about half of all CPU; (2) tiered JIT keeps hot code at Tier0 in the test process, because new methods keep JIT-ing while 1400 tests start; (3) PhysicsWorker's SemaphoreSlim and ManualResetEventSlim spin and yield before they block, about 6 CPU-seconds of kernel time; (4) a few long classes set the wall time, because xUnit runs one class's tests in sequence.

User's decisions, 2026-10-24: check a fresh slot at first use and drop the tail scan, and also give test worlds a small InitialCapacity; turn TieredCompilation off in the test projects; measure the spin-wait with the Blue Danube generator and drop the spin if it is at all workable; split RailsFollowTests, ReboundTests, SpinStationBodyTests and other long classes.


## Notes

**2026-10-24T20:20:20Z**

Built on branch ter-hxdc-faster-physics-tests (Claude, 2026-10-24).
- EntityTable: a fresh slot is checked (free, generation 0, no body or emitter bit, clear in every column) by a precondition when the table first takes it, in AllocateSlot and in EndGameplay for spawns past their pool. AssertInvariants no longer scans the unused slots, and its mark arrays and emitter loop cover slotCount, not Capacity. Hand mutations: dropping the precondition, dropping the column part of IsFresh, skipping the check on the fallback path, and putting the tail scan back each fail a test. Equivalent for the tests: the body and emitter bits of IsFresh (no test can set a bit in an unused slot) and the arrays sized by Capacity.
- Test worlds start with WorldTesting.TestCapacity = 64 entities.
- TieredCompilation is false in every *.Tests project (Directory.Build.props).
- PhysicsWorker: ManualResetEventSlim with spinCount 0 for both handoffs. Blue Danube generator in Release, 3 runs each: with spin 29.5 s wall, 44-46 CPU-s; without 26-35 s wall, 35-42 CPU-s; the same script byte for byte.
- Five long tests or theories moved into nested classes (tests/STYLE.md, 'Long tests').
- Physics tests, Checked: from 35-70 s wall and about 150 CPU-s to about 10 s wall and 42-48 CPU-s. The wall time is now bound by total CPU, not by one class. About 15 s of kernel time remains, spread over Jolt's worker wake-ups and thread waits.
