---
id: ter-odo6
status: open
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
