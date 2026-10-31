---
id: ter-60fa
status: open
deps: []
links: []
created: 2026-10-31T20:26:08Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [physics, allocation]
---
# The step threads of a turning-frame scene allocate from about tick 1,100 to 2,500

Found in ter-a00h. In the scene of ContactAllocationTests.StepsOfAwakeBodiesInATurningFrameAllocateNothingOnAnyThread (10 crates at rest in inertial space inside a station that turns at 0.3 rad/s), run longer than the test, the physics thread and the calling thread allocate in every run from about tick 1,100 to 2,500 of the world: windows of 200 ticks from tick 140 gave 13,664, 6,912, 1,080, 816, 408, 864 and 408 bytes on the physics thread, and 0, 848, 440, 976, 240, 392 and 88 bytes on the calling thread, the same in every run and with DOTNET_TieredCompilation=0, DOTNET_TieredPGO=0 and DOTNET_ReadyToRun=0. So it is the code under test. The test's windows end at tick 520 and do not see it. Next step: find the tick and the allocation (StepAllocations from ter-a00h counts the step threads), then fix the code or explain it.

