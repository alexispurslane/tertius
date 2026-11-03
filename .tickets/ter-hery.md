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

Done on branch ter-hery-jolt-thread-count (2026-11-02). Revised 2026-11-03 after the review, from the kept evidence below.

Cause. The count was one too low, so it was not a thread that the runtime starts. A thread of the process ends between the two counts: the Jolt worker of a backend that was just disposed. Dispose joins the worker, but the kernel still lists it for a short time. ThreadsStartedBy disposes a warm-up backend just before its first count, and the previous test or loop disposes its backend too. Two old ways to count lose a worker to such a thread:
- The plain count of Process.Threads. The ended worker is in the first count and absent from the second. Process.Threads lists one entry per kernel thread and merges none (dotnet/runtime, Interop.libproc.cs, GetAllThreadsInProcess). In the evidence runs, its entry count equals the kernel's thread count whenever no thread ended between the two reads.
- A set difference of ProcessThread.Id. On macOS this ID is the low 32 bits of the thread's pthread_t address, the handle that proc_pidinfo flavor PROC_PIDLISTTHREADS gives (XNU osfmk/kern/bsd_kern.c, fill_taskthreadlist: cthread_self). A new worker can get the freed address of a disposed worker that the kernel still lists. Then its ID is already in the first set, even when no thread ends.

Evidence. A throwaway test repeated ThreadsStartedBy's sequence 300 times: a warm-up backend of 1 worker, Dispose, snapshot, a backend of 2 workers, snapshot. Each snapshot reads the kernel's unique IDs, Process.Threads, the raw PROC_PIDLISTTHREADS handles, and the unique IDs again, back to back. The source is /private/tmp/hery/evidence2/ThreadSnapshotEvidence.cs. The logs are evidence2.log (1-minute load 5.6) and evidence2-heavy.log (load 19.0) in the same folder.

| Count of the 2 workers | Short at load 5.6 | Short at load 19.0 |
|---|---|---|
| Plain count of Process.Threads | 127 of 300 | 76 of 300 |
| Set difference of ProcessThread.Id | 127 of 300 | 81 of 300 |
| Set difference of unique kernel IDs | 0 of 300 | 0 of 300 |

- In every loop where the plain count was short, a thread ended between the reads. At load 5.6, the warm-up's worker was still listed at the first snapshot in 285 loops and ended before the second in all 285. In the 158 loops where it ended before Process.Threads was read, the plain count was right by chance.
- At load 19.0 the plain count fell to 0 and to -1 in 8 loops, where the 2 workers of the previous loop also lingered and ended.
- In 5 loops at load 19.0, the warm-up's worker stayed listed in both snapshots and no thread ended. The plain count was right, but the ProcessThread.Id set difference was 1: a new worker took the lingering worker's ID.
- Each thread that the unique IDs show ending was a disposed backend's worker: the warm-up's, or one of the previous loop's. In 1 short loop at load 19.0, a thread ended between the reads of one snapshot, and the log does not say which.

The earlier throwaway logs (evidence.log, full.log and full2.log in /private/tmp/hery) came from a source that was not kept. Their uniqueEnded field showed 0 in the short loops, and the kept runs contradict that. What their started field counts is not known. Its value of 1 for 2 workers in full2.log run 18 fits a ProcessThread.Id set difference. This note uses only their pass and fail counts.

Method. The tests count the set difference of IDs: the thread IDs present after the backend starts and absent before. On macOS the IDs are the kernel's unique 64-bit thread IDs from proc_pidinfo flavor 28, PROC_PIDLISTTHREADIDS. XNU defines it in bsd/sys/proc_info_private.h, and the SDK header leaves it out. On Linux they are ProcessThread.Id, the kernel's thread ID, which Linux gives out again only after it wraps around pid_max. The margin of one thread stays, because on macOS nothing tells a thread that the runtime starts in the meantime from a Jolt worker. Rejected exact marks:
- Jolt names its workers ("Worker N") only on Windows and Linux (Jolt/Core/JobSystemThreadPool.cpp, ThreadMain). ter-zrk6 records a Linux count by name with no margin. The Linux branch keeps the margin only because it never ran on Linux.
- joltc does not expose JobSystemThreadPool::GetMaxConcurrency.
- JobSystemThreadPool::SetThreadInitFunction can mark each worker. But joltc's JPH_JobSystemThreadPool_Create starts the threads in the constructor and exposes no hook. So it needs a joltc patch and a src seam, which is out of proportion for a test fix.
No src change.

Tests. AThreadThatEndsWhileABackendStartsLowersNoCount pins the counting rule on a snapshot pair. AThreadThatStartsAfterAnotherEndedHasAnIdOfItsOwn starts two threads one after the other and requires that the second has an ID that the first did not have. Both were red on the old rules and green on the new ones at a load of 5.8. The logs and the patch that switches the old rules on are in /private/tmp/hery/red:
- "Expected ThreadsStartedBetween(before: [1, 2, 3], after: [1, 2, 4, 5]) to be 2 because Jolt started threads 4 and 5, and thread 3 ended, but found 1."
- "Did not expect collection to intersect with {1847161056UL} because an ID that came back would hide a new thread from the count, but found the following shared items {1847161056UL}."

Stress, JoltThreadCountTests alone, each run in a fresh Release test host:
- Before, 100 runs at a 1-minute load of 6.7 to 12.9 (mean 9.5): 1 failed run, "Expected ThreadsStartedBy(new() { WorkerThreads = 2 }) to be between 2 and 3, but found 1."
- After, 100 runs at a load of 5.7 to 6.5 (mean 6.1): 0 failed.
- After, 100 runs at a load of 14.0 to 18.9 (mean 15.7): 0 failed.
- After, 100 runs at a load of 19.7 to 32.0 (mean 24.6): 0 failed (/private/tmp/hery/stress-after-heavy).
Full Physics test project, Release: before 2 failed of 50 runs (load mean 23.6), after 0 failed of 25 runs (load 5.3 to 27.1, mean 14.7). These pass counts alone cannot show a lower failure rate, because the old rate was low. The snapshot table above shows it.
