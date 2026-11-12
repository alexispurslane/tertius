---
id: t0-4fth
status: open
deps: [t0-ylwm, t0-f7zv, t0-jpwf, t0-9a9e]
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
