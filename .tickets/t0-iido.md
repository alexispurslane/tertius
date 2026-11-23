---
id: t0-iido
status: closed
deps: [t0-5tyj, t0-3vwx, t0-zmjx]
links: []
created: 2026-11-12T13:56:24Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Harden Design A, update ARCHITECTURE.md and merge t7-3e22-retained-deviation

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 9. Merges ter-18hc and ter-wkvw with Design A in one merge (user, 2026-11-12).


## Notes

**2026-11-13T01:01:22Z**

2026-11-12: now step 18 of the rewritten design (Design X), after step 17 (t0-5tyj). The built steps t0-ylwm, t0-hfo4, t0-q6jl, t0-anp3 and t0-f7zv close with this merge. ARCHITECTURE.md: the light-age record's codes and the moved mark, the accumulation and its evidence, the diffuse filter, the storage image counts.

**2026-11-22T15:09:21Z**

Decision of the user, 2026-11-22, on the remedy under a moving render scale, in the user's words: "I want videos of the walk and lamp switch under normal engine conditions — that is, dynamic render scaling — because that's always on (or should be). When we merge Design X, we should *not* hold render size, we should let it scale as before, and yes, we'll get main-like behavior, but we'll have a platform of better logic to work off of, to try to improve". The orchestrator's reading, which binds this line: (1) Design X merges with the render scale controller as it is. No hold of the render size, not before the merge and not as a planned fix after it. My design H (the held scale) is rejected. (2) The user accepts that Design X behaves about like main while the render size moves. (3) How the region's drift test and a moving render size fit together stays open as later work on top of the merged Design X, in t4f-71qp. No design and no build now. A test that survives a size change is one candidate for then, not a decision. (4) The merge itself still waits for the videos, the rest of the acceptance record and the combined check.

**2026-11-22T16:23:27Z**

The merge decision waits, by the orchestrator's word of 2026-11-22, for two results: the cause of Design X's bright speckled window jambs at a render scale near 1 (measured on the Mac at a constant scale, with the fair lamp switch), and the dynamic-scale videos taken again after the wobble's fix is on main (the projection's aspect from the output extent, built by the back-face line).

**2026-11-22T20:53:26Z**

The merge decision is the orchestrator's since 2026-11-22, by the user's hand-over, on these gates: (1) the cost variant built with red-green tests (t0-zmjx); (2) tc-8142 fixed; (3) Checked and Release green on the merged code; (4) the per-scene table with no scene worse than main by the user's bar; (5) the two films under the dynamic render scale on the final code, with their motion measured (t0-3vwx). When all hold, the line orchestrator tells the orchestrator, who does the last read of the diff and says go. The user sees the films afterwards. The direction stands: Design X is "strictly better", it merges without holding the render size, and it is "a platform of better logic to work off of".

For gate 5 the line orchestrator reversed its answer 55 (the walk is not taken again): both films are taken again on the final code, each with a new scale log from the card. The combined check's lead has the notice.

**2026-11-22T21:19:42Z**

For section 14 of the review of the approach, reported to the orchestrator on 2026-11-22: the ceiling of rect-lamps after the back-face layer, daub.py's ceiling part at draw 132 and the fixed scale 2/3, automatic exposure / 4.08 stops: main 284025851 0.0114 / 0.0100, Design X merged with it (a7e373197) 0.0090 / 0.0086, both clamps off 0.0083 / 0.0081 (the combined check's tables.txt, lines 46 to 53). On 2026-11-14 the values were 0.0567 on main and 0.0545 with Design X. The whole still's daub did not move with the layer (0.0117 / 0.0102 without it on c4c1f1f2f, 0.0119 / 0.0102 with it), so the fall came before c4c1f1f2f; the cause is not traced. The per-scene table on the final code prints the ceiling's part again.

**2026-11-22T23:52:18Z**

The orchestrator's decisions, 2026-11-22: gate 5 is met (both films on the final code, 05a27262d, with their motion measured; the lamp switch is a clear gain under the dynamic scale). The walk's trail after a stop does not hold the merge: both codes show the trail and Design X's is longer by about 1 point of light for 20 to 40 draws, the whole image settles at once on both, both pass the survey, the user saw the same numbers in the earlier walk film and named Design X 'strictly better', and every other take of the acceptance is better than main; the orchestrator shows the user the video as the one take where Design X loses. Remaining gates: the cap's mutants, Release suite and review; tc-8142; the per-scene table; the full suites on the line's head. When all hold, I tell the orchestrator with the base and head commits of the diff.

**2026-11-23T01:51:07Z**

A fact about the card from the orchestrator, 2026-11-22 (bug t1-80jd on main at fa36fc77d, not proven): the first run of a card job seems to draw about 10 to 18 % slower per full-size draw than the later runs of the same job.

The scale check on the merged code 16d915a67 (job f8b8cba5, one run) was therefore a slow first run. It stands and is stronger: 0 size changes through the lamp switch on a slow run. Its rest at 0.944 is the card, not Design X and not main's step 2.

The run orders behind the two decisions, which I read from the jobs' result files and run scripts:
- The cost after a light change (job 1091056e, cost-switch-card-16ms-x-xb-xd.png): order x, xb, xd, no warm-up. Design X without a fix was the first run (it rested at 0.956, against 0.993 in an older job). The decision compared the cap (run 2) with "own rays wait for the evidence" (run 3), each against its own idle span; the variant's draws 10 to 13 cost 25.2, 24.1, 25.3 and 22.8 ms against 17.6 ms settled, 30 to 44 % above. The choice of the 8-draw restart does not rest on a first run and stands.
- The spread of the catch-up rays (jobs d80337b3 and the second, card-scale-shapes1.png and card-scale-shapes2.png): cardrun.sh starts each job with one discarded run of every build (2 and 3 runs), so no measured run was first. The difference between 1/16 and 1/32 is a few size steps (1/16: 3 / 5 / 3 changes in job 1 and 8 / 9 / 4 in job 2; 1/32: 0 / 1 / rises only, one run), which is a thin sample and not a first-run fault. My answer 84: one more job with two runs each of today's cliff, the cap, and 1/16 and 1/32 with and without the cap, before the table of three codes.

From now on every comparison job of the line starts with one discarded warm-up run of the job. The merge's gates do not wait for the rerun.

**2026-11-23T04:16:34Z**

A sixth gate from the orchestrator, 2026-11-22 about 23:15: one take of a light change in the bounce-lit bay (bay-sunbeam with its sun switched, or the nearest light change that its script allows), the cap against today's cliff, the boil and the grain per draw for draws 1 to 15 and the pictures at draws 2 and 8. Reason: the fair test of t0-gumm showed that today's catch-up rays halve the early grain after a reset in the bay, and the cap removes the catch-up rays after a light change. No take tested that case in a bounce-lit scene. If the cap shows the bay's blotches after a light change, the orchestrator wants to know before the merge. Ordered at the combined check's lead at 23:16, before the sweep. The merge's record also says that the fair test did not test this case, and tools/README.md gets the engine fact of the draw index with the merge (written on the line on 2026-11-22).

**2026-11-23T04:25:48Z**

The sixth gate's take, by the combined check's lead, 2026-11-22 about 23:22 by the machine's clock: the cap after a light change in the bounce-lit bay. Pictures in the lead's worktree, not committed at 23:25: docs/research/t0-gumm-catch-up-spread/pics/gate-sun-zoom.png, gate-sun-strip.png, gate-sun-chart.png. The lead commits them with the sweep.

The take: bay-sunbeam's view and state, frozen, at the constant scale 2/3 and the fixed exposure of -4.0; the sun goes off at draw 200 and on at draw 400 of a film of 600 draws (a new scratch patch, scratch-sun-switch.patch, TERTIUS_X_SUN_OFF=<from>-<to>). The cap is the lead's A build (the line's merge 710834cc0 with the acceptance record's scratch-light-evidence-cap.patch, the same rule as 05a27262d); today's cliff is the shape (1, 8, 0) with light evidence restarting at 4 draws. The own-ray log confirms the codes: today traces catch-up rays in draws 4 to 13 after each light change, 2.46 rays per pixel after off and 4.65 after on; the cap traces none.

Measured (slowest region's settle in draws; boil of draws 1 to 8 and grain of draws 1 to 15 in %; then the narrow crop's):
- Sun off, today: 178; 0.783; 2.58; narrow 0.813, 0.81.
- Sun off, the cap: 178; 0.600; 2.41; narrow 0.586, 0.53.
- Sun on, today: 161; 11.860; 7.72; narrow 19.319, 1.30.
- Sun on, the cap: 162; 10.744; 9.29; narrow 17.833, 1.51.
The bay's spread between runs from the fair test: grain 0.08, boil of draws 1 to 8 0.02.

The answer: after the sun comes on, the cap shows coarser, block-shaped grain than today's cliff from draw 3 on: 20 % more grain over draws 1 to 15 and 16 % more on the narrow crop. Its boil is not higher. After the sun goes off, the cap is cleaner on every measure. The settle is the same.

The line orchestrator looked at the three pictures. After the sun goes off the two codes look alike. After the sun comes on, at draw 8 the cap's right wall carries coarse bright clumps where today's carries a finer speckle, and both floors carry a soft cloud. The difference is plain in the zoom at full size and small in the strip. It is not the heavy blotching of the fair test's start, where no catch-up ray gave 53 % more grain.

The card column from the logs of jobs 1 to 3: the draws that today's cliff holds the render scale more than 0.02 under its rest after an event: start 60 to 63 (a start without any catch-up ray gives 50 to 54), lamps off 49 to 54, lamps on 49 to 52, cut 41 to 48. The cap, "none" and C32 give 0 after off, on and the cut. So after a light change or a cut, about 40 to 50 draws of the cliff's price are the controller's slow return after the 8 dear draws.

The line orchestrator's reading for the gate: the cap trades 20 % more grain for about 12 draws after a light comes on in a bounce-lit room against a render scale that falls by about 0.15 and stays low for about 50 draws on the card, and it is cleaner after a light goes off. The decision is the orchestrator's.

**2026-11-23T04:27:05Z**

The orchestrator's judgment of the sixth gate, 2026-11-22 about 23:30: the gate passes. The cost for the merge's record: after the sun comes on in the bounce-lit bay the cap has 20 % more grain for about 12 draws than today's cliff (9.29 % against 7.72 % over draws 1 to 15; narrow crop 1.51 % against 1.30 %), and its right wall is somewhat coarser at draw 8; after the sun goes off the cap is the cleaner code (boil of draws 1 to 8 0.600 % against 0.783 %); against that, today's cliff holds the card's render scale more than 0.02 under its rest for 49 to 54 draws after a light change and the cap for 0. Pictures: docs/research/t0-gumm-catch-up-spread/pics/gate-sun-zoom.png and gate-sun-strip.png on the branch t3/combined-renderer-check, committed with the sweep. The rule was measured on the lead's scratch build of the same line (scratch-light-evidence-cap.patch on 710834cc0), not on 05a27262d. The fair test did not test a light change in a bounce-lit scene; this take did. State of the six gates at 23:33: (1) the cap built with red-green tests, met; (2) tc-8142 fixed, met; (3) Checked and Release green on the merged code, running at the acceptance lead; (4) the per-scene table, rows committed by the acceptance lead in ddee55073, not yet reported; (5) the two films under the dynamic render scale, met; (6) the cap after a light change in the bay, met.

**2026-11-23T04:45:30Z**

State of gate 3 from the acceptance lead, 2026-11-22 23:47: the full Checked suite passed on its commit ddee55073, 11,220 tests, 0 failed, 27 skipped; its head 39064630e differs from ddee55073 only in docs, tickets and ARCHITECTURE.md. One test more waits to be committed: it kills the surviving mutant P03 of the variance fix (tc-8142), was green on the fix and red on the mutant, and sits in a scratch copy. Its commit's hook builds Rendering, and the full Release suite builds too, so both wait for 22 GiB of free disk (21 at 23:47; the cause of the fall is the back-face line's raw data, which that line deletes per take). The lead deleted its runs' pictures (1.1 GiB) after it committed tables.txt, and it takes three more rounds of the deck's timing, because the first three ran under a load that rose to 9.3.

**2026-11-23T05:25:20Z**

State of the six gates, 2026-11-23 00:26 by the machine's clock, on the line's head ae3d9ed2e (the acceptance branch bf55b37f6 merged as f47290508). (1) The cap built with red-green tests: met, 05a27262d and b543151da, 11 mutants killed. (2) tc-8142 fixed: met, 6d234dcef, green on the card (job a8d320d8), the last surviving mutant killed by 8400ec2a0. (3) Checked and Release green on the merged code: Release full on 8400ec2a0, 11,208 tests, 0 failed, 27 skipped; Checked full on ddee55073, 11,220 tests, 0 failed, 27 skipped. Under src and tests the line's head equals 8400ec2a0, and ddee55073 differs from it by one added test whose class passed in Checked (12 of 12). One full Checked run on the final tree is ordered at the acceptance lead to remove that caveat. (4) The per-scene table on the merged code against main 3549d3283: stills 9 better, 1 equal, 0 worse; light share 97.7 to 100.4 % of the clamps-off reference against 81.6 to 102.7 %; daub lower in all three stills; fair cabin switch 82 / 54 draws against 142 / 92; bay-sun-off 30 / 22 against 49 / 37; the deck's GPU span +0.211 ms over six rounds. Worse by a measure: lamp-hall-16 after off, 37 draws against 25, inside the goal of 60; the flicker measure of the films rect-bay and bay-sunbeam, 0.00247 against 0.00190 and 0.00522 against 0.00397, with blotch and level better; the walk's trail after the stop. The orchestrator judges this gate. (5) The two films under the dynamic render scale on the final code: met, t0-3vwx. (6) The cap after a light change in the bounce-lit bay: met, with its cost recorded above. The diff to read: base 3549d3283 (main at the line's last merge of it; main's 21 later commits change nothing under src, tests or analyzers), head ae3d9ed2e; under src, tests and analyzers 49 files, 6,187 insertions and 1,213 deletions. A test merge of the line with main c720e14d2 (git merge-tree, no checkout touched) conflicts only in three ticket files: t7-3e22.md, ter-4iic.md and ter-6fka.md. Still to come on the line, docs and tickets only: the merge of the combined check's branch with the records of t0-gumm and t0-qvm1, which holds the gate's two pictures.

**2026-11-23T05:57:57Z**

2026-11-23, 00:57 New York time, the line orchestrator. Design X is on main. The merged head is 0688b36c6, pushed to GitHub and to the knot.

How it merged, by the orchestrator's procedure:
1. In the line's worktree, main c720e14d2 merged into the line as 8454ffb15. Three tickets had notes from both sides (t7-3e22, ter-4iic, ter-6fka), and each keeps both. Main moved by four ticket commits during the test run, so a second merge of main f0fc7984c followed as 0688b36c6, without a conflict.
2. In the main checkout, only `git merge --ff-only t7-3e22-retained-deviation` ran. None of the local changes of other sessions there lies on a file of the line's diff.
3. `git push origin main` reached both URLs at the first try.

The condition: the full Checked run on the final code. The acceptance lead ran `tools/test-all.sh -c Checked` on bf55b37f6: 11,221 tests, 0 failed, 27 skipped (Core 1207, Simulation 2007, Physics 1500 with 8 skipped, Rendering 3486 with 11 skipped, Host 2089 with 8 skipped, Unions 59, Style 276, Slop 597). The merged head differs from bf55b37f6 under src, tests and analyzers in no file, and under tools in two lines of tools/README.md. The Release run is the earlier one on 8400ec2a0: 11,208 tests, 0 failed, 27 skipped. No Release run exists on the last code state, which adds the test that kills P03 and the docs.

What the record of this merge must say, as the orchestrator ordered: the fair test of the catch-up rays did not test a light change in a bounce-lit scene, and the cap's cost after a light change in the bay (the sixth gate) was measured on a scratch build of the same rule on 710834cc0, not on the merged shader.

The state of the home ticket t7-3e22 after this merge is in its own note of the same time.
