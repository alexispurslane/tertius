---
id: ter-5to8
status: open
deps: []
links: []
created: 2026-10-11T20:08:45Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [tests, flaky]
---
# Make EventListTests.SteadyStateUseDoesNotAllocate stable: it measured 7,848 bytes once

On 2026-10-11, during ter-uucg, one full run of tests/Tertius.Simulation.Tests failed SteadyStateUseDoesNotAllocate with 'Expected allocated to be 0L, but found 7848L'. Three runs of the class alone and a second full run passed. The test warms up with one call to Exercise, then measures 100 calls with GC.GetAllocatedBytesForCurrentThread.

Likely cause, not confirmed: a one-time cost on the test thread during the measured loop, for example a tiered-JIT transition (on-stack replacement of the loop), a type load or a static constructor that the single warm-up call did not reach. Next step: reproduce it under load (the full suite, or several runs in parallel), then find what allocates, for example with DOTNET_TieredCompilation=0 or more warm-up calls, and fix the test or the code. Per the testing rules, a flaky test is triaged, not retried.

