---
id: tc-tc8c
status: open
deps: [t0-zmjx]
links: []
created: 2026-11-22T23:26:54Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# A block with light evidence and motion keeps the wide box and 8 draws of a moving view: measure a lamp switch during a walk

Since t0-zmjx (2026-11-22, commit 05a27262d) light evidence cuts a block's histories to at most FullRateFrames, 8 draws, in place of the fast cap of 4. The evidence levels are a total order, and the block takes the strongest: LightEvidence (3) stands above MotionEvidence (2). So a block whose samples bring a recent light code and the motion bit keeps the widened box, and its histories hold up to 8 draws. Motion includes the camera's own motion, so every lamp switch during a walk makes such blocks.

While the cut was at the fast cap, the reason for that order was that a history at the fast window's length holds no lag for the box to find (docs/plans/bounce-light-confidence.md, section 8.2). With the cut at 8 the reason no longer holds. The review of t0-zmjx found the gap. No take has measured the case: the acceptance takes of t0-5tyj ran still lamp switches, and the walk was not taken again.

The candidate, if the take shows a fault: a block level of two facts in place of the total order. Motion anywhere in the block drops the widening of the box. Light or region evidence sets the cap at FullRateFrames. That changes the motion evidence, which t0-zmjx left out of scope.

## Acceptance Criteria

- A take of a lamp switch during the cabin walk (or another walk past a lamp that switches), on the cap's code, beside the same take with the composed level as a scratch variant: settle per region, boil in the 60 draws after each switch, and the lag error of the walk.
- A decision recorded in section 8.2: keep the total order with a measured reason, or build the composed level with tests first.
- ALightCodeWithTheMotionBitKeepsTheWidenedBoxOfLightEvidence follows the decision.

