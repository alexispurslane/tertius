---
id: t0-vg3w
status: open
deps: []
links: []
created: 2026-11-23T05:27:38Z
type: bug
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# The settled GPU span of the fair cabin switch is 0.14 to 0.30 ms higher on main 3549d3283 with Design X than on the line before the merge: which of main's changes costs it

Found by the acceptance lead of t0-5tyj on 2026-11-22 (docs/research/design-x-acceptance-2026-11-22/README.md, 'The takes on the merged code'), not hunted. The orchestrator ordered this ticket with the merge of Design X so that the rise is not lost.

Measured on the Mac at the constant render scale 2/3, the mean GPU span over film frames 40 to 159 of the fair cabin switch (lamps on, settled), in two paired rounds at a one-minute load under 8 (tools/queue-pair.sh of the record):
- round p1: 15.30 ms for the cap on the line's 57b4045ae, 15.60 ms for the cap on 16d915a67, +0.30 ms;
- round p2: 15.48 ms against 15.62 ms, +0.14 ms.

Both sides carry the cap, so the rise is not the cap's. Between the two sides lie: main's commits from c4c1f1f2f to 3549d3283 (the back-face depth layer b032604f1, the stored inverse depth of the ray's descent f6b55d811, steps 1 and 2 of the wobble's fix 28b0184fd and 3549d3283), the line's commits after 57b4045ae, and the fix of tc-8142 (6d234dcef, the clamp's moments around the pixel's own fast history).

What is not known: no run of main alone on this switch's cost exists, so the takes do not say whether main itself rose by the same amount. On the deck, Design X's excess over main on the same merged code is +0.211 ms over six paired rounds, inside the allowance of +0.30 ms, and on the film rect-bay it is -0.150 ms. So the binding check passed, and this ticket is about where 0.14 to 0.30 ms of the cabin's settled span went.

## Acceptance Criteria

Paired rounds on the fair cabin switch's settled span that split the rise: main c4c1f1f2f against main 3549d3283 without Design X, and the merged code with and without the fix of tc-8142. The record names the change that costs it, or shows that the rise is inside the spread between rounds. If one change costs it, a ticket for that change exists.

