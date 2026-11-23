---
id: ter-8pco
status: open
deps: []
links: []
created: 2026-11-22T21:13:03Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-6fka
tags: [rendering, experiment]
---
# Ray census: which share of the bounce light's mean and variance comes from misses, far hits and near hits

Experiment E1 of docs/research/lighting-system-review-2026-11-22/README.md, section 12. Stills rect-lamps, the cabin of orbit and bay-sunbeam on main, at the render scales 2/3 and about 0.96, 400 draws after 240 settling draws, clamps on. A patch of the survey's probe records per diffuse sample whether it hit or missed, the hit distance and its weighted light. Report per region the share of the mean light and of the variance from misses, hits beyond 2 m and hits within 2 m.

## Acceptance Criteria

A table per still and region, the probe patch committed as evidence, no engine change merged. Reading: if misses and far hits carry under about 10 % of the variance, no far-field store eases the mottle, and the step's case rests on the eye-bound faults alone.


## Notes

**2026-11-22T21:20:12Z**

2026-11-22, orchestrator: take the census in the scenes whose no-clamp floor is above the bar: cabin-light-cabin (daub 0.0285 with no clamp), bay-sunbeam (0.0149) and exit-cabin or the cabin of orbit (0.0131). Keep rect-lamps only as the control that is at the bar (0.0079).

**2026-11-22T23:55:33Z**

2026-11-22, the line orchestrator. The census is launched as a continuation of the wobble fix's lead, behind two short tasks (Release at the final code of ter-qt72, and the speck count of t7-3e22). Child thread: thread:delegated-task:command%3Amcp%3A49b05ffc-1378-4e84-92ef-b16c94dc7acb%3Adelegate-task%3Aslabs-4-ter-qt72-lead-1. taskId: node:delegated-task:command%3Amcp%3A16c143fb-11fb-406a-8dd4-b82c3cf44220%3Athread-send%3Aslabs-4-continue-specks-then-ter-8pco-census. clientRequestId: slabs-4-continue-specks-then-ter-8pco-census. Target: claudeAgent, claude-opus-5-5, effort high. Worktree /Users/alexispurslane/.t3/worktrees/tertius/t3-1f553e45-00934a8d076a004a, branch t3/fix-projection-aspect-wobble at main 998165d42. No reviewer stage: no engine change merges, and the line orchestrator inspects the record. Definitions that I set in the brief: a miss is a diffuse sample whose walk finds no surface on the screen and takes the fallback's light. The share of the variance of a class c is the sum over a region's pixels of Cov(X_c, X) over the sum of Var(X), where X is a sample's weighted light and X_c is X for samples of class c and 0 elsewhere, over 400 draws. These shares add up to 1, which the classes' own variances do not. The sample is recorded as the accumulator receives it, after the clamps, and before them too if that is cheap. One region is the sunlit ceiling of bay-sunbeam, with the class shares of the brightest 0.1 percent of its samples, for ter-fe63. The record goes to docs/research/ray-census/.
