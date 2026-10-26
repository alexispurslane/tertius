---
id: ter-nkyv
status: open
deps: []
links: [ter-cqgt]
created: 2026-10-25T20:21:22Z
type: bug
priority: 2
assignee: Alexis Purslane
tags: [tools, testing, physics]
---
# The Physics tests stop with exit 143 after about 25 minutes in a full run under load

Twice on 2026-10-25, in the ter-nl5u worktree, a full test run (dotnet test --solution Tertius.slnx) ended with exit code 143 (SIGTERM) in Tertius.Physics.Tests, while every other project passed. Once in Release, after 26 min 51 s with 926 tests passed, at a load average of 48 to 100. Once in Checked, after 24 min 48 s with 463 of 1411 tests passed, at a load average of about 90. Each time the same project, run alone right after, passed in 10 to 12 s. So a test hangs or slows by orders of magnitude when the machine is loaded, and something stops the process at about 25 minutes. Find which test stalls (a hang dump, or the test platform's --hangdump option), and whether it is a Jolt job-thread starvation, a lock, or a wait with no time limit. Related: ter-cqgt, the slow physics tests under coverage.

## Acceptance Criteria

A full Checked and Release run at a load average of 50 or more finishes Tertius.Physics.Tests, or the stalling test is named and fixed.

