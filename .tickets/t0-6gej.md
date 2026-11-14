---
id: t0-6gej
status: in_progress
deps: []
links: []
created: 2026-11-14T15:07:19Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t0-qwx8
---
# The light-age record counts a change only over an area (sub-pixel strips, step 1)

docs/plans/sub-pixel-strips.md, sections 5.2 and 17, step 1. Extract insideChangedArea (the opening of FSR's light-change mask) and use it in lightAgeOf, with its three tests and ARCHITECTURE.md. Branch t0-qwx8-area-changes; merges into main before Design X, because it removes the young records (false light evidence) in a still: 0.136 % of the cabin's pixels with jitter. Scheduled as the next build after the Design X builder's current step (t0-e8rh), not in parallel. Before the merge, merge main into a scratch copy of t7-3e22-retained-deviation to see the conflict in lightAgeOf and screen_trace.glsl.


## Notes

**2026-11-14T17:31:20Z**

Built (branch t0-qwx8-area-changes). insideChangedArea in screen_trace.glsl holds the opening of FSR's light-change mask (some 3 x 3 block of the 5 x 5 records around the pixel changes whole); lightChangeMaskAt calls it on this draw's records, and lightAgeOf, through insideChangedLastArea in screen_trace_plan.comp, on the last draw's records around the reprojected center when a pending change reaches its fourth draw. Outside an area the move becomes the record's light and the age goes on.

Tests (LightAgeHistoryTests), red on main first where marked:
- AStillSubPixelStripBetweenAPanelAndTheSkyKeepsNoRecentLightAgeUnderTheJitter: red on main, 1,709 young pixel-draws on 26 pixels over draws 32 to 300 (FSR render scale 2/3, 18 phases). Ten glowing panels with a strip of 10x their glow 0.7 render pixels tall at their lower edge, sky below, at ten sub-pixel places. A grid simulation (/tmp/t0-6gej/sim) found the layout: a strip between a surface and the sky; a strip on a uniform surface counts once at most.
- ALastingBrighteningOfALineOneOrTwoPixelsTallCountsNoLightAge (1 and 2 rows): red on main (ages 0 at the line); locks in the stated cost; also checks that the seen count goes on.
- ALastingDimmingOverAnAreaCountsOnItsFourthDrawUnderTheJitter: green on main and after; keeps the timing under 18 jitter phases.
- ALastingDimmingCountsOnItsFourthDrawAlsoAtTheEdgesOfItsAreaBesideTheSky: green on main and after; added in hardening for S05 and S10.

Census (census-still, cloud job e994d364, $0.047), light evidence share of block-draws, Design X 5a1d70cae / scratch merge of this branch into it:
- rect-lamps bounce 1.95e-2 / 0; reflections 5.13e-3 / 0.
- exit-cabin bounce 9.17e-3 / 0; reflections 1.74e-2 / 0.
Motion evidence 0 in all. Acceptance 12 (at most 1e-4) fails on Design X and passes on the merge for both scenes and both signals.

Merge into Design X: no textual conflict. Git merged screen_trace.glsl (Design X moves the bindings and record bits around the mask; this branch replaces the mask's body) and screen_trace_plan.comp (Design X changes storeHitLight; this branch the tail of lightAgeOf) automatically. The merge builds, and LightAgeHistoryTests (60), ScreenTraceRenderTests and AccumulatorRenderTests (144) pass on it in Checked.

Mutants (tools/mutate.py, Mac, /tmp/t0-6gej/mutate and mutate2): 11 rows, 10 killed, 1 survived.
| id | change | result |
|---|---|---|
| S01 | no block counts as an area | killed (MovingLightUpscaleTests) |
| S02 | every change counts as an area | killed (strip, line, mask bar tests) |
| S03 | && to \|\| in the block test | killed |
| S04 | blocks 3 x 2 | killed (line, 2 rows) |
| S05 | blocks below the pixel left out | survived, then killed by the new edge test |
| S06 | mask marks every changing pixel | killed (AStillGlowingBarThinnerThanAPixel...) |
| S07 | record counts every lasting move | killed |
| S08 | line move keeps the old light | killed |
| S09 | line move restarts the seen count | survived, then killed by the line test's seen-count check |
| S10 | record's window one row too high | survived, then killed by the new edge test |
| S12 | opening around the pixel, not the reprojected center | survived. Judgment call: in a still the center is the pixel; under motion the windows differ by one draw's motion, and an area wider than that motion plus 3 pixels holds both; only a patch narrower than the motion of one draw tells them apart, which no scene of ours shows. |
Dropped before the run: reading this draw's records in the plan, a race within the dispatch, so no deterministic result. Coverage: every changed GLSL line runs in the tests (each has a killed mutant); the C# changes are tests only.
