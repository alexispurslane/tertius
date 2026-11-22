---
id: t0-acxm
status: in_progress
deps: [t0-xlcu, t0-ion0, t0-xdck]
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

**2026-11-21T21:16:07Z**

Design of the remedy, part 2 (line orchestrator, 2026-11-21, after the cost record t0-xdck, docs/research/t0-xdck-drift-cut-cost/README.md). The design is now complete for the build.

What the cost record measured, film rect-bay on the Mac at 1920 x 1080
- The region's evidence with a cut to 4 draws makes 54 % of the render pixels trace their own ray over the film, 68 to 70 % in its middle. That costs +5.63 ms a frame (paired spread 0.03 ms), 35 % of main's 16.04 ms, for as long as the light moves.
- A cut to 8 draws costs nothing: -0.19 ms (spread 0.04 ms). Its film against main: level +0.86 / +0.31 against +1.17 / +0.54, blotch 0.206 / 0.195 against 0.245 / 0.231, specks in the last frame 0.63 against 1.28, lag error of bay-sun 0.131 against 0.267. Two measures are slightly above main's: specks in the middle frame 0.20 against 0.18, and flicker 0.00256 against 0.00206 with a limit of 0.006. Its still bay-sunbeam equals the still without regional evidence.
- A cut to 4 draws without own rays fails: 3.61 specks in the last frame against main's 1.28.
- The scratch test costs 11.7 ms at every pixel and 8.0 ms at block centers inside the accumulation pass, and nothing with its code present but not run. So the cost is how the probe runs the test, not the test.

The decision, which I took: the region's evidence cuts the history to at most FullRateFrames, 8 draws, the shortest length that keeps the block's shared ray. Reasons: the bay film's bar is no worse than main, and this action meets it with no frame time. The cut to 4 draws gives a film nearer to the still in its middle frame (mean gray 141 against 154, main 161, the still 119), but costs 5.6 ms a frame while a light moves, and Design X's whole allowance is 0.30 ms. The line's report lists this answer with the frames, so that Alexis can reverse it.

The levels. The block's evidence stays one value from a closed set, ordered by how certain the knowledge is, and the one reader acts on it:

| Level | Source | Action on every pixel of the block with L above the fast cap |
|---|---|---|
| NoEvidence (0) | none | as today: the wide box, the anti-lag |
| RegionEvidence (1) | the region's light moved (this ticket) | L becomes at most FullRateFrames. The box keeps its widening. No anti-lag, as under LightEvidence: the evidence states the change. This is the measured action |
| MotionEvidence (2) | as today | as today: the narrow box, the anti-lag |
| LightEvidence (3) | as today: a ray hit a recent light change | as today: L becomes the fast cap. The box keeps its widening |

- Motion stands above the region. Reason: the narrow box measures the change at the pixel itself, which is exact, and it follows a light change as main does. The region's statistic is the weaker knowledge. Under a camera move the region gives no evidence at all (part 1: no still taps).
- MotionEvidence and LightEvidence change their numbers. The level lives only in the spatial dispatch's shared value, in no image, so no layout changes.
- The region evidence image holds 0 or 1 per block. evidenceOfBlock turns 1 into RegionEvidence and takes the maximum with the samples' level.
- FullRateFrames moves from screen_trace.glsl to a place that the ray plan and the spatial dispatch both include, so that the two cannot drift apart. The bound's reason is the plan's rule, so it is one constant.
- The reflections take the same level and action from their own ring, as every measured variant did.

The test's own cost comes first in the build
- Step 1 of the build is a timing spike: the region drift pass alone, with the block light image and the ring, and with its evidence ignored by the reader. Take the deck (scripts/temporal-upscale-timing.json) and the film rect-bay on the Mac, against the line's head, as the cost record took its rounds.
- The budget: the pass and the tap store together add at most 0.10 ms to the deck's frame span, for both signals. Design X holds +0.200 ms of its +0.30 ms.
- Why the spike can pass where the probe failed: the probe ran 289 scattered reads of a full-size array image in one thread of a group of 32, 14,400 times. The pass reads a block light image of 160 x 90 texels, which stays in the cache, with every thread of a group doing the same work.
- If the plain 17 x 17 loop misses the budget, try these in order and record each: a box sum in two steps (17 reads along x, then 17 along y), then one dispatch for both signals in place of two. If the budget still fails, stop and report: that is a fork for the orchestrator.

Order of the work
1. The timing spike above.
2. Tests first, then the build: the tap store in the temporal dispatch, the pass, the level and its action in the spatial dispatch, the constant's move.
3. Takes in the bay only: the film rect-bay and its frames beside main and the still, the lag film bay-sun, the stills bay-sunbeam and exit-cabin, the deck's frame span and the film's frame span. The control: the build's film numbers lie near the probe's cut to 8 draws (+0.86 / +0.31, lag error 0.131). The validity rule of part 1 (still taps) is new against the probe, and in a film with a fixed camera it changes nothing.
4. Hardening, ARCHITECTURE.md, the design doc, review, fix.
Not in this ticket: the room of scene orbit with the camera person. Its takes follow the shadow fix (t0-9fo0), on the build of this ticket.

Tests added to part 1's list
8. A block with regional evidence and no other keeps a history of at most 8 draws, and the ray plan gives its pixels the shared ray.
9. A block with regional evidence and motion evidence acts as under motion evidence alone.
10. A block with regional evidence and a ray's light evidence cuts to the fast cap.

**2026-11-21T21:18:14Z**

Addition (line orchestrator, 2026-11-21, on the parent orchestrator's order). The parent looked at the frame sheet and the span chart and kept the decision: the region's evidence cuts the bounce history to at most 8 draws. It also kept the three other answers of the line. It reports one limit to Alexis: in the film's middle frame the cut to 8 draws stays nearer to main than to the still (mean gray 154, main 161, the still 119), so the remedy removes the trail's end but only part of its middle.

One cheap measurement, after the timing spike, as a scratch patch on the production build, with no change of src for it: a cut to 6 draws where a pixel whose block has regional evidence counts as mature for the ray plan, so the cut brings no own ray. A pixel that is young for another reason keeps its own ray. The film rect-bay under it: level, blotch, specks of the middle and the last frame, flicker, and the frame span against the cut to 8 in the same rounds. If its last-frame specks are at or under main's 1.28, its flicker under 0.006, its frame span inside the budget, and its level better than the cut to 8, the lead reports it with one frame sheet beside the cut to 8, and the parent decides. If not, 8 stays, and the numbers go into the next report. Sent to the lead as a steer, clientRequestId design-x-line-t0-acxm-steer-cap6.

The room's takes stay blocked: the shadow fix now waits for wider variants of its filter, which Alexis asked for.

**2026-11-21T23:12:12Z**

Timing spike of the region drift pass (remedy lead, 2026-11-21). Base: the line's head 8ec383c6e. Each side is a Release demo at 1920x1080, --in-flight 2, under the exclusive GPU lock, beside gpu-clock (1296 MHz in every run). Each round runs the deck (scripts/temporal-upscale-timing.json) and the film rect-bay, for each side alone. A difference is the side's medianMs minus base's in the same round. The spread is the largest minus the smallest difference of the four rounds. The budget is +0.10 ms on the deck.

| Side | What it records | Rounds | Deck | Film |
|---|---|---|---:|---:|
| plain | the tap store, and a region pass of 17 × 17 imageLoads per block, for both signals | r2 to r5 | +0.271 (spread 0.082) | +0.289 (spread 0.062) |
| box | the tap store, and a region pass with a box sum in two steps through shared memory, for both signals | b1 to b4 | +0.254 (spread 0.196) | +0.270 (spread 0.113) |
| box again | the same binary | c1 to c4 | +0.242 (spread 0.127) | +0.202 (spread 0.069) |
| off | the tap store only, no region pass | b1 to b4 | +0.080 (spread 0.203) | +0.073 (spread 0.100) |
| one | the tap store, and the box region pass for the diffuse signal only | c1 to c4 | +0.133 (spread 0.181) | +0.209 (spread 0.103) |

The side "one" is the least that one dispatch for both signals can cost: one dispatch and one barrier flush. A single dispatch for both signals also does the work of both, so it costs at least as much. So all three ways of the design miss the budget. The box sum costs about what the plain loop costs, so the pass's arithmetic is not the cost. The cost is mostly fixed: each region dispatch and its barrier flush cost about 0.06 to 0.13 ms on MoltenVK, and the tap store about 0.08 ms.

This is the fork that the brief names. I stopped there. The record: docs/research/t0-acxm-region-evidence/spike/ holds plain.patch, box-and-switches.patch (with the SCRATCH switches off and one) and timing.txt. The tools are in the folder's tools/.

A way out that the brief does not list, not built: fuse the region test into the spatial dispatch. Each spatial workgroup is one block of 8 × 8 pixels. So each workgroup can sum the 17 × 17 taps around it, about 5 loads per invocation, then update and test its own ring, and share the bit through shared memory. The taps go into the barrier that already runs before the spatial dispatch. So the fused test adds no dispatch and no barrier flush. Its expected cost is near the side "off", about +0.08 ms, plus the workgroup's sum. No other workgroup reads a block's ring, so the ring needs no barrier.

**2026-11-21T23:13:10Z**

Fork of the timing spike, and my answer (line orchestrator, 2026-11-21 18:15). The lead's spike, four rounds per side on the Mac against the line's head, record at 1c7712b49 on t3/fix-retained-rounding-weight (docs/research/t0-acxm-region-evidence/spike/):
- The region drift pass with the plain 17 x 17 loop, both signals: deck +0.271 ms (spread 0.082), film +0.289 ms (spread 0.062).
- The box sum in two steps: deck +0.254 / +0.242 ms, film +0.270 / +0.202 ms.
- One region dispatch for the bounce light only: deck +0.133 ms (spread 0.181), film +0.209 ms.
- The tap store alone: deck +0.080 ms (spread 0.203), film +0.073 ms.
So the work in the pass is not the cost. Each compute dispatch with its barrier costs about 0.06 to 0.13 ms on MoltenVK. The budget of +0.10 ms fails in every listed form.

My answer: the fused design, which the lead proposed. The region's test runs inside the spatial dispatch: the workgroup of a block sums its 17 x 17 taps over its 64 invocations in a fixed order, updates and tests its own ring, and shares the result with the block's level. This replaces flows 2 and 3 of part 1. The separate pass and the region evidence image are not built. The ring's owner is the spatial dispatch, which is the one reader of the evidence, and the evidence is of the same draw. The test's knowledge stays in one include. The budget stays +0.10 ms on the deck, measured with at least eight rounds and paired differences, because the spike's spreads are as large as the budget. If both signals miss it, the bounce light alone is taken with the film as its check. If that misses, the lead stops and reports.

**2026-11-22T01:47:04Z**

Timing of the fused form (remedy lead, 2026-11-21), after the settlement of the fork. The region's test runs inside the spatial dispatch. The separate pass and the region evidence image are gone. Base: the line's head 8ec383c6e, as in the first spike. Each round waits until the machine's load over one minute is below 8 or 10, and the table gives the load beside each run (tools/table.py). The budget is +0.10 ms on the deck, for both signals.

Three forms ran:
- serial: each invocation sums its share of the 17 × 17 taps into its own slot. Invocation 0 adds the 64 slots and runs the ring's test alone, while the other 63 wait. Two barriers.
- parallel: 8 invocations each add 8 slots, and 12 invocations each add 3 of the ring's draws. Invocation 0 adds those parts. The older window's sums subtract its first draw, so that the spread keeps its precision. Three barriers. Every sum has a fixed order.
- parallel, bounce light only: the same binary with TERTIUS_SPIKE_REGION=one, which skips the test of the reflections through a sentinel ring slot.

| Side | Rounds at low load | Deck | Film rect-bay |
|---|---|---:|---:|
| serial | f2, g2 to g4 | +0.347, then +0.397 / +0.402 / +0.287 | +0.314, then +0.190 / +0.273 / +0.162 |
| parallel, both signals | g2 to g9 (8) | +0.274 (spread 0.102) | +0.139 (spread 0.138) |
| parallel, bounce light only | g2 to g9 (8) | +0.174 (spread 0.142) | +0.287 (spread 0.149) |

Round g1 ran at a load of about 23 and scattered (+0.44 to +0.55 ms). It is in timing.txt and left out of the medians. Every run's clock was 1296 MHz.

Both orders of the settlement miss the budget. The fused form for both signals costs +0.274 ms on the deck. The fused form for the bounce light alone costs +0.174 ms. The tap store alone cost +0.080 ms in the first spike. So the test inside the spatial dispatch costs about 0.09 ms per signal above the taps.

The film's numbers are not a pure cost. The bounce-only side costs more in the film than the side with both signals. The film's light moves, so the reflections' region evidence there changes the work of the passes after it. The deck is the budget's measure.

The record: docs/research/t0-acxm-region-evidence/spike/ holds fused-serial.patch, fused-parallel.patch (with the scratch switch one) and the tables in timing.txt. I stopped there, as the settlement says, and took no film take of the bounce-only form. The src changes stay uncommitted in the worktree.

**2026-11-22T01:52:39Z**

Settlement of the second fork (line orchestrator, 2026-11-21, about 21:00): the region's test runs only at anchor blocks.

The measured facts (remedy lead, record d4acd51c1 on t3/fix-retained-rounding-weight): the fused form costs +0.274 ms on the deck for both signals and +0.174 ms for the bounce light alone, against the budget of +0.10 ms. The tap store alone cost +0.080 ms. The budget is the rest of Design X's allowance of 0.30 ms above main on the deck (t0-89zi), so I do not raise it.

The cause of the cost is the model, not the place of the code: every block of 8 x 8 pixels ran its own test, with 289 taps, 36 ring reads and three barriers in each of about 14,000 workgroups, and the temporal dispatch wrote a tap for every block. But the regions of two neighbor blocks share 272 of their 289 taps, so their answers are nearly the same answer.

The new model: one test per anchor.

- An anchor is a block whose two coordinates are multiples of `AnchorStepBlocks` = 8 = `RegionRadiusBlocks`. So 1 block of 64 is an anchor, and the regions of neighbor anchors overlap by half.
- The test itself does not change: the anchor's region is the 17 x 17 blocks around it, the windows are 18 draws, the tolerance is 3 standard deviations, and the validity rule stays.
- Only an anchor's workgroup in the spatial dispatch sums its region's taps, updates its ring and tests. The ring image has the anchors' resolution. The ring's draw also holds the test's answer.
- The taps come from the images that the spatial dispatch binds already: `sampleImage` and `surfaceTest` at each block's tap pixel. The `blockLight` image and the temporal dispatch's tap store go away, unless a tap's value needs a fact that only the temporal dispatch holds. If so, say which fact, and keep the store.
- Every block takes the region's evidence of the draw before: `RegionEvidence` if any anchor whose region holds the block saw its light move. With a step equal to the radius, those are the up to four anchors around the block. A block reads only ring layers that no workgroup writes in this draw, so the read has no race and one machine gives the same bits in every run.
- The answer acts one draw late. The windows are 18 draws long, so one draw does not matter. Say so in the comment.
- The action does not change: `RegionEvidence` cuts the history to at most `FullRateFrames`, the box keeps its widening, and no anti-lag runs.

Signatures (region_drift.glsl stays the only owner of the ring's layout and the test):

```glsl
const int AnchorStepBlocks = RegionRadiusBlocks;   // an anchor at every 8th block in x and in y
bool isRegionAnchor(ivec2 block);
// The anchor's workgroup: sums its region's taps, writes this draw and its answer into its ring. Uniform in the workgroup.
void updateRegionAnchor(ivec2 anchorBlock);
// Whether an anchor whose region holds `block` saw its region's light move in the draw before.
bool regionLightMovedBefore(ivec2 block);
```

Left out, and why: a smooth share between the anchors in place of the yes or no. The evidence's edges now lie on a grid of 64 pixels. If the bay film shows that grid, the share is the next step, and the lead reports it as a fork.

Tests: tests 1 to 10 keep their statements, with the anchor's region in place of the block's region. Three more: a block takes the evidence of an anchor whose region holds it. A block outside every moved anchor's region keeps its history. The evidence acts in the draw after the anchor's test.

Timing: eight rounds on the deck against 8ec383c6e at a low load, as before. The budget stays +0.10 ms for both signals. If the anchored form misses it, try the bounce light alone. If that misses too, stop and report: the choice between a cost above the allowance and no remedy then goes up to the user.

**2026-11-22T04:14:48Z**

Timing of the anchored form (remedy lead's report, 2026-11-21, about 23:10; the tables come with the build's first commit). Deck, both signals: median +0.091 ms over 29 paired rounds at a load under 8, quartiles +0.056 / +0.123, inside the budget of +0.10 ms. Four queues of 8 rounds gave +0.115, +0.061, +0.088 and +0.094 ms. Bay film: -0.078 ms. Bounce light only: +0.010 ms. The test off: +0.010 ms. Every block's read with no anchor update: +0.022 ms. Accepted changes of the lead: a block on an anchor row or column reads up to 9 anchors, the read runs in 9 threads and merges by atomicMax, the signature is regionAnchorMovedBefore(block, candidate), the tap reads sampleImage, surfaceTest and gbufferDistance in the spatial dispatch and skips limitIncomingEstimate (a prototype that is off by default), and blockLight and storeRegionTap are gone. Two additions that I ordered (answer number 14, not blocking): one bay film side with the region's test for the bounce light only, and one GPU trace pair of the deck that names the passes with the rise. Reason: Design X then stands at about +0.29 ms of its allowance of 0.30 ms, and the reflections' test holds about 0.08 ms of it. I decide between both signals and the bounce light alone from the film.

**2026-11-22T05:19:04Z**

The anchored form: timing and build (remedy lead, 2026-11-21), after the settlement of the second fork. The region's test runs only at anchor blocks, every 8th block in x and in y. Base: the line's head 8ec383c6e. The tables are in docs/research/t0-acxm-region-evidence/spike/timing.txt.

Timing on the deck, GPU frame span against base, 8 paired rounds per side, with the load beside each run:

| Side | Rounds | Deck | Film rect-bay |
|---|---|---:|---:|
| anchored, each block reads its anchors in one thread | h1 to h8 | +0.135 (spread 0.114) | −0.070 |
| anchored, test off | h1 to h8 | +0.010 (spread 0.092) | +0.009 |
| anchored, the read in 9 threads | k1 to k8 | +0.115 (spread 0.092) | −0.066 |
| the same, with the scratch switches compiled in | m1 to m8 | +0.061 (spread 0.099) | −0.084 |
| the same two side by side | n1 to n8 | +0.088 and +0.094 | −0.076 and −0.076 |
| bounce light only | m1 to m8 | +0.010 (spread 0.146) | +0.153 |
| every block's read, no anchor update | m1 to m8 | +0.022 (spread 0.133) | +0.024 |

Pooled over the 29 runs of the read in 9 threads whose loads were both under 8: deck +0.091 ms (quartiles +0.056 and +0.123), film −0.078 ms. That fits the budget of +0.10 ms. The queues scatter between +0.06 and +0.12 ms at the same load, so one queue of 8 rounds does not settle a difference of 0.02 ms. The film is cheaper than base because the evidence cuts histories while its light moves.

Why the read runs in 9 threads. In the first anchored build, invocation 0 read up to 9 ring texels one after another, with an early return, while the other 63 invocations waited at the barrier. Every one of the 14,400 workgroups paid that latency. Now invocation k reads candidate k, and the answers merge through the atomicMax that the level already uses. A maximum does not depend on the order. The signature changed for it: regionAnchorMovedBefore(block, candidate) in place of regionLightMovedBefore(block).

What the build differs in from the settlement's sketch:
- A block on an anchor's row or column lies in the regions of up to 9 anchors, not 4, because a region reaches exactly one step. The read covers all of them.
- The tap reads sampleImage, surfaceTest and gbufferDistance at each block's tap pixel in the spatial dispatch. So the blockLight image and storeRegionTap are gone, and the temporal dispatch is main's again. The tap skips limitIncomingEstimate: it is a prototype that is off by default (specialization constant 9), and with it off it returns the sample unchanged.
- The light-age variant covers the spatial dispatch alone again, as before the remedy.
- The ring is RGBA32F at the anchors' resolution, 36 layers: r the region's light, g the count of valid draws, b the answer. At 1280 × 720 that is 20 × 12 anchors, 138 KB per signal.
- The older window's sums subtract its first draw, so that the spread keeps its precision when it is small beside the light.

Red and green. The tests ran red with TERTIUS_SPIKE_REGION=off, the scratch switch that runs no region test. Six failed. Five failed for the missing feature: no history was cut to 8 draws, the falling light trailed at 0.647 against 0.5, and no anchor's ring showed a move. The sixth was my own test's count of the layout: a light age adds 2 storage images to the accumulator, the sample age and the ring, not 3. I fixed that test. The tests of absence and of precedence passed red, as they must: a still region, a camera move, the exposure, a moving surface, a block outside every moved anchor, the growth back to the cap, motion above the region and light above the region. Their kill comes from the mutants.

Green: 40 of 41. The trail test's bound was wrong. I took the lag of a box mean of 8 draws, 3.5 draws. A history held at 8 draws blends at the weight 1/9, and a mean of that weight trails a steady fall by 8 draws. The test measured 0.5407, 8.1 draws. Its new statement: the history trails the fall by at most 9 draws.

The scratch switches are out of src. docs/research/t0-acxm-region-evidence/spike/switches.patch puts them back for the takes: TERTIUS_SPIKE_REGION=one (bounce light only), off, and reads.

The full Checked suite after green found 25 failures in Rendering, of two kinds.

1. A bug of the test, fixed: a constant history was cut to 8 draws. Twenty of the failures were constant reflection histories that must grow past 2,048 frames. With samples of exactly one light, the older window's spread is exactly 0, so the tolerance is 0. The newer mean then summed the same 18 values in another order than the older one, so the two means differed in the last bit, and that passed the tolerance. Now every sum of the test subtracts the older window's first draw, so equal draws give a difference of exactly 0. The tests of the precise history pass again.

2. False evidence in a still view, which the design accepts at about 1 % of block draws. A probe read the rings over the 64 still draws of LightAgeHistoryTests: the reflections' region light swings between 0.24 and 0.46 from draw to draw, a spread of about 15 %, with one tap per block. Their test fired at draws 44 and 45, so 2 of the 28 draws that it tested. The bounce light's fired in none. The cut left the reflections near 26 draws at draw 64. Three tests changed for it, each with its reason in a comment:
- LightAgeHistoryTests.ByDefaultALastingDimmingOfTheEmitterCutsTheReflectionHistoryToTheFastCap and ACutHistoryGrowsBackWithTheAgeOfTheLight: their precondition, a warm history longer than 32 draws, is now longer than FullRateFrames. That keeps their statement: only light evidence cuts such a history to the fast cap. TheCappedPreciseHistoryAlsoFallsToTheFastCap already uses 8.
- ScreenTraceRenderTests.TheBounceLightOfALitPoolKeepsItsEdgeWithRealRays: the filtered light still rises over 37 pixels, but the unfiltered slow history now rises over 27 and not 31. Region evidence cuts it to 8 draws now and then in that still view, and a shorter history follows the real edge more closely. The bound, which holds the measured width until the user decides (t0-anp3), is now unfiltered + 10.

So the reflections' region test fires falsely in a still view at a rate well above the bounce light's in this scene. That bears on whether the reflections' test earns its 0.08 ms. The bay takes measure the side with the bounce light only beside the side with both.

The full Checked suite after these fixes: every project green. Core 1,207, Simulation 2,007, Physics 1,492 passed (8 skipped), Rendering 3,313 passed (11 skipped) of 3,324, Host 2,072 passed (8 skipped), Unions 59, Style 276, Slop 597.

**2026-11-22T06:20:40Z**

The bay takes after the merge of the line's head with the shadow fix (remedy lead, 2026-11-22). Record: docs/research/t0-acxm-region-evidence/README.md. Every side holds the fix: M is main c4c1f1f2f, B the build c98d3d2da, B1 the build with the region's test for the bounce light only (spike/switches.patch, TERTIUS_SPIKE_REGION=one), B6 the scratch cut to 6 draws (cut6.patch), A the clamps-off reference. The build sides take probe-acxm.patch, the survey's probe fitted to the push constant regionSlot.

Film rect-bay, middle / last frame:

| Side | Level | Blotch | Specks | Flicker |
|---|---|---|---|---|
| M | +1.17 / +0.53 | 0.238 / 0.231 | 0.18 / 0.89 | 0.00204 |
| B | +0.84 / +0.28 | 0.180 / 0.162 | 0.21 / 1.07 | 0.00263 |
| B1 | +0.85 / +0.29 | 0.187 / 0.162 | 0.22 / 0.27 | 0.00273 |
| B6 | +0.56 / +0.22 | 0.129 / 0.123 | 0.39 / 2.23 | 0.00341 |

Lag film bay-sun, median lag / lag error: M 28.3 draws / 0.270, B 12.7 / 0.150, B1 14.0 / 0.150. Stills, daub against A: bay-sunbeam M 0.0311, B 0.0228, B1 0.0225; exit-cabin M 0.0273, B 0.0167, B1 0.0166.

Timing, 8 paired rounds, loads 4.05 to 7.48, 1296 MHz: B − M deck +0.279 ms (spread 0.072), film −0.245; B1 − B deck −0.026 (spread 0.106), film +0.223; B6 − B deck +0.003 (spread 0.060), film −0.052.

GPU trace of the deck, line's head 36c3fd0e8 against B (--no-counters, with TERTIUS_DIFFUSE_FILTER=on for t0-8jfj): mean frame span +0.036 ms, median +0.068 ms. The rise lies in accumulate indirect diffuse (+0.040 ms) and accumulate indirect specular (+0.049 ms), the passes of the two spatial dispatches. No pass after the reflections' spatial dispatch rose, so the false-fire share on the deck was not measured.

The cut to 6 draws: level and flicker pass, and the span is within budget, but its last-frame specks are 2.23 against the bar of 1.28. So the cut stays at 8 draws, and src does not change for it.

Against main, B is better in level, blotch, lag and daub, and worse in the film's last-frame specks (1.07 against 0.89) and flicker (0.00263 against 0.00204, limit 0.006). The trail is not closed: the last frame is still 28 % too bright. B1 has the same level and lag error as B with 0.27 last-frame specks. The choice between B and B1 is the orchestrator's.

**2026-11-22T07:26:33Z**

Hardening of the remedy (remedy lead, 2026-11-22).

Coverage: Rendering's tests in Debug with coverage, against 36c3fd0e8: the changed C# lines 30/30 covered, branches 30/30. The run passed 3,318 tests (11 skipped).

Mutants: one tools/mutate.py list of 36 (docs/research/t0-acxm-region-evidence/mutants/list.json, from tools/mutants.py). No Stryker: the remedy changed no line of Core, Simulation or Physics. The first run killed 24 and 12 survived. Six new tests in AccumulatorRenderTests+RegionDrift kill 7 of them, and C05 was a list error (its test lies in the nested class EvidenceUnderEachHistory). The rerun (mutants/rerun.json) killed all 8.

| id | line | change | result |
|---|---|---|---|
| R01 | region_drift.glsl:46 | anchors where any coordinate is a multiple of 8 | killed by AnAnchorsRingHoldsTheMeanOfTheTapsOfItsOwnRegion (new) |
| R02 | :58 | layer two draws before | killed |
| R03 | :73 | no reset check in regionAnchorMovedBefore | equivalent: at a reset draw every history restarts below the fast cap, so no evidence level acts in that draw |
| R04 | :76 | candidate % 2 | killed by ABlockOneStepFromAnAnchorTakesThatAnchorsEvidence (new) |
| R05 | :75 | first anchor rounded down | killed |
| R06 | :77 | last anchor one block further | killed by ABlockMoreThanOneStepFromAnAnchorDoesNotTakeThatAnchorsEvidence (new) |
| R07 | :80 | answer inverted | killed |
| R08 | :121 | a tap outside the images counts as inside | killed |
| R09 | :124 | a tap without a surface counts | killed by ARegionWhoseTapsShowASurfaceInLessThanHalfHasNoValidDraw (new) |
| R10 | :125 | a moving surface's tap counts | killed |
| R11 | :127 | no division by the exposure scale | killed |
| R12 | :135 | every tap must count | killed by ARegionWhoseTapsShowASurfaceInAtLeastHalfGivesEvidence (new) |
| R13 | :136 | count of valid draws without its cap | equivalent: the count only meets the test "< 36", which it passes in the same draws without the cap, and a float counts whole draws exactly up to 2^24 |
| R14 | :160 | test from 18 valid draws | killed |
| R15 | :32 | DriftSigmas 2 | killed |
| R16 | :171 | sqrt(1 / W) | killed |
| R17 | :170 | spread divided by W, not W − 1 | judgment: it narrows the tolerance by 3 %, which moves a still region's fire rate by less than the scatter of a count over 400 draws, so a test would need thousands of draws |
| R18 | :162 | newer sum without the shift | killed |
| R19 | :186 | region shifted by one block | killed |
| R20 | :188 | oldest ring layer not read | killed |
| R21 | :194 | each adder drops one slot | killed by the new validity test (rerun) |
| R22 | :201 | older window one draw newer | judgment: the windows then overlap by one draw, and the evidence ends about one draw sooner after a stop; a test that pinned that draw would test the window's constant, not a behavior |
| R23 | :215 | newer window takes the draw 18 back | killed |
| R24 | :225 | no reset of the count | killed by AfterACameraCutARegionGivesNoEvidenceUntilBothWindowsHoldOnlyDrawsAfterIt (new) |
| R25 | :228 | answer never stored | killed |
| R26 | :206 | squares changed | killed |
| S01 | accumulate_spatial.comp:52 | no anchor update | killed |
| S02 | :60 | region evidence as motion evidence | killed |
| S03 | :165 | cut to 7 draws | killed |
| S04 | :25 | RegionEvidence above LightEvidence | killed |
| C01 | GpuTypes.cs:844 | slot modulo 35 | killed |
| C02 | TemporalSignal.cs:450 | ring left out of the barrier | killed |
| C03 | TemporalSignal.cs:501 | anchors rounded down | killed |
| C04 | TemporalSignal.cs:747 | ring fit to the pixel extent | killed |
| C05 | TemporalSignal.cs:562 | ring for a signal without a light age | killed in the rerun by a crash of the test host (exit 139): the signal set then writes binding 13, which that layout lacks |
| C06 | DescriptorLayouts.cs:1272 | layout without binding 13 | killed |

The first run took 1,878 s and the rerun about 8 minutes.
