---
id: t1-yy2f
status: open
deps: []
links: []
created: 2026-11-15T01:24:22Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-kh4g
tags: [rendering, design]
---
# The back layer gives a surface that the back-face pass does not draw the back face of the solid behind it (the borrowed back face)

Found in stage 1b (docs/research/back-face-depth-layer/stage-1b/pictures.md, part 3). The probe's back-face pass draws the one-sided solid runs only. A hair card in front of the floor then takes the floor's back face as its own, and its slab spans the air between the two: a median of 4.12 m on 131,399 card pixels of lamp-hall-16-hair. Under the layer as the slab, 24 % of the reflection rays there hit, against 2 % under the 10 % rule, and 95.0 % of those hits have their crossing of the hit pixel's plane behind the ray's start. The stills hide it in that view, because the hit light and the miss light behind the cards are alike. The design of stage 2 must settle it. Line orchestrator's answer, not measured: the pass draws the two-sided and cutout runs with no face culled and with the cutout test, as Design 1 in the record says, so that a card's own fragment ends its slab at the front's distance. An open one-sided mesh still has no back face of its own. For it, a flag that the mesh is not closed, computed at mesh load, can put it in the same draw. The design decides the owner of that flag.

## Acceptance Criteria

The design document of the back-face depth layer states the rule for a surface that is not closed, and a test view with a bright object behind hair cards shows no hit on a card from a ray that starts behind the card's plane.

