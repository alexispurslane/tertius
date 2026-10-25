---
id: ter-cqgt
status: open
deps: []
links: []
created: 2026-10-25T02:43:12Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [tools, testing]
---
# The coverage run's Release physics tests take over 20 minutes and drive the load to about 140

On 2026-10-24, tools/coverage/run.sh in a worktree ran the Release physics tests under coverage for 21 minutes, at about 930 % CPU and a load average of 141 on this 10-core machine, before the orchestrator stopped that process. The other modules finished in a few minutes each, and the Debug physics run finished. The likely cause is xUnit's parallel test classes together with Jolt's job threads, each instrumented. Find out whether the run needs a cap on the test parallelism under coverage, or whether a test misbehaves under instrumentation in Release.

## Acceptance Criteria

A coverage run of every module in Debug and Release finishes with the load below twice the core count, and tools/README.md says how long it takes.

