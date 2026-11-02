---
id: ter-hery
status: in_progress
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

**2026-11-02T23:06:48Z**

2026-11-02: launched as workflow run 20eed5e0-d19f-4281-91af-f1384e10fc32 (version 869b0b07), base a5f727e95, branch ter-hery-jolt-thread-count, brief /tmp/briefs/hery.txt. The recorded failure counted 8 threads where 9 to 10 were expected, so a thread of the process ended between the two counts; the margin of one covers only a thread that starts.
