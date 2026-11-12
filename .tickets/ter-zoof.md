---
id: ter-zoof
status: open
deps: []
links: []
created: 2026-11-12T21:34:47Z
type: task
priority: 2
assignee: Alexis Purslane
---
# GPU test runs take the GPU lock in shared mode, so that a timed run never overlaps them

On 2026-11-12 the Mac's load reached 44 to 53 three times while an agent timed the deck under the exclusive GPU lock, because another agent's Checked or Release suite ran the Rendering and Host GPU tests at the same time. Those tests do not take the lock (tools/gpu-lock.sh: only timed runs and untimed stills take it). The sweep agent lost three of eight Mac traces to a falling GPU clock this way. Fix: every test run that uses the GPU takes the lock in shared mode for its whole run, wherever it starts (the pre-commit hook, tools/test-all.sh, or a plain dotnet test in a worktree). One candidate is a module initializer or fixture in tests/Tertius.Testing that takes the shared lock through the same record and liveness rule as tools/gpu-lock.sh and tools/stryker/run.py. Measure what it costs the suites in waiting time, and say whether the CPU-only test projects also need a guard, because a CPU load near 40 may lower the GPU clock by itself.

