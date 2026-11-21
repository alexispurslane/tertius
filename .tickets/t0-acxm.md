---
id: t0-acxm
status: open
deps: [t0-xlcu, t0-ion0, t0-9fo0, t0-xdck]
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

**2026-11-21T17:22:15Z**

Design of the remedy, part 1 (line orchestrator, 2026-11-21): the region's drift as a second source of the block's light evidence. One point stays open until the experiment t0-xdck reports: the length that regional evidence sets, and whether it brings own rays. The build starts after that point is settled. The room's acceptance waits for the shadow fix and its takes (t0-9fo0).

What the experiments settled
- The lag test (the region's recent mean of samples against the long mean of its histories, with the anti-lag as its action) does not close the film. Its action stops when the histories meet a recent mean that itself lies 9 draws behind (t0-xlcu: +1.36 / +0.64 at best, against main's +1.18 / +0.53).
- The drift test closes it (t0-ion0: +0.43 to +0.47 / +0.19 to +0.23, and the film's frames look like the still of the same state). It asks whether the region's light moves, from the samples alone, and gives the block the same light evidence that a ray gives. So it is decision Y3 as written: one evidence value per block, one reader. The histories are not part of the test, so the clamp, the store and the filter's write-back cannot hold it open in a still.
- This reads decision Y1 ("the region's recent mean of samples drifts from the long mean that its histories hold") as: the region's recent mean drifts from its own older mean. That is my answer, taken from the two measurements. It is listed in the line's report so that Alexis can reverse it.

The model
- Stock: for each signal with a light age, one ring per ray block (8 x 8 pixels). It holds the region's light of each of the last 2 W draws and the count of valid draws in a row. Owner: the new region drift pass. Nobody else writes it.
- The region's light of a draw at a block = the mean, over the 17 x 17 ray blocks around it, of one tap pixel's sample luminance divided by the draw's exposure scale. Only taps whose surface does not move on screen count (bit 9 of the surface test image), and taps with a sample.
- A draw is valid at a block when at least half of the region's taps inside the image count. Reason: the mean must be of mostly the same surfaces from draw to draw. A camera move makes every tap move, so the draw is not valid, and the count restarts. The count also restarts when the signal's histories restart (a start, a resize, a camera cut).
- The test at a block, valid when the count reaches 2 W: Rnew = the mean of the last W draws, Rold = the mean of the W draws before, dold = the standard deviation of the older W draws. The region's light moved when |Rnew - Rold| > DriftSigmas x dold x sqrt(2 / W). The tolerance is a measured quantity (decision Y4): the spread of the region's own light in the older window, so a step inside the newer window does not raise it.
- W = 18 draws, the window that the takes measured. Its first reason was the jitter's 18 phases under FSR at the render scale 1.5, so that a cycle of the jitter cancels in each window. The room's takes after the shadow fix say whether a cycle remains.
- DriftSigmas = 3. At 2 the test fired in 6 to 7 % of a clean still's block draws, where a normal distribution gives 4.6 %: the drift's tails are heavier. At 3 it fired in about 1 %, with 5 % more specks in the lamp hall against 34 % at 2, and the film's level stays far below main's (+0.47 / +0.23).
- Flow 1: the temporal dispatch holds each pixel's sample. At the block's tap pixel it also stores the sample's luminance over the exposure scale, and whether the tap counts, in a block light image at block resolution.
- Flow 2: the region drift pass, one invocation per block. It reads the 17 x 17 block lights, writes this draw's ring entry, computes the test from the ring (no running sums, so no rounding feeds back), and writes the result into a region evidence image at block resolution: NoEvidence or LightEvidence.
- Flow 3: the spatial dispatch's evidenceOfBlock takes the stronger of the sample ages' level and the region evidence of its block. It stays the one reader and the one place that acts.
- The evidence can be one draw late if the pass order is cheaper that way. The wait before the first cut is 13 draws or more, so one draw does not matter.

Signatures (GLSL, in a new include region_drift.glsl, with their C# twins where a test needs them)

    const uint RegionWindowDraws = 18u;   // W
    const int RegionRadiusBlocks = 8;     // 17 x 17 ray blocks
    const float DriftSigmas = 3.0;

    // One draw of a block's ring. light = the region's light of that draw. validDraws = the count of valid draws in a
    // row up to that draw, at most 2 W, and 0 for a draw that is not valid.
    struct RegionDraw { float light; float validDraws; };

    // The region's light of this draw at `block`, from the block light image.
    RegionDraw regionDrawOf(ivec2 block, float validDrawsBefore);

    // Whether the region's light moved at `block`, from the ring's last 2 W draws. False until 2 W valid draws exist.
    bool regionLightMoved(ivec2 block);

    // In accumulate_spatial.comp: the block's level is the stronger of its samples' level and its region's.
    uint evidenceOfBlock(ivec2 pixel);   // unchanged signature, one more source

C#: the ring, the block light image and the region evidence image belong to the signal (TemporalSignal), made only for a signal with a light age. Memory at 1280 x 720: 14,400 blocks x 36 draws x 2 half floats = 2.1 MB per signal, and two small images.

Tests first (accumulator fixture, real shaders)
1. A still region with noisy samples gives regional evidence in at most a stated small share of its block draws.
2. While a region's light falls steadily, its bounce history stays short and its light follows the samples' recent mean within a stated bound. This is the trail's bound.
3. After the light stands still again, the evidence ends within 2 W draws, and the history grows long again.
4. A camera move gives no regional evidence, and none for 2 W still draws after it.
5. A change of the exposure alone gives no regional evidence.
6. A moving object inside a still region does not count in the region's light.
7. A signal without a light age has no ring and no pass.

Acceptance
- By the user's bar: the film rect-bay shows no trail in its frames beside main and the still, with a level no worse than main's. The stills bay-sunbeam and exit-cabin look no worse than main and than the line before the remedy: no new blotches, daubs or bright specks.
- The deck's frame span stays within +0.30 ms of main for the whole of Design X (it has +0.200 ms now). The frame span while a region's light moves: by the decision that follows t0-xdck.
- Vulkan: no new device feature, no subgroup operation, the same bits on one machine in two runs.
- ARCHITECTURE.md and docs/plans/bounce-light-confidence.md describe the region's evidence. Hardening: one tools/mutate.py list for the GLSL and Rendering lines.
- The room (the still rect-lamps, the switch, the stand film): after the shadow fix, by t0-9fo0.

The second design, and why it is not built: the regional lag test with the fast history of 4 draws as its recent mean (W4). It needs no ring. It is a second reader with its own action beside the evidence's reader, against decision Y3, and its best film (+0.89 / +0.43) trails twice as much as the drift test's. It stays a candidate in t0-9fo0's takes only as a control.

Out of scope: the differences at equal jitter phase (rule Q2), which doubled the false evidence in clean stills. The long Halton sequence (t0-f238), which is Alexis's decision. A drift test at ray-block statistics other than one tap per block.
