---
id: t0-qvm1
status: open
deps: []
links: [t0-gumm, t4f-71qp]
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

**2026-11-22T22:30:58Z**

The orchestrator's decision, 2026-11-22: this ticket stays out of the gates of the merge of Design X (no picture shows the rectangles, and Design X is better than main in every still). It is the first follow-up after the merge. The hold on new trust rules behind the ray census (ter-8pco) does not cover it: it repairs an existing rule and adds none.

The model, which a fix must state: the share of a still picture under the history's cap is about the false fire rate per draw times the draws of recovery. Today that is 0.8 % times 24 draws, about 19 %, and the map measures 20.8 %. A fix names the fire rate that its recovery time can bear.

First step, before anyone tunes the rate: measure what the rule buys and what it costs, so that this ticket can also end with "remove the rule". Design X with the region test off (TemporalSignal.TestsRegionDrift false, or the remedy's "without" build of t0-acxm) against Design X as it is, at the fixed scale 2/3 on the Mac: the cabin still's daub and its map of the history length; the still exit-cabin's daub against the reference with both clamps off (the per-scene table has Design X at 0.0173 and the no-clamp floor at 0.0131 there: how much of that gap do the false fires make); the fair lamp switch's settle; the bay film's trail, which the region evidence was built to remedy. If the rule buys little settle and little of the trail's remedy and costs a fifth of the picture's history, the simpler system wins.

This step is ordered at the combined check's lead (my answer 78), to run while it waits on the card for the catch-up ray experiment t0-gumm. A scratch build is allowed if no build or switch exists.

**2026-11-22T23:52:18Z**

The orchestrator's decision, 2026-11-22: one follow-up, not three. The region's drift test has three measured faults, and they are one design question: when may the test count, and at what rate may it fire.
1. It fires falsely in a still scene and holds about a fifth of the picture under the history's cap (this ticket; the map of t0-gumm).
2. Its count restarts on a moving render size, so it ran on 12 % of its anchor-draws after a lamp switch without the cap (t4f-71qp; take B of t0-3vwx).
3. Its count restarts while the camera moves, so after a stop it runs on 34.7 % of its anchor-draws in the first 60 draws, and Design X's trail after a stop fades more slowly than main's (the final walk film of t0-3vwx: center region +4.61 % against +3.73 % at 20 draws, slowest region 47 draws against 25).
This ticket is the first follow-up after the merge of Design X and holds all three; t4f-71qp is linked and stays as the record of fault 2. The follow-up may end with a repaired rule or with no rule.

The first step is unchanged, with one take added: the rule off against the rule on. Takes: the cabin still (daub and history map), the still exit-cabin (daub against the reference with both clamps off), the fair lamp switch (settle), the bay film's trail, and now the walk's stop: with the rule off, how long is the trail? Ordered at the combined check's lead.

**2026-11-23T02:56:48Z**

Two side results of the catch-up ray experiment (t0-gumm, the combined check's lead, commit 6c03e34d2), kept here by the orchestrator's order of 2026-11-22. Neither is hunted. (1) One size change after a light change cost the cap more than 20 draws of settle: on run 2's card scale log, which holds one noise change of 0.004 at draw 402, 9 draws after the lamps go on, the cap's slowest region does not settle within 100 draws; on run 1's log, which holds no change, it settles in 80 draws, as the spread at 1/32 does in 81. The likely cause is the count restart of the region's drift test on a moving render size (t4f-71qp), one of the three faults that this ticket joins. (2) After the camera cut, the last frames of the spread's pane are 2.0 to 2.9 % brighter than the cap's at draws 80 and 119, while the center's mean is equal at draws 2, 10 and 40 and the probe scene luminance of the two codes agrees within 0.1 %. The runs differ only in their scale logs: the cap falls to 0.85 with 40 size changes and the spread stays at 0.98. So the light is the same and the shown picture differs. The likely cause is FSR's history after the size changes. Not proven.

**2026-11-23T03:17:34Z**

The first step, done by the combined check's lead on 2026-11-22: region evidence off against on (its commits 52b5482f7 and f3e76efbf, record docs/research/t0-qvm1-region-off/README.md). Both codes are the cap's commit 05a27262d at the constant scale 2/3; the code without the rule runs the drift test and caps no history (scratch-no-region-evidence.patch). Pictures: pics/rect-bay-middle-still-on-off.png, pics/exit-cabin-rule-on-off-truth.png, pics/history-map-rule-on-off.png, pics/walk-rule-on-off.mp4 (3840 x 1080, 60 frames a second, 260 frames by ffprobe). The line orchestrator looked at the two stills' pictures: the bay's frame without the rule is pale all over, which is the trail; exit-cabin's floor is a little more even without the rule and most even in the truth.

Measured, with the rule / without it:
- The fair lamp switch, settle of the whole image and of the slowest region: after off 71 and 82 draws / 189 and none in 200; after on 34 and 54 / 68 and none in 100. The light 60 draws after off: 107.0 % / 122.5 % of the settled level.
- The bay film rect-bay: level +1.09 / +2.69, blotch 0.232 / 0.580, flicker 0.00247 / 0.00091. The lag film bay-sun: median lag 13.8 / 42.9 draws, largest 27.5 / 123.5, median error 0.149 / 0.296.
- The walk's stop: slowest region's settle 47 / 53 draws; the center's light at 0, 20 and 40 draws +5.87, +4.61, +3.58 % / +5.52, +4.27, +3.26 %.
- The still exit-cabin's daub (the clamps-off truth 0.0132): whole 0.0173 / 0.0153, walls 0.0146 / 0.0120, floor 0.0125 / 0.0113. cabin-light-cabin (truth 0.0285): 0.0348 / 0.0320.
- The still cabin's pixels with a history of 8 to 31.5 draws: 20.8 % / 0 %.

The lead's verdict, which the line orchestrator shares: the rule stays. Without it the switch's regions do not settle and the bay trails three times longer. Its cost lies in the stills, where every fire is false: about half of exit-cabin's daub gap to the truth (0.0020 of 0.0041; three quarters on the walls) and short histories in a fifth of the still cabin.

The line orchestrator's reading of the cause (from the test's definition, not measured): the false fires are the test's own nominal rate and no bug. The test compares the mean of the last 18 draws with the mean of the 18 before against 3 standard deviations that it estimates from 18 draws. That statistic follows Student's t with 17 degrees of freedom, and its two-sided chance beyond 3 is about 0.8 %, the measured rate of false fires per tested anchor-draw. Each fire costs a region about 24 draws of regrowth, which gives the fifth of the picture. So the test asks whether a region's light changed at all, where the action needs to know whether it changed by an amount that a 32-draw history cannot follow. The test already holds that amount: the difference of the two window means over their mean. The switch's change is tens of percent; a false fire's is the noise of an 18-draw mean.

Proposed second step (measure only, no rule): log the drift's relative size at each fire, |mean of the last 18 - mean of the 18 before| over the older mean, in the still cabin, exit-cabin, the fair lamp switch, rect-bay, bay-sun and the walk. If the false fires and the true fires separate by size, two repairs stand for the design: (A) a higher threshold of the same test, which lowers the false rate and delays small true changes; (B) a size gate beside the test, so that a region fires only where its drift is both real and larger than a stated share of its light. The choice waits for the log and is the orchestrator's.

**2026-11-23T03:18:40Z**

Answer 88 of the line orchestrator, 2026-11-22 23:20: the second step is ordered at the combined check's lead as a log only. For every tested anchor-draw of the build with region evidence on: the drift's relative size, the statistic, and whether it fired; in the still cabin, exit-cabin, the fair lamp switch, rect-bay, bay-sun and the walk. From the log the lead computes, for (A) a threshold of 4 or 5 and (B) the test at 3 with a size gate of 2, 5 or 10 %, the share of false fires dropped and the delay of the first true fire. It runs after the fair test of t0-gumm or shares its runs. No rule is built before the merge of Design X; the choice between A and B is the orchestrator's.
