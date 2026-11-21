---
id: t0-acxm
status: open
deps: [t0-xlcu, t0-ion0, t0-9fo0]
links: [t0-xlcu, t0-ion0]
created: 2026-11-15T00:42:53Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# Under Design X, the bay's bounce light trails a sunbeam that narrows and goes out far more than on main

Take T16 of docs/plans/bounce-light-confidence.md, 2026-11-14, the survey's motion film rect-bay (scripts/rect-bay.json: the bay of demo-surface from tick 0, fixed camera, 180 frames; the sunbeam through the roof opening narrows and goes out). The survey compares a film frame with a still of the same state; 'level' is the film frame's light over the still's, minus 1.

| Build | level, middle frame | level, last frame | blotch (worst) |
|---|---:|---:|---:|
| main ab54c785e | +1.18 | +0.53 | 0.245 |
| branch before the dark-settle fix (73f4a6548) | +1.82 (worst of both frames) | | 0.485 |
| branch with the fix (eccad009a) | +1.86 | +1.75 | 0.488 |
| branch, both clamps off (TERTIUS_A_NO_CLAMP=1) | +2.31 | +4.15 | 0.386 |

All on the Mac, one after the other. Main trails too (ter-05wb recorded +0.84 with the bounce light alone), and the film fails the survey's limits in every build. The branch trails more: at the last frame its bay is 2.75 times as bright as the still, main's 1.53 times. The fix of t0-f8oc does not cause it: the branch before the fix gives the same.

Cause, measured by the evidence census of the film (branch with the fix, 211 draws): light evidence in 3.1 % of the block-draws of the bounce light and 1.5 % of the reflections', motion evidence in none. A ray gives light evidence only when it hits a surface whose direct light changed in the last 7 draws. Here that surface is the thin edge of the narrowing sun patch, which few rays hit. Every other block keeps its history of 32 draws and its wide box, and each later bounce adds its own delay. Main's narrow box follows the fast history in every pixel, which costs main the mottle and the lost light in stills.

The same gap shows in the cabin's lamp switch (t0-5tyj): later bounces carry no light age. Design R's history of 128 draws without evidence (t0-70v1) makes the trail longer where no evidence acts.

Needs a design: what tells a block that the amount of its light moves, when no ray of it hit a changed surface. Picture: docs/research/design-x-acceptance-2026-11-14/pics/rect-bay-film.png.


## Notes

**2026-11-15T01:06:30Z**

2026-11-14, three probes of a remedy, scratch switches on the probe build of the branch (never in src; scratch tree /private/tmp/t7-3e22/x2/x, not kept). All with the fix's action (light evidence cuts the length, the box stays wide). Mac, same session as the table above.

| Take | main | fix | window 28 | spread 4 | region 8 |
|---|---:|---:|---:|---:|---:|
| rect-bay film, level at the middle / last frame | +1.18 / +0.53 | +1.86 / +1.75 | +1.57 / +1.36 | +0.79 / +0.83 | +0.89 / +0.43 |
| rect-bay film, blotch at the middle / last frame | 0.245 / 0.231 | 0.370 / 0.488 | 0.380 / 0.439 | 0.627 / 0.293 | 0.218 / 0.211 |
| cabin switch, slowest region, draws after off / on | 75 / 52 | 66 / none in 100 | not taken | 78 / 92 | 113 / 51 |
| cabin switch, whole image, draws after off / on | 36 / 24 | 12 / 27 | not taken | 24 / 22 | 14 / 19 |
| rect-lamps still, long mean of ceiling / walls, % of the reference | 78.4 / 81.2 | 96.0 / 99.5 | as the fix (a still has no evidence) | as the fix | 89.4 / 90.4 |
| rect-lamps still, specks per 10,000 px | 7.05 | 6.01 | as the fix | as the fix | 9.61 |
| bay-sunbeam still, long mean of walls / floor, % | 90.3 / 84.1 | 98.7 / 99.2 | as the fix | as the fix | 98.9 / 99.4 |
| bay-sunbeam still, daub against the reference | 0.0311 | 0.0209 | as the fix | as the fix | 0.0208 |

- window 28 (TERTIUS_X_RECENT_LIGHT_AGE=28): a light code counts as recent for 28 draws in place of 7. A small gain. An age-bounded cut with the same window gave +1.92 / +1.88, no gain.
- spread 4 (TERTIUS_X_DILATE=4): a block also takes the light evidence of the blocks within 4 blocks of it. The level reaches main's size, but the frame is patchy where blocks with and without evidence meet (looked at: a darker zone below the opening in the last frame).
- region 8 (TERTIUS_X_REGION=8, TERTIUS_X_REGION_TOL=10): at each pixel, the mean of the slow history and the mean of the fast history over one tap per ray block within 8 blocks (289 taps, any surface). Where the two means differ by more than 10 %, the anti-lag shortens the history by the excess. It trails less than main in the film and settles the cabin's lamps-on like main. Its price in this first form: rect-lamps loses half of the still's gain in light and gains specks, and the cabin's center settles later after the lamps go off. My reading, not measured: the regional fast mean swings with the jitter's cycle of 18 draws in rect-lamps (t0-qwx8), which the test takes for a lag.

Neither probe is a design. They show that the trail is a gap in what tells a block of a change, and that a test over many ray blocks closes it where per-block ray evidence does not.

**2026-11-15T08:40:13Z**

Alexis's answer of 2026-11-14 to the design question of this ticket: the lag test over a region. Recorded as decisions Y1 to Y6 in docs/plans/bounce-light-confidence.md, section 2.3. In short: a block learns of a change from its own rays or from its region, where the region's recent mean of samples drifts from the long mean that its histories hold. Ray evidence that reaches to the blocks nearby is rejected. The ray's knowledge and the region's statistic are one evidence value per block with one reader. The tolerance needs a reason and comes from a measured quantity where possible. The lamp hall still's light under the fix (96.0 and 99.5 % of the reference) is part of the remedy's acceptance, next to the film rect-bay and the cabin switch. Nothing merges into main before the remedy passes. The cycle measurement (t0-mzhr) settled the recent window: a box mean over 18 draws. Two measurements come before the remedy's build ticket: why the 18-draw window shows more light than the reference in a still (t0-y46f, running), and which measured quantity gives the tolerance (its own ticket, next).

**2026-11-15T12:20:44Z**

Decision of the parent orchestrator, 2026-11-15 07:20: the bar for the film rect-bay is no worse than main. The stop rule stays. The drift experiment (t0-ion0) decides. If it misses the bar, the line passes that up with film frames beside main and with the next design.

**2026-11-15T13:23:00Z**

Hold (line orchestrator, 2026-11-15 08:30, on the parent orchestrator's word). The back-face line found a shadow leak of the sun in rect-lamps (t8-rjno), and the thin sunlit strips that cause the jitter's 18-draw cycle in this line's records can be the same leak. So the remedy's design holds every part whose only reason is the strips until that line's diagnosis is in: the 18-draw window in place of the fast history as the test's recent mean, the differences at equal jitter phase (rule Q2 of t0-ion0), and a tolerance sized for the 18-draw teeth. If the strips go with the shadow's fix, the first candidate is the simplest one again: the fast history of 4 draws as the recent mean, which gave the bay film +0.89 / +0.43 against main's +1.18 / +0.53 and failed only by its loss of the still's light under the jitter. The mottle ticket t7-3e22 holds the full record, and t0-ion0 takes a check of how much of the cycle the sun's strips cause.

**2026-11-15T16:31:30Z**

The bar of 2026-11-15 (Alexis's words are on t7-3e22): the remedy passes by the engine's stills and films, which must show no blotches, daubs, fireflies, trail or flicker and must look good. The reference explains a fault and is not the bar. A little lost light is acceptable, so decision Y5 (the stills keep their gain against the reference) now reads: the stills must not look worse than main. The bay film's bar stays: no worse than main.
