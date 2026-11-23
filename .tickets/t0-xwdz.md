---
id: t0-xwdz
status: in_progress
deps: []
links: [ter-bcwr, ter-4iic, t58-np60, t0-y88b]
created: 2026-11-23T12:08:20Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
tags: [rendering, temporal]
---
# After a reset, 23 to 50 % of the pixels still trace their own reflection ray in draw 8, and the tail lasts to about draw 12

Found by the lead of the announced dip of the render scale (ter-bcwr) on 2026-11-23, in the own-ray logs of the catch-up ray experiment (/private/tmp/t0-gbgj/gumm-mac/{cut,start}-today/ownrays-none.txt, 1280 x 720, today's code). Reflection / diffuse own rays per draw after a reset: draws 0 to 7 about 678,000 / 678,000 (every pixel); draw 8: 154,400 / 2,306 after a camera cut and 335,822 / 32,997 after a start; draw 9: 43,798 / 1,991 (cut); draw 12: 15,711 / 1,988 (cut). So the bounce light's own rays end at draw 8 as designed, and the reflections' do not: 23 % (cut) to 50 % (start) of the pixels still trace their own reflection ray in draw 8. On the card draw 8 costs 1.9 to 3.6 ms more than draw 9 for it (15.9 to 16.5 ms per unit of area against 12.8 to 13.4). At a start, draw 8 is the first ordinary measurement of the render scale's controller, and it alone lowers the ordinary request to 0.92 to 0.96, which takes 28 to 39 draws to return (part of the start's own dip, t58-np60).

The cause is inferred from the shader code and is not logged: the anti-lag of accumulate_spatial.comp acts once a history is longer than the fast cap (4) and cuts the length to max(fastCap, L (1 - lag / antiLagShare)). The reflections' box takes no retained widening, so in draws 5 to 7 a young and noisy reflection history moves the clamp and is cut back to 4 at many pixels. Those pixels read under FullRateFrames in draw 8 and some for a few draws more.

## Design

Step 1, measure and build nothing: log the reflections' history length per draw 4 to 12 after a cut and after a start (the share of pixels under FullRateFrames, and the share that the anti-lag shortened in that draw), to confirm or refute the inferred cause. Step 2, only with the cause confirmed, write the design as a note here. Two directions to weigh then, neither chosen: the anti-lag does not shorten a reflection history that is still young (its excursions are its own noise, not a change), or the plan's rule for an own reflection ray counts the draws since the reset and not the stored length. Each changes what the first draws after a reset look like, so the picture of draws 0 to 20 decides.

## Acceptance Criteria

The record holds the per-draw log and names the cause with its evidence. If a change follows: after a cut and after a start, the reflections' own rays in draw 8 fall to the level of draw 12, the picture of the first 20 draws is not worse than today's in a side-by-side video, and at a start the render scale's ordinary request is not lowered by draw 8.


## Notes

**2026-11-23T14:12:05Z**

2026-11-23, 09:12 New York time, line orchestrator of the Design X line. Step 1 starts on the line's free slot (answer 100, reported to the orchestrator): the lead of the render scale's dip, in its worktree t3-52bfcd29-81249085ce80dd9f, branch t0-xwdz-reflection-tail from main 4bb8489b3. It measures and builds nothing that merges: a scratch probe logs the reflections' history length in draws 4 to 12 after a camera cut and after a start, on the Mac, and the record goes to docs/research/t0-xwdz-reflection-tail. Money: 0.

**2026-11-23T14:13:53Z**

2026-11-23, 09:13 New York time, line orchestrator. Step 1 did not start: no brief went out. The line's free slot goes to the shadow softness ticket t1-af5w, which the orchestrator gave to the line at priority 1 on the user's word. This ticket is open again and waits for a slot.

**2026-11-23T16:52:05Z**

2026-11-23, 11:52 New York time, line orchestrator. Step 1 starts: the lead of the region's cap (worktree t3-c3911144) takes it after its record of the cap, on a branch t0-xwdz-reflection-tail from the line's head. It logs the reflections' history length in draws 4 to 12 after a camera cut and after a start: the share of pixels under FullRateFrames and the share that the anti-lag shortened in each draw. On the Mac, money 0, a scratch probe that does not merge. It brings a verdict on the inferred cause and no design.

**2026-11-23T17:42:18Z**

2026-11-23, 12:42 New York time, line orchestrator of the Design X line: step 1's interim result (the lead's record on the branch t0-xwdz-reflection-tail, 40fff9335). The inferred cause is partly confirmed. The anti-lag cuts young reflection histories in draws 4 to 7 (5 to 6 % of the cabin's surface pixels per draw, 6 to 12 % of the bay's), and the 15.6 % (cabin) and 21.2 % (bay) that are behind schedule at draw 7 trace their own ray in draw 8. Most cuts are small and do not reach the fast cap of 4. A second cause: 9.1 % of the cabin's pixels hold a length in [7.999, 8) at draw 7 and fail the plan's test historyLengthOf >= FullRateFrames; they give about 36 % of draw 8's 176,403 own rays. They were under 8 before draw 7's anti-lag, so they come from cuts under 0.001 in draws 4 to 6 or from rounding. The tail after draw 9: the anti-lag cuts 8 to 12 % of the pixels per draw from a length of 8 or more, and 17 to 38 % of those cuts end under 8. The v3 log, which splits the tiny cuts from the rounding, runs now. From the orchestrator: the near-8 lengths look like a rounding defect; if the log shows rounding, the fix is small and the line orchestrator settles it.

**2026-11-23T17:35:54Z**

Step 1, the v2 log (record docs/research/t0-xwdz-reflection-tail/, line head c768e1ed8, frozen fair test, 1280 x 720). In a frozen world the start and the cut back give the same log draw for draw. Draw 8's own reflection rays: 19.5 % of the cabin's surface pixels (176,403) and 25.3 % of the bay's. Verdict: partly confirmed. The anti-lag cuts 5 to 12 % of the young histories per draw in draws 4 to 7, so 15.6 % (cabin) and 21.2 % (bay) are behind schedule at draw 7. Most cuts are partial and do not reach the fast cap of 4. Not in the inferred cause: 9.1 % of the cabin's lengths end draw 7 in [7.999, 8) and fail the plan's >= 8 test in the 32-bit history, about a third of draw 8's own rays. The tail after draw 9 comes from cuts of histories of 8 or more (8 to 12 % of the pixels per draw, 17 to 38 % of them ending under 8 in the cabin). A length test at 7.5 saves 36 to 70 % of draw 8's own rays in the cabin. Open: the v3 log (scratch-tail-log.patch, written, applies, not built because the disk was under 22 GiB) splits the near-8 lengths into tiny cuts and rounding, counts the 7.5 split, and gives the histogram of the cuts' starting lengths.

**2026-11-23T17:52:08Z**

Step 1 finished with the v3 and v4 logs (record docs/research/t0-xwdz-reflection-tail/). Draw 8's own reflection rays by the own texel's length after draw 7: cabin 37.4 % in [7.99, 8), 13.8 % in [7.5, 7.99), 43.5 % under 7.5, 3.4 % a weighted tap under 8, 1.9 % the lobe fit (bay: 34.1, 7.5, 54.5, 2.6, 1.3 %). The near-8 lengths are a rounding defect of the reprojected place: in a still frame the place lies a float rounding off its own texel, so the other three taps take weights of 1e-7 to 1e-3, and next to a neighbor that a real cut shortened the length falls less than 1e-3 under its schedule (97.8 % of the near-below lengths in the cabin, 98.7 % in the bay). The mean, the blend and the 32-bit store do not round, tiny cuts give 1.4 % at most. The tail's cuts in draws 9 to 12 mostly start from on-schedule histories and end at every length from 4 up, 20 to 64 % under 8. In the bay about 15,000 pixels per draw are cut from [5, 6) back to the fast cap. First change proposed: make a still frame's reprojection read its own texel with weight 1 (37.4 % and 34.1 % of draw 8's own rays). The young-history cuts and the tail's cuts are for step 2.

**2026-11-23T17:54:22Z**

2026-11-23, 12:54 New York time, line orchestrator of the Design X line: step 1 is finished and its record is on the line (docs/research/t0-xwdz-reflection-tail, merge 8b8ef76d0). Draw 8's own reflection rays split three ways by the own texel's length after draw 7 (cabin 176,403 / bay 224,864): 37.4 % / 34.1 % from lengths in [7.99, 8), which a rounding of a still frame's reprojected place makes; 13.8 % / 7.5 % from [7.5, 7.99); 43.5 % / 54.5 % from under 7.5, the anti-lag's partial cuts of draws 4 to 7. The tail after draw 9 is the anti-lag's cuts of histories on schedule, 8 to 12 % of the pixels per draw, of which 45 to 20 % (cabin) and 64 to 40 % (bay) end under 8; in the bay about 15,000 pixels per draw go from [5, 6) back to [4, 5) and never reach 8. My decision (answer 103, as the orchestrator allowed for a rounding defect): the rounding is its own bug, t0-y88b, fixed at the one place that makes a reprojection's weights, and it is built first. The design of the rest (the partial cuts and the tail) follows in this ticket after that fix, because the fix changes the counts that the design starts from.

**2026-11-23T21:18:36Z**

## Step 2: two designs for the rest (the line orchestrator of the Design X line, 2026-11-23, 16:18 New York time). No build before the probe's pictures.

The rounding defect has its own fix (t0-y88b). After it, draw 8 still traces 106,254 own reflection rays in the cabin and 142,056 in the bay (docs/research/t0-y88b-still-place/README.md). This note is about those, and about the tail.

### Terms

- **The length** is a pixel's history length of one signal, in draws: the alpha of the slow history. It is the weight of the stored mean against one new sample, so the next sample enters with the weight 1 / (length + 1).
- **A cut** is the anti-lag's shortening of the length in one draw (accumulate_spatial.comp, step 4).
- **A new history** is one that began fewer than `FullRateFrames` (8) draws ago: after a reset, or where the pixel came onto the screen.
- **The plan** is screen_trace_plan.comp. It gives a pixel its own ray of a kind while the length that it reads is under 8.

### What the logs say, read again

1. The plan's test asks one question of the length: is this history new? The length answers another question after a cut: how much does the stored mean count against a new sample? The two agree until the first cut and never after it. This is the cause of every own ray in this ticket that the rounding did not make.
2. The cuts are not a matter of young histories alone. In a frozen world, where nothing changes, the anti-lag cuts 5.5 % of the cabin's surface pixels in draw 8 and 11.7 % in draw 12 (bay: 7.6 % and 11.9 %). In draw 12, 76 % of them start from histories that are on schedule. 20 to 64 % of the cuts end under 8.
3. So a still frame pays for own reflection rays in every draw: 36,855 in the cabin and 62,369 in the bay at draw 12 (4.1 % and 7.0 % of the surface pixels, measured before the fix of the still place). In the bay, about 15,000 pixels go from [5, 6) back to [4, 5) in every draw and never reach 8.
4. Why the box cuts in a frozen world (my reading of the code, not measured): the reflections' box is 1.5 standard deviations of the 3 x 3 fast histories, with no widening for the history's own noise. A single noisy value lies outside such a box at roughly one pixel in eight. The narrow box is wanted: it is the reflections' filter against bright specks (SignalParameters.Specular). So the clamp stays, and the question is only what a cut does to the length and to the rays.
5. The code already decided this question twice for a known change. Light and region evidence cut a history to `FullRateFrames` and no lower, "the shortest length that keeps the block's shared ray", because a shorter restart made every pixel of the block trace its own rays. Motion evidence narrows the box and does not cut, for the same reason. Only the clamp's own cut still goes under 8.

### Design 1: no shortening ends under `FullRateFrames`

One rule for every shortening of a history: it ends at 8 or above, and a new history is not shortened. Then a length under 8 has one meaning, a new history, which is the meaning that the plan reads. No new state.

```glsl
float restart = min(historyLength, FullRateFrames);
float antiLagged = max(restart, historyLength * (1.0 - min(1.0, lag / parameters.antiLagShare)));
historyLength = evidence == LightEvidence || evidence == RegionEvidence ? min(antiLagged, FullRateFrames) : antiLagged;
slow = clamped;
```

where

- `historyLength` = the length before this draw's cut, in draws, with this draw's sample counted.
- `FullRateFrames` = 8 draws: under it the plan gives the pixel its own ray.
- `restart` = the least length that this draw's cut can leave: the length itself for a new history, and 8 for every other.
- `lag` = how far the clamp moved the slow history's light, as a share of the luminance of the mean of the 3 x 3 fast histories.
- `parameters.antiLagShare` = the lag at which the cut is complete: 0.25 for the reflections and 0.5 for the bounce light.
- `antiLagged` = the length after the cut.
- `evidence` = the evidence level of the pixel's block of 8 x 8 pixels in this draw.
- `clamped` = the slow history's light inside the box. It does not change in this design.

The fast cap leaves the floor, because `restart` is never under it where the clamp runs. A pixel whose cap is under 8 (a roughness under about 0.19) is never cut. It traces its own ray in every draw today, and it still does.

### Design 2: an age beside the length

The length keeps its meaning and its cuts down to the fast cap of 4. A second value per pixel and signal, the age, counts the draws since the history began, up to 8. A cut does not change it. The plan reads the age.

```glsl
// screen_trace_plan.comp
if (historyAgeOf(r, kind) >= FullRateFrames && fits(p, c, kind))
    continue;
```

where

- `historyAgeOf(r, kind)` = the draws since the pixel's history of `kind` began, read at the pixel's place in the last frame through the same taps and weights as the length.
- `fits(p, c, kind)` = whether the block's ray fits the pixel (for a reflection, the lobe fit).

The age needs a channel in each signal's history, a reset with the length, and a rule for its reprojection (the mean of the taps that pass, like the length).

### The two designs side by side

| | What a length under 8 means | The next sample's weight after a full cut | New state | What the anti-lag still does |
|---|---|---|---|---|
| Design 1, one floor | A new history, and nothing else | 1/9 at most (today up to 1/5) | None | Clamps the light into the box in the same draw, and shortens a long history to 8 at the least |
| Design 2, an age | A new history or a cut one. The age tells them apart | Up to 1/5, as today | One channel per signal, written by the accumulator and read by the plan | All that it does today |

Both share:

- A cut buys no own ray. Draw 8's own reflection rays fall to the two small groups that are not cuts: the lobe fit, and a tap of a newer neighbor in the footprint (together 5.3 % of the cabin's 176,403 and 3.9 % of the bay's 224,864 before the fix of the still place).
- The own rays that a still frame pays for in every draw go too (point 3 above). So the level of draw 12 falls as well, and the acceptance compares with today's draw 12.
- The clamp of the light does not change. Light, region and motion evidence do not change. `FullRateFrames` and the fast cap keep their values.
- Under a moving camera, a reflection that lags is still clamped in the same draw, but it no longer gets its own ray. This is the picture that must be seen: a pan over a glossy floor.
- The rule sits in the one dispatch of both signals. Whether the bounce light follows is the second fork below.

### The forks

1. **The weight after a cut.** Design 1 lets a cut history take its next samples at 1/9 at most. Design 2 keeps 1/5 and pays one channel per signal and a second store that can disagree with the length.
2. **The bounce light.** One rule for both signals, or the reflections alone. The bounce light's own rays at draw 8 are few (2,306 of 678,000 in the ticket's first log), so the rays are no reason. The picture is: with one rule, a bounce pixel that the anti-lag cuts keeps 8 draws in place of 4 to 8. This touches what t1-g1jd holds.

### My recommendation

Design 1, for the reason of point 5: the code already holds this rule for light and region evidence, with the reason written beside it. Design 1 makes it the rule of every shortening and removes the one path that disagrees. For the bounce light, one rule if the stills agree.

### The probe before the choice (answer 112)

A scratch probe, on the Mac, money 0, nothing that merges. The lead of the still place's fix takes it after its merge, in its worktree, on a branch from the line's head with that fix in it. Three switches in one probe build:

- `F`: design 1 for the reflections.
- `Fd`: design 1 for the bounce light too.
- `A`: design 2's picture in a frozen take. The plan reads the frame's count of draws since the reset in place of a per-pixel age. In a frozen take after a reset both are the same number at every pixel, so the stand-in is exact there and only there.

Takes, each for the probe with all switches off, `F`, `F` with `Fd`, and `A`:

1. The fair cabin and bay, frozen, after a cut: the own rays of each kind per draw 4 to 20 and at draw 100, with t0-xwdz's tail log.
2. The same takes' films of draws 0 to 20, side by side, by the video rules. A dither or a slow shutter is not wanted: real frames, one fixed exposure.
3. Settled stills of the cabin, the bay and rect-lamps' glossy floor (frozen world, settled exposure): the mottle strength on the flat areas, 1:1 crops at the reflections, and a count of bright specks.
4. A pan of about 1 texel per draw over the bay's floor and over rect-lamps' glossy floor (not for `A`, whose stand-in does not hold there): a side-by-side film, and the own rays per draw.
5. The GPU frame span in the settled still, the switch off against `F`, on a quiet machine, under one hold of the GPU lock. The table names its main as the probe with the switch off.

The lead brings the tables and the pictures, and no choice.
