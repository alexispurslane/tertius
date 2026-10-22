---
id: ter-k287
status: open
deps: []
links: []
created: 2026-10-22T12:02:00Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-9gj5
---
# Find the allocation of the push step under load

With the physics step on the ticking thread (an experiment, rejected for the tests), PartPushTests.CarryingThePushesAllocatesNothingAfterWarmUp found 96,000 bytes in 100 ticks while the whole suite ran, and 0 alone. Today the step runs on the physics thread, which that test does not measure. Find out whether the step of pushing parts allocates on a busy machine, for example through lock contention, and fix it if the engine allocates.

