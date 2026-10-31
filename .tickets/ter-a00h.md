---
id: ter-a00h
status: open
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

**2026-10-31T20:26:01Z**

2026-10-31, reproduction (step 1 of the fix). A temporary probe test ran the scenes of the three failing tests in Release through dotnet test (the test platform, as tools/test-all.sh runs it). For each counted tick it split the bytes of the whole process into the calling thread (GC.GetAllocatedBytesForCurrentThread), the physics thread (a temporary counter around IPhysicsBackend.Step in PhysicsWorker), Jolt's contact callbacks (a temporary counter in ContactListener) and the rest. On an idle machine (1-minute load 3 to 10), the rest was 0 in every window. Under 20 to 24 extra busy loops beside the other builders (1-minute load 68 to 116 on 10 cores), the rest was not zero in some windows: once 120 bytes in a window of the rails scene, with no listener running, in 12 runs of the three scenes (36 windows). The calling thread and the physics thread gave the same bytes as on the idle machine. So under load, a thread that runs no work of the step allocates inside the counted ticks.

**2026-10-31T20:26:01Z**

2026-10-31, evidence of the allocating thread. The runtime's per-allocation event (Microsoft-Windows-DotNETRuntime, keyword GCSampledObjectAllocationHigh 0x200000) works only when EventPipe turns it on at startup (DOTNET_EnableEventPipe=1 with DOTNET_EventPipeConfig=Microsoft-Windows-DotNETRuntime:0x200000:5). With that, an in-process EventListener got each event with its OS thread id and type handle, recorded only inside the counted ticks. It named the calling thread, the physics thread (by pthread_threadid_np) and the finalizer thread (by a finalizer that recorded its own id). In 12 loaded runs, every allocation on a thread that is not the listener's own, the calling thread or the physics thread was on the finalizer thread: System.GCMemoryInfoData (288 bytes each) and ConditionalWeakTable<..., SharedArrayPoolThreadLocalArray[]>.Enumerator, with System.Reflection.Emit.DynamicResolver+DestroyScout outside the counted ticks. That is .NET's shared ArrayPool trimming itself after a full collection (Gen2GcCallback calls GC.GetGCMemoryInfo), which the GC.Collect before each tick starts. GC.WaitForPendingFinalizers did not keep it out of the tick under load. The finalizer thread runs no work of the step, so this is case 3 of the ticket: the measurement is wrong, not the code. The thread of the 173 and 36 bytes per tick of the rails test (no collection in its windows) was not caught in these runs, but no allocation on a thread of the step showed in any loaded window.

**2026-10-31T20:26:01Z**

2026-10-31, found on the way, not this ticket's flake: in the turning-frame scene of StepsOfAwakeBodiesInATurningFrameAllocateNothingOnAnyThread, run longer than the test, the step threads allocate the same bytes in every run from about tick 1,100 to 2,500 (windows of 200 ticks after tick 140: physics thread 13,664, 6,912, 1,080, 816, 408, 864, 408 bytes; calling thread 0, 848, 440, 976, 240, 392, 88). It is the same with DOTNET_TieredCompilation=0, DOTNET_TieredPGO=0 and DOTNET_ReadyToRun=0, so it is the code under test, not the JIT. The test's windows end at tick 520, so the test never sees it. Recorded as its own ticket, ter-60fa.

**2026-10-31T21:01:39Z**

2026-10-31, settled design of the fix (step 3). tests/Tertius.Testing/StepAllocations.cs, beside Allocations.Fewest, counts what the threads of a world's steps allocate, with no production change. (1) The ticking thread: GC.GetAllocatedBytesForCurrentThread around the window. (2) The physics thread: StepAllocations is an IPhysicsBackend that wraps the world's backend and forwards every call; its Step takes the physics thread's own count at the end of each step, measured from the start of the first step, so a window covers the steps and the worker loop between them. (3) Jolt's job threads: the only managed code on them is the backend's three contact callbacks, which joltc reads through one process-wide table pointer (ManagedContactListener::s_Procs in joltc.cpp at the pinned revision). While a StepAllocations lives, joltc gets a table of counting wrappers that call the backend's own callbacks and add what each call allocated on a thread other than the physics thread, whose whole step counts already. Dispose gives joltc the backend's table again. The wrapper table lives as long as the process, as the backend's does. The backend's table pointer comes from ContactListener's private static field 'callbacks' by reflection, with a loud error if it is renamed, so production code gets no test hook. Not counted: the finalizer thread, the thread pool, the test platform's threads, and gameplay workers (the test worlds run with MaxDegreeOfParallelism = 1, so gameplay runs on the ticking thread). Only one StepAllocations lives at a time (the constructor refuses a second), and the tests that use one stay in their DisableParallelization collections, because the wrappers count the contact reports of every world in the process. No production-code hook was needed, so this is not a fork.

**2026-10-31T21:01:39Z**

2026-10-31, settled details. The three classes keep their two windows and the smaller of the two, as before: the fix changes only what is counted, not the number of windows or the bound. ContactAllocationTests still collects the garbage before each tick, for the reason in the test (a collection drops JoltPhysicsSharp's managed wrappers of locked bodies), but it no longer waits for the finalizers: the wait was only there to keep the finalizer thread out of a whole-process count, and under load it did not. The new tests of StepAllocations make the physics thread allocate through FaultInjectingBackend.EditNextOutput, which runs inside the step on the physics thread. A first version overflowed Jolt's per-frame body pair and contact constraint limits to make the backend log on the physics thread, but whether Jolt overflows depends on how its jobs run: it logged in every idle run and in none of 7 of 24 loaded runs, so that version was dropped. tests/STYLE.md, 'Test doubles', and the doc of Allocations now point to StepAllocations.

**2026-10-31T21:01:39Z**

2026-10-31, proof (step 4). (a) A temporary allocation of a 1-byte array in FrameWorld.OnContactPersisted on every thread except 'Tertius physics' made StepsWithManyPersistingContactsAndAPendingPairAllocateNothingOnAnyThread fail: 'the windows gave 2488 and 2658 bytes per tick'. With the job-thread part of the count also turned off (CountJobThread returned at once), the same test passed, so the callback wrappers are what catch an allocation on a Jolt job thread. Both edits were removed. (b) With the physics-thread count broken (physicsThreadBytes = 0), AnAllocationOnThePhysicsThreadDuringAStepCounts failed with 296 bytes against 1,048,576. (c) Against a stub that counted the whole process, AnAllocationOnAThreadThatRunsNoWorkOfTheStepDoesNotCount failed with 1,048,976 bytes. (d) Under load: 24 runs in a row of ContactAllocationTests, RefusedRailsAllocationTests and StepAllocationsTests (8 tests) in Release through dotnet test, with 22 extra busy loops beside the other builders, at a 1-minute load of 63 to 180 on 10 cores (5-minute 110 to 139). All 24 runs passed, 192 of 192 tests.

**2026-10-31T21:14:57Z**

2026-10-31, follow-up commit: NewWorldWithStepAllocations takes no debugChecks parameter (every caller turned the checks off, and the counted ticks run as in a game), and ContactAllocationTests keeps the ball of its ghost test in a static field, BouncyBall, so that the touched test methods stay under the 60-line limit of the commit report. Required checks on the final code: dotnet build Tertius.slnx -c Checked (0 errors, 0 warnings), dotnet test --solution Tertius.slnx -c Checked (9,577 passed, 14 skipped, 0 failed), and the same with -c Release (9,564 passed, 14 skipped, 0 failed).
