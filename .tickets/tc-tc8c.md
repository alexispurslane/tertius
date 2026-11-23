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


## Notes

**2026-11-22T23:54:04Z**

Line orchestrator's answer 80 (2026-11-22): not before the merge of t7-3e22. Under light evidence a history now holds at most 8 draws, so a wide box there keeps stale light of a moving view for at most about 8 draws, against 4 before. No take shows a fault from it, and a change of the box under two kinds of evidence needs its own measurement.

The form to measure (the orchestrator's design note). The block's evidence is a maximum over ordered levels today, so the stronger kind hides the weaker one. The two actions are independent: region and light evidence act on the history's length, and motion evidence acts on the box. A set of bits, one per kind, with each action reading its own bit, is the one model that covers every pair of kinds, and the hidden pair has no place to happen. The dispatch then combines the block's bits with an atomicOr in place of the atomicMax. This is the "composed level" of the description in general form. Do not build it before the take.

Which motion the motion bit means (checked in the code on 05a27262d): both kinds. The resolve sets it from `moves || moved` (screen_trace_resolve.comp:106 and 157). `moved` is a ray's hit on a moved surface. `moves` is movesOnScreenAt(pixel): the pixel's own surface point moves on screen by at least LeastMotion, 1/32 pixel, and motion.glsl says "the camera's moves count as well as the surface's". So every block of a walk has motion evidence, and the take is a lamp switch during a walk. A lamp switch beside a moving object in a still view is the second case.
