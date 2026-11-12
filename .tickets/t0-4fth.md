---
id: t0-4fth
status: open
deps: [t0-ylwm, t0-f7zv, t0-jpwf, t0-9a9e, t0-y4qv]
links: []
created: 2026-11-12T13:56:24Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Takes T3 to T10 of Design A, the block test's constants and the screenshots

Design: docs/plans/bounce-light-confidence.md, section 22. Branch t7-3e22-retained-deviation. Step 7.


## Notes

**2026-11-12T14:01:07Z**

Orchestrator, 2026-11-12: measure on the GPU workbench (tools/runpod/WORKBENCH.md on main): cost A/B against main with ab_run.sh, and the quality A/B (stills, films, daub scores computed inside the job, a job returns at most 8 MB), several variants in parallel jobs. Compare only inside one job. The Mac's GPU lock is kept for one confirmation take of the final candidate before the merge and for Apple-compiler questions.

**2026-11-12T15:45:31Z**

Budget, orchestrator 2026-11-12: Design A's remaining measurement series (from step 3 on) gets $3 in total on the GPU workbench. Spent before the budget: 15 jobs, $1.26 logged ($0.38 of execution; the rest worst-case counts of cancelled or failed jobs). Rules: --timeout close to the need, no cancels of running jobs, several variants per job where they share a build (one job per still view, variants as env knobs on one binary).

**2026-11-12T15:57:12Z**

Rule from Alexis via the orchestrator, 2026-11-12: judge mottle and specks with numbers and pictures. Number-only survey cases (stand, climb) are for sweeps and the light's fall over time. Every keep-or-drop decision of a variant, and the final report, also needs full-resolution crops beside the true reference (a long run with the history clamps off), not only beside main: look at the crops first, write what you see, then read the scores, and name the score at which the mottle stops being visible (the daub limit of about 0.010 from the blind test). Put still captures in the same job as the numbers when that costs little. Crops for Alexis: rect-lamps' ceiling band, the far lamps' specks, and the cabin, deck and plate before and after.

**2026-11-12T19:00:12Z**

Take T3 started early (job filter-ab, $0.0437, rect-lamps to draw 924, action off, filter on and off). Result: the filter's write-back into S causes the false block evidence of bounce light (2.1e-2 against 1.9e-4 per block-draw; D's deviation ratio 1.4 against 1.0). Design change in section 8: compare F with the own-sample mean R. New step ticket t0-y4qv blocks this one. Spent so far under the $3 budget: $0.0342 (step 6 smoke) + $0.0437 (filter-ab) = $0.078.
