---
id: ter-gh8i
status: open
deps: []
links: []
created: 2026-10-31T22:14:02Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, allocation]
---
# PhysicsWorker's two ManualResetEventSlim objects allocate 120 bytes once, at a random tick, under load

Found in ter-a00h (see its note of 2026-10-31 on the rails probe). Under load, StepAllocations counted 120 bytes on a step thread in 10 of 1,120 windows of 200 ticks of the rails scene of RefusedRailsAllocationTests, at most once per run and at a random tick (tick 10 to 7,010), mostly on the physics thread and once on the ticking thread. A ping-pong of two ManualResetEventSlim(spinCount: 0) between two threads, with no Tertius code, does the same: in 100 trials of 20,000 rounds under load, 5 trials allocated 120 bytes once on one thread after round 10, at round 154 to 12,949: 1 of 40 trials with tiered compilation and 4 of 60 with DOTNET_TieredCompilation=0. (The 19th trial of every process also allocated about 7,400 bytes on both threads at round 200. It follows the probe's own 320 KB of arrays per trial, so it is not the worker's.) So it is .NET's blocking path of ManualResetEventSlim (likely a lazy object of its lock at the first contention), not the JIT. The allocation tests divide a window by 200 ticks, so 120 bytes gives 0 bytes per tick and no test fails. Decide whether a step that allocates 120 bytes once per process is a bug; if so, make the worker's hand-off allocate nothing after the first step, for example by forcing that lazy object at construction, or with a primitive that does not allocate when it blocks.

