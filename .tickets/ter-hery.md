---
id: ter-hery
status: open
deps: []
links: []
created: 2026-11-02T20:56:04Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, tests, flaky]
---
# JoltThreadCountTests' default-backend count fails under load

Found 2026-11-02 in the Release run of tools/test-all.sh on the merge of ter-b1j3 (9fda8f414), at a 1-minute load near 18 on 10 cores: Tertius.Physics.Tests.JoltThreadCountTests.ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne failed once, and the same test passed when run alone right after (2 of 2). The test counts the threads of the whole process before and after it builds a backend, with a margin of one thread (ter-773q). The runtime can start a thread of its own (for example a thread-pool or finalizer thread) between the two counts, and under load that is more likely. To decide: count only the threads that Jolt starts (for example by name or by the job system's own count), or widen the margin with a reason, so that the test pins Jolt's count and nothing else. The failure has been seen before (an earlier orchestration noted it as a known flake).


## Notes

**2026-11-02T23:40:03Z**

Done on branch ter-hery-jolt-thread-count (2026-11-02).

Cause. The ticket's guess, a thread that the runtime starts, was wrong: the count was one too low. Two effects on macOS together hide a Jolt worker from the count.
- A disposed backend's Jolt worker stays in the kernel's thread list for a short time after Dispose joins it. ThreadsStartedBy disposes a warm-up backend just before its first count, and the previous test disposes its backend too.
- ProcessThread.Id is not unique over time on macOS. A new thread takes the ID of a thread that just ended (the values, such as 0x6C9670E0, look like the low half of a pthread_t address).
So a plain count, and also a set difference of ProcessThread.Id, can count one worker too few.

Evidence. A throwaway test repeated the test's own sequence 300 times at a 1-minute load of 18.9: warm-up backend of 1 worker, Dispose, IDs, backend of 2 workers, IDs. The warm-up's worker was still listed after Dispose in 44 loops. The old count and a ProcessThread.Id set difference both found fewer than 2 threads in 211 loops. A set difference of the kernel's unique thread IDs (proc_pidinfo, PROC_PIDLISTTHREADIDS) found exactly 2 in all 300, also in the 8 loops where a thread really ended between the two snapshots. The thread pool's thread count did not change in any loop. In 50 full Release runs of the Physics tests with logging (load 14 to 41, mean 23.6), 2 runs failed with a count of 1 for 2 workers, with no change of the thread pool.

Method. Jolt names its workers ("Worker N") only on Windows and Linux (Jolt/Core/JobSystemThreadPool.cpp, ThreadMain), so macOS has no mark for them, and joltc does not expose JobSystemThreadPool::GetMaxConcurrency. So the tests use the set difference: the thread IDs present after the backend starts and absent before. On macOS the IDs are the kernel's unique 64-bit thread IDs from proc_pidinfo. Elsewhere they are ProcessThread.Id, the kernel's thread ID on Linux, which Linux gives out again only after it wraps around pid_max. The margin of one thread stays, because on macOS nothing tells a thread that the runtime starts in the meantime from a Jolt worker. No src change.

Tests. AThreadThatEndsWhileABackendStartsLowersNoCount pins the counting rule on a snapshot pair (red on the old rule: 1 for 2). AThreadThatStartsAfterAnotherEndedHasAnIdOfItsOwn starts two threads one after the other and requires the second to have a new ID that the first did not have (red on ProcessThread.Id: the second thread had the first thread's ID 1821798624).

Stress, JoltThreadCountTests alone, each run in a fresh Release test host:
- Before, 100 runs at a 1-minute load of 6.7 to 12.9 (mean 9.5): 1 failed run, "Expected ThreadsStartedBy(new() { WorkerThreads = 2 }) to be between 2 and 3, but found 1."
- After, 100 runs at a load of 5.7 to 6.5 (mean 6.1): 0 failed.
- After, 100 runs at a load of 14.0 to 18.9 (mean 15.7): 0 failed.
Full Physics test project, Release: before 2 failed of 50 runs (load mean 23.6), after 0 failed of 25 runs (load 5.3 to 27.1, mean 14.7).
