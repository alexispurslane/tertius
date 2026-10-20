---
id: ter-zsku
status: closed
deps: [ter-zg77]
links: []
created: 2026-10-19T21:02:13Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [frames, physics, design]
---
# Should a kinematic body without a motion intent hold still in its semantic frame, whatever numeric frame holds it?

Found by ter-zg77 (2026-10-19). The design says that in a step without a motion intent a kinematic body holds still: physics sets its velocity to zero in its home. A fast or far kinematic body lives in a following frame, and a following frame without a plan falls by its semantic frame's field (the rule of ter-dhkf for hulls, which ter-zg77 extends to plain kinematic bodies). So a still kinematic body 2^23 m or more from its frame's origin falls in a gravity field, while the same body nearer the origin holds still. That makes a numeric frame visible to gameplay, which numeric frames must never be. Option A: a following frame without a plan holds still in its semantic frame, so its anchor holds still whatever numeric frame holds it; its free members still feel the full field. Cost: a Kepler hull whose component skips one tick stops for a step, then its next sample jumps it back. Option B: keep the rule and name the limit. Ask the user; the orchestrator leans to A, by the rule that numeric frames change nothing that gameplay sees.


## Notes

**2026-10-19T21:09:14Z**

Decided by the user on 2026-10-19: option A. A following frame without a plan holds still in its semantic frame, so its anchor, hull or plain body, holds still whatever numeric frame holds it, as a kinematic body without a motion intent does near the origin. Its free members still feel the full field. The old fallback (fall by the field) dates from when every following frame held a Kepler orbit. Accepted cost: a ship on an orbit whose component skips one tick holds still for that step, and its next sample jumps it back onto the orbit. Build it after ter-zg77 merges, on its own branch: the rule in FrameTree's numeric update, the tests (a still plain kinematic body at 2^23 m in a gravity field holds still; a Kepler hull whose component skips a tick holds still for that step and is back on its orbit after its next sample; numeric frames on and off give the same motion), and the docs (numeric-frames.md, rails-components.md, ARCHITECTURE.md).

**2026-10-19T23:24:02Z**

2026-10-19: the first builder stopped at about 480k tokens of context, by the user's decision, and its work is commit 1ae706b on ter-zsku-following-frame-holds-still. Done: the rule (an anchor without a motion intent has the plan of a kinematic body that holds still, and its frame lands on it); physics aims every still anchor of a following frame at its plan (AimStillAnchors); velocities given at the start of a step and hand-off velocities convert by the home's excess at the start of the step (FrameTree.HomeInstantVelocityOverChordAtStepStart), because with the end-of-step excess a body spawned beside a stopping frame was 3.8 km off its orbit and a crate handed off in that step 500 m off. Cause of the Kepler fallout: a Kepler hull spawned without a first motion intent holds still in its first step, as it does without numeric frames, so those scenes get their first motion intent. Left: about 7 Physics tests, the Core tests, the docs, the hand mutations and the full runs.

**2026-10-20T01:27:41Z**

2026-10-19, merged. Coverage of the changed lines in src/ (Core, Simulation and Physics tests, Debug): 98 of 98 lines, 52 of 64 branches. The partial branches need no test: FrameTree.Numeric.cs lines 49, 69, 106 and 1306 are the short-circuit parts of Contract.Requires and Contract.Assert, which fail only on a bug; JoltPhysicsBackend.Step.cs lines 292 and 302 are the filter of AimStillAnchors (a home whose anchor is not a still kinematic hull in that home) and the bound check of the intents span. Three allocation tests fail only under coverage instrumentation, as known. The review found 9 defects, all fixed with failing tests first; the main one: in the step where a following frame stops or starts, a body leaving its bubble and a dynamic hull inside it read half the frame's change of velocity too much (1,000 m/s at 2 km/s). A ship floating beside a fast Kepler station now reads what it reads without numeric frames (3 cm/s off the station's sample velocity on the fast test orbit), in place of the earlier rule that added the frame's jerk term to every awake body.
