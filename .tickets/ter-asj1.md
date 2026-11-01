---
id: ter-asj1
status: in_progress
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


## Notes

**2026-11-01T00:11:35Z**

Settled (2026-10-31): (1) WhatAContactCallbackAllocatesOnAThreadOtherThanThePhysicsThreadCounts calls the counting wrappers 16384 times (1 MB of payload) from a thread that the test starts before the window and releases inside it, after 10 ticks name the live physics thread; the read is the internal StepAllocations.CountingCallbacks (internal to Tertius.Testing, no src change). (2) The Dispose test found a real bug: the counter stored the forwardTo table as the table to restore, so Dispose gave joltc the test's allocating callbacks. Fixed: the wrappers call wrappedCallbacks, and Dispose gives joltc ContactListener.Callbacks again, which the constructor now requires also with forwardTo; the copied backendCallbacks field is gone. AWorldMadeAfterACounterThatCalledOtherCallbacksHearsItsContacts was red on the old code. (3) An exact-count test was cheap, so a7 is killed, not accepted: WhatAContactCallbackAllocatesOnThePhysicsThreadCountsOnce calls the wrappers 16384 times on the physics thread through FaultInjectingBackend.EditNextOutput and bounds the window below 2 x calls x 64 bytes; an array of 64 bytes is 88 bytes, so counted once the window is about 1.44 MB and counted twice 2.88 MB, against the 2 MB bound. Hand mutations, each 5 of 5 runs killed: a1 (0 * before), a8 (Dispose without SetProcs), a8b (Dispose restores wrappedCallbacks, the old bug), a7 (== -physicsThread).
