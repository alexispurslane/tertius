---
id: t0-5tyj
status: in_progress
deps: [t0-f8oc]
links: []
created: 2026-11-13T01:01:07Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Take Design X's stills, climb, motion films, frame time, Linux check and screenshots

Design: docs/plans/bounce-light-confidence.md (Design A with Design X's evidence), section 23. Branch t7-3e22-retained-deviation. Step 17, takes T15 to T19 (section 21). Stills, climb (T2a and T2b), motion films and screenshots on the Mac under the shared GPU lock; frame time (T17) under the exclusive lock; the Linux check (T18) on the GPU workbench. Crops beside the truth first, then the scores. Acceptance 1 to 4, 10, 11, 13 and 14.


## Notes

**2026-11-14T23:46:41Z**

2026-11-14, takes T15 to T19 for the branch with the dark-settle fix (eccad009a; cloud jobs used the scratch commit b4b54ff79 with the same shaders) against main ab54c785e. Mac takes under the GPU lock, each comparison on one machine; scratch files in /private/tmp/t7-3e22/x2 (not kept), pictures in docs/research/design-x-acceptance-2026-11-14/pics. Two takes are worse than main, so the branch does not merge yet: the cabin's center region after the lamps return (acceptance 5) and the deck's frame span (acceptance 13, ticket t0-89zi).

Acceptance 1, long mean per region as a share of the reference (the branch with both clamps off), survey stills at one fixed exposure (4.08 stops for rect-lamps, -4.0 for the bay; light.py inverts the tone curve of the 8-bit stills, which is not the HDR measure of take T1a): rect-lamps ceiling/walls main 78.4/81.2 %, branch 96.0/99.5 %; bay-sunbeam walls/floor main 90.3/84.1, branch 98.7/99.2; docking-bay walls/floor main 89.5/93.0, branch 97.8/99.7. The limit is 97 to 102 %: the branch misses it by 1.0 point in rect-lamps' ceiling, main misses it in every region. The stills of the branch before the fix are equal to the fix's to every printed digit: a still has no evidence.

Acceptance 2 and 4, survey stills at draw 132 (blotch, limit 0.04; specks per 10,000 px; daub against the reference's long mean, bar 0.008 to 0.010; the reference's own shown still in brackets): rect-lamps blotch main 0.0530, branch 0.0933 (0.1064); specks 7.05, 6.01; daub 0.0432, 0.0403 (0.0416). bay-sunbeam blotch 0.0343, 0.0268; specks 0.055, 0.064; daub 0.0311, 0.0209 (0.0149). docking-bay blotch 0.0372, 0.0265; specks 0.160, 0.143; daub 0.0396, 0.0281 (0.0206). exit-cabin blotch 0.0200, 0.0150; specks 0.241, 0.198; daub 0.0268, 0.0150 (0.0126). rect-lamps fails the blotch limit in both builds, as in step 3: the branch shows 87 % of the reference's light at draw 132, as the reference's own still does. Crops beside the reference, looked at first: bay-sunbeam's floor has large soft blotches on main and none on the branch; rect-lamps' ceiling band has soft patches on main and a smoother band on the branch; the fine specks are the same. The daub stays above the epic's bar in every still, so t7-3e22 stays open.

Acceptance 5, slowest region off/on (whole image off/on), draws: cabin main 75/52 (36/24), branch 66/not settled in 100 (12/27); bay-sun-off main 49/36 (27/20), branch 36/13 (15/7); lamp-hall-16 main 32/25 (6/5), branch 36/25 (7/5). The cabin's center region r11 ends 6.0 % low 99 draws after the lamps return, main 2.0 %; the whole image -2.2 % against -0.9 %. Bounce alone: branch 61/none (9/57), main 60/65 (39/48). Reflections alone: branch 66/none (34/92), main 103/78 (74/5). So acceptance 8 does not apply.

Acceptance 6, walk (cloud job, both builds): the branch fails in r11 alone (135 draws; r01 30, r12 33), main fails in r10 (198), r00 (61) and r12 (67). Acceptance 7, stand: r11 does not settle within 355 draws after tick 306 in either build.

Acceptance 9, lag films, median lag in draws and median lag error (share of the light): bay-sun main 28.55/0.2644, branch 26.93/0.2679 (+0.0035); lamp-hall-16-moving 4.43/0.0276, 4.45/0.0275; lamp-hall-64-spot-moving -0.06/0.0106, -0.28/0.0116 (+0.0010). Within the allowance of 0.005.

Acceptance 11, climb (cloud job): the band's peak as a share of the same build's band with the filter off: branch 0.970, main 1.108. Acceptance 12, census of rect-lamps, exit-cabin, docking-bay and station-cabin (cloud job, 329 draws each): no block-draw with motion or light evidence in either signal.

Acceptance 13: median GPU frame span of the deck, three rounds in turn: main 19.007, 19.074, 18.990 ms; branch 19.536, 19.482, 19.549 ms; +0.50 ms against the limit +0.3 ms. Ticket t0-89zi holds the traces: the filter +0.235 ms, the ray plan +0.166 ms, three more passes +0.14 ms; the cost stays with the evidence's action off.

Acceptance 14, Linux (cloud worker): the cabin switch gives 66/not settled (12/30) for the branch and 75/52 (37/25) for main, within 3 draws of the Mac. The Checked GPU tests with validation on Linux did not run: they are a gate of the merge.

T19: headless screenshots of the bay and of rect-lamps, main beside the branch, in the pictures folder. Cloud cost by the job log: $0.0521 + $0.0924 + $0.1078 = $0.25. The motion films (T16) follow in the next note.

**2026-11-15T01:19:43Z**

2026-11-14, take T16: the 18 films of the survey's motion part, main ab54c785e against the branch with the fix. 14 films on the Mac, one build after the other per film; rect-lamps, bay-sunbeam and exit-deck in the cloud, both builds in one job. A third take is worse than main: the film rect-bay (ticket t0-acxm).

Blotch of the worst sampled frame against a still of the same state (limit 0.04), main -> branch, and the level (the frame's light over the still's, minus 1):

| Film | blotch main | blotch branch | level main | level branch |
|---|---:|---:|---:|---:|
| rect-bay | 0.245 | 0.488 | +1.176 | +1.863 |
| rect-lamps | 0.063 | 0.095 | -0.056 | -0.070 |
| bay-sunbeam | 0.330 | 0.395 | -0.601 | -0.567 |
| exit-floor | 0.295 | 0.316 | +0.203 | +0.211 |
| docking-landing | 0.181 | 0.169 | +0.012 | -0.013 |
| lamp-hall-4-spot-moving | 0.090 | 0.077 | +0.002 | +0.002 |
| lamp-hall-64-moving | 0.066 | 0.066 | +0.006 | +0.007 |
| lamp-hall-16-moving | 0.042 | 0.042 | +0.005 | +0.004 |
| rigs-crowd | 0.036 | 0.037 | 0.000 | -0.001 |
| exit-cabin | 0.031 | 0.024 | +0.010 | +0.007 |
| pillar-room | 0.018 | 0.012 | 0.000 | 0.000 |
| spot-box | 0.015 | 0.011 | -0.001 | -0.001 |
| skin-head | 0.011 | 0.008 | 0.000 | 0.000 |
| exit-deck | 0.009 | 0.008 | +0.001 | +0.001 |
| hair-bent-cards | 0.007 | 0.006 | 0.000 | 0.000 |
| rect-lights | 0.005 | 0.005 | 0.000 | 0.000 |
| reflections | 0.002 | 0.001 | 0.000 | 0.000 |
| orbit-umbra | not measured | not measured | 0.000 | 0.000 |

Flicker and grain flicker do not rise in any film (the largest change is rect-bay's flicker, 0.0020 -> 0.0010). The same films fail the survey's limits in both builds. The four films where the branch scores worse are films whose light is on its way in both builds: their level is far from 0. In the films with a level near 0 the branch's blotch is equal or lower. I looked at rect-bay's frames (pics/rect-bay-film.png); I did not look at the frames of the other 17 films.

Cloud cost of the films and the rect-bay diagnosis by the job log: $0.1078 + $0.0758 + $0.0978 + $0.0755. The track's cloud total is $0.50.

**2026-11-15T01:31:24Z**

2026-11-14, the cabin switch read by the second measure (parent orchestrator's correction): the level as a share of the reference. Computed from the kept result files of the takes with the bounce light alone (TERTIUS_PROBE_SIGNAL=2), because no reference run of the full image exists; t0-mzhr takes it for the full image. Level = a region's light in the switch run divided by the reference's control with the lamps in the final state, at the same film frame. The reference is the branch with both clamps off.

| Build | settle, slowest region, off / on | level at 60 draws after off, whole / center | level at 60 draws after on, whole / center | lowest level in the 60 draws after on, center |
|---|---|---|---|---|
| main | 60 / 65 | 0.899 / 0.910 | 0.805 / 0.729 | 0.645 |
| branch before the fix | 101 / none in 100 | 0.987 / 0.926 | 0.934 / 0.865 | 0.736 |
| branch with the fix | 61 / none in 100 | 1.006 / 0.951 | 0.951 / 0.887 | 0.774 |
| reference | 122 / none in 100 | 1.064 / 1.051 | 0.942 / 0.940 | 0.911 |

Main's own controls stand at 0.835 (whole) and 0.769 (center) of the reference's with the lamps on, and at 0.875 and 0.892 with the lamps off: main settles fast to a level that is 11 to 23 % too dark. The fixed branch's controls equal the reference's to three digits. So by the first measure the branch is slower than main after the lamps return, and by the second it is nearer to the reference than main at every one of these points. One exception: in the 60 draws after the lamps go off, the center's lowest level is 0.778 on the fixed branch and 0.820 on main, which is the dip after a cut.

**2026-11-22T11:14:17Z**

Design of the last acceptance takes (line orchestrator, 2026-11-22). The takes of 2026-11-14 are out of date: since then the line got the frame time cuts, the half-float store fix, the rounding weight fix, the sun's shadow fix from main, and the remedy for the bay film's trail (the region's drift test). Main got the store fix and the shadow fix. So the user's decision on the merge (t0-iido) needs every take again, on today's heads. This is an experiment plus one tool fix. No change under src or tests.

The sides:
- M = main at c4c1f1f2f, with main's survey probe patch.
- X = the line's head, with the line's survey probe patch after the fix below.
- A = X with both history clamps off, the reference that explains a fault. It is not the bar.

Step 0, the tool fix (bug tc-w3c0): bring the two hunks of docs/research/t0-acxm-region-evidence/probe-acxm.patch into tools/renderer-survey/probe.patch, so that probe-build works on the line's head. Its acceptance is in that ticket: the still rect-lamps equals the build from probe-acxm.patch byte for byte.

What is taken already on today's code, and is cited, not taken again (the code under src did not change since 6d8a68332):
- The room (docs/research/t0-9fo0-room-after-shadow-fix): the still rect-lamps, the switch cabin and the stand film, on M and X.
- The bay (docs/research/t0-acxm-region-evidence): the film rect-bay, the lag film bay-sun and the stills bay-sunbeam and exit-cabin, on M and on the remedy's build before its review fixes. Take the film rect-bay once more on X, and say whether its numbers equal the cited ones.
- The deck's frame span: +0.254 ms against M (spread 0.145), from the remedy's paired rounds.

The new takes, each on M and on X through the survey, with the same measure on both sides:
1. Stills, the survey's whole still part. Blotch, specks and level for every case. The daub against A and the light share for rect-lamps, bay-sunbeam and docking-bay (acceptance 1, 2 and 4).
2. Motion films, all 18. Flicker, grain flicker, and the blotch and the level of the worst sampled frame.
3. Switches: bay-sun-off and lamp-hall-16 (acceptance 5). The cabin is cited.
4. Lag films: lamp-hall-16-moving and lamp-hall-64-spot-moving (acceptance 9). bay-sun is cited.
5. The walk cabin-walk and the climb cabin-climb (acceptance 6 and 11).
6. The census of the four stills rect-lamps, exit-cabin, docking-bay and station-cabin on X (acceptance 12): the share of block-draws with motion evidence and with light evidence, and beside it the share of tested anchor-draws in which the region's test fires.
7. Frame time, take T17 (acceptance 13): three rounds in turn of the deck on M and on X under the exclusive lock, at a low machine load. Check the load before each round, and repeat a round that scatters. Also record the frame span of the film rect-bay on both sides.
8. Linux, take T18 (acceptance 14): the whole Checked Rendering test project with validation on a Linux worker, with no validation message. The switch bay-sun-off on Linux, which must lie within about 3 draws of the Mac's. Read t0-89zi and t0-iido for the pixel equality of the frame time cuts on Linux, and take what they name.
9. Screenshots, take T19: two to four headless screenshots of the real renderer on X, main beside them.

Rules of the pictures. A film or a still where X scores worse than M in any measure gets a sheet: M beside X, with the still of the same state where the survey keeps one. Open each sheet with the Read tool and say by the bar what it shows. The films rect-bay, bay-sunbeam, docking-landing, exit-floor and rect-lamps get a sheet in any case, because their light is on its way in both builds.

The record: docs/research/design-x-acceptance-2026-11-22/README.md. It starts with one table for the user's decision: one row per scene and take, with "better", "equal" or "worse" than main by the bar, and the picture's path. Then every take where X is worse than M, named plainly. Then the plan's acceptance points 1 to 14 with pass or fail on both sides. Measured facts stay apart from the reading.

Acceptance of this ticket: the record with its pictures and tables, the probe patch fixed, one review with every finding resolved, and no change under src or tests.

**2026-11-22T11:15:02Z**

Launch (line orchestrator, 2026-11-22). The room's lead continues as the acceptance lead in its thread thread:delegated-task:command%3Amcp%3A8450ae50-a640-49ce-ab14-e0397e6a826d%3Adelegate-task%3Adesign-x-line-t0-xlcu-lead-1, clientRequestId design-x-line-t0-5tyj-acceptance-takes, run ordinal 14, worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-c3911144, branch t3/measure-regional-lag-tolerance, from the line's head 57b4045ae. Cloud budget 1.50 dollars. Step 0 is the probe patch fix of tc-w3c0.

**2026-11-22T13:43:48Z**

From the parent orchestrator, 2026-11-22 (recorded by the line orchestrator). (1) The user looked at the stills table and wrote: "I think Design X is strictly better, since the 'worse' captures aren't visibly worse to me either." So the 18 stills that score worse do not count against Design X and need no more work. (2) The user decided to merge the back-face layer into main. The combined check t0-gbgj starts only on the parent's word, with main's new commit. (3) One more take in this record, for the merge decision: the region's test under the dynamic render scale (t4f-71qp). The switch cabin with the dynamic scale on, the render size of every draw, the test's fires after each switch, and the draws to settle beside the fixed-scale X, X0 and M. Sent to the acceptance lead as a steer, clientRequestId design-x-line-t0-5tyj-steer-dynamic-scale.

**2026-11-22T14:10:29Z**

Early result (b) of the acceptance lead, 2026-11-22: the 18 motion films, M = main c4c1f1f2f, X = the line at 57b4045ae. Measured by the lead: 5 better on X, 9 equal, 4 worse, orbit-umbra too dark to measure on both sides. The 4 worse: rect-bay flicker 0.00204 to 0.00273 and bay-sunbeam flicker 0.00393 to 0.00519 (limit 0.006, both with a better blotch and level), docking-landing end level +0.014 to -0.037, exit-floor end blotch 0.073 to 0.086. Also: the switch bay-sun-off settles in 24 / 17 draws on X against 50 / 38 on M, lamp-hall-16 and the two lag films are equal, and the bay's light at a fixed exposure is 97 to 99 % of the clamps-off reference on X against 81 to 92 % on M. My reading of the four sheets (pics/film-rect-bay.png, film-bay-sunbeam.png, film-docking-landing.png, film-exit-floor.png in the lead's record): no film of X shows a fault that M lacks. In rect-bay, M's end frame is brighter on the left wall than M's still, and X's end frame is close to X's still. In docking-landing, the fine grain beside the sunbeam's patch is in all four columns. In exit-floor the four columns look alike.

**2026-11-22T14:11:47Z**

Added item from the orchestrator, 2026-11-22, sent to the lead as a steer (request design-x-line-t0-5tyj-sun-patch-edge): in the film bay-sunbeam, the middle frame's sun patch on the left wall has a saw-toothed left edge in the M film and in the X film, and a smooth edge in both stills. It is on both sides, so it is no fault of Design X. The lead makes one full-size crop of M film, X film and a still, gives the teeth's size in pixels and says whether they crawl. If they are real, it files a bug under ter-8hyd with two candidates named and not fixed: the cascade's texel staircase under the one-sided filter of t8-rjno where the patch clips to white, or the history's reset under the sun's motion. The orchestrator agreed with my reading of the motion films and relays it to the user.

**2026-11-22T14:14:46Z**

Early result (c) of the acceptance lead, 2026-11-22: the walk cabin-walk is the one take where X is worse than M by the bar's trail. Measured by the lead: after the camera stops both sides start 7 to 10 % too bright in the walked regions. Draws until the slowest region stays within 5 % of its control: M 26, X 48 (the survey's goal is 60). Per region M / X: r01 23 / 45, r10 26 / 48, r11 15 / 32, r12 1 / 21, r20 15 / 33. r11's excess at draws 0, 20, 40, 60, 100, 150: M +6.8, +4.3, +2.6, +1.7, +0.8, +0.3 %; X +7.7, +5.9, +4.3, +3.1, +1.9, +1.0 %. Chart pics/walk-cabin.png. The climb cabin-climb is not worse: the bounce band's peak over its light with the filter off is 1.012 on M and 0.984 on X. My reading of the chart: X starts about 1 % above M and keeps an excess of 1 to 3 % for about 20 draws longer; the fault is the same on both sides and both pass the goal. Region r12 shows a saw-tooth of about 2 % with a period of about 9 draws on both sides. Answer that I gave myself: no fork, and the lead adds one sheet of frames of the stop (M beside X at 0, 20, 40 draws and settled, with an amplified difference row), because the user judges by pictures. If one more walk take is cheap, it also measures the line without the remedy, to say whether the wider clamp or the region ring's restart at a camera move causes the slower fade.
