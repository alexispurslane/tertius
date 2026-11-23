---
id: ter-8pco
status: closed
deps: []
links: [ter-6fka, t0-gumm]
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

**2026-11-23T01:56:31Z**

2026-11-22, the line orchestrator. One addition to the census, for the cause of the ceiling specks of t7-3e22, which five films showed on main before and after step 2 of ter-qt72 at some render sizes only. After the ticket's stills, the lead takes the lamp hall on main at a size that bursts (1072 x 603) and at the nearest size that does not. For the ceiling around the near lamps it reports the classes of the brightest 0.1 percent of the diffuse samples (miss, near hit, far hit), their hit distances, their light before and after the clamps, and their count per draw. The question is which samples make the specks and what differs between the two sizes. If the diffuse samples are clean and the picture has specks, the record says so, because the cause is then in the reflection samples or in the upscaler. Two takes, no engine change. The result is its own section of the census's record and a note in t7-3e22.

**2026-11-23T02:50:17Z**

2026-11-22, the lead. The census is done. Record: docs/research/ray-census/README.md, with the probe patch (census-probe.patch, the survey's probe and the census edits against 3549d3283), the tools, each take's results, maps and regions. Five stills (rect-lamps as the control, bay-sunbeam, cabin-light-cabin, exit-cabin, and sunlit-nose for ter-fe63) at render scales 2/3 and 0.9584, 240 settling draws and 400 counted draws, clamps on. Result: far hits (2 m or more) carry 73 to 104 percent of the variance in every region but exit-cabin's wall below its windows, where misses carry 87 percent. Near hits carry at most 24 percent (cabin-light-cabin's shaded floor). Misses plus far hits carry 76 to 100 percent, so the review's condition 'under about 10 percent' fails in every region. The caps never bound a sample. The two scales agree within 0.02. The census mean is 1.00 to 1.12 times the slow history (ter-18hc is the suspect; not tested). The bay's sunlit patch: its brightest 0.1 percent are all far hits at 2.8 to 11 m, uncapped. Probe check: with the unpatched resolve, the per-pixel sums of X and X squared of rect-lamps at 2/3 are bitwise equal to the census's. The lamp-hall part for t7-3e22 is section 7 of the record. No engine change.

**2026-11-23T02:52:02Z**

2026-11-22, the line orchestrator. The census is merged into main by fast-forward at 0ed87f9e6, and I read the record docs/research/ray-census/README.md and looked at its pictures. No engine change merged. The result against the ticket's reading: far hits carry 73 to 104 percent of the bounce light's variance in every region but one, and misses carry 87 percent on exit-cabin's wall below its windows. Misses plus far hits carry 76 to 100 percent, so the condition 'under about 10 percent' fails in every region, and a far-field store can ease the mottle as far as the classes say. The census does not say how much of the far hits' variance a store removes. The caps never bound a sample. The probe's sums are bitwise equal to the unpatched renderer's. The sunlit ceiling of bay-sunbeam: the brightest 0.1 percent of its samples are all far hits at 2.8 to 11 m. The lamp hall part named the specks' source: a sunlit window sill thinner than a pixel, which one jitter phase of 25 draws as a row of direct light 162 against 0.22 on the ceiling. That is ter-fe63's mechanism, and its evidence moved there.
