# Spread the catch-up rays of a young history over more draws (t0-gumm, experiment)

The user's words, 2026-11-22: "i think catch up rays are important after a reset of the history. maybe spreading them out more somehow?" Today the ray plan gives a pixel a ray of its own in every draw while its history is shorter than `FullRateFrames`, 8 draws: a cliff. This experiment replaces the cliff with one share function, so that the same rays can arrive over more draws at a lower peak. It is scratch code. The patches stay in this folder and never go into `src`.

## Terms

- A block is 2 × 2 pixels. In each draw one pixel of a block, the chosen pixel, traces the block's ray of a kind. An own ray is a ray that another pixel of the block traces for itself.
- A catch-up ray is an own ray that a pixel traces because its history is young, not because the block's ray does not fit it.
- L is the length of a pixel's slow history of a kind, in draws: the slow history's alpha, as the plan reads it from the last draw.
- The shape (PeakShare, EndFrames, Fall) gives the share of draws on which a young pixel traces a catch-up ray: `share = L >= End ? 0 : PeakShare · (1 − L / End)^Fall`, with `End = min(EndFrames, cap)`, and Fall 0 a box. (1, 8, 0) is today's cliff.
- The tree: X5, the line's merge `710834cc0` (tree `e232080f`): Design X with light evidence restarting a history at 4 draws, without the cap of `t0-zmjx`.

## The scratch code

`scratch-ownray-log.patch` (`TERTIUS_X_OWNRAY_LOG`) logs each draw's own-ray lists: their counts, and of them those in even columns and in even rows. `scratch-catch-up-share.patch` builds the share in `Shaders/screen_trace_plan.comp`. Both apply after X5's probe patch and `t0-3vwx`'s region, dynamic-scale and history-log patches (`tools/builds.sh`: `xt` is today's code with the log, `xg` adds the share).

```glsl
            if (fits(p, c, kind) && !catchUpTurn(pixel, kind, catchUpShare(historyLengthOf(r, kind), capOf(p, kind), youngHistory(ownAge, blockCodes[kind]))))
                continue;
```

where

- `fits(p, c, kind)` = whether the block's ray from its chosen pixel `c` fits the pixel `p`, as today.
- `historyLengthOf(r, kind)` = L.
- `capOf(p, kind)` = the history's cap at the pixel in draws: 32 for the diffuse signal; for the reflections, 4 up to roughness 0.1, linear to 32 at 0.7, as `SignalParameters.Specular` and `capAt` give it. The plan has no signal parameters, so the scratch carries them as constants.
- `youngHistory(ownAge, blockCode)` = whether the history restarted less than End draws ago (see "Where the ticket's design did not fit").
- `catchUpShare(L, cap, young)` = the share above for a young history whose cap is at least 8 draws; today's rule (1 while L < 8, else 0) for every other history.
- `catchUpTurn(pixel, kind, share)` = whether the pixel takes its turn in this draw: `phase = offset + drawIndex · 2654435769` in 32-bit unsigned arithmetic, a turn when `phase < share · 2^32`. 2654435769 is 2^32 divided by the golden ratio.

The shape comes from `TERTIUS_X_CATCHUP="<peak> <end> <fall>"` through three new push constants of the tracer, so one build serves every shape and one cloud job can run many.

## Where the ticket's design did not fit, and the forms chosen

1. **The young test.** The ticket's share is a function of L alone, ending at the cap. In the settled cabin at 2/3, many diffuse histories stay below the cap of 32 for good. The map below shows why: region evidence that fires falsely, not the anti-lag as I first read it (the patch's comment still says the anti-lag). Shape (1, E, 0) on today's code, mean diffuse own rays per draw over the settled draws 150 to 192 (today: 2,164):

   | End | 12 | 16 | 24 | 32 |
   |---|---|---|---|---|
   | Diffuse own rays per draw | 33,844 | 65,315 | 130,638 | 163,080 |

   So about 161,000 of 921,600 pixels (17 %) sit between 8 and 32 draws. L alone cannot tell such a history from one that grows after a light change or a reset, and any End above 8 draws would trace own rays there for ever. The chosen form gates the share by the time since the history's restart. A history is young while one of these is under End draws:
   - the draws since the last reset (`drawIndex`: a start or a camera cut empties every history);
   - the draws since the pixel's own exact light changed (its light-age record);
   - the draws since its block's rays last saw a changed light: the youngest light code of the last draw's samples over the plan's 8 × 8 workgroup, which is the spatial dispatch's block. This is the source of the light evidence that restarts the block's histories, and it covers the pixels whose own direct light did not change. Without it, about half of today's peak stayed after a lamp switch (286,000 to 322,000 diffuse own rays per draw in draws 1 to 10 after off, against today's 545,000 to 675,000). The read is the one that the acceptance lead's variant XD made: the plan reads the last draw's sample ages (the images lose `writeonly`), with two barriers that every invocation reaches before any return.

   A history that is not young keeps today's rule exactly, so a settled view traces today's own rays.
2. **The offset.** The blue-noise mask holds 8 bits per channel. The offset takes the mask's first frame at the pixel (red for the reflections, green for the diffuse rays) as its high 8 bits and `pcg3d` of the pixel as its low 24, so that small shares still get distinct offsets.

## The settled history lengths of the cabin

![the settled history lengths](pics/history-map-cabin.png)

`pics/history-map-cabin.png`: the cabin of the switch film with the lamps on through the whole run (no switch), at the constant scale 2/3, draw 190 on today's code `xt` (`scratch-history-dump.patch`, `tools/histmap.py`). Left the still, right the slow diffuse history's length per render pixel, in five bands (`historymap.txt`):

| Band, draws | Under 8 | 8 to under 16 | 16 to under 24 | 24 to under 31.5 | 31.5 or more (the cap) |
|---|---|---|---|---|---|
| Share of the pixels | 1.9 % | 9.7 % | 6.6 % | 4.5 % | 77.4 % |

- Under 8 draws: the window openings, which show space and hold no history.
- 8 to 31.5 draws, 20.8 % of the pixels: whole rectangles about 136 render pixels high, which is the size of a region of the drift test (17 × 17 blocks of 8 pixels), on the ceiling, the walls and the floor. In the same run, the region test fires on 1 to 11 of its 238 tested anchors in each of the draws 100 to 131, although nothing changes. Region evidence caps a region's histories at 8 draws, and they regrow by one per draw, so the rectangles show fires of different ages. These are the false fires of `t0-qvm1`.
- The short histories do not lie at edges, at the jambs or on the floor's glow, and they are not grain spread over the view.
- Main has no region test. There is no dump of main `ac5f23915` (`m5h` has no dump patch), so the map has no main row.

## The card, job 1: the render scale under the first shapes

![the card's render scale](pics/card-scale-shapes1.png)

`pics/card-scale-shapes1.png` (job 1 ran the old cut film: 100 frames on the bright chase camera, then a cut to the cabin at draw 133; after it the cabin stays saturated white for over 100 draws, so job 2 uses a cut inside the cabin): the render scale per draw on the card (RTX 2000 Ada, 1920 × 1080, the dynamic scale at 16.67 ms), from 5 draws before each event to 59 after. Black is today's cliff (1, 8, 0), red (0.0625, 32, 0), blue (0.125, 32, 0), green (0.25, 32, 0), orange the cap (`scratch-light-evidence-cap.patch` of the acceptance record: light evidence restarts at 8 draws, so today's plan traces no catch-up ray after a light change). One cloud job, `card.sh shapes1`, $0.0542; one warm-up run of each build first. The logs are in `logs/card-shapes1/`, the table in `cardscale-shapes1.txt` (`tools/cardscale.py`).

The bar is the idle span, draws 133 to 192 of the switch film (lamps on, nothing changes): 0 scale changes for every code, at a scale of 0.988 or 0.989. Its GPU span is 17.2 to 17.5 ms, above the target, and the controller's dead band holds the scale.

| Code | Start: changes, largest fall, scale lost | Off: changes, fall, lost | On: changes, fall, lost | Cut: changes, fall, lost |
|---|---|---|---|---|
| Today (1, 8, 0) | 57, 0.232, 7.38 | 54, 0.144, 3.71 | 53, 0.150, 4.02 | 49, 0.113, 3.02 |
| (0.25, 32, 0) | 45, 0.127, 5.51 | 32, 0.045, 1.76 | 33, 0.052, 2.02 | 31, 0.053, 2.16 |
| (0.125, 32, 0) | 42, 0.102, 4.52 | 14, 0.020, 0.81 | 23, 0.026, 1.05 | 16, 0.029, 1.27 |
| (0.0625, 32, 0) | 39, 0.091, 4.09 | 3, 0.005, 0.22 | 5, 0.009, 0.40 | 3, 0.014, 0.61 |
| The cap | 57, 0.234, 7.54 | 0, 0, 0 | 0, 0, 0 | 50, 0.127, 3.35 |

"Changes" counts the draws of the window whose scale differs from the draw before. "Largest fall" is the scale before the event minus the lowest scale in the window. "Scale lost" is the sum over the window of (the scale before − the scale), in draws. In no window with a fall does the scale come back to its level before the event within 60 draws, because the controller rises slowly.

- No shape passes the bar. Each moves the scale after the switch and the cut. At (0.0625, 32, 0) the move is small: 3 to 5 changes and a fall of 0.005 to 0.014. The share falls with the peak, about by half for each halving of the peak.
- The start falls for every shape: 0.091 still at (0.0625, 32, 0), against 0.232 today. Part of the start's fall is not catch-up rays (`t58-np60`: the scale falls to about 0.77 at every start). The shape (0, 32, 0), with no catch-up ray at all, measures that part in job 2.
- The cap holds the scale after off and on, with no change at all, as the orchestrator predicted. After the cut, the cap falls as today does (0.127 against 0.113), because it changes only the light evidence.

## The card, job 2: the search, the start's two parts, and the codes with the cap

![the card's render scale, job 2](pics/card-scale-shapes2.png)

`pics/card-scale-shapes2.png`: as job 1's chart, for six of job 2's codes. Black today, red A (the cap), blue (0, 32, 0) with no catch-up ray, green B32 (0.03125, 32, 0), orange C32 (the cap with (0.03125, 32, 0)), violet C' at 1/16 (the cap, and the spread rays only after a start or a cut). Job `card.sh shapes2`, $0.0846, through main's `gpu_job.py` after the user raised the daily cap. The cut film is the new one (cut inside the cabin at draw 134). Logs in `logs/card-shapes2/`, the table of all eleven codes in `cardscale-shapes2.txt`.

| Code | Start: changes, fall, scale lost | Off: changes, fall | On: changes, fall | Cut: changes, fall |
|---|---|---|---|---|
| Today (1, 8, 0) | 57, 0.232, 7.39 | 54, 0.154 | 53, 0.151 | 56, 0.140 |
| A: the cap | 57, 0.231, 7.42 | 0, 0 | 0, 0 | 58, 0.140 |
| (0, 32, 0): no catch-up ray | 30, 0.070, 3.29 | 0, 0 | 0, 0 | 6, rises only |
| (0.015625, 32, 0) | 34, 0.081, 3.76 | 0, 0 | 1, 0.004 | 3, rises only |
| B32: (0.03125, 32, 0) | 32, 0.084, 3.93 | 0, 0 | 1, 0.005 | 3, rises only |
| B16: (0.0625, 32, 0), second run | 35, 0.092, 4.19 | 8, 0.013 | 9, 0.008 | 4, 0.003 |
| (0.0625, 16, 0) | 45, 0.093, 3.98 | 8, 0.013 | 21, 0.018 | 11, 0.011 |
| (0.0625, 32, 1) | 43, 0.091, 4.00 | 1, 0.004 | 12, 0.014 | 4, 0.002 |
| C32: the cap with (0.03125, 32, 0) | 31, 0.086, 4.03 | 0, 0 | 1, 0.004 | 3, rises only |
| C16: the cap with (0.0625, 32, 0) | 34, 0.092, 4.30 | 5, 0.009 | 6, 0.009 | 11, 0.011 |
| C' at 1/16: the cap, light clocks off | 32, 0.093, 4.30 | 0, 0 | 0, 0 | 5, 0.007 |

- **The noise of the card's scale.** The idle span had 0 changes in every code except one: C16's idle span has 3 changes. B16 ran in both jobs: after off 3 and 8 changes (falls 0.005 and 0.013), after on 5 and 9. So one change of 0.004 or 0.005 lies inside the card's noise.
- **The search.** The largest peak share that leaves the scale alone within that noise is 1/32: B32 and C32 have no change after off, one change of 0.004 to 0.005 after on, and after the cut only small rises. 1/64 gives the same. 1/16 moves the scale in both runs. EndFrames 16 at 1/16 moves it more, because it gives the same rays in half the draws. Fall 1 at 1/16 lies between 1/16 and 1/32. So neither EndFrames nor Fall beats a smaller peak with the box.
- **The start in two parts** (the scale's largest fall, then the scale lost over 60 draws):
  - The part without catch-up rays, from (0, 32, 0): 0.070, 3.29. This is the dear first draws of `t58-np60` and the other start costs.
  - The catch-up rays' part, today minus that: 0.162, 4.10. At B32 it is 0.014, 0.64.
- **The cut** without catch-up rays raises the scale a little (up to 0.003). After a cut, the draws with empty histories cost less than settled draws in some pass.
- **What one ray must buy.** A share of 1/32 for 32 draws gives one catch-up ray per restarted pixel, and this card allows no more without a move of the scale. So the Mac's measures must show whether that one ray buys anything: C against A (the cap alone), and B against today's cliff. The table says so plainly, also if the answer is "nothing that the measures see".
- **The share is a cost budget.** A faster card or a higher frame time target fits a larger share, so a production form could take the share from the controller's headroom. Not built.
- **C'** gives the cap's flat scale after a light change, because no history counts as young then. After the start and the cut it runs C's code. C' therefore needs no Mac run of its own.
- (0, 32, 0) still traces today's own rays for the reflections whose cap is under 8 draws (smooth surfaces), so "no catch-up ray" holds for the diffuse rays and the rough reflections.

## The card, job 3: two runs of each code, 1/16 against 1/32

![the card's render scale, job 3, from 0.95 to 1](pics/card-scale-shapes3-zoom.png)

`pics/card-scale-shapes3-zoom.png`: the render scale per draw, as job 1's chart, but from 0.95 to 1 (a scale under 0.95 sits on the bottom line). Black A's second run, red and blue B32's two runs, green C32's first run, orange and violet B16's two runs. `pics/card-scale-shapes3.png` shows today, A, B32 and B16 from 0.5 to 1. Job `card.sh shapes3` with `RUNS=2`, $0.0898: one warm-up run of each build, then each code once, then each code again (run 1 and run 2). Logs in `logs/card-shapes3/`, the table in `cardscale-shapes3.txt`.

The warm-up runs also cover the fact `t1-80jd` (the first run of a card job may draw 10 to 18 % slower; not proven): no compared run is the first run of the job. Jobs 1 and 2 had the same warm-up.

| Code | Off: changes, fall (run 1 / run 2) | On: changes, fall | Cut: changes, fall | Idle: changes |
|---|---|---|---|---|
| Today (1, 8, 0) | 54, 0.151 / 53, 0.156 | 53, 0.154 / 53, 0.154 | 56, 0.139 / 56, 0.144 | 0 / 0 |
| A: the cap | 0, 0 / 0, 0 | 0, 0 / 1, 0.004 | 56, 0.141 / 56, 0.140 | 0 / 0 |
| B16: (0.0625, 32, 0) | 2, 0.009 / 8, 0.013 | 10, 0.009 / 11, 0.013 | 4, 0.003 / 8, 0.006 | 0 / 0 |
| B32: (0.03125, 32, 0) | 0, 0 / 0, 0 | 4, 0.013 / 1, 0.004 | 3, rises only / 3, rises only | 0 / 0 |
| C16: the cap with (0.0625, 32, 0) | 3, 0.010 / 1, 0.005 | 12, 0.013 / 8, 0.009 | 10, 0.011 / 8, 0.006 | 0 / 0 |
| C32: the cap with (0.03125, 32, 0) | 0, 0 / 0, 0 | 1, 0.004 / 0, 0 | 4, 0.002 / 3, rises only | 1 / 0 |

The start, for the record (changes, fall, run 1 / run 2): today 57, 0.233 / 57, 0.238; A 57, 0.235 / 57, 0.238; B16 36, 0.093 / 32, 0.093; B32 32, 0.086 / 31, 0.087; C16 33, 0.093 / 34, 0.094; C32 31, 0.085 / 31, 0.086.

- **The card's noise.** A traces no catch-up ray after a light change, yet its second run changes the scale once by 0.004 after on, and C32's first run changes it once in the idle span. So one change of about 0.004 is the card's noise.
- **The spread between two runs of one code** (changes, then the largest fall): B32 differs by 0 and 0 after off, 3 and 0.009 after on, 0 and 0 at the cut. B16 differs by 6 and 0.004 after off, 1 and 0.004 after on, 4 and 0.003 at the cut. C32 differs by at most 1 and 0.004 in each window.
- **1/16 against 1/32.** After off, every run of 1/16 changes the scale (1 to 8 changes, falls 0.005 to 0.013), and no run of 1/32 does. After on, every run of 1/16 changes it 8 to 12 times, and every run of 1/32 0 to 4 times. At the cut, every run of 1/16 falls 0.003 to 0.011, and the runs of 1/32 rise or fall 0.002 at most. The one overlap is B32's first run after on: its fall of 0.013 (in 4 changes) is as large as 1/16's. **So the card tells 1/16 from 1/32**: the two runs of 1/32 do not differ as much as 1/16 differs from 1/32. The answer of job 2 stands: 1/32 is the largest peak share that leaves the card's scale within its noise. B32 sits at that limit, because one of its four light-change windows came out above the noise. C32 sits inside it in each of its windows.
- The idle levels of the runs lie between 0.981 and 0.989. Each window's fall counts from its own run's level before the event.

## The three codes: the table, the rule and the recommendation

**The base.** Every code here is built on X5, the line's merge `710834cc0` (tree `e232080f`). A and C add the cap as the acceptance record's `scratch-light-evidence-cap.patch`, the same rule as the production commit `05a27262d`. Main's step 2 of the wobble fix (`3549d3283`, merged to the line as `6f54dd615`, line head `aeed1895b`) came after this base, and no code here has it.

**The codes.**

- Today: X5's cliff, (1, 8, 0), with light evidence restarting a history at 4 draws.
- A: the cap. Light evidence restarts a history at 8 draws, so no history is young after a light change, and the plan keeps today's cliff after a start or a cut.
- B: the share (P, 32, 0) with the restart at 4 draws, and all three clocks in the young test.
- C: the cap and the share (P, 32, 0), with all three clocks.
- C': the cap and the share, with only the reset's clock (`drawIndex < End`, the fourth shape value 0). After a light change it runs A's code path, and after a start or a cut C's. So it needs no run of its own.
- None: (0, 32, 0), no catch-up ray at all after a reset (the Mac's start and cut, `tools/queue-mac6.sh`). With the cap, after a light change it runs A's code path.

P is 1/32 (B32, C32) or 1/16 (B16, C16).

### The cost

| Code | Card, start: largest fall (run 1 / 2) | Card, off: changes | Card, on: changes | Card, cut: changes, fall | Mac, extra GPU ms over 60 draws: off / on / start / cut |
|---|---|---|---|---|---|
| Today | 0.233 / 0.238 | 54 / 53 | 53 / 53 | 56, 0.139 / 56, 0.144 | 79 / 75 / 91 / 78 |
| A | 0.235 / 0.238 | 0 / 0 | 0 / 1 | 56, 0.141 / 56, 0.140 | 2 / 9 / as today / as today |
| B32 | 0.086 / 0.087 | 0 / 0 | 4 / 1 | 3, rises / 3, rises | 6 / 12 / 42 / 8 |
| C32 | 0.085 / 0.086 | 0 / 0 | 1 / 0 | 4, 0.002 / 3, rises | 3 / 11 / 39 / 10 |
| C' at 1/32 | as C32 | as A | as A | as C32 | as A after a light change, as C32 after a start or a cut |
| B16 | 0.093 / 0.093 | 2 / 8 | 10 / 11 | 4, 0.003 / 8, 0.006 | 12 / 19 / 52 / 18 |
| C16 | 0.093 / 0.094 | 3 / 1 | 12 / 8 | 10, 0.011 / 8, 0.006 | 13 / 18 / 48 / 22 |
| None | 0.070 (job 2, one run) | 0 (job 2) | 0 (job 2) | 6, rises (job 2) | not run |

- The card's columns come from job 3 (two runs per code), the last row from job 2. One change of about 0.004 is the card's noise.
- The start falls by about 0.070 for every code, also with no catch-up ray. That part is the dear first draws of `t58-np60`, so rule 1 counts only the catch-up rays' part above it.
- The Mac's GPU spans (`tools/spans.py`, timing runs with the GPU lock held alone, at 2/3) are noisy under load. A runs today's code at a start and a cut, but its timing runs gave 217 and 177 ms. So those two cells say "as today".

### The picture on the Mac, at the constant scale 2/3

The slowest region's settle in draws, the boil without the fade over draws 1 to 59 in %, and the grain over draws 0 to 15 in % (`tools/events.py`):

| Code | Off: settle, boil, grain | On: boil, grain | Start: settle, boil, grain | Cut: settle, boil, grain |
|---|---|---|---|---|
| Today | 142, 0.276, 2.14 | 0.177, 1.01 | 139, 0.109, 0.86 | 134, 0.106, 0.88 |
| A | 139, 0.267, 1.61 | 0.174, 0.89 | as today | as today |
| B32 | 142, 0.291, 2.11 | 0.191, 1.00 | 139, 0.114, 0.89 | 133, 0.112, 0.87 |
| C32 | 139, 0.266, 1.61 | 0.174, 0.88 | as B32 | as B32 |
| C' at 1/32 | as A | as A | as B32 | as B32 |
| B16 | 142, 0.290, 2.11 | 0.190, 1.00 | 139, 0.113, 0.89 | 133, 0.112, 0.87 |
| C16 | 139, 0.265, 1.62 | 0.173, 0.89 | 139, 0.113, 0.89 | 133, 0.112, 0.87 |
| None | as A (with the cap) | as A (with the cap) | 139, 0.114, 0.89 | 133, 0.112, 0.87 |

- **The spread between runs with the event moved by 1 and 2 draws** (`tools/queue-mac3.sh`). Today after off: settle 142, 140, 138, and boil 0.276, 0.268, 0.274, so the boil's spread is 0.008. After on: boil 0.177, 0.175, 0.177. At the cut, the three runs give the same numbers to the last digit. B32 after off: settle 142, 139, 137, and boil 0.291, 0.283, 0.287. At the cut, its three runs are equal. The start cannot move.
- After on, no code's upper and middle regions settle within the 100 draws of the film. The bottom row settles in 77 to 91 draws in today's three runs, and every code lies inside that span.
- A and C32 differ by at most 0.001 in boil and 0.01 in grain after a light change. A against today: the same settle within the spread, and less grain after off (1.61 against 2.03 to 2.14), because the cap keeps 8 draws of history where today keeps 4.

### The picture under the card's scale

The runs of the final videos (`tools/queue-mac5.sh`) play back each code's own card scale log, the second run of job 3, so the cliff's fall of the scale shows in the picture. Each run's own size log gives the card log's scale on every draw. The measures are as above (`videos-events.txt`):

| Code | Off: settle, boil, grain | On: settle, boil, grain | Start: settle, boil, grain | Cut: settle, boil, grain |
|---|---|---|---|---|
| Today | 173, 0.182, 2.24 | none, 0.140, 1.06 | 156, 0.112, 0.87 | 153, 0.105, 0.89 |
| A, run 2's log | 135, 0.208, 1.97 | none, 0.139, 1.02 | 158, 0.110, 0.87 | 152, 0.104, 0.89 |
| A, run 1's log | 135, 0.208, 1.97 | 80, 0.152, 1.01 | | |
| C32 | 140, 0.211, 1.95 | 81, 0.141, 1.03 | 154, 0.111, 1.08 | 132, 0.106, 1.02 |

- **After off**, today settles 38 draws after A and 33 after C32. Its scale changes 90 times in the video. At 2/3, the three settled within 3 draws of each other.
- **After on**, A's run-2 log has one size change of 0.004 at draw 402, which is the card's noise. With it, A's upper and middle regions do not settle within 100 draws. On run 1's log, which has no size change after on, A settles in 80 draws, as C32 does in 81. So one size change after a light change costs more than 20 draws of settle. The likely cause is `t4f-71qp` (the region's drift test restarts its count at every change of the render size). Not hunted.
- **At the cut**, C32 settles in 132 draws, and A and today in 152 and 153. A and today fall to a scale of 0.85 and change size 40 and 44 times. C32 holds 0.98.
- **At the start**, the three settle within 4 draws.
- **The grain of the first 16 draws** is the cliff's price on the picture: C32 1.08 against 0.87 at the start, and 1.02 against 0.89 at the cut. At 2/3 the difference was 0.03. At draw 2 after the start, C32's floor shows a fine speckle that A and today do not show (`pics/start-draw2-floor.png`: A, today, C32, a crop of 800 × 440 source pixels; the render scale at draw 2 is 0.929 (1776 × 1000) for A, 0.929 (1776 × 1000) for today and 0.969 (1856 × 1040) for C32).

![draw 2 after the start: A, today, C32](pics/start-draw2-floor.png)

**The cut's fade in the videos** (the orchestrator's question). `videos.txt` puts C32's center region (r11) 1.7 to 2.1 % further below its last frame than A's at 2, 10 and 40 draws after the cut. The source frames show where that comes from:

- In the PNGs, the center's mean display-linear luminance is the same at those draws (A, today, C32): 0.1642, 0.1640, 0.1641 at draw 2, 0.1750, 0.1749, 0.1743 at draw 10, and 0.1853, 0.1851, 0.1854 at draw 40. The gap appears later. At draws 80 and 119 after the cut, C32 is 2.9 % and 2.0 % brighter than A (0.1963 against 0.1908, and 0.1996 against 0.1956). So the gap is in the reference frame, the video's last frame.
- The catch-up rays do not cause it. At 2/3, the probe's scene luminance of the center (`frames.series`) is the same for today, none and C32 within 0.1 % at every draw compared.
- Under the card's scale, the probe's scene luminance of A and C32 also agrees within 0.1 % at draws 80, 119 and 199 after the cut. The grain at draws 40, 80 and 119 is equal too (rms 1.97 to 2.04 of 255). So the renderer's light is the same, and the difference lies in the shown picture after it.
- The runs differ only in their scale logs. A falls to 0.85 at draw 144, changes size 40 times, and is back at 0.983 by draw 214. C32 holds 0.985. So the render scale's changes cause the gap, most likely through FSR's history after the size changes. That is not proven.

### What one catch-up ray per restarted pixel buys

A share of 1/32 for 32 draws gives one catch-up ray per restarted pixel. **It buys nothing that the measures see.**

- After a light change, C32 measures as A, which traces none.
- At the start and the cut, B32 and C32 measure as "none" to the last digit.
- B against today: B boils more than today after off (0.283 to 0.291 against 0.268 to 0.276) and after on (0.188 to 0.191 against 0.175 to 0.177), by 0.013 to 0.015 at each matched shift of the event, more than the spread of 0.008. Its settle is the same within the spread. So B buys back today's cost, but not today's picture after a light change.

Today's cliff, 8 rays per restarted pixel, buys a little at the start and the cut: a boil of 0.109 against 0.114 at the start and 0.106 against 0.112 at the cut. That is about 5 % less, and more than the spread, which is zero at the cut. The settle is the same at the start, and 1 draw slower at the cut. Its price is the card's fall: 0.14 at the cut, and 0.24 at the start, of which about 0.16 is the cliff's.

### The rule

1. **No cost peak that moves the card's scale.** Today fails at every event. A fails at the start and the cut (falls of 0.24 and 0.14). B16 and C16 fail after off, after on and at the cut. B32 sits at the limit: one window of four after a light change is above the noise. C32, C' at 1/32 and None pass.
2. **Then the faster settle, unless its boil is higher by more than the spread.** C32, C' at 1/32 and None measure the same at every event: the same settle, and boil and grain within 0.001 and 0.01. (B32 settles as they do but boils 0.014 to 0.025 more after a light change, so it would lose here too.)
3. **Then the smaller reach and less code.** None needs no catch-up machinery after a reset: the plan skips the cliff while the draws since the reset are under End (`drawIndex < 32` in the scratch). C' needs the share and its turn but only the reset's clock. C32 needs all three clocks, with a new read and two barriers.

### The recommendation

**The cap and no catch-up ray after a reset.** It is "None" with the cap: A's code, plus a plan that gives no catch-up ray to a history whose cap is 8 draws or more while the draws since the last reset are fewer than 32, as the shape (0, 32, 0) ran it. The smooth reflections, whose cap is under 8 draws, keep today's rule. It passes the card at every event, and it gives the same picture on the Mac as C32. It needs one existing read and no new state.

Its price on the picture is the grain of the first draws after a start or a cut, under the card's scale: 1.08 against A's 0.87 at the start, and a fine speckle at draw 2. C' at 1/32 does not remove it, because C32 shows it too. The only way back to A's smooth first draws is the cliff, and the cliff moves the card's scale by 0.14 to 0.24.

### The production form

- **Reads that exist in the plan today:** the draw index since the last reset (`trace.drawIndex`, in the push constants), and the pixel's own light-age record (`lightAgeOf`, from the last draw's `lastLightAge`). None and C' need only the draw index. A C or B form also needs the light-age record, which exists.
- **A new read:** the block's youngest light code (C and B only). The plan would read the last draw's sample-age images (`specularSampleAge`, `diffuseSampleAge`, today `writeonly`) and reduce them over its 8 × 8 workgroup in shared memory, with two `barrier()` calls that every invocation reaches before any return. That also needs a memory barrier from the last draw's tracer writes to this read. The scratch form ran only on MoltenVK. It was not checked on Linux.
- **Restarts that no clock sees:** region evidence (the drift test's real and false fires cap a region's histories at 8 draws), the anti-lag's cut of a history, and a disocclusion (a surface newly in view while the camera moves). A history that is not young keeps today's rule, so these restarts still get today's cliff of own rays while their history is under 8 draws. With the cap, light evidence restarts at 8 draws, so a light change gives none.
- **The share is a cost budget.** A faster card or a higher frame time target fits a larger share, and a production form could take the share from the controller's headroom. Not built.

### Parity

- **The column split:** within each draw parity, even and odd columns get catch-up rays within 0.33 % of each other. So the column parity does not follow the draw parity (`t2-8foa`).
- **The draw-parity excess:** in the light-change windows only, the odd draws take about 2.3 % more catch-up rays than the even draws. At a start, the four counts lie within 0.34 % of a quarter.
- **The light code's clock:** the excess shows only where the light clocks decide youngness. The block's light code comes from the last draw's samples, whose block-ray pixels move with the draw parity. That may cause it. Not hunted.

## The videos

The three files `pics/preview-switch-three.mp4`, `pics/preview-start-three.mp4` and `pics/preview-cut-three.mp4` (A | today | C32) are previews only. They do not follow the line's video rules:

- They run at 30 frames a second and show only the pictures that the measure runs kept, so draws are skipped.
- Each pane is 960 × 540.
- The scale is the constant 2/3, so they hide the cliff's price, which is the fallen render scale.
- In the switch preview, the corner shows the film frame, not the draw. The draw is the film frame + 33.

The final videos are `pics/<event>-final.mp4` (`tools/queue-mac5.sh`, then `tools/videos.py`). They meet the rules: 3840 × 720 (three panes of 1280 × 720), 60 frames a second, one draw per frame, H.264 `yuv420p` at CRF 12, the render scale per pane, a draw counter, and the fixed exposure 4.08. `videos.txt` holds each video's scale range and size changes per pane, and the check of three decoded frames against the source PNGs (every pane within 0.43 %).

The C32 pane also stands for the recommended code, the cap with no catch-up ray (answer 85 of the orchestrator). At the start and the cut the two measure the same at 2/3 to the last digit, and their card logs are close: at the start a fall of 0.070 (none, job 2) against 0.085 and 0.086 (C32, job 3), and at the cut 6 changes that only rise (none) against 4 changes with a fall of 0.002, and 3 that only rise (C32). Each code plays back its own card scale log, the second run of card job 3. The start has no film of its own on the card, so it plays back the switch film's log from draw 0, the card's start window.

## Check 1: the shape (1, 8, 0) gives today's own-ray counts

The switch film (the fair lamp switch of the cabin, the lamps off at draw 193 and on at 393), at the constant render scale 2/3 (1280 × 720) through the replay path, on the Mac (`check1.txt`, `tools/ownrays.py`, logs in `logs/`):

- `xg` with no shape and with `TERTIUS_X_CATCHUP="1 8 0"`: the own-ray log equals today's (`xt`) byte for byte in all 492 draws.
- The settled diffuse own rays of the shapes (0.25, 32, 0) and (0.125, 32, 0) equal today's in each of the draws 150 to 192. The settled reflection own rays differ by at most 15 per draw (0.46 %), with a mean of +1.1. Those runs' histories differ since the start, so the reflections' young pixels below 8 draws differ a little. This is not a catch-up ray of a settled history.

![own rays per draw](pics/ownrays-switch-23.png)

`pics/ownrays-switch-23.png`: diffuse own rays per draw at the start, after off and after on. Today's line lies under the red line of (1, 8, 0), because the two are equal.

| Run, diffuse own rays | Start: peak, draws above, catch-up rays | Off: peak, draws above, catch-up rays | On: peak, draws above, catch-up rays |
|---|---|---|---|
| Today (Design X, restart at 4) | 678,200, 8, 5.41 M | 675,283, 12, 6.79 M | 670,848, 10, 6.37 M |
| (0.25, 32, 0) | 175,984, 32, 5.41 M | 176,014, 31, 5.25 M | 176,251, 31, 5.22 M |
| (0.125, 32, 0) | 92,106, 32, 2.71 M | 94,665, 31, 2.64 M | 94,515, 31, 2.63 M |

"Draws above" counts the draws above the settled count by more than 10 % of today's peak. "Catch-up rays" is the sum over 60 draws of the own rays above the settled count. (0.25, 32, 0) spreads today's total of the start over 4 times the draws, as the ticket asks. After a light change it gives 23 % fewer rays than today, because today's restart at 4 draws buys 11 catch-up draws, not 8.

**The parity check.** The catch-up rays by (column parity, draw-index parity), over each event's 60 draws, diffuse: the four counts lie within 0.34 % (start), 2.28 % (off) and 2.15 % (on) of a quarter for (0.25, 32, 0), and within 0.12 %, 2.59 % and 2.32 % for (0.125, 32, 0). The excess after a light change is between the draw parities and alike in both column parities: within each draw parity, even and odd columns differ by at most 0.33 %. So the column parity does not follow the draw parity, which is the fault of `t2-8foa`. The odd draws of the light-change window take about 2.3 % more catch-up rays than the even draws; the cause is not found. The reflections' four counts lie within 3.1 %, and their column split within a draw parity within 1.65 %. Those counts also hold the own rays of the pixels whose block ray does not fit, which change from draw to draw.

## The fair test: a start and a cut in scenes lit by bounce light

The line orchestrator's order of 2026-11-22. Two scenes lit mostly by bounce light, each with a start and a camera cut, for three codes: today (1, 8, 0, without the cap), none (0, 32, 0 with the cap: no catch-up ray) and C32 (1/32, 32, 0 with the cap). The scenes:

- bay: demo-surface's bay-camera after 420 ticks, the survey's still bay-sunbeam (the bay lit by one sunbeam and its bounce), at the fixed exposure of -4.0 stops;
- cabin: orbit's person camera after 60 ticks (the switch film's view) with the switch's lamps off in every draw, at the fixed exposure of 4.08 stops.

The runs are Mac runs at 1920 × 1080 and the constant render scale 2/3 (`tools/fairrun.sh`). The films are frozen (`scratch-frozen.patch`, `TERTIUS_X_FROZEN=1`): the world runs its ticks, and then every capture right after a capture draws once without a step of time. So the sun, the station's turn and every lamp hold still, and only the history after the reset changes from draw to draw. `tools/fairmeasure.py` measures each run with the formulas of `tools/events.py`:

- the settle: the draws from the event until the slowest region of a 3 × 3 grid stays within 5 % of its level in the film's last 20 frames;
- the boil: per draw, the mean over 24 px blocks (6 px blocks on the narrow crop) of |b[k] − (b[k−1] + b[k+1]) / 2|, in % of the crop's mean; a linear fade gives zero;
- the grain: the rms of the luminance less its 5 × 5 box mean, in % of the crop's mean.

The narrow crop is a strip 22 px (cabin) or 30 px (bay) wide on a lit wall beside an edge. `tools/fairsum.py` makes the table and the per-draw chart. `tools/fairpics.py` makes the picture sheets.

### The cut equals the start, so the spread comes from the world

In a frozen film, the draws after the cut are bit-identical to the draws after the start, in both scenes and for every code (every row of `frames.series`, and the last PNG). The cause is that FrameHistory.Begin restarts the draw index at every reset:

```csharp
DrawIndex: reset ? 0 : lastDrawIndex + 1,
```

where

- `reset` = whether this draw resets every history: the first drawn frame, a camera cut, a change of the output size, or the first draw of a still;
- `lastDrawIndex` = the draw index of the last drawn frame;
- `DrawIndex` = the draw's index since the last reset. The noise of the rays and FSR's jitter phase follow it.

So every reset in this engine draws the same noise sequence, and a cut moved by 1 or 2 draws (the order's spread) gives the same pictures. In a live world, a later cut changes only the state of the world. `tools/queue-fair2.sh` therefore measures the spread on the start with the world run 1 and 2 ticks longer (start+t1, start+t2, `FAIR_TICKS_ADD`). This spread measures how much a small change of the world moves the measures. It does not measure a change of the noise phase, which would need a scratch offset on the draw index. That was not built.

### The table

The start (the cut is identical). Settle is the slowest region's settle in draws. Boil and grain are in %. The broad crops are the floor, the wall and a third surface (the ceiling, or the bay's back wall). The grain is the mean of the floor and the wall.

| Scene | Code | Settle | Boil 1–59 | Boil 1–8 | Grain 0–15 | Narrow boil 1–59 | Narrow boil 1–8 | Narrow grain 0–15 |
|---|---|---|---|---|---|---|---|---|
| bay | today | 164 | 0.309 | 1.370 | 4.44 | 0.258 | 1.127 | 0.77 |
| bay | none | 164 | 0.372 | 1.801 | 6.80 | 0.337 | 1.808 | 1.10 |
| bay | C32 | 164 | 0.369 | 1.798 | 6.66 | 0.336 | 1.817 | 1.10 |
| cabin | today | 133 | 0.318 | 1.026 | 3.06 | 0.445 | 1.391 | 2.02 |
| cabin | none | 133 | 0.322 | 1.069 | 3.12 | 0.457 | 1.457 | 2.13 |
| cabin | C32 | 133 | 0.320 | 1.055 | 3.13 | 0.456 | 1.411 | 2.10 |

The spread over start, start+t1 and start+t2 (the largest value less the smallest):

| Scene | Code | Settle | Boil 1–59 | Boil 1–8 | Grain 0–15 | Narrow boil 1–59 | Narrow boil 1–8 | Narrow grain 0–15 |
|---|---|---|---|---|---|---|---|---|
| bay | today | 5 | 0.004 | 0.001 | 0.05 | 0.005 | 0.026 | 0.01 |
| bay | none | 5 | 0.002 | 0.015 | 0.08 | 0.006 | 0.042 | 0.01 |
| bay | C32 | 5 | 0.003 | 0.019 | 0.07 | 0.007 | 0.051 | 0.01 |
| cabin | today | 0 | 0.000 | 0.000 | 0.00 | 0.001 | 0.009 | 0.00 |
| cabin | none | 0 | 0.000 | 0.000 | 0.00 | 0.003 | 0.011 | 0.00 |
| cabin | C32 | 0 | 0.000 | 0.000 | 0.00 | 0.001 | 0.007 | 0.00 |

The split by draws (the bay start):

| Code | Boil 1–8 | Boil 9–59 | Narrow boil 1–8 | Narrow boil 9–59 | Grain 0–2 | Grain 3–8 | Grain 9–15 |
|---|---|---|---|---|---|---|---|
| today | 1.370 | 0.142 | 1.127 | 0.122 | 7.24 | 3.34 | 4.19 |
| none | 1.801 | 0.148 | 1.808 | 0.107 | 6.62 | 7.31 | 6.45 |
| C32 | 1.798 | 0.144 | 1.817 | 0.104 | 6.65 | 7.09 | 6.30 |

In the cabin, today's grain 3–8 is 3.19 against none's 3.53, and its grain 9–15 is 3.00 against 2.85.

`pics/fair-chart.png` shows the grain of draws 0 to 15 and the boil of draws 1 to 15 per draw, for the broad crops and the narrow crop. Black is today, red is none, blue is C32. `pics/fair-<scene>-draw<2 or 8>.png` show today | none | C32 at draws 2 and 8 after the start. `pics/fair-<scene>-narrow-draw<n>.png` show the narrow crop at 4 times size and a part of the floor. At draw 2 in the bay, today's bounce is smooth, and none and C32 show block-sized blotches on the floor and the right wall.

### What the fair test shows

- The settle is the same for the three codes in both scenes.
- C32 equals none within the spread, or about 1 % over it. One ray at a share of 1/32 buys nothing that this test sees.
- In the bay, today's code beats none by much more than the spread. The boil of draws 1 to 8 is 1.37 against 1.80 (−24 %), the narrow crop's boil of draws 1 to 8 is 1.13 against 1.81 (−38 %), and the grain of draws 0 to 15 is 4.44 against 6.80.
- In the cabin with its lamps off, the difference goes the same way but is small: the boil of draws 1 to 8 is 1.026 against 1.069 (−4 %). It is above the spread of the world, but the spread of the noise phase is unknown.

The line orchestrator's reading of the code: a catch-up ray gives no extra sample. It only makes the pixel's sample independent of its block mates, so any gain shows in the boil of draws 1 to 8 and on narrow surfaces. The data agrees on the boil: in the bay, all of today's boil gain is in draws 1 to 8, and draws 9 to 59 boil alike. It agrees on narrow surfaces: the relative gain on the narrow crop (−38 %) is larger than on the broad crops (−24 %). The grain goes further than the reading. Today's grain gain in the bay starts at draw 3 and lasts through draw 15, and in draws 0 to 2 today has more fine grain than none. My reading, not measured: the correlated block samples of draws 1 to 8 enter the history as blotches and stay there until the history replaces them. The sweep below contradicts this reading: the grain of a draw follows the own rays traced in that draw and the few before it.

## The gate: the cap after a light change in the bounce-lit bay

The line orchestrator's gate of the merge (2026-11-22, 23:25): does the cap show blotches after a light change in a bounce-lit scene that today's cliff does not show? It passed (the line orchestrator, 23:32).

- The scene and the event: bay-sunbeam's view and state (demo-surface, bay-camera, 420 ticks), frozen, at the constant render scale 2/3 and the fixed exposure of -4.0 stops. The sun goes off at draw 200 and on again at draw 400 of a 600-draw film (`tools/fairsun.sh`). The bay start settles by draw 164, so each event starts on a settled picture, with 200 draws between the events. The probe could turn the sun off only for a whole run, so `scratch-sun-switch.patch` adds `TERTIUS_X_SUN_OFF=<from>-<to>` on the probe's count of draws. `frames.series` confirms the switch at draws 200 and 400.
- The codes: the cap is the A build of this record: X5, the tree of the line's merge `710834cc0`, with the acceptance record's `scratch-light-evidence-cap.patch` (build `xby`). It is not the final code `05a27262d`. Today's cliff is `xg` with the shape (1, 8, 0) (build `xgy`). Both builds carry the first-filmed, frozen and sun-switch patches.
- The own-ray logs confirm the codes. Today traces catch-up rays in draws 4 to 13 after each light change, since the light evidence arrives at draw 4. Its peak is 33 % of the pixels after off and 50 % after on, and its total is 2.5 and 4.7 rays per pixel. The cap traces none.

| Event | Code | Settle | Boil 1–8 | Grain 1–15 | Narrow boil 1–8 | Narrow grain 1–15 | Catch-up rays per pixel |
|---|---|---|---|---|---|---|---|
| sun off | today | 178 | 0.783 | 2.58 | 0.813 | 0.81 | 2.46 |
| sun off | cap | 178 | 0.600 | 2.41 | 0.586 | 0.53 | 0.00 |
| sun on | today | 161 | 11.860 | 7.72 | 19.319 | 1.30 | 4.65 |
| sun on | cap | 162 | 10.744 | 9.29 | 17.833 | 1.51 | 0.00 |

The cap's cost: 20 % more grain for about 12 draws after the sun comes on (grain 1–15: 9.29 against 7.72 %, narrow 1.51 against 1.30 %), coarse and block-shaped on the right wall (`pics/gate-sun-zoom.png`, the floor and the right wall at full size at draws 4 and 8 after the sun comes on). Its boil is not higher. After the sun goes off, the cap is cleaner on every measure, since today's catch-up rays add boil there. `pics/gate-sun-strip.png` shows draws 2 and 8 after each event (cap above, today below). `pics/gate-sun-chart.png` shows the measures per draw. The settle is the same. The boil after the sun comes on is high for both codes because the bounce fades in over several draws, and a fade that is not linear over three draws counts as boil. The tools: `tools/fairsun.sh`, `tools/queue-sun.sh`, `tools/gatesum.py`.

A reading of the line orchestrator, not measured as a rule: the catch-up rays helped where the new light is brighter and rarer than the old (the start, the sun coming on), and hurt where it is darker (the sun going off). A ray buys where the samples' variance rises. The logs of this record hold no cheap number for the samples' variance per region: the region log holds region means, and the own-ray logs hold counts.

### The fault that both codes share

After the sun comes on, both codes show a soft cloud on the floor and speckle on the upper right wall in the first draws. That is not the cap's fault. It belongs with the start's blotches (the fair test): the first draws of a bounce-lit room after any large change. The design chosen for the catch-up rays must improve it, not only hold it. No shape of the sweep below makes the start better than today's cliff.

## The sweep: what a catch-up ray buys at which draw

The line orchestrator's question (2026-11-22, 23:20): can the catch-up rays be spread over more draws and keep the picture? The bay's start as in the fair test, for shapes (peak, end, fall) with the cap and on the reset's clock only (the shape's fourth value 0). Each run also writes the own-ray log (`tools/queue-fair3.sh`, `tools/queue-fair4.sh`, `tools/queue-fair5.sh`, `tools/sweep.py`).

The first run of c3 (1, 3, 0) gave today's cliff bit for bit. On `scratch-catch-up-share.patch`, a pixel that is no longer young falls back to today's rule: an own ray in every draw while its history is under 8 draws. So a shape whose end is under 8 changed nothing at a start. `scratch-catch-up-short.patch` keeps a reset pixel young until max(end, 8) draws after the reset, with the shape's share of 0 after its end (build `xkz`). h16 on `xkz` gives h16's pictures on `xcz` bit for bit, since a shape whose end is 8 or more is unchanged.

The catch-up rays per pixel are the own diffuse rays above the settled level, summed over draws 0 to 59, divided by the render size's pixels. About 28 % of the pixels never trace an own ray at the start, so today's total is 5.8 and not 8.

| Shape | Peak share of pixels | Catch-up rays per pixel | Boil 1–8 | Grain 3–8 | Grain 9–15 | Narrow boil 1–8 | Narrow grain 3–8 | Narrow grain 9–15 |
|---|---|---|---|---|---|---|---|---|
| today (1, 8, 0) | 72 % | 5.79 | 1.370 | 3.34 | 4.19 | 1.127 | 0.61 | 0.74 |
| h16 (0.5, 16, 0) | 36 % | 5.78 | 1.471 | 4.62 | 4.57 | 1.360 | 0.82 | 0.73 |
| f32 (0.5, 32, 1) | 36 % | 5.97 | 1.494 | 5.09 | 5.13 | 1.397 | 0.93 | 0.81 |
| q32 (0.25, 32, 0) | 19 % | 5.78 | 1.622 | 5.89 | 5.47 | 1.635 | 1.05 | 0.86 |
| e32 (0.125, 32, 0) | 10 % | 2.89 | 1.716 | 6.62 | 5.94 | 1.705 | 1.12 | 0.92 |
| c3 (1, 3, 0) | 72 % | 2.18 | 1.638 | 7.18 | 6.28 | 1.316 | 1.04 | 0.88 |
| none (0, 32, 0) | 0 | 0 | 1.801 | 7.31 | 6.45 | 1.808 | 1.12 | 0.96 |

What one catch-up ray per pixel buys against none (the drop of the measure divided by the rays per pixel):

| Shape | Grain 3–8 per ray | Boil 1–8 per ray |
|---|---|---|
| today | 0.69 | 0.074 |
| h16 | 0.47 | 0.057 |
| f32 | 0.37 | 0.051 |
| q32 | 0.25 | 0.031 |
| e32 | 0.24 | 0.029 |
| c3 | 0.02 | 0.075 |

`pics/sweep-strip.png` shows the floor and the right wall at draws 2 and 8, today | none | h16 | q32 | e32 | f32 | c3. `pics/sweep-chart.png` shows the measures per draw and the share of pixels that trace an own diffuse ray in each draw.

What the sweep shows:

- No shape spreads the same rays over more draws and keeps the picture. At today's total, half the peak (h16) keeps about two thirds of today's grain gain and three quarters of its boil gain. A quarter of the peak (q32) keeps about a third.
- The grain of a draw follows the share of own rays traced in that draw and the few draws before it. c3's grain jumps to none's at draw 3, the draw its rays stop. Today's grain rises from 3.3 toward 4.2 after its rays stop at draw 8. So a ray buys grain in the draws around the one it is traced in. This contradicts my earlier reading (the fair test) that the early blotches stay in the history, which predicted that an early ray buys more than a late one.
- The boil of draws 1 to 8 differs. c3's rays in draws 0 to 2 buy it at today's rate per ray, and on the narrow crop they buy 72 % of today's gain.

## Two designs for the cost of the catch-up rays

The line orchestrator chooses between them:

1. The spread: a budget in rays over time. The catch-up rays of a reset or a light change are spread over more draws at a lower peak, so no draw is dear. The sweep shows the price: the picture loses a third of today's gain at half the peak and two thirds at a quarter.
2. A known dip: a budget in pixels. The renderer tells the controller which draws trace the catch-up rays, the controller gives those draws a lower render scale, and it returns to the rest at once after them. The picture stays today's.

The forms that the line orchestrator compares (2026-11-22, 23:32):

- (a) the short full-rate shape c3 with the controller as it is: it fails on the picture (grain 3–8 7.18 against none's 7.31). No card log exists for it.
- (b) today's 8 draws with a known dip: the estimate below.
- (c) both together: it fails on the picture, as (a).

The card's slow return today (`tools/cardreturn.py` on the logs of jobs 1 to 3): the draws from the event to the last draw at which today's cliff holds the render scale more than 0.02 under its rest. The rest is the median of the settled draws before the event, or the median of the last 40 draws after it. Both give about the same counts: the start 60 to 63, the lamps off 49 to 54, the lamps on 49 to 52, and the cut 41 to 48 (job 1's old exterior cut 36 to 48). After off, on and the cut, "none", C32 and the cap give 0 draws under. At the start, "none" gives 50 and C32 with the cap 53 to 54: the start dips about 50 draws without any catch-up ray.

The estimate of (b), not a measure (`tools/knowndip.py` on today's logs of jobs 2 and 3). The dear draws are the draws that trace catch-up rays in the Mac's own-ray logs of the same code and films: the start 0 to 7, the lamps off 197 to 206, on 397 to 406, and the cut 133 to 141 (the frame on the chase camera at 133 is a cut too). A dear draw d gets min(rest, scale[d] · sqrt(16.67 ms / span[d])), the scale at which its logged span would fit the target if the cost grew with the pixels. Every other draw keeps the rest.

| Event | Logged: draws under the rest − 0.02 | Logged: lowest scale | Estimate (b): draws under | Estimate (b): lowest scale | Picture (Mac) |
|---|---|---|---|---|---|
| start | 59 to 62 | 0.762 to 0.768 | 8 | 0.779 to 0.789 | today's |
| lamps off | 46 to 49 | 0.828 to 0.839 | 10 | 0.759 to 0.786 | today's |
| lamps on | 46 to 47 | 0.825 to 0.838 | 10 | 0.760 to 0.789 | today's |
| cut | 41 to 44 | 0.841 to 0.853 | 8 | 0.752 to 0.777 | today's |

The estimate leaves out the part of a draw's cost that does not grow with the pixels, so the real dip must be deeper. It also leaves out the start's own dip of about 50 draws, FSR's history across two sudden changes of the scale, and the controller's dead band. So (b) trades about 40 to 50 draws at about 0.83 to 0.85 for 8 to 10 draws at about 0.75 to 0.79.

## Files

- `scratch-ownray-log.patch`, `scratch-catch-up-share.patch`, `scratch-history-dump.patch`: the scratch patches. The last writes one draw's slow diffuse history length for the map.
- `check1.txt`: the output of `tools/ownrays.py` for check 1. `historymap.txt`: the output of `tools/histmap.py`.
- `cut-exterior.json`: job 1's cut film, from 100 frames on the bright chase camera into the cabin; after it the cabin stays white for over 100 draws (`t58-rcd6`, `pics/cut-exterior-white.png`).
- `cut.json`: the cut film, the cabin's person camera (the switch's view) for a still and 100 frames, one frame on the chase camera, then the person camera from draw 134 for 200 frames. At draw 134 the draw index restarts at 0 and the diffuse history mean falls from 25.6 to 0.98 draws. `start.json`: the cabin's person camera filmed from draw 0 for 200 frames.
- `logs/`: the own-ray logs, and the card's scale and time logs per job (`logs/card-<job>/<film>-<code>/`).
- `cardscale-<job>.txt`: the output of `tools/cardscale.py` per card job.
- `tools/` (each script says what it does in its first lines): `builds.sh`, `ownrays.py`, `histmap.py`, `card.sh` and `cardrun.sh` (the card's job), `cardscale.py` (the card's scale per event), `threepane.py` (the previews), `macrun.sh` (a Mac run of an event), `queue-mac2.sh`, `queue-mac3.sh`, `queue-mac5.sh` and `queue-mac6.sh` (the Mac's queues), `compact.sh` (packs and measures the finished runs), `events.py` (the settle, boil and grain per event), `spans.py` (the Mac's GPU spans), `videos.py` (the final videos).
- `videos.txt`: the final videos' scale ranges and frame checks. `videos-events.txt`: the measures of the video runs under the card's scale.
- Commit `6b1e3deb9`, tagged `t0-3vwx`, also added `scratch-no-region-evidence.patch` and changed `tools/builds.sh` here, for the region take of `t0-qvm1`.
- `scratch-frozen.patch`: the frozen films of the fair test (`TERTIUS_X_FROZEN=1`). `tools/fairrun.sh` (a Mac run of the fair test), `tools/fairmeasure.py` (its measures), `tools/queue-fair.sh` (the bay's start and cut, stopped before its cut+1 and cut+2 runs) and `tools/queue-fair2.sh` (the cabin, the spread runs, then `t0-qvm1`'s region log), `tools/fairsum.py` (the table and `pics/fair-chart.png`), `tools/fairpics.py` (`pics/fair-*.png`).
- `scratch-sun-switch.patch` (the gate's sun switch), `scratch-catch-up-short.patch` (a shape that ends before 8 draws keeps its share). `tools/fairsun.sh`, `tools/queue-sun.sh` and `tools/gatesum.py` (the gate, `pics/gate-sun-*.png`); `tools/queue-fair3.sh`, `tools/queue-fair4.sh`, `tools/queue-fair5.sh` and `tools/sweep.py` (the sweep, `pics/sweep-*.png`); `tools/cardreturn.py` (the card's slow return) and `tools/knowndip.py` (the estimate of form (b)).
- The cloud spend of the three card jobs: $0.0542 + $0.0846 + $0.0898 = $0.2286, of the $1.50 allowed.
