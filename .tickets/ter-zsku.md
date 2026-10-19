---
id: ter-zsku
status: open
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
