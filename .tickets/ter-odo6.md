---
id: ter-odo6
status: closed
deps: []
links: []
created: 2026-10-12T13:55:44Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [tests]
---
# Allocation tests that demand exactly 0 bytes over a loop can flake

Found in ter-swgm's follow-up. A test that counts GC.GetAllocatedBytesForCurrentThread over a loop and demands exactly 0 bytes can fail once when the runtime compiles a hot method again during the loop: that one-time allocation lands on the current thread. GravityAllocationTests.TheEffectiveGravityOfABodyAllocatesNothingWhateverTheFieldOfItsFrame failed once that way in a full run and now divides the bytes by the number of calls, as AllocationTests does per tick. These still demand exactly 0 bytes: EventListTests.SteadyStateUseDoesNotAllocate, ImpostorManagerTests (steady state, about line 733), HookAndCameraTests (about line 55) and ImpostorTests (about line 538).

## Acceptance Criteria

Every allocation test measures per call or per tick, or warms up past the runtime's recompilation, and says which in a comment.


## Notes

**2026-10-17T00:20:12Z**

2026-10-16: EventQueryTests.NoQueryAllocates failed once in a full Debug run of Simulation during ter-gd22, and passed alone and in later runs. Another exact-zero allocation test that flakes.

**2026-10-17T01:30:01Z**

2026-10-17: ContactAllocationTests.StepsWithManyPersistingContactsAndAPendingPairAllocateNothingOnAnyThread (ter-pnco) fails under tools/coverage/run.sh in Debug and in Release, and passes without coverage (558 and 559 Physics tests, 0 failed). It counts bytes on every thread, and the coverage instrumentation allocates on Jolt's worker threads when they first run managed code. Read a failure of this test under coverage as a tool artifact, or make the test detect the coverage collector.

**2026-10-20T20:43:46Z**

2026-10-20, coverage of ter-tzf6: Host's CaptureMailboxTests.ARequestStopsWaitingWhenTheWindowCloses (a 5 s timing test) failed once under tools/coverage/run.sh in Release, and passed 3 of 3 alone in Release without coverage. The branch does not touch it. A timing test that fails under the load of the coverage run, like the two ContactAllocationTests.

**2026-10-21T17:33:43Z**

2026-10-21: RefusedRailsAllocationTests.ARailsComponentThatTheGuardRefusesAllocatesNothingInTheTicksAfterItsLog also fails under tools/coverage/run.sh in Debug, beside the two ContactAllocationTests, on the branch of ter-h74c, which changes no engine code. It passes without coverage.

**2026-10-21T20:42:26Z**

2026-10-21: under tools/coverage/run.sh on the branch of ter-6u2m, ScriptUnionTests.ReadingAStepOrATargetAllocatesNothingBeyondTheReadOfItsCaseRecord (Host Debug) and PuffTableTests.AStepAndABirthAllocateNothingAfterWarmUp (Simulation Release) also failed once. Both pass without coverage in Debug and Release.

**2026-10-22T12:15:53Z**

2026-10-22, ter-a4s2: every test that measures GC.GetAllocatedBytesForCurrentThread now goes through Allocations.Fewest (tests/Tertius.Testing/Allocations.cs): the fewest bytes of up to three windows, with the reason in its doc comment. The per-call and per-tick divisions are gone, and the tests demand exactly 0 bytes again. A loop of 40 suite runs under load found the one-time allocation in one window only (7,400 bytes, then 0 and 0), also with tiered compilation off. Not covered: the ContactAllocationTests, which count every thread and keep their own two windows, and the failures under the coverage collector, which are a tool artifact.
