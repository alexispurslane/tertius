---
id: ter-a00h
status: in_progress
deps: []
links: []
created: 2026-10-31T17:25:55Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [tests, flaky]
---
# Make ContactAllocationTests.StepsOfAwakeBodiesInATurningFrameAllocateNothingOnAnyThread stable under load: it measured 1 byte per tick in both windows once

2026-10-31, the Release run of tools/test-all.sh on the ter-3yy5 merge (rendering only, no physics change), at a load of about 34 on 10 cores: 'Expected Math.Min(first, second) to be 0L because a step of awake bodies in a turning frame must not allocate (the windows gave 1 and 1 bytes per tick), but found 1L'. The same class passed alone in Release a minute later at a load of 42. The test counts the whole process's allocations (GC.GetTotalAllocatedBytes(precise: true)) over 200 ticks per window, so 1 byte per tick is 200 to 399 bytes in each window. Likely cause, not confirmed: an allocation on another thread of the test process during the windows, for example the test platform's progress timer or a thread pool thread that the load makes the pool inject; a slow tick under load lets more of them land in a window. ter-a4s2 made the other allocation tests take the fewest of up to three windows (Allocations.Fewest); this test takes the smaller of two windows of its own. Next step: reproduce under load, find the allocating thread (for example with an EventListener on the GC AllocationTick event or with dotnet-trace), then fix the test or the code. Per the testing rules, a flaky test is triaged, not retried.


## Notes

**2026-10-31T18:30:43Z**

2026-10-31, second occurrence: the Release run of tools/test-all.sh on the ter-ox1a merge (no physics change), at a 5-minute load of about 37, failed with the same text, 'the windows gave 1 and 1 bytes per tick'. The class passed alone in Release a minute later. Both failures give exactly 1 byte per tick in both windows of 200 ticks, so something allocates about 200 to 399 bytes in each window under load, not a one-time cost. Raised to P2: it now fails one full run in two on a busy machine.

**2026-10-31T19:20:49Z**

2026-10-31, third occurrence: the Release run of tools/test-all.sh on the ter-uu2y merge (no physics change), at a 5-minute load of about 23 while a coverage run of another branch ran, failed two other allocation tests of the same kind. ContactAllocationTests.StepsWithManyPersistingContactsAndAPendingPairAllocateNothingOnAnyThread: 'the windows gave 3 and 1 bytes per tick'. RefusedRailsAllocationTests.ARailsComponentThatTheGuardRefusesAllocatesNothingInTheTicksAfterItsLog: 'the windows gave 173 and 36 bytes per tick'. Both classes passed alone in Release a minute later. So the flake is not one test: every test that counts the whole process's allocations over a window can fail on a busy machine. A fix belongs in the shared measurement, not in one test.

**2026-10-31T19:38:11Z**

2026-10-31, fourth occurrence: the Release run of tools/test-all.sh on the ter-nwhv merge (no physics change) failed StepsOfAwakeBodiesInATurningFrameAllocateNothingOnAnyThread again, 'the windows gave 1 and 1 bytes per tick', at a 5-minute load of about 44. It passed alone in Release a minute later.

**2026-10-31T19:41:12Z**

2026-10-31: workflow run e4e12510-6c35-41d8-8186-df3a8096e685 (version 869b0b07-3413-4b06-8707-a88a91bf6e91), branch ter-a00h-allocation-windows from faed5b97. Settled: the tests keep proving that no thread that does the step's work allocates; threads that do no work of the step do not count; no retry, extra windows or raised bound. A production-code hook for the measurement is a fork.
