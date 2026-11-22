---
id: tc-g2zz
status: open
deps: []
links: []
created: 2026-11-22T20:14:43Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# After a light change, Design X traces a ray at every changed pixel for about 11 draws: the GPU span rises from 15.3 to 22.9 ms for 10 to 13 draws

Measured by t0-5tyj (docs/research/design-x-acceptance-2026-11-22, "The frame's cost after a lamp switch"): the fair cabin switch at the fixed 2/3 on the Mac. After each switch, X's GPU span is a flat block at 22.8 to 22.9 ms from draw 4 to draw 13 to 16, against main's 17.6 ms for draws 4 to 10. Over the 60 draws after a switch X costs +1.2 ms more than main on average. Under the dynamic render scale this block drops X's scale to about 0.83 to 0.88 after each switch on the RTX 2000 Ada.

Cause, read from the code: light evidence sets historyLength = fastCap (4 draws) in accumulate_spatial.comp for the light ages 0 to 6 (RecentLightAge = 7), and the ray plan gives a pixel its own ray while its history is shorter than FullRateFrames (8 draws). Main only shortens a history through the anti-lag.

Two scratch variants remove the block (scratch-light-evidence-cap.patch = XB, scratch-plan-rate-at-fast-cap.patch = XC): peak 15.6 to 15.7 ms, no draw above the settled span + 1 ms. XB settles +5 / +4 draws slower than X and fades visibly more slowly. XC settles as X, with 0.01 to 0.02 points more boil in the 60 draws after off. Video: pics/switch-cabin-fair-fixed-23-x-xb-xc.mp4.

## Acceptance Criteria

The user chooses a design (XB, XC or another). With it, the fair cabin switch at the fixed 2/3 shows no draw more than 1 ms above the settled span after either switch, and its settle and boil are no worse than X's by the record's measures. Red-green tests come with the change.

