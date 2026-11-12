---
id: ter-g3tk
status: open
deps: []
links: []
created: 2026-11-10T09:38:12Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# After a walk stops in the cabin, the light is 13 % to 47 % too bright and drains over 100 to 200 draws

Survey walk part, cabin-walk (scene orbit, camera person), main with the light-change record's repair (011c56360): /private/tmp/tb8zew/sv-fix/results/walk/cabin-walk.json. Exposure fixed at 4.08 stops; the end still comes about 360 draws after the stop with the history kept. At the stop every one of the 10 regions is 1.13 to 1.47 times its end-still level; at 60 draws 1.04 to 1.29; at 120 draws 0.96 to 1.08; at 199 draws 0.99 to 1.06. The whole image settles within 5 % in 94 draws, and two regions never do in 200. So motion makes the image too bright and a long history drains the excess; it is not a lag. The cut on evidence (ter-wkvw) cannot fix it, because a walk past still lamps changes no direct light. Open: which signal carries the excess, and whether the light is already too bright before the walk (the walk starts about 90 draws after the load). The research plan is at /private/tmp/tertius-walk-overshoot-20261110/plan.md.

## Acceptance Criteria

The signal that carries the excess is named, with a split by light signal. After the fix, every region of cabin-walk stays within 5 % of the walk's control at the same tick (tools/README.md, the walk case) in at most 60 draws after the stop, and no other survey view gets worse.

