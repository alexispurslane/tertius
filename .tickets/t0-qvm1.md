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

**2026-11-23T08:43:47Z**

2026-11-23, 03:43 New York time, the line orchestrator. The two designs of the repair, for the orchestrator to combine. Nothing is built. The replay at the end decides with numbers.

## Terms, one name each

- x(t) = the region's light of draw t at one anchor: the mean of the counted taps of its 17 x 17 blocks (as today).
- W = 18 draws, the length of each of the test's two windows (RegionWindowDraws).
- P = the jitter's phase count at the draw's render size (Fsr3Upscaler.JitterPhaseCount): 18 at the scale 2/3, 25 at 1072 x 603, 32 at the lowest scale 0.5.
- drift D(t) = the mean of the newer window less the mean of the older window, in light units.
- s1(t) = the standard deviation of the older window's W draws, in light units: the scatter of the region's light from draw to draw.
- assumed spread a(t) = s1(t) * sqrt(2 / W): the spread of D if the draws are independent. This is today's noise model.
- measured spread m(t) = the root of a running mean of D squared over the anchor's tested draws: the spread of D as this anchor really shows it.
- bar = 3 times the spread in use (DriftSigmas). Today a drift above the bar is a fire.
- cap c(t) = the longest history length that the anchor allows the blocks of its region in the next draw, 8 to 32 draws. 32 means no evidence. Today a fire gives 8.

## The model of the cost and of the gain

A still picture pays for every draw in which the test lets a noise excursion shorten a history: the share of the picture under the full length is about the false rate per anchor-draw times the draws of regrowth. Today that is 0.8 to 4.8 % times 24 draws, and the count on main measures 10.9 % (lamp hall), 21.1 % (still cabin) and 29.3 % (bay). A moving light gains from every draw in which a history is no longer than the light's drift allows. So the rule has two parts with two questions. The detector asks: how much of the drift can the noise not explain? The response asks: which history length does that much drift allow? Each design repairs one of the two parts.

## Design 1: repair the detector (the measured spread, design E), keep the cliff

The fault it removes: the assumed spread is too small where the region's light wanders from draw to draw (independence ratio up to 2.7 in the lamps-off cabin), so the bar is too low there.

- New stock: one value per anchor, m squared. It lives in the ring's free fourth channel of the draw's layer, written by the anchor's workgroup with the draw. No new image.
- The update, at every tested draw: m2 = m2 + (min(D * D, 9 * b * b) - m2) / N, where b is the spread in use of the draw before. The min keeps a true change from raising the spread without bound, and the update never stops, so the value cannot lock.
- The spread in use: b = max(a, m). The bar can only rise against today's. After a reset, a stop of the camera or a change of the render size, m starts at 0 and the test is today's until m has grown.
- N, the span of the running mean in draws: the replay tries 4, 8 and 16 times (P + W), which is 144, 288 and 576 draws at the scale 2/3. The reason for its size: D of two draws is independent only when they lie P + W draws apart, so the span holds N / (P + W) independent values. The choice is the shortest span whose false rate in the independent still (exit-cabin) stays at the nominal 0.8 %.
- The response stays: a drift above the bar caps the region at FullRateFrames, 8 draws.

The test line, as a sketch (not built):

    float spread = max(olderSpread * sqrt(2.0 / window), sqrt(measuredSquare));
    bool moved = abs(drift) > DriftSigmas * spread;

What it cannot do: in a region with independent draws (exit-cabin, ratio 0.98) the measured spread equals the assumed one. The false rate stays at the nominal 0.8 % there, and 0.8 % times 24 draws is still a fifth of the picture. A bar of 3 with a cliff of 24 draws has that cost by its own arithmetic.

## Design 2: repair the response (a cap that follows the drift), keep the assumed spread

The fault it removes: any drift above the bar costs the same 24 draws, also a drift that is 5 % above it. The log shows that false fires and rect-bay's weak true fires have the same size (statistic 3.05 to 4.10 against 3.08 to 4.31, 10th to 90th percentile), so no detector separates them. The response can treat them alike and still be right for both.

The shape and its reason. Take the part of the drift that the noise cannot explain, u = |D| - bar. A history of length n behind a light that moves by u in W draws lags it by n * u / W. The same history holds noise of s1 / sqrt(2 n), in the region's own light. The cap is the length at which the two are equal:

    n * u / W = s1 / sqrt(2 n)   gives   n = (W * s1 / (sqrt(2) * u)) ^ (2/3)

kept between FullRateFrames (8) and the full length (32). No constant is tuned: W and DriftSigmas are today's. With the assumed spread, in units of the statistic z = |D| / a, the same line reads n = W / (2 (z - 3)) ^ (2/3):

| Statistic z | 3.0 | 3.2 | 3.3 | 3.6 | 4.0 | 4.7 | 7 (a lamp switch's median) |
|---|---|---|---|---|---|---|---|
| Cap in draws | 32 | 32 | 24 | 16 | 11 | 8 | 8 |

So the cap is continuous at the bar: 32 there, 8 from a statistic of 4.7 on. A median false fire of the stills (3.3 to 3.7) costs 8 to 17 draws of regrowth in place of 24, and a fire under 3.2 costs none. The switch's fires (median 6.9 and 8.6) reach the floor. rect-bay's weak fires (3.1 to 4.3) get caps of 32 to 10.

- No new stock. The ring's answer channel holds the cap in place of a bit.
- The payload changes: a block takes the smallest cap of the anchors whose regions hold it (a minimum does not depend on the order). The evidence level RegionEvidence stays, and the spatial dispatch reads the cap where it reads FullRateFrames today:

    historyLength = evidence == LightEvidence ? min(antiLagged, FullRateFrames) : evidence == RegionEvidence ? min(antiLagged, regionCap) : antiLagged;

- The ray plan is not touched: a cap of 8 or more never gives a pixel its own ray, as today.

What it cannot do: where the light wanders (lamps-off cabin), the assumed spread is 2.25 times too small, so the false fires' statistic is high (median 3.7, 90th percentile 5.3) and the cap still falls to 8 to 14 draws there.

One open point of the shape, named and not hidden: the noise side uses the region mean's scatter s1. A pixel of the picture after the diffuse filter can be noisier than the region's mean by a factor p, which scales every cap by p ^ (2/3). The design takes p = 1 (the anchor's own units). The replay shows the table for p = 2 and 4 too. A form that reads each pixel's own retained deviation in place of s1 is possible and is left out until the replay says that p matters.

## The two points where the designs differ

| Point | Design 1 | Design 2 |
|---|---|---|
| The noise model | The measured spread of the anchor's own drift, never under the assumed one | The assumed spread, as today |
| The response | The cliff: any fire caps the region at 8 draws | The cap follows the drift that the noise cannot explain, 32 at the bar to 8 |
| New stock | One value per anchor in the ring's free channel | None |
| Weak where | Regions with independent draws: the nominal 0.8 % stays | Regions whose light wanders: the bar stays too low |
| After a reset or a stop | Today's test until the span has filled | Full from the first tested draw |
| Delay of a true fire | None | About 2 draws more until the floor at a lamp switch (the statistic must pass 4.7, not 3) |

The two repairs act on different parts and on different stills, so both together are a coherent third choice: the measured spread in the detector and the continuous cap in the response. My recommendation is both, if the replay confirms the arithmetic above. Counting over time (design K, fire at the 6th draw in a row) stays in the replay as the fallback row.

## What both designs share

1. The window under a moving render scale. The window stays W = 18 draws: its length sets the noise of a mean. What follows the render scale is the distance between the two windows: each draw of the newer window is compared with the draw P draws before it, which has the same jitter phase.

    D(t) = (1 / W) * sum over i from 0 to W - 1 of ( x(t - i) - x(t - i - P) )

   At the scale 2/3, P = W and this is today's test to the bit. At any other scale the jitter cancels in every pair, where today's two windows of 18 leave a remainder of the jitter. The older window is the W draws from t - P - W + 1 to t - P. The ring grows from 36 to at most 32 + 18 = 50 layers (P is at most 32, because the lowest render scale is 0.5), and P comes to the dispatch as a parameter beside regionSlot. The test runs after P + W valid draws in a row.
2. A change of the render size restarts the count, as today (regionContinues): the taps' scene points and the jitter's cycle both change. Under a scale that changes in most draws the test does not run. The card's log of the cabin switch on the merged code holds one render size from draw 83 to the end, so the test runs when it is needed there. t4f-71qp keeps the measure of how often the windowed game's size changes. A ring that continues across a size change is left out until that measure asks for it.
3. The tap, the region, the anchors, the validity rule, the one-draw delay of the answer and the fixed order of the sums stay. Only the bounce light runs the test.
4. The slower trail after the walk's stop (fault 3 of this ticket) is not repaired here. The take with the rule off shows 53 draws against 47 with it, so the rule is not its cause.

## The replay that decides, before any build

On the logged region lights x(t) per anchor and draw, CPU only. The candidates: T (today), E (design 1) with its three spans, C (design 2) with p = 1, 2 and 4, EC (both), and K6. Each with the pairing at the distance P of its log.

- The stills (every excursion is false): exit-cabin, the bay, the lamp hall at 1072 x 603 (P = 25, the one other scale that the orchestrator asked for) and the cabin, once its late draws are checked. Measures: the share of blocks whose model history is under 31.5 draws, to compare with the count's 10.9, 21.1 and 29.3 %; and the mean of sqrt(32 / length) - 1 over the blocks, the noise that the short histories add. For the lamp hall also today's two windows of 18 against the pairing at 25, which measures the jitter's remainder.
- The fair cabin switch: the settle of a model history per anchor that follows the logged light under the candidate's caps. Its length grows by 1 per draw to 32 and takes the cap of the draw before; settle is the first draw from which it stays within 5 % of the end level. The model is calibrated on the two measured cases: today 71 and 82 draws, the rule off 189 and none in 200.
- bay-sun and rect-bay: the model history's lag behind the logged light (today's lag film: median 13.8 draws with the rule, 42.9 without), and the caps that rect-bay's weak fires get.

A limit of the replay, known before it runs: the logged light comes from a run under today's rule, and the rays read the histories that the rule cut. The logs of the build without the rule exist for the switch, so the replay runs on both and gives the bracket.

First, by the orchestrator's order: the still cabin's dump draw 190 is early, and its grain rises from 0.0065 to 0.0112 with the rule off. So the count is repeated at a late draw, and the cabin's region log shows whether its light still moves at draw 190. If it does, the cabin is a case of a weak true drift and not a frozen still.

**2026-11-23T09:37:11Z**

2026-11-23, 04:37 New York time. The replay of the repair's candidates is done: docs/research/t0-qvm1-region-repair-replay/README.md, on main 165a964e7 for every new run. Nothing is built.

Part 1: the still cabin is a weak true drift at draw 190 (its region light rises 0.5 % with the rule and 1.4 % without it in draws 100 to 199) and frozen from about draw 400. On main it still holds 17.85 % short histories at draw 890, at a fire rate of 0.76 %. So each still's frozen part is its draws from 400 to the end.

The model's check: T replays every logged fire exactly, and its block model of the length matches the real dumps to 0.13 points or better (lamp hall 10.94 against 10.96 %, cabin 21.05 against 21.18 %, bay and exit-cabin exact). On the switch's log with the rule it settles within 3 to 17 draws of the measured settles. It cannot reproduce the no-rule side, because the logged light itself ends 4 to 6 % off the controls. Its lags are about half the picture's. So the settles and the lags compare candidates against T only.

| Candidate | Short blocks, four stills | Noise | Switch off (whole, slowest) | bay-sun lag | rect-bay lag |
|---|---|---|---|---|---|
| T | 19.7 to 32.1 % | 0.114 | 74, 90 | 7.2, 26.3 | 7.2, 27.0 |
| E8 | 7.5 to 11.7 % | 0.040 | 96, 105 | 7.2, 32.2 | 21.3, 31.7 |
| C1 | 9.1 to 16.0 % | 0.032 | 80, 98 | 7.5, 22.5 | 7.9, 31.0 |
| C2 | 4.4 to 9.5 % | 0.013 | 86, 104 | 8.3, 26.1 | 10.5, 32.5 |
| EC1 | 2.2 to 4.0 % | 0.007 | 101, 110 | 7.5, 31.4 | 28.4, 31.6 |
| K6 | 2.3 to 3.7 % | 0.012 | 76, 96 | 7.2, 29.1 | 7.3, 34.6 |

- E and EC lose rect-bay: a sustained drift raises the measured spread, so the bar rises over the motion (median lag 18 to 31 draws against T's 7.2).
- p measured after FSR: median 0.53 (exit-cabin), 1.12 (lamp hall), 0.61 (bay), 1.17 (cabin). So C1 is the continuous cap that the measure supports, and it keeps 9 to 16 % of the blocks short.
- The pairing at P = 25 in the lamp hall fires at 1.09 % against 0.95 % with today's windows: no visible jitter remainder.
- The numbers favor K6 among the candidates. It cuts the stills' short blocks about eightfold, keeps the switch within 2 to 6 draws of T and the median lags at T's, and costs about 8 draws of rect-bay's largest lag. Its cost in the cabin's weak drift is most of T's model gain: -1.14 % against T's -0.92 % and no rule's -1.24 %.

**2026-11-23T09:55:03Z**

2026-11-23, orchestrator: I read the replay's table (docs/research/t0-qvm1-region-repair-replay/README.md) and opened its picture. Point 2 is settled: the response is the continuous cap at p = 1 (C1). Reasons: it cuts the frozen stills' added noise 3.6 times (0.114 to 0.032), it keeps the switch within 6 and 8 draws of today's settle and the median lags within 0.7 draws, it keeps 62 % of the weak drift's gain, and it has no chosen constant. The measured p (0.53 to 1.17) supports p = 1. The pairing by the jitter's cycle leaves the design: it costs 14 ring layers and shows no gain in the lamp hall at 1072 x 603. Point 1 (the noise model) and a count gate before the cap wait for the added rows: the second form of the measured spread, the lamps-off cabin as a fifth still, the hybrid, and the 90th percentile of the lags. My rule for an addition: it joins C1 only if it lowers the added noise of a still where C1 alone stays visibly noisy (the lamps-off cabin is the candidate) and costs the switch and the weak drift no more than a few draws. Otherwise C1 is built alone, because the simpler rule wins at an equal look.

**2026-11-23T09:45:00Z**

2026-11-23, 04:45 New York time. The replay's second take (docs/research/t0-qvm1-region-repair-replay/README.md, "The second take"), CPU only on the existing logs. The stills' surface now comes from the logs, and T's shares move by at most 0.17 points against the first take's dump surface.

| Candidate | Short blocks, four stills / lamps off | Noise, four / lamps off | Switch off | bay-sun / rect-bay anchors' lag, 90 % | Switch light +20 after off / on | Cabin drift |
|---|---|---|---|---|---|---|
| T | 19.6-32.0 / 29.0 % | 0.113 / 0.127 | 74, 90 | 17.6 / 18.7 | 166.2 / 73.7 % | -0.92 % |
| C1 | 9.0-16.0 / 15.0 % | 0.032 / 0.041 | 80, 98 | 23.3 / 26.8 | 167.3 / 73.5 % | -1.04 % |
| CK3 | 4.9-8.7 / 8.8 % | 0.019 / 0.026 | 80, 98 | 25.0 / 27.8 | 168.3 / 73.2 % | -1.09 % |
| K6 | 2.3-3.6 / 5.5 % | 0.012 / 0.020 | 76, 96 | 26.8 / 29.8 | 171.9 / 72.2 % | -1.14 % |
| E'C1-8 | 2.7-4.6 / 9.1 % | 0.008 / 0.024 | 84, 104 | 24.0 / 31.3 (median 13.8) | 167.3 / 73.1 % | -1.09 % |
| none | 0 | 0 | 133, 149 | 34.2 / 32.1 | 212.3 / 56.5 % | -1.24 % |

- E' does not lock: it skips 0.14 to 0.34 % of the frozen tested draws, and its m reaches 0.98 to 0.99 of E's. Only the lamps-off cabin skips about 4 % of all tested draws, mostly while its light settles, and its m stays 11 to 16 % under E's. The gate halves E's motion fault but does not remove it: rect-bay's median lag is 15.1 (E'4) and 10.6 (E'8) against T's 7.2. A slow true drift raises m in its draws under the bar.
- The lamps-off cabin is the continuous cap's weakest still (C1 15.0 %), and K6 drops most of its false fires once the light settles (0.86 % from draw 400).
- On the true-change side, C1, CK3 and K6 lie close: the 90th-percentile lags differ by 3 to 3.5 draws, the switch's light 20 draws after off by 4.6 points, and the cabin's drift by 0.10 points. On the still side they differ by 2.7 times in noise. The line's view: CK3, one count over C1, gives the balance, at about half of C1's still cost and within 1 to 2 draws of C1's tails. C1 is the choice if no chosen constant is allowed.

**2026-11-23T09:59:06Z**

2026-11-23, orchestrator: I read the replay's added rows. Point 1 is settled: the noise model stays today's assumed spread, because both forms of the measured spread cost rect-bay's true drift 3 to 14 draws of median lag. The count gate (CK3) does not join now: no still is shown to stay visibly noisy under the cap alone (0.041 of added noise in the worst still). So the build is the continuous cap at p = 1, with today's assumed spread, two windows of 18 and the ring of 36. One row is still out: the line's variant in which the bar in use shrinks while an excursion lasts, B = 3 a / sqrt(1 + (r - 1) / W). I gave the line the choice by this rule, so that the build does not wait for me: the variant becomes the cap's form if it buys back at least half of the cap's loss in the anchors' 90 % lag (bay-sun 17.6 to 23.3, rect-bay 18.7 to 26.8) and keeps the added noise of every frozen still at or under 0.06. Otherwise the cap is built as chosen. The build starts on the acceptance lead's slot after that row. Its real takes decide, not the replay: the frozen stills' length maps and grain, the fair cabin switch's settle, rect-bay's film, and a side-by-side video of a still and of rect-bay, today against the build.

**2026-11-23T10:00:52Z**

2026-11-23, 05:03 New York time. The replay's last row, CP (C1's cap with a bar that shrinks while the excursion lasts, B = 3 a / sqrt(1 + (r - 1) / W)), in docs/research/t0-qvm1-region-repair-replay/README.md, "The third take". CP's stills: 9.8 to 16.9 % short blocks (C1 9.0 to 16.0 %), noise 0.036 (C1 0.032), the lamps-off cabin 16.1 % (C1 15.0 %). The switch after off: 76, 96 (C1 80, 98). The anchors' 90th percentile lag: bay-sun 21.8 (C1 23.3, T 17.6), rect-bay 26.5 (C1 26.8, T 18.7, none 32.1). So CP does not buy back rect-bay's tail. The false runs under today's bar end within 4 or 5 draws at the 90th percentile (99th: 9 to 15, longest 18), and rect-bay's true runs are short too, so the shrink stays near 0.9 of the bar where it matters. The replay is done.

**2026-11-23T10:03:27Z**

2026-11-23, 05:03 New York time, line orchestrator. The choice on the shrinking bar, and the design of the build.

**The choice (answer 98, by the orchestrator's rule).** The shrinking bar (CP) does not become the cap's form. The rule asks that it buy back at least half of the cap's loss in the anchors' 90 % lag. It buys back 1.5 of 5.7 draws in bay-sun (23.3 to 21.8, against today's 17.6) and 0.3 of 8.1 draws in rect-bay (26.8 to 26.5, against today's 18.7). Its added noise passes (0.036 in the four stills, 0.046 in the lamps-off cabin, limit 0.06). The cause: the runs above the bar are short, false and true alike (90 % end within 4 or 5 draws), so the bar falls only to about 0.9 of its height where it acts. So the build is the continuous cap at p = 1 as chosen.

**The design of the build: the region's cap.**

Terms, each with one meaning:
- The region's light: the mean of an anchor's counted taps in one draw, free of the exposure (as today).
- The drift D: the mean of the newer window of W = 18 draws less the mean of the older window.
- The older spread s1: the standard deviation of the older window's 18 draws (`olderSpread` in `regionTestOf`).
- The bar: `DriftSigmas * s1 * sqrt(2 / W)`, today's tolerance, unchanged.
- The excess u: |D| less the bar.
- The region's cap n: the longest history, in draws, that the region's light allows. n = (W * s1 / (sqrt(2) * u))^(2/3), and at least `FullRateFrames` (8). Where u is 0 or less, the region gives no cap.

The reason of the formula (decision Y4 wants a reason and no tuned constant): a history of n draws lags a drift of u per window by n * u / W, and its noise is s1 / sqrt(2 n). The cap is the n at which the two are equal. In units of the test's statistic z = |D| / (s1 * sqrt(2 / W)), n = W / (2 (z - 3))^(2/3): z 3.3 gives 25, 3.6 gives 16, 4.0 gives 11, and 4.7 or more gives 8. A history is at most 32 draws long, so a cap above 32 changes nothing.

What causes what:
- Who decides: the anchor's workgroup, invocation 0, in `updateRegionAnchor`. It owns the test and now also the cap. It writes the answer into its ring's layer of this draw.
- Where the result is stored: the ring's b channel. Its meaning changes from "1 where the light moved" to the region's cut, 1 / n, and 0 where the region gives no cap. The reciprocal is stored for two reasons. An all-zero texel is a valid "no cap". A block takes the strongest answer of its anchors with a maximum, which does not depend on the order of the invocations (the bits of floats that are not negative order as the floats do, so `atomicMax` on `floatBitsToUint` works).
- Who reads it: every block, one draw late, through one function of `region_drift.glsl`. The block's cut is the largest cut of the anchors whose regions hold it.
- What it changes: the spatial dispatch's line of the cap. Sketch: `historyLength = evidence == LightEvidence ? min(antiLagged, FullRateFrames) : evidence == RegionEvidence ? min(antiLagged, regionCap) : antiLagged;` where regionCap = 1 / the block's cut.

Signatures (GLSL, `region_drift.glsl`; the lead can change a name or a detail with a recorded reason):
- `float regionCutOf(RegionDraw now)` replaces `bool regionTestOf(RegionDraw now)`: the region's cut of this draw, 0 until 2 W valid draws in a row exist and 0 where the drift is at or under the bar.
- `float regionAnchorCutBefore(ivec2 block, uint candidate)` replaces `bool regionAnchorMovedBefore(ivec2 block, uint candidate)`: the cut that the anchor `candidate` gave in the draw before, 0 for a candidate that does not exist and in a draw that does not continue the draw before.

Settled:
1. The bar, the two windows of 18, the ring of 36 layers, the taps, the validity rule, the one draw of delay and `regionContinues` do not change.
2. One store for one fact. The block's region evidence and the block's cut must not be two values that can disagree: derive the level from the cut (a cut above 0), or drop the level and keep the cut.
3. The order of the levels stays: under motion evidence or light evidence the region's cap does not act, and the anti-lag still shortens a history below the region's cap.
4. Vulkan portability: no reliance on an infinity or a NaN. Bound the division where u is small and where s1 is 0. With s1 = 0 and D exactly 0 the region gives no cap, as today (the windows' sums subtract the older window's first draw for this). With s1 = 0 and |D| above 0 the cap is 8, today's answer.
5. The floor of 8 has one owner and one comment with its reason: a shorter history makes every pixel of the block trace its own ray (`screen_trace_plan.comp`).
6. Determinism: one machine gives the same bits in every run. The sums keep their fixed order.

The readers of the ring's b channel, all of which change with its meaning:
- `regionAnchorMovedBefore` in `region_drift.glsl`, the only shader reader, called from `evidenceOfBlock` in `accumulate_spatial.comp`.
- The comment of `TemporalSignal.RegionRingFormat` (`TemporalSignal.cs`) and the ring's comment in `region_drift.glsl`.
- The tests that read a ring layer with `ReadImageLayer` in `AccumulatorRenderTests` (the region's class).
- The region log of `docs/research/t0-qvm1-region-off/tools/regionlog.sh` and the tools that read its output, and `docs/research/t0-acxm-region-evidence/tools/mutants.py`. They are records of past takes: change none, but name in the build's record that a new log's b is a cut.
- `docs/plans/bounce-light-confidence.md`, sections 8.2, 8.7, 18 and 20.

Out of scope: a count gate (the measured next step if the real stills still show the blocks), a measured spread, the pairing by the jitter's cycle, the tap's pooling (one pixel per block), and the restart at a change of the render size (`t4f-71qp`).

Acceptance, by real takes on the built code against main (the replay is not the bar):
1. The frozen stills (lamp hall, the still cabin at a late draw, the bay, and the lamps-off cabin): the map of the history's length, the share of short histories and the grain.
2. The fair cabin switch: the settle after off and after on (today 71 and 82 draws; the rule off 189).
3. rect-bay's film: no worse than main.
4. Side-by-side videos, today against the build, of one frozen still and of rect-bay, with frames looked at 1:1.
5. The cost of the spatial dispatch does not rise beyond the noise of the take.
