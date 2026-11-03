---
id: ter-zrk6
status: open
deps: []
links: []
created: 2026-11-03T00:10:34Z
type: task
priority: 4
assignee: Alexis Purslane
tags: [physics, tests, linux]
---
# On Linux, the Jolt thread count tests count only threads named Worker N, with no margin

JoltThreadCountTests (tests/Tertius.Physics.Tests/WorkerThreadsTests.cs) count the thread IDs that are present after a backend starts and absent before (ter-hery). They allow one thread more than Jolt's worker count, because on macOS nothing marks a Jolt worker, so a thread that the runtime starts in the meantime counts too. On Linux, Jolt names each worker "Worker N" (Jolt/Core/JobSystemThreadPool.cpp, ThreadMain, under JPH_PLATFORM_LINUX). The Linux branch of ThreadIds counts the same way as macOS only because it never ran on Linux. To do on a Linux machine: count the new threads whose name (/proc/self/task/<tid>/comm) starts with "Worker ", and require exactly the worker count, with no margin. Make sure first, in a run, that the name is set before the backend's constructor returns, because each worker sets its own name after it starts. If the name can come late, record that and keep the margin.

## Acceptance Criteria

On Linux, both thread count tests require the exact worker count, and 100 runs of the class under load pass. The class doc states the margin only for macOS.

