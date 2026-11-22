---
id: ter-9t54
status: closed
deps: []
links: []
created: 2026-11-22T21:13:04Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-6fka
tags: [rendering, experiment]
---
# Sender sum outside the engine: one lamp's lit patches in the lit-block room against the path trace, with and without visibility

Experiment E2 of docs/research/lighting-system-review-2026-11-22/README.md, section 12. A script, no engine build. Scene lit-block-back (docs/research/back-face-depth-layer/stage-2/). Sample the spot lamp's lit area on the block's back face as 8 x 8 patches with flux from the spot's intensity and the face's albedo. Sum the irradiance at the floor patch in the gap and at the back wall above the block, with an exact box test for visibility and without it. Add a second closed room behind a wall to measure the leak through a wall.

## Acceptance Criteria

The two sums against the path trace's one-bounce values (0.085 at the back wall, 0.064 at the floor gap, in the units of t2b-xeo7), the leak as a share of the room's light, the script and a short record committed.


## Notes

**2026-11-22T21:31:35Z**

2026-11-22, orchestrator: merged. The sender sum gives 76.6 % of the path trace's one bounce at the back wall and 86.3 % at the floor gap, where the engine has 9 to 71 times too little today. The shortfall of 14 to 23 % is not explained (the model omits the glossy reflection and the floor lamp's bounce). Without a wall test, a closed room behind a wall gets 4.8 % (floor) and 12.6 % (far wall) of the lit room's comparable light. That is above the review's limit of a few percent. Reading: design S fixes the dim room and cannot ship without a test of the wall between a sender and the shaded point.
