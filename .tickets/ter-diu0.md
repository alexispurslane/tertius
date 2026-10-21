---
id: ter-diu0
status: in_progress
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

