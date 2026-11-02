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


## Notes

**2026-11-02T08:31:15Z**

Done on branch ter-773q-jolt-thread-bound (2026-11-02).

Determinism. Jolt's Docs/Architecture.md, "Deterministic Simulation", names two conditions: the same order of API calls and the same binary. The job system is not one of them. Jolt's own UnitTests/Physics/PhysicsDeterminismTests.cpp compares 0 workers with 15 workers. The new test WorkerThreadsTests.AHeapEndsInTheSameBitsWithOneThreeOrNineWorkers steps a heap of 100 cubes, one island of about 260 contacts and so above the large island splitter's 128, with 1, 3 and 9 workers, and the world hashes agree. It passed before the change too. The existing tests with 1 and 4 workers (SpinStationLandingTests, StationSliceTests) still pass.

Change. JoltBackendOptions.WorkerThreadsFor(processorCount) holds the rule: WorkerThreads when it is positive, else processorCount - 1, at least 1. The backend passes WorkerThreadsFor(Environment.ProcessorCount) to Jolt and keeps it as JoltPhysicsBackend.WorkerThreads.

Demo. Environment.ProcessorCount and hw.activecpu are both 10 on this machine, so the window's default backend runs 9 workers before and after.

CPU of the test hosts with DOTNET_PROCESSOR_COUNT=4, sampled every second with ps (pcpu, and the rate of the CPU time):
- Physics, before: pcpu peak 544, mean 158. Rate peak 573, mean 224.
- Physics, after: pcpu peak 487 and 313 in two runs, mean 214 and 199. Rate peak 570 and 459, mean 313 and 314.
- Simulation, before: pcpu peak 181, mean 130. After: pcpu peak 340, mean 231. The run lasts 9 s, so it gives 4 to 9 samples.
- Physics without the variable, after: pcpu peak 436, rate peak 615.
The load of the machine was between 7 and 50 from other agents, so the means vary. The change does not move these hosts, because no Physics or Simulation test steps a default backend: WorldTesting uses JoltBackendOptions.Unpaced (1 worker), and a few tests set 2 or 4. The steady part of a Physics run sits at about 400% (6 to 9 busy threads in ps -M). The short peak of about 570% comes in the first 2 s, from about 38 threads at about 10% each.
