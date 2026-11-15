---
id: t0-acxm
status: open
deps: []
links: []
created: 2026-11-15T00:42:53Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Under Design X, the bay's bounce light trails a sunbeam that narrows and goes out far more than on main

Take T16 of docs/plans/bounce-light-confidence.md, 2026-11-14, the survey's motion film rect-bay (scripts/rect-bay.json: the bay of demo-surface from tick 0, fixed camera, 180 frames; the sunbeam through the roof opening narrows and goes out). The survey compares a film frame with a still of the same state; 'level' is the film frame's light over the still's, minus 1.

| Build | level, middle frame | level, last frame | blotch (worst) |
|---|---:|---:|---:|
| main ab54c785e | +1.18 | +0.53 | 0.245 |
| branch before the dark-settle fix (73f4a6548) | +1.82 (worst of both frames) | | 0.485 |
| branch with the fix (eccad009a) | +1.86 | +1.75 | 0.488 |
| branch, both clamps off (TERTIUS_A_NO_CLAMP=1) | +2.31 | +4.15 | 0.386 |

All on the Mac, one after the other. Main trails too (ter-05wb recorded +0.84 with the bounce light alone), and the film fails the survey's limits in every build. The branch trails more: at the last frame its bay is 2.75 times as bright as the still, main's 1.53 times. The fix of t0-f8oc does not cause it: the branch before the fix gives the same.

Cause, measured by the evidence census of the film (branch with the fix, 211 draws): light evidence in 3.1 % of the block-draws of the bounce light and 1.5 % of the reflections', motion evidence in none. A ray gives light evidence only when it hits a surface whose direct light changed in the last 7 draws. Here that surface is the thin edge of the narrowing sun patch, which few rays hit. Every other block keeps its history of 32 draws and its wide box, and each later bounce adds its own delay. Main's narrow box follows the fast history in every pixel, which costs main the mottle and the lost light in stills.

The same gap shows in the cabin's lamp switch (t0-5tyj): later bounces carry no light age. Design R's history of 128 draws without evidence (t0-70v1) makes the trail longer where no evidence acts.

Needs a design: what tells a block that the amount of its light moves, when no ray of it hit a changed surface. Picture: docs/research/design-x-acceptance-2026-11-14/pics/rect-bay-film.png.

