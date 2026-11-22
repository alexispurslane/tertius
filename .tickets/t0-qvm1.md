---
id: t0-qvm1
status: open
deps: []
links: [t0-gumm]
created: 2026-11-22T11:13:56Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# The region's drift test fires falsely at anchors whose light scatters with rare bright draws: 8 to 14 % of the draws beside the lamp hall's windows

Found by the room's takes, part B (docs/research/t0-9fo0-room-after-shadow-fix/README.md, 'The region test's fires'). In the still rect-lamps on the line's head 6d8a68332, the bounce light's region test (region_drift.glsl) fires in 1.22 % of the tested anchor-draws after draw 133, where the light does not move. 230 of the 240 anchors fire at 0.78 to 0.83 %, which is this test's rate on Gaussian noise: it estimates its spread from 18 draws, so 3 standard deviations give about 0.8 %, not 0.27 %. The 10 busiest anchors, all at the side walls' windows, fire at 8 to 14 % of the draws. Their region's light scatters by 11 to 17 % from draw to draw, against a median of 5.2 %. A random order of their draws fires as often, so the spread causes the fires, not a trend. Each false fire caps the bounce history of the blocks in the anchor's region at 8 draws for one draw. No picture and no score shows a fault there: the walls' daub is 0.0118 with the remedy and 0.0116 without it. So this is not needed for the merge of Design X. Build it only if a scene shows more noise near such anchors.

Two ways that the room's lead named, not tried: a spread from all 36 draws of the ring, or a spread that ignores the brightest draws. A third: a tap that is the mean of a block's samples, not one pixel. First find which pixels of those regions cause the spread. The replay script regionnoise.py of the record gives every logged answer exactly, so a changed test can be tried on the logged light without a build.


## Notes

**2026-11-22T22:30:06Z**

New evidence from the catch-up ray experiment (t0-gumm), 2026-11-22: a map of the bounce light's history length in the settled cabin still (Design X on the line's merge 710834cc0, lamps on, draw 190, scale 2/3; docs/research/t0-gumm-catch-up-spread/pics/history-map-cabin.png on t3/combined-renderer-check). 20.8 % of the pixels hold a history between 8 and 31.5 draws, in whole rectangles of the drift test's region size on the ceiling, the walls and the floor, far from the windows too. In that run the test fires on 1 to 11 of 238 anchors in each of the draws 100 to 131.

The arithmetic that this ticket's first text did not do: a false fire rate of 0.8 % per tested anchor-draw is one fire per 125 draws for an anchor, and each fire caps its region at 8 draws, which needs 24 draws to grow back to the cap of 32. So about 24 / 125 = 19 % of the picture is under the cap at any moment; the map measures 20.8 %. The rate on plain noise, not only the 10 busy anchors at the windows, is the cost.

Measured effect so far: none in a picture or a score (take B of t0-3vwx: the left wall's boil on settled draws of one size is 0.05 % on Design X and on main; the stills' daub is lower on Design X). Second effect: the history's length is no clock of a restart, which made the catch-up ray experiment read a restart from three other clocks.

My recommendation to the orchestrator: no gate of the merge; the first follow-up after it. The aim is a test whose false fire rate fits its cost of 24 draws: by the ticket's replay script regionnoise.py, find the threshold or the spread estimate at which under about 2 % of a still picture is under the cap. Priority raised from 3 to 2.
