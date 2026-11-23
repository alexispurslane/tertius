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

**2026-11-23T03:18:50Z**

Correction by the line orchestrator, 2026-11-22 22:20 by the machine's clock: the clock times in my notes and messages of this evening after 21:50 were guessed and ran ahead. The messages that I labelled 22:00, 22:30, 22:40, 23:20 and 23:25 were sent between about 21:50 and 22:17. The commit times of the line's branch are right. From here on I read the clock before I write a time.

**2026-11-23T03:19:29Z**

The orchestrator's answer of 2026-11-22, about 22:18: the rule stays, and removal is off the table. The reading of the false fires as the test's nominal rate and the model are accepted. Answer 88 stands. Its lean before the table is repair B, the size gate, because it uses the quantity that the action needs and that the anchor already holds; a higher threshold (A) delays every true fire and still acts on a change that the history could follow. The log must show for B, at each gate, the delay of the first true fire in the lamp switch and in the bay's lag film and the share of the stills' false fires that pass. If a gate of 5 % keeps the switch's 71 / 82 draws and drops most false fires, B is chosen. The orchestrator also names design B2: the gate in the action and not in the test, so that a history is cut only to the length that can follow the measured change, and the rule has no threshold of its own. The line orchestrator's form of B2 for the log (a derivation, not measured): a history of length n behind a light that drifts by the share d between two windows of 18 draws lags it by about d n / 18, so the longest history that stays within the settle band b = 5 % has n = 18 b / d, kept between FullRateFrames (8) and the cap (32). A drift under 2.8 % then cuts nothing. The bright speckle on the bay's left wall and floor, in the still as in the film with the rule, is not this rule's; it fits the ray census's class and waits for ter-pdw8. Nothing is recorded against it on this line.

**2026-11-23T04:45:30Z**

The second step, done by the combined check's lead on 2026-11-22 (its commit 626581572, 23:43; record docs/research/t0-qvm1-region-off/README.md, "The second step"; pictures pics/region-fires-quantiles.png and pics/region-b2-nstar.png). Mac runs of the cap's commit 05a27262d with region evidence at the constant scale 2/3: exit-cabin, rect-bay, bay-sun, the fair lamp switch and its two still controls, and the earlier walk log. The lead's offline test agrees with the GPU's answer at all 851,425 tested anchor-draws.

Terms: d is the drift's relative size, |mean of the newer 18 draws - mean of the older 18| over the older mean. The statistic is that difference over (the older window's standard deviation times the root of 2 / 18); the test fires above 3.

The fires' quantiles (10, 50, 90 %), d then the statistic:
- false, exit-cabin (1,927 fires): 0.093, 0.140, 0.209; 3.05, 3.33, 4.10.
- false, cabin with lamps on (1,526): 0.031, 0.047, 0.103; 3.06, 3.43, 4.70.
- false, cabin with lamps off (5,226): 0.048, 0.072, 0.121; 3.12, 3.71, 5.32.
- false, the switch's still draws 69 to 192 (532): 0.031, 0.048, 0.093; 3.06, 3.39, 4.44.
- true, switch off (8,104): 0.098, 0.259, 0.401; 4.17, 6.93, 14.82.
- true, switch on (8,453): 0.125, 0.396, 0.888; 4.62, 8.62, 23.28.
- true, bay-sun (6,073): 0.156, 0.611, 1.696; 4.45, 12.46, 23.90.
- true, rect-bay's first 40 draws (55): 0.090, 0.149, 0.350; 3.08, 3.55, 4.31.

The candidate rules: share of all false fires dropped; exit-cabin's; the switch's off: first fire's delay and an anchor's extra delay, median / largest; bay-sun's; the share of rect-bay's film fires kept:
- A4, the statistic above 4: 71.4 %; 87.1 %; 0, 1 / 13; 3, 1 / 27; 72.9 %.
- A5: 90.7 %; 98.0 %; 1, 3 / 14; 4, 3 / 31 with 2 anchors that never fire; 46.3 %.
- B5 %, the test at 3 and d at least 5 %: 20.1 %; 0 %; 0, 0 / 11; 3, 0 / 2; 100 %.
- B10 %: 69.0 %; 14.7 %; 2, 3 / 15; 3, 0 / 15; 99.3 %.
- B2 (cut to the length 18 x 5 % / d between 8 and 32) after the test: the first true fires as today; the cabin's and the switch's false fires cut to 13 to 19 draws in place of 8; exit-cabin's still cut to 8 at 75.7 %. B2 without the test would cut at 19 to 58 % of the stills' tested anchor-draws, against today's 0.9 to 4.8 %, so the derivation's reading of a drift under 2.8 % as the only noise is wrong.

The plain answer: neither a higher threshold nor a size gate separates the two kinds of fire in every scene. The size gate fails on exit-cabin, whose dim anchors give false fires as large in d as rect-bay's true ones. The higher threshold loses rect-bay's weak true fires, whose statistic is that of the false fires. By the orchestrator's own test (a gate of 5 % must drop most false fires), B is not chosen. The log measures no settle, so the switch's 71 / 82 draws under a changed rule are not proven either way.

The line orchestrator's reading: the share of tested anchor-draws that fire in a still is 0.9 % in exit-cabin, near the test's nominal 0.8 %, and 4.8 % in the cabin with its lamps off, six times nominal. The test assumes independent draws. Two causes can raise the rate, and both can be checked on the logs: the region's light wanders slowly in a bounce-lit room, so the noise of an 18-draw mean is larger than the per-draw deviation over the root of 18; and the rule may feed itself, because a false fire shortens histories, the rays then read noisier light, and the region wanders more.

Answer 93 of the line orchestrator: three numbers more from the logs, CPU only, ordered at 23:45: (1) the share of fires per still with the rule and without it (the first step's build without the rule logged its fires); (2) per anchor in each still, the deviation of the 18-draw means against the per-draw deviation over the root of 18; (3) the run lengths of consecutive fires, false against true, and a candidate that fires only at the k-th consecutive draw above 3, for k = 3, 6, 9, on the same table; also the share of fires in rect-bay's film. No rule is built. The design of the repair waits for these and is the orchestrator's choice.

**2026-11-23T04:46:45Z**

The orchestrator's decisions on the log, 2026-11-22 late evening. (1) Neither A (a higher threshold) nor B (a size gate) is chosen: the log refutes the lean toward B, by the table in the note above. (2) Answer 93 stands; of the three checks, the one on the noise model matters most. (3) No repair of the region test is built until ter-pdw8 has reported and the orchestrator has set the first change of ter-6fka. Reason: both candidate causes come from what the rays read. The region's light wanders because a ray reads the last draw's filtered light, and a false fire feeds itself through that same read. ter-pdw8 changes that read, and the far-field step after it changes the samples' noise again, so a test tuned to today's noise would be tuned twice. The cost of waiting is known and small: a fifth of a still picture with a history of 8 to 31 draws, and a daub of 0.0173 against 0.0153 in exit-cabin. (4) The three checks become this ticket's design note for the build after the structural step's first change: one sentence each on whether the false rate is a property of the estimator (the correlation between draws) or of the rule's own action (the loop); if the estimator, the quantity that a repaired test compares the drift with and the state it costs; if the loop, how much of the stills' cost B2 after the test removes. (5) For rect-bay's weak fires: count over time, not a higher bar; the candidate that fires at the k-th consecutive draw above 3 is the third row beside A and B. Sent to the lead.

**2026-11-23T04:49:56Z**

The design note for the repair of the region's drift test, from the three checks on the logs (the combined check's lead, commit a38840da9, 2026-11-22 23:49; record docs/research/t0-qvm1-region-off/README.md, second step). No rule is built; the build waits for ter-pdw8's report and the first change of ter-6fka.

1. The rule's own action is not the cause of the stills' false fires. Without region evidence the switch film's still part fires at 1.65 % of its tested anchor-draws, against 1.80 % with it, and the independence ratio is the same (median 1.25 against 1.27). So there is no loop worth a design, and B2 after the test is not needed to break one.

2. The estimator is the cause. The test takes the noise of an 18-draw mean as the per-draw deviation over the root of 18, which holds only for independent draws. The independence ratio, the measured deviation of an anchor's 18-draw means over that value (10, 50, 90 %): exit-cabin 0.89, 0.98, 1.08; the cabin with its lamps on 1.08, 1.43, 2.08; with its lamps off 1.50, 2.25, 2.72; the switch's still part 0.87, 1.27, 1.83. With the noise estimate multiplied by each anchor's own ratio (an oracle over the whole run) the false rate falls to or under the nominal 0.8 %: lamps off 4.8 to 0.24 %, lamps on 1.40 to 0.23 %, the switch's still part 1.80 to 0.52 %; exit-cabin, whose draws are independent, stays at 0.90 to 0.94 %.

3. The test's own 18 draws cannot give the correction: a factor from the older window's lag-1 autocorrelation brings the lamps-off cabin only to 3.7 %. The wander is slower than one window sees.

4. What a repaired test compares the drift with: the measured spread of the anchor's own 18-draw window means over a longer span. The cheapest state is 2 running values per anchor, updated once per 18 draws: 1,020 values at 1920 x 1080 (510 anchors), against today's 36 ring layers times 510. It needs several windows after a reset before it can test; a ring of 8 windows would cost 4 times today's ring.

5. rect-bay's weak fires are mostly true: its film fires at 70 % of its tested anchor-draws, against 0.9 to 4.8 % in the stills. Persistence separates the two kinds: a true run of fires lasts 47 to 51 draws at the switch (median) and 116 in bay-sun; a false run lasts 1 to 2 draws (median), with a 90th percentile of 4 to 13. The candidate "fire only at the k-th consecutive draw above 3": K3 drops 33 to 69 % of the false fires per scene and keeps 94 % of rect-bay's; K6 drops 56 to 92 %, delays the first fire by 5 draws and keeps 87 % (A4 keeps 73 %); K9 drops 69 to 98 % and keeps 82 %. The lamps-off cabin keeps its false fires longest, because its light wanders most.

The line orchestrator's reading for the design, when its time comes: two candidates stand, and they can be combined. (E) Repair the noise model: compare the drift with the measured spread of the anchor's own window means. It removes the false fires at their cause, costs 2 values per anchor and a longer warm-up after a reset, and leaves exit-cabin at the nominal 0.9 %. (K) Count over time: fire at the k-th consecutive draw above the bar. It needs one counter per anchor and no warm-up, keeps rect-bay's weak true fires better than a higher bar, and delays every true fire by k - 1 draws; it works least where the light wanders most. Both must be measured again on the samples that the rays bring after ter-pdw8 and the structural step, because both of the stills' causes come from what the rays read. Neither is chosen.

**2026-11-23T05:01:11Z**

The orchestrator's answer on the three checks, 2026-11-23 shortly after midnight: accepted. The estimator is the cause and the loop is not. Designs E (the measured spread of the anchor's own window means) and K (count consecutive fires) stay in this design note, neither chosen, both to be measured again after the back-face line's experiment and the first change of the structural step. News from that experiment (ter-pdw8): a time mean per pixel of what a ray reads is closed, because it leaks (a pixel holds two surfaces over the jitter), and the general mottle needs a far-field store. So the wander that E measures will change with the far-field step, not before.

**2026-11-23T05:27:38Z**

A design finding from the orchestrator's read of the merge's diff, 2026-11-23, for this design note. RegionWindowDraws is 18 because that is the jitter's phase count at the render scale 2/3, so that a jitter cycle cancels in each window. The render scale is dynamic: the back-face line measured 25 phases at 1072 x 603 and 27 at 1040 x 585. At any scale but 2/3 a window does not hold a whole number of jitter cycles, so its mean carries the jitter's remainder, and the test sees it as wander. Every measure of the false rate so far ran at the constant scale 2/3, the one scale where the window fits. So two conditions join the repair: (1) the design (E or K) names what the window is under a moving render scale; (2) the log of the false rate is taken at one other scale before a design is chosen.

**2026-11-23T08:38:26Z**

2026-11-23, orchestrator: the hold on this repair's design ends. Its two conditions hold (ter-pdw8 is closed, and the surface share t1-wl7c is accepted as the first change of ter-6fka), and the count of short histories by writer (docs/research/t1-g1jd-short-histories-by-writer/) shows that on main the region's drift test writes 10.93 % (lamp hall), 21.10 % (still cabin) and 29.33 % (bay) of a frozen still's short diffuse histories, in whole blocks. I opened the bay's and the cabin's sheets: main and the anti-lag-off build show the same square blocks of 8 to 31.5 draws, and both builds with the region evidence off show one flat full length. The Design X line writes two designs into this ticket and builds nothing before my word. My direction for the two designs, reversible: they differ on two points, and I choose a side on each. Point 1, the noise model: the assumed spread of today against the measured spread of the anchor's own window means (design E). Point 2, the response: today's cliff (any fire cuts the region to FullRateFrames = 8) against a continuous response, where the region's cap follows the size of the measured drift against the measured noise, from 32 at the bar down to 8 at a drift as large as a lamp switch's. My prior is the continuous response, because the line's own log shows that false fires and rect-bay's weak true fires overlap in size, so no detector separates them. Under a continuous response a weak fire, true or false, costs a little length, and a weak true drift needs only a little. Counting over time (design K) stays a fallback, because it adds delay to every true fire. Both designs state what the window is under a moving render scale (RegionWindowDraws = 18 equals the jitter's phase count only at scale 2/3, and regionContinues restarts the test at each size change). Test of both before any build: a replay on the logged region means, with the false fire share of the three frozen stills, the settle draws of the fair cabin switch (today 71 and 82 against 189 without the rule) and rect-bay's weak true fires. Open first: the still cabin's grain rises from 0.0065 to 0.0112 with the rule off, and its dump draw 190 is early in the run, so its fires can be true. The line repeats that count at a late draw before it uses the cabin as a frozen case.
