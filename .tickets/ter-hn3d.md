---
id: ter-hn3d
status: in_progress
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

**2026-11-02T09:22:33Z**

Cause: Parallel.For runs one replica on the thread that calls Tick and queues the others to the thread pool. A run on eight workers takes about 100 ms. If no pool thread is free for that long, because the pool's threads run other test classes, the calling thread takes every chunk itself and the test counts one thread. The engine has no defect: in a normal run, 191 to 200 of the 200 ticks ran gameplay on several threads (a temporary probe, 16 runs).

Reproduction before the fix. Under the official settings (nice 20, DOTNET_PROCESSOR_COUNT=4), the Simulation test host passed 0 failures in about 250 runs: 20 on a quiet machine, 30 and 40 at loads of 10 to 50, 100 of a heavy subset of classes, and 3 beside the Physics, Core and Unions hosts. The official Stryker initial runs of ter-hxca failed 1 time in 10 (/tmp/hxca/cal-*/*/out/logs). A busy-pool model made it fail on purpose: a temporary probe queued 16 work items of Thread.Sleep(3000) to the pool just before the first run on eight workers, as other test classes hold the pool threads. The old test failed 10 of 10 runs with "Expected runs.EightWorkersGameplayThreads to be greater than 1, but found 1", the message of the Stryker failure.

Fix, in the test only: GameplayThreads in DeterminismTests records the threads that run gameplay, and in a run on several workers it holds the calling thread in its first entity until another thread runs one. The hold waits at most 30 s (SpinWait.SpinUntil), so a world that runs every chunk on the calling thread fails the test instead of hanging it.

After the fix, the same busy-pool model passed 200 of 200 runs. The run on eight workers took at most 8.1 s and used 3 to 8 threads.

Hand mutation: World.RunGameplay's Parallel.For replaced by `for (int c = 0; c < count; c++) runChunk(c);`. The thread test failed after 62 s (the 30 s hold in each of the two runs on eight workers) with "found 1", and the other DeterminismTests passed. World.Tick.cs was restored and touched.

**2026-11-02T09:37:15Z**

The commit report named Run (122 lines) and Chatter.Handle (80 lines), which the fix touched. Both are now split into named steps (SpawnFrames, SpawnScatter, SpawnCollisionCourses, TickAndCount, HashOf; FoldIntoHash, Wander, Talk, SpawnChild, Churn) with the same order of random draws: the one-worker run ends in the same RunResult before and after (Hash 12162390961502245046, Messages 24147, HandOffs 25, Contacts 53). On the final code, the hand mutation failed the thread test again after 62 s, and the busy-pool model passed 10 of 10 runs.
