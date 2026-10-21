---
id: ter-diu0
status: closed
deps: [ter-ftk4]
links: []
created: 2026-10-21T15:12:58Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-rb26
tags: [demo, video]
---
# Demo jets as parts: the main engine, the side jets, the turning jets in pairs and the person's back jet push as attached entities

Split from ter-6u2m on 2026-10-21, so that the half that needs only parts (ter-ftk4) starts without the particles. The demo section of docs/plans/parts.md says what to build: the main engine as one part on the line of thrust through the center of mass, keeping the hull's rotation; the side jets as parts for the six directions of TranslationThrusters; the turning jets as pairs of opposite jets at opposite places, each pair a couple about one hull axis, with AttitudeAutopilot ending in an allocation of the pairs' throttles; the person's back jet as one part on the person. The hull's flight program sends each part its command by message, one tick later than today. The demo drops Shuttle.CenterOfMass and reads BodyFacts.CenterOfMass (the user's choice of 2026-10-21). Measure the docking's miss and the attitude loop's settle time with the extra tick against today's. No emitters here: the plumes (ter-6u2m) add them.


## Notes

**2026-10-21T19:07:03Z**

Merged into main on 2026-10-21, after one review-and-fix (six defects fixed: the airlock's W hold of 241 ticks, the key latency of OrbitClusterTests, and stale text in three plans and one test report). Every suite passes under coverage in Debug and Release, except the allocation tests of ter-odo6. Coverage of the changed lines: 782 of 816 lines and 288 of 319 branches. The uncovered lines are argument guards (ShuttleJets' checks of the center of mass, the thrust and the inertia, the nozzle's check of a hull, the passenger's frame guard). No screenshots: the jets change no mesh and draw nothing yet, and the plumes ticket gives each part its emitter.
