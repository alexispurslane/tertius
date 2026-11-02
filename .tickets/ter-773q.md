---
id: ter-773q
status: open
deps: []
links: [ter-hxca]
created: 2026-11-02T04:50:04Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [testing, physics, tools]
---
# The default Jolt job system follows the runtime's processor count, so that DOTNET_PROCESSOR_COUNT bounds a test host

Found in ter-hxca on 2026-11-01. The official Stryker run (tools/stryker/changed.py) bounds the xunit threads of its test host to 4 with DOTNET_PROCESSOR_COUNT and starts it at nice 20. The bound holds for xunit, but a Simulation or Physics test host still reached about 700% CPU for a few seconds. src/Tertius.Physics/JoltPhysicsBackend.cs creates new JobSystemThreadPool() when JoltBackendOptions.WorkerThreads is 0, and Jolt then sizes its native worker threads from the hardware (cores - 1), not from Environment.ProcessorCount. So 4 xunit threads that each step a default backend can run up to 4 x 9 Jolt workers on this 10-core machine.

Proposed: when WorkerThreads is 0, pass numThreads = Environment.ProcessorCount - 1 (at least 1), so that the runtime's processor count, and so DOTNET_PROCESSOR_COUNT, bounds the job system too. Check first that the Jolt step stays deterministic across thread counts, and measure the demo's physics step time before and after on this machine. This is a src change, so it needs its own ticket and tests.

## Acceptance Criteria

1. A test host with DOTNET_PROCESSOR_COUNT=4 that runs the Physics and Simulation tests stays near 400% CPU in ps. 2. The physics tests and the determinism tests pass. 3. tools/README.md, Stryker.NET pitfalls, drops the note about the Jolt threads.

