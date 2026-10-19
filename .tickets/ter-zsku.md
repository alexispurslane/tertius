---
id: ter-zsku
status: open
deps: []
links: []
created: 2026-10-19T21:02:13Z
type: task
priority: 2
assignee: Alexis Purslane
tags: [frames, physics, design]
---
# Should a kinematic body without a motion intent hold still in its semantic frame, whatever numeric frame holds it?

Found by ter-zg77 (2026-10-19). The design says that in a step without a motion intent a kinematic body holds still: physics sets its velocity to zero in its home. A fast or far kinematic body lives in a following frame, and a following frame without a plan falls by its semantic frame's field (the rule of ter-dhkf for hulls, which ter-zg77 extends to plain kinematic bodies). So a still kinematic body 2^23 m or more from its frame's origin falls in a gravity field, while the same body nearer the origin holds still. That makes a numeric frame visible to gameplay, which numeric frames must never be. Option A: a following frame without a plan holds still in its semantic frame, so its anchor holds still whatever numeric frame holds it; its free members still feel the full field. Cost: a Kepler hull whose component skips one tick stops for a step, then its next sample jumps it back. Option B: keep the rule and name the limit. Ask the user; the orchestrator leans to A, by the rule that numeric frames change nothing that gameplay sees.

