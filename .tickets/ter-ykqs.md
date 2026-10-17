---
id: ter-ykqs
status: closed
deps: []
links: [ter-dhkf]
created: 2026-10-17T07:05:17Z
type: bug
priority: 1
assignee: Alexis Purslane
tags: [physics, frames]
---
# A fast body in a falling numeric frame can pass through a wall, because a ghost has no linear cast

From ter-dq92 and ter-2gg0 (2026-10-17): Jolt gives a kinematic body no linear cast, and casts only along a dynamic body's own motion. Without numeric frames, a fast crate's linear cast stops it at a static wall. In its falling numeric frame the crate is slow and the wall's anchor ghost comes at it fast, so a ghost (mesh or box) that arrives from beyond Jolt's 2 cm speculative distance and moves more than half the crate or the wall's thickness in one step passes through the crate. Example: a crate at 60 m/s (1 m per step at 60 Hz) meets a wall thinner than 1 m from a gap outside the band. The same holds for the new stick-out ghosts. This defeats the purpose of falling frames for fast bodies near walls, and makes numeric frames visible to gameplay. Ideas to weigh: a body in a falling frame whose path within the lookahead meets an anchor leaves its falling frame, so that its own linear cast runs against the real wall; or the ghost pass casts the relative motion itself. Test every idea beside the same scene without numeric frames.


## Notes

**2026-10-17T20:01:35Z**

Fixed by ter-dhkf (merged 2026-10-17): a static body, the interior of a frame owner and a still kinematic body now hold a fast group in their own system, where Jolt's linear cast stops the body at the real wall. Tests: NumericFrameAnchorTests (thin box wall, thin mesh wall, interior wall, kinematic door).
