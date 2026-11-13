---
id: t0-f7zv
status: open
deps: [t0-anp3]
links: []
created: 2026-11-12T13:56:23Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Takes T1 and T2 of Design A, and set RetainedSigmas

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 3.


## Notes

**2026-11-12T14:01:07Z**

Orchestrator, 2026-11-12: measure on the GPU workbench (tools/runpod/WORKBENCH.md on main): cost A/B against main with ab_run.sh, and the quality A/B (stills, films, daub scores computed inside the job, a job returns at most 8 MB), several variants in parallel jobs. Compare only inside one job. The Mac's GPU lock is kept for one confirmation take of the final candidate before the merge and for Apple-compiler questions.

**2026-11-12T15:43:16Z**

2026-11-12, step 3 on the GPU workbench: takes T1a, T1b and T2a. Scratch branches t7-3e22-measure (this branch + tools/renderer-survey/probe.patch + knobs TERTIUS_A_RETAINED_SIGMAS and TERTIUS_A_NO_CLAMP, a negative ClampSigmas that skips both signals' clamps and so the anti-lag) and t7-3e22-measure-main (main b1ce2701b + probe.patch), never merged; job scripts tools/measure-t7/ there. One job per still and part, every variant inside the same job. Truth = Design A with both clamps off. Kept: /private/tmp/t7-3e22/measure-3/kept (JSON of every job, crops, table.py prints the tables).

T1a, regional light (fixed exposure 4.08 stops, stillsig rules; lightsl.py's regions and smooth mask). "Long" = mean of draws 297-924 as % of the truth's long mean; "shown/own" = the draw-132 still as % of the variant's own long mean (how far it has settled).

| Still, region | A, RetainedSigmas 2 | A, RetainedSigmas 3 | main | truth shown/own | A2 shown/own | main shown/own |
|---|---:|---:|---:|---:|---:|---:|
| rect-lamps ceiling | 97.8 | 97.8 | 81.9 | 92.8 | 93.5 | 98.6 |
| rect-lamps walls | 99.2 | 99.2 | 79.6 | 86.0 | 86.8 | 97.3 |
| bay-sunbeam walls | 99.2 | 99.2 | 94.0 | 99.3 | 99.5 | 100.0 |
| bay-sunbeam floor | 99.1 | 99.1 | 88.8 | 98.4 | 98.7 | 99.8 |
| docking-bay walls | 98.4 | 98.4 | 91.7 | 98.6 | 99.1 | 100.0 |
| docking-bay floor | 99.6 | 99.6 | 94.2 | 99.3 | 99.4 | 100.2 |

RetainedSigmas 3 differs from 2 by at most 0.03 points: at 2 the widened box almost never binds. Every region of A lies in 97.8-99.6 % (acceptance 1 holds). Set RetainedSigmas = 2 (the design's value; 3 buys nothing).

T1b, survey still part (auto exposure, ACES, no bloom), draw 132. daubT = daub.py against the truth's long mean (5 x 5 median), daubOwn = against the variant's own long mean; specks per 10,000 px, blotch and level against own.

| Still | Variant | daubT | ceiling/walls or walls/floor (daubT) | daubOwn | specks | blotch |
|---|---|---:|---|---:|---:|---:|
| rect-lamps | truth | 0.0438 | 0.0500 / 0.0275 | 0.0415 | 14.61 | 0.110 |
| rect-lamps | A2 | 0.0401 | 0.0485 / 0.0280 | 0.0375 | 7.13 | 0.102 |
| rect-lamps | A3 | 0.0403 | 0.0496 / 0.0278 | 0.0378 | 7.19 | 0.103 |
| rect-lamps | main | 0.0429 | 0.0515 / 0.0278 | 0.0348 | 7.80 | 0.054 |
| bay-sunbeam | truth | 0.0185 | 0.0171 / 0.0240 | 0.0183 | 0.06 | 0.028 |
| bay-sunbeam | A2 | 0.0229 | 0.0227 / 0.0240 | 0.0244 | 0.06 | 0.027 |
| bay-sunbeam | A3 | 0.0230 | 0.0227 / 0.0240 | 0.0244 | 0.08 | 0.027 |
| bay-sunbeam | main | 0.0328 | 0.0307 / 0.0392 | 0.0331 | 0.08 | 0.034 |
| docking-bay | truth | 0.0260 | 0.0269 / 0.0205 | 0.0256 | 0.11 | 0.027 |
| docking-bay | A2 | 0.0332 | 0.0352 / 0.0210 | 0.0356 | 0.28 | 0.028 |
| docking-bay | A3 | 0.0332 | 0.0352 / 0.0210 | 0.0356 | 0.25 | 0.028 |
| docking-bay | main | 0.0425 | 0.0429 / 0.0409 | 0.0430 | 0.35 | 0.038 |

A2's daub against the truth is below main's in all three stills (-7 %, -30 %, -22 %), far from the epic's bar of 0.008-0.010. Specks fall or stay. rect-lamps blotch: A2 0.102 against main 0.054 (limit 0.04; main fails it too). The cause is settling, not mottle: A2's walls stand at 86.8 % of their own long mean at draw 132, as the clampless truth (86.0 %); main's clamp hid that slow settle by keeping its long mean 20 % low. So acceptance 2 fails in rect-lamps for both, and the settle work of steps 4-7 (block evidence) must bring it in.

Crops (kept/*-crop-*.png: top row long means, bottom row draw-132 stills; columns truth, A2, A3, main; each panel scaled to the truth's mean over the crop): looked at beside the truth first. Bounce-light edges (docking-bay floor-sun-edge and ship-shadow, bay-sunbeam left-wall-strip and corner-wedge): no widening of A2's edges against the truth is visible at 1:1; main's long mean is darker beside the sun patch and along the crease. A 1-2 render-pixel widening (the 17-18 px against 16 px of the test) cannot be judged by eye at this scale; no edge profile was measured. Wall wedges and corner: A2's draw-132 still is smooth where main's shows blotches along the bay-sunbeam crease. A2's shaded walls (left wall of both bays) show a coarser grain than the truth's still. rect-lamps ceiling: the truth holds the clampless fireflies; A2's long mean matches main's look, its draw-132 still a few faint pale puffs.

T2a, the far-ceiling climb (seeds.json, bounce light alone at 3.15 stops; r11 = the centre cell of the survey's 3 x 3 grid; 12 continued stills of 33 draws):

| Variant | t300 still 0 | t300 still 5 | t300 still 6 | t300 still 11 | t300 peak | t306 peak |
|---|---:|---:|---:|---:|---:|---:|
| A2 | 0.162 | 0.309 | 0.299 | 0.337 | 0.337 | 0.219 |
| A3 | 0.163 | 0.309 | 0.299 | 0.337 | 0.337 | 0.219 |
| truth (no clamp) | 0.163 | 0.309 | 0.299 | 0.337 | 0.337 | 0.219 |
| main | 0.155 | 0.279 | 0.236 | 0.286 | 0.286 | 0.174 |
| A2, filter off | 0.156 | 0.347 | 0.349 | 0.353 | 0.353 | 0.216 |
| main, filter off | 0.149 | 0.260 | 0.263 | 0.267 | 0.267 | 0.174 |

Verdict: the band's peak exceeds main's, +18 % at t300 and +26 % at t306, so acceptance 11's required check fails as written. The second job (the last three rows, with A2 and main again, which repeated the first job's numbers exactly) shows why: A2 equals the clampless truth to three decimals and ends at 97 % of its own filter-off plateau (0.346, mean of stills 5-11), while main's peak is 110 % of main's filter-off plateau (0.259). The higher r11 is light that main's clamp lost, not a stronger climb; the picture (kept/climb-t300-s11-sheet.png: main and A2 filtered on top, filter off below) shows main's bright stripe where the ceiling meets the far wall and a broader, brighter ceiling in A2 without the stripe. The filter-off runs change the hits' feedback too, so they are not T2b's single-bounce reference. New concern: with the filter on, A2's r11 still rises at draw 396 (0.162 to 0.337), while the filter-off run reaches 0.33 by draw 132. Slow settling with the filter on is for T4-T6. Decision for the user: compare the climb against each variant's own filter-off light (target of acceptance 11) instead of main's peak.

Workbench: 8 jobs that counted, $0.38 at their logged cost; 7 earlier jobs (one failed, six cancelled) counted $0.87 at their worst cases. Problems: (1) probe.patch does not pass the analyzers (CA1517, TS0002), and my first build command piped through tail without pipefail, so the renders silently used the image's stale baked demo; build with -p:RunAnalyzers=false -p:TreatWarningsAsErrors=false and set -o pipefail. (2) Four 1920 x 1080 demo renders at once ran out of device memory (vkAllocateMemory ErrorOutOfDeviceMemory); two at once work. (3) gpu_job.py's output to a file is buffered until it ends; use python3 -u to see the "sent" line.

**2026-11-13T01:01:22Z**

2026-11-12: T1a, T1b and T2a stand for Design X unchanged (the wide box and the carry stay). T2b moves to take T15 of step 17 (t0-5tyj). This ticket closes at the merge with the other built steps.
