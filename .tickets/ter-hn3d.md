---
id: ter-hn3d
status: closed
deps: []
links: []
created: 2026-11-02T06:37:40Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [tests, flaky]
---
# DeterminismTests.TheRunOnEightWorkersReallyRunsGameplayOnSeveralThreads fails when the thread pool is small and busy

On 2026-11-02, ter-hxca's official Stryker run (tools/stryker/changed.py 'fde41c1a2^1' <out> --head fde41c1a2) stopped in Stryker's initial test run, because this test failed: runs.EightWorkersGameplayThreads was 1. The run starts Stryker at nice 20 with DOTNET_PROCESSOR_COUNT=4, so the test host has a thread pool sized for 4 processors, and xunit runs 4 test classes at once. The same settings passed in cal-6 and cal-7, and the rerun started right after. So the test depends on the scheduling: when the pool threads are busy with other test classes, the thread that calls Tick can run every chunk itself. Log: /tmp/hxca/cal-8-flake/Core/out/logs. The official run stops on any failing initial test (break-on-initial-test-failure), which is right, but each such stop costs a rerun. Make the test check what it means (that the run can use several threads) in a way that holds under a busy pool, for example by holding the calling thread until a second thread has taken a chunk.


## Notes

**2026-11-02T07:57:03Z**

2026-11-02: workflow run ef9ae0ea-2bbc-438d-9c94-1fbf981f12d0 (version 869b0b07), branch ter-hn3d-determinism-busy-pool from 207f9672b.

**2026-11-02T09:22:33Z**

Cause: Parallel.For runs one replica on the thread that calls Tick and queues the others to the thread pool. A run on eight workers takes about 100 ms. If no pool thread is free for that long, because the pool's threads run other test classes, the calling thread takes every chunk itself and the test counts one thread. The engine has no defect: in a normal run, 191 to 200 of the 200 ticks ran gameplay on several threads (a temporary probe, 16 runs).

Reproduction before the fix. Under the official settings (nice 20, DOTNET_PROCESSOR_COUNT=4), the Simulation test host had 0 failures of the thread test in 193 runs: 20 on a quiet machine, 30 and 40 at loads of 10 to 50, 100 of a heavy subset of classes, and 3 beside the Physics, Core and Unions hosts. The logs in /tmp/hn3d hold 48 more runs with 0 failures (cap2, cap4, probe, probe5, probe4x3), but they keep only a summary line, so their settings are not on record. The official Stryker initial runs of ter-hxca failed 1 time in 10 (/tmp/hxca/cal-*/*/out/logs). A busy-pool model made it fail on purpose: a temporary probe queued 16 work items of Thread.Sleep(3000) to the pool just before the first run on eight workers, as other test classes hold the pool threads. The old test failed 10 of 10 runs with "Expected runs.EightWorkersGameplayThreads to be greater than 1, but found 1", the message of the Stryker failure.

Fix, in the test only: GameplayThreads in DeterminismTests records the threads that run gameplay, and in a run on several workers it holds the calling thread in its first entity until another thread runs one. The hold waits at most 30 s (SpinWait.SpinUntil), so a world that runs every chunk on the calling thread fails the test instead of hanging it.

After the fix, the same busy-pool model passed 200 of 200 runs (after16a, after16b and after16c). Those logs keep only a summary line, so the times and thread counts of these runs are not on record. The third note repeats the measurement on the final code with one line per run.

Hand mutation: World.RunGameplay's Parallel.For replaced by `for (int c = 0; c < count; c++) runChunk(c);`. The thread test failed after 62 s (the 30 s hold in each of the two runs on eight workers) with "found 1", and the other DeterminismTests passed. World.Tick.cs was restored and touched.

**2026-11-02T09:37:15Z**

The commit report named Run (122 lines) and Chatter.Handle (80 lines), which the fix touched. Both are now split into named steps (SpawnFrames, SpawnScatter, SpawnCollisionCourses, TickAndCount, HashOf; FoldIntoHash, Wander, Talk, SpawnChild, Churn) with the same order of random draws: the one-worker run ends in the same RunResult before and after (Hash 12162390961502245046, Messages 24147, HandOffs 25, Contacts 53). On the final code, the hand mutation failed the thread test again after 62 s, and the busy-pool model passed 10 of 10 runs.

**2026-11-02T10:25:00Z**

The review fixes. GameplayThreads.RecordCurrentThread is now RecordThreadAndHoldTickThread, so the call in Chatter.Handle shows the hold. The flag anotherThreadRan is gone: the hold waits for threads.Count > 1, because the tick thread is already in the set. The frames of the workload are one record struct, WorkloadFrames. The reason for the hold lives only in the GameplayThreads summary.

The thread test no longer returns early on one processor. With the hand mutation (Parallel.For in World.RunGameplay replaced by `for (int c = 0; c < count; c++) runChunk(c);`), the old test passed at DOTNET_PROCESSOR_COUNT=1 after 61.5 s, and the new test failed with "found 1" after 61.5 s at DOTNET_PROCESSOR_COUNT=1 and after 61.4 s at DOTNET_PROCESSOR_COUNT=4. Without the mutation, the new test passed 10 of 10 runs at DOTNET_PROCESSOR_COUNT=1 and nice 20. World.Tick.cs was restored and touched.

Measurement on the final code, with the busy-pool model at nice 20, DOTNET_PROCESSOR_COUNT=4, 16 work items of 3000 ms, only the DeterminismTests class. Logs in /tmp/hn3d/fix, one line per run, and the probe lines in probe-before.txt and probe-after.txt.

| Code | Runs | Thread test failed | Run on eight workers | Threads that ran gameplay |
|---|---:|---:|---|---|
| Base 207f9672b | 20 | 20 | 152 to 167 ms | 1 |
| Final | 200 | 0 | 5.8 to 7.3 s | 3 (16 runs), 4 (75), 5 (106), 6 (3) |

**2026-11-02T10:37:31Z**

2026-11-02: merged into main as 49f41b688. tools/test-all.sh passed on the staged merge in Checked and Release, 0 failed. Hardening: the branch changes no src line (only DeterminismTests.cs and this ticket), so no coverage or mutation of src applies. The builder's hand mutant of World.RunGameplay (Parallel.For made sequential) fails the test at DOTNET_PROCESSOR_COUNT 1 and 4. The first official Stryker run on Simulation after this merge is the first test of the fix in the place where it failed.
