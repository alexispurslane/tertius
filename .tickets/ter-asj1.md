---
id: ter-asj1
status: open
deps: []
links: []
created: 2026-10-31T23:06:30Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [tests, physics]
---
# StepAllocations: tests that kill the job-thread and Dispose mutants on every run

Found by the orchestrator's hand mutations of ter-a00h (2026-10-31, worktree at a7ffb2e7, each mutant built and run against StepAllocationsTests). (1) CountJobThread with 'long bytes = 0 * before;' (job-thread bytes never counted) survived 4 of 4 runs: WhatAContactCallbackAllocatesCountsOnWhicheverThreadRunsIt kills it only when a Jolt job thread runs at least one contact callback, and on this machine at load 8 to 17 Jolt ran every callback of the 100-box scene on the physics thread, whose step count already holds them. (2) Dispose without 'JoltNative.ContactListenerSetProcs(backendCallbacks);' survived: joltc keeps the counting wrappers, which then call the disposed counter's table, so after a counter made with a forwardTo table (the test's allocating callbacks) every later world in the process gets those callbacks instead of the backend's own. No test makes a world after such a counter. (3) Low: JobThreadCountBefore with the physics-thread test broken (callbacks on the physics thread counted twice) survived; it can only raise a count, never hide an allocation, but it can turn a window under the 200-byte allowance into a failure.

## Design

For (1), call the counting wrappers from a managed thread that is not the physics thread, inside a window, so that the result does not depend on Jolt's job scheduling: for example a test-only read of the wrapper table and a payload large enough to tell it from the ticking thread's own allocations, with the thread started before the window. For (2), dispose a counter made with a forwardTo table, then step a plain world with contacts and require that the world's contact handling runs (the backend's own callbacks are installed again). For (3), decide whether an exact-count test is cheap; if not, record it as accepted.

## Acceptance Criteria

Each of the three mutants fails a test on every run (or (3) is recorded as accepted with its reason). Checked and Release runs of the Physics tests pass.

