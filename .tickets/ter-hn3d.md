---
id: ter-hn3d
status: open
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

