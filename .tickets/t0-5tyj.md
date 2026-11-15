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
