---
id: ter-1b21
status: closed
deps: [ter-1bnq]
links: []
created: 2026-10-16T23:00:45Z
type: feature
priority: 1
assignee: Alexis Purslane
parent: ter-s9m0
tags: [physics, character]
---
# Characters in turning frames: up, standing, walking and jumping

Design: docs/plans/spin-gravity.md. Section 'Characters'. Field plus rotation terms on the velocity; up and standing from field plus centrifugal, also in the air; pressed along the ground normal and moving along the ground plane while standing.


## Notes

**2026-10-18T14:08:43Z**

From the Blue Danube demo survey (2026-10-18): check whether the choice between the walking and the weightless form (CharacterForms.cs ~155, EffectiveGravity) counts the centrifugal term near the hub of a turning frame.

**2026-10-20T02:48:42Z**

2026-10-19, from the review of stage 2 of ter-1bnq: (1) extend InstantVelocity and HandedOffVelocities to a walking character that does not stand, so that its velocity read and its hand-off take the rotation terms as a dynamic body's do. (2) Until this ticket, a walking character's velocity rule reads only the linear gravity, which is zero in a station without a field, so people in a spinning station float about 5 cm above the floor and none stands. Rerun SpinStationCharacterBenchmarks (benchmarks/Tertius.Benchmarks/SpinStationBenchmarks.cs) when this ticket is done: it measured 100 us per tick for 100 people in the station and 167 us in a frame that does not turn, which do not compare like with like while the people float.

**2026-10-20T08:22:16Z**

2026-10-20, part 1: the belted passenger of the demo, a walking character in the air, now feels the cabin's rotation terms, and slid 22.4 mm from its seat at the start of the climb (bound 10 mm) because Passenger's belt read the field less the proper acceleration only. The orchestrator settled it by the model (the belt pushes against the gravity that physics applies): ter-ukj2 is folded into this ticket, and builder 2 merges main (with ter-fsst's FrameTree.FeltGravity) and moves Passenger onto FeltGravity with every DemoLaunchTests bound unchanged.

**2026-10-20T08:55:33Z**

2026-10-20, from ter-vja8: once characters feel the rotation terms, (1) add a test that the person in SpinStationLandingTests (APersonWhoLeavesTheFloorAt50MetersPerSecondAcrossTheStationLandsOnTheFarSideAndStaysOnIt) passes within about 1 m of the axis and meets the floor above 50 m/s relative to the station, which is the spike's crossing (the inertial line through the axis at 40 m/s, meeting the floor at 50.8 m/s); until now the person crosses on a straight chord 59.7 m from the axis. (2) Add the velocity bound relative to the falling frame for the person in SpinStationLandingTests' crossing in a falling frame, which today holds only for the crate (the person moves at up to 0.89 m/s relative to its falling frame, because it feels no rotation terms). Merge main into the branch first if ter-vja8 has landed.

**2026-10-20T11:23:29Z**

2026-10-20, part 3, decided by the user: (1) in the air, a character's up turns toward the semantic gravity at a limited angular rate, not at once (replaces the instant turn of fork F5); the rate is one named constant in the backend options, its default chosen by measurement so that a one-step flip of the felt gravity (the demo's ramp jolt turned the belted passenger's up 135 degrees in one tick under the instant rule) turns up by only a few degrees, while the 30 m/s jump across the station still lands upright. (2) The demo's belted passenger follows that air rule, with nothing holding its up to the seat; the user accepts the tipping it gives, so the demo tests' bounds widen to the measured values with reasons and the script is regenerated.

**2026-10-20T12:33:46Z**

2026-10-20, part 4, decided by the user: the walk in the ground plane (fork F6) follows every walkable face that Jolt reports, moving bodies included (one rule for every face). WindowsSliceTests' lit-cabin test changes on purpose, because the walk now follows the inner airlock door's rounded lower edge at 49.2 degrees while the door opens.

**2026-10-20T13:35:48Z**

2026-10-20, merged. Cost (SpinStationCharacterBenchmarks, Release, ShortRun, 100 people): 178.2 us per tick in the turning station with 100 of 100 standing (feet -2.0e-5 m to 1.8e-4 m above the floor), 163.4 us in a station that does not turn (100 of 100). Before this ticket: 100 us turning with the people floating 5 cm above the floor. Coverage of the changed lines (Core, Physics, Host, Debug): 146 of 158 lines (92.4%), 52 of 60 branches. The uncovered lines need no test: CharacterStartVelocity's HasValue, Value and ToString are union helpers for debugging; the default arm of NextCharacterVelocity's switch is a contract that only a bug reaches; InGroundPlane's fallback for a walk wholly along the ground's normal (a walk across up cannot be, on a walkable face); and Geometry's second fallback axis of a 180-degree turn when the vector lies along X (the first fallback is pinned by the 180-degree flip test). Two allocation tests fail only under coverage instrumentation, as known. The review's 11 findings were fixed, among them the spike-crossing test from ter-vja8 (the person passes within 1 m of the axis and meets the far floor above 50 m/s), a teleport that sets up from its rotation so the air rate applies, and PressedWeight as a pure rule with tests.
