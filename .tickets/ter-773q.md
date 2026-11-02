---
id: ter-773q
status: in_progress
deps: []
links: [ter-hxca, ter-lbyp]
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

**2026-11-02T07:57:03Z**

2026-11-02: workflow run 37399dd5-bb5c-45bc-beb4-7209425bcfa6 (version 869b0b07), branch ter-773q-jolt-thread-bound from 207f9672b. A dependence of the step's result on the thread count comes back as a fork.

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

**2026-11-02T08:49:24Z**

Review fixes (2026-11-02).

Determinism evidence. The island test showed that the old heap of 100 cubes in 4 layers of 5 by 5 was 25 separate columns: its largest awake island held 4 contacts, the floor contact and 3 between layers, because cubes 2 cm apart in a layer never touch. So it never reached Jolt's large island splitter (above 128 constraints), and the note above was wrong about 260 contacts. The heap is now a stepped pyramid of 91 cubes (layers of 6 by 6 down to 1 by 1, each layer half a cube in), so each cube lands on four cubes below. WorkerThreadsTests.HeapIsland counts the real touches of the largest group of connected cubes while all 91 are awake: 258, a lower bound on the island's constraints. AHeapEndsInTheSameBitsWithOneTwoOrTheDefaultNumberOfWorkers compares 1 worker, 2 workers and a default backend: 9 workers on this machine, 3 under DOTNET_PROCESSOR_COUNT=4. It no longer starts a fixed 9.

The count that reaches Jolt. JoltThreadCountTests (a collection without parallelization) counts the threads of the process before and after it builds a backend, with a margin of one thread. An explicit count of 2 starts 2 or 3 threads, and a default backend starts WorkerThreadsFor(Environment.ProcessorCount). With the old call new JobSystemThreadPool() in place, both failed under DOTNET_PROCESSOR_COUNT=4 with 9 threads. Both passed 30 runs in a row, 10 each with the variable at 4, at 1 and unset. The managed copy JoltPhysicsBackend.WorkerThreads is gone, because no test reads it now.

CPU after the review fixes, DOTNET_PROCESSOR_COUNT=4, load 9 to 10: Physics pcpu peak 494, mean 227, rate peak 527, mean 314 (14 samples). Simulation pcpu peak 362, mean 252, rate peak 389, mean 324 (3 samples). The start-up peak of the Physics host is unchanged. ter-lbyp holds it.

**2026-11-02T09:42:53Z**

Hardening (2026-11-02).

Coverage of the changed lines (tools/coverage, Debug and Release): lines 2/2, branches 2/2. All test projects passed in both configurations.

Mutants. The official Stryker run (changed.py main) ran on Physics. Its test projects were Core, Physics and Simulation, with the new test ANegativeWorkerCountGivesTheDefaultCount in the working tree. The listed mutants ran with tools/mutate.py under DOTNET_PROCESSOR_COUNT=4, because on this machine the runtime's count and the hardware's count are both 10, and a mutant that falls back to Jolt's hardware count then gives the same number.

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| Physics-769 | src/Tertius.Physics/JoltBackendOptions.cs:46 | WorkerThreads > 0 ? ... -> (true?WorkerThreads :...) | killed | JoltThreadCountTests.ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne, WorkerThreadsTests.ANegativeWorkerCountGivesTheDefaultCount, WorkerThreadsTests.ADefaultBackendOnOneProcessorRunsOneWorker | - |
| Physics-770 | src/Tertius.Physics/JoltBackendOptions.cs:46 | WorkerThreads > 0 ? ... -> (false?WorkerThreads :...) | killed | JoltThreadCountTests.ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne, WorkerThreadsTests.AnExplicitWorkerCountHoldsWhateverTheProcessorCount | - |
| Physics-771 | src/Tertius.Physics/JoltBackendOptions.cs:46 | WorkerThreads > 0 -> WorkerThreads < 0 | killed | JoltThreadCountTests.ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne, WorkerThreadsTests.AnExplicitWorkerCountHoldsWhateverTheProcessorCount, WorkerThreadsTests.ANegativeWorkerCountGivesTheDefaultCount | - |
| Physics-772 | src/Tertius.Physics/JoltBackendOptions.cs:46 | WorkerThreads > 0 -> WorkerThreads >= 0 | killed | JoltThreadCountTests.ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne, WorkerThreadsTests.ADefaultBackendOnOneProcessorRunsOneWorker, WorkerThreadsTests.ADefaultBackendRunsOneWorkerPerProcessorMinusOne | - |
| Physics-773 | src/Tertius.Physics/JoltBackendOptions.cs:46 | Math.Max -> Math.Min | killed | WorkerThreadsTests.ANegativeWorkerCountGivesTheDefaultCount, WorkerThreadsTests.ADefaultBackendOnOneProcessorRunsOneWorker, WorkerThreadsTests.ADefaultBackendRunsOneWorkerPerProcessorMinusOne | - |
| Physics-774 | src/Tertius.Physics/JoltBackendOptions.cs:46 | processorCount - 1 -> processorCount + 1 | killed | WorkerThreadsTests.ANegativeWorkerCountGivesTheDefaultCount, WorkerThreadsTests.ADefaultBackendOnOneProcessorRunsOneWorker, WorkerThreadsTests.ADefaultBackendRunsOneWorkerPerProcessorMinusOne | - |
| Physics-JoltPhysicsBackend.cs-R92 | src/Tertius.Physics/JoltPhysicsBackend.cs:92 | the branch removed 2 lines here | removed | | - |
| L01 | src/Tertius.Physics/JoltPhysicsBackend.cs:92 | the removed lines back: default -> Jolt's hardware count | killed | JoltThreadCountTests.ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne | 5.7 |
| L02 | src/Tertius.Physics/JoltPhysicsBackend.cs:92 | no numThreads in the configuration | killed | JoltThreadCountTests.ABackendWithAnExplicitWorkerCountStartsThatManyJoltThreads, JoltThreadCountTests.ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne | 4.5 |
| L03 | src/Tertius.Physics/JoltPhysicsBackend.cs:92 | WorkerThreadsFor(...) -> WorkerThreads | killed | JoltThreadCountTests.ADefaultBackendStartsOneJoltThreadPerProcessorOfTheRuntimeMinusOne | 4.3 |
| L04 | src/Tertius.Physics/JoltPhysicsBackend.cs:92 | ProcessorCount -> ProcessorCount + 1 | survived | | 3.8 |
| N01 | src/Tertius.Physics/JoltBackendOptions.cs:46 | > 0 -> != 0 (negative count to Jolt) | killed | WorkerThreadsTests.ANegativeWorkerCountGivesTheDefaultCount | 3.9 |
12 rows: killed 10, timeout 0, survived 1, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 1

Stryker evidence: 1070 s wall; schemata build 10 compile tries (9 failed, each removing the mutants that broke it); 1 initial test run of 4542 tests in 42 s; per-test coverage capture 630 s; 0 Dubious tests; 6 mutants ran a mean of 1123.0 of 4542 tests, 0 ran all tests; 1-minute load mean 15.7, max 46.6; 3 test host processes at most, at nice 20, together at most 320% and on mean 98% of one core. No timeouts, so the load limit does not apply, and the run did not print NOT VALID.

Triage:
- R92 (removed): L01 puts back the removed lines, and JoltThreadCountTests kills it.
- JoltPhysicsBackend.cs:92 (no Stryker mutant): L01 to L04 cover the line.
- JoltBackendOptions.cs:45 (no Stryker mutant): judgment call. The line holds only the signature of WorkerThreadsFor, and its body on line 46 holds all six Stryker mutants.
- L04 survived: judgment call. JoltThreadCountTests allows one thread more than the expected count, because the runtime can start a thread while the test counts, so a count one too high at the call site passes it. A tighter count makes the test depend on the runtime's own threads, and an off-by-one in this one argument is not a likely slip. The arithmetic of the count is in WorkerThreadsFor, and its unit tests pin it exactly.

New test: WorkerThreadsTests.ANegativeWorkerCountGivesTheDefaultCount. joltc maps a numThreads of zero or less to -1, and Jolt then sizes its workers from the hardware. So a negative WorkerThreads must not reach Jolt. The test was red on N01 first. The other decisions (an explicit count holds, the default follows the runtime's count, at least one worker) already had tests at both levels: WorkerThreadsFor and the thread count of the process.

Checked build and the full Checked tests: 9841 total, 9827 passed, 0 failed, 14 skipped.
