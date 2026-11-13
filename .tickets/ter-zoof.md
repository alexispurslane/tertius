---
id: ter-zoof
status: closed
deps: []
links: []
created: 2026-11-12T21:34:47Z
type: task
priority: 2
assignee: Alexis Purslane
---
# GPU test runs take the GPU lock in shared mode, so that a timed run never overlaps them

On 2026-11-12 the Mac's load reached 44 to 53 three times while an agent timed the deck under the exclusive GPU lock, because another agent's Checked or Release suite ran the Rendering and Host GPU tests at the same time. Those tests do not take the lock (tools/gpu-lock.sh: only timed runs and untimed stills take it). The sweep agent lost three of eight Mac traces to a falling GPU clock this way. Fix: every test run that uses the GPU takes the lock in shared mode for its whole run, wherever it starts (the pre-commit hook, tools/test-all.sh, or a plain dotnet test in a worktree). One candidate is a module initializer or fixture in tests/Tertius.Testing that takes the shared lock through the same record and liveness rule as tools/gpu-lock.sh and tools/stryker/run.py. Measure what it costs the suites in waiting time, and say whether the CPU-only test projects also need a guard, because a CPU load near 40 may lower the GPU clock by itself.


## Notes

**2026-11-13T00:53:01Z**

Done on branch ter-zoof-gpu-test-lock (2026-11-12). GpuTestLock in tests/Tertius.Testing is an xunit assembly fixture of Tertius.Rendering.Tests and Tertius.Host.Tests: it holds the GPU lock in its shared mode through the main checkout's tools/gpu-lock.sh from the first test to the last, and frees it within 0.2 s of the test process's end. gpu-lock.sh runs its command with TERTIUS_GPU_LOCK_HELD=exclusive or shared, and a test run that sees it takes nothing, so a test under its parent's take cannot wait for it forever. Test runs ask for a cap of 16 shared holders; a still keeps its cap of 3.

Cost: a free take and release cost 0.08 s per GPU project. Over the 94 hours before, timed runs held the lock alone 36 % of the time (holds: median 1 min, p90 15 min, max 91 min), so a suite that starts at a random moment waits about 5.6 min on average; that history still holds untimed exclusive takes from before the shared mode, so the wait from now on should be lower. Seen live: a Host class waited 772 s behind another agent's exclusive mutation take.

tools/mutate.py: it takes no lock and needs none now, because each mutant's test run takes the shared lock. An exclusive wrapper only makes timed runs wait, as the 28 minutes of the tile agent did; a shared wrapper is harmless.

CPU-only projects: no guard. Their suites take under a minute together (Core 16 s, Simulation 14 s, Physics 35 s), and the long CPU load, the official Stryker run, goes to the cloud by default. Open a ticket if a trace is lost while only CPU work runs.
