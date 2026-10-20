---
id: ter-1b21
status: open
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
