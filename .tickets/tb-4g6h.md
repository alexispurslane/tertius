---
id: tb-4g6h
status: open
deps: []
links: [tb-8zew, ter-wkvw]
created: 2026-11-10T05:55:41Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-icn3
---
# Under a walk, 7.5 % of a dimmed emitter's interior never counts its change

Found by tb-8zew's test AWalkTowardADimmingEmitterStillCommitsItsChange (128 x 72, the camera walks 4 cm per draw toward the glowing block, whose glow falls 20 %). Six draws after the dimming, 1,262 of the 1,364 interior pixels hold age 2 and 102 never changed (age -30), on main's shader and on tb-8zew's alike. With a still camera, at least 95 % commit (ATwentyPercentDimmingOfAnEmitterResetsTheAgeOfItsPixels). Likely cause, not measured: under motion the pixels' pending counts start on different draws, so a neighbor's committed new light enters a pixel's 3 x 3 range before its fourth pending draw, the light falls back inside the range, and the count drops. The pixel keeps its old light and never commits. Effect: those receivers' histories are not cut by the age. The test asserts 90 % today; raise it to 95 % with the fix.

