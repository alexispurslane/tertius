---
id: t0-xdck
status: open
deps: []
links: []
created: 2026-11-21T17:20:59Z
type: task
priority: 1
assignee: Alexis Purslane
parent: t7-3e22
---
# The cost of the region's drift evidence in the bay film: frame span and trail of a cut to 4 draws with own rays against cuts that keep the shared rays

Before the remedy's design (t0-acxm): regional light evidence lasts as long as the light moves, and a history at the fast cap makes every pixel trace its own rays. The experiment measures that cost and two cheaper actions.

## Design

Written by the line orchestrator, 2026-11-21. An experiment with the scratch probe of t0-ion0, for the design of the remedy (t0-acxm). It changes nothing under src, tests or tools. It uses only the bay, which holds no shadow leak, so it does not wait for the shadow fix.

### Why

The drift experiment (t0-ion0) showed that the region's drift, as a second source of light evidence, closes the bay film's trail. It measured the image only. The model of its cost was not measured:

- Regional evidence gives a block LightEvidence. The one reader then sets the bounce history's length L to the fast cap, 4 draws.
- The ray plan gives a pixel its own ray while L is below FullRateFrames, 8 (screen_trace_plan.comp, historyLengthOf(r, kind) >= FullRateFrames).
- Light evidence of a ray lasts 7 draws after a change. Regional evidence lasts as long as the region's light moves: in rect-bay 71 to 84 % of the block draws of the whole film.
- So while a sunbeam moves, nearly every pixel of the region traces its own rays in every draw. A slow sunbeam is a common state of a rotating station, not a rare event. On the deck's camera cuts, where every pixel traces its own rays, the mean frame span was 21.9 ms.

So the remedy's action needs a measured choice between image and frame span before its design.

### The variants

All on the probe build of t0-ion0 (the nearest-half store on, the fix's action for the rays' own light evidence), rule Q3 (the drift at 3 standard deviations), in the film rect-bay under today's jitter:

| Name | Regional evidence sets L to | Own rays |
|---|---|---|
| M | main, no probe | main's rule |
| X | no regional evidence (the fix alone) | below 8 draws |
| D4 | the fast cap, 4 draws (as t0-ion0 measured) | below 8 draws, so in every evidence draw |
| D8 | FullRateFrames, 8 draws, the shortest length that keeps the block's shared ray | none from regional evidence |
| D4s | the fast cap, 4 draws | none from regional evidence: a scratch switch that makes the plan give own rays only below the fast cap in this film |
| C4 | every bounce history held at 4 draws, the control | below 8 draws |

D8 and D4s need one new scratch switch each. Light evidence of a ray keeps its cut to the fast cap in every variant. If the code shows a better way to switch the own rays off for D4s, take it and record it.

### The measures, per variant

1. The GPU frame span over the film, on the Mac under the exclusive GPU lock at 1920 x 1080 with two frames in flight, as tools/demo-timing.sh takes it: the median and the highest span, and the span of the ray passes. If the tool gives the span per frame, also a chart of the span over the film's frames.
2. The share of the pixels that trace their own ray, per frame, if the probe can count it cheaply. If not, say so.
3. The film's level and blotch at the middle and the last frame, and the lag film bay-sun's median lag error.
4. One sheet of film frames at the middle and the last frame: the still of the same state, M, D4, D8, D4s. The eye judges first, by the user's bar.
5. The bay still (bay-sunbeam) under D4, D8 and D4s: specks, blotch and daub, with one crop sheet. A still has false evidence in about 1 % of its block draws under Q3, so this shows what each action costs a still.

### The questions

1. How much frame span does D4 cost during the film against M and X?
2. Does D8 or D4s keep the film no worse than main, by the eye and by the level, at a frame span near X's?
3. Which action does the remedy take? Give the measured numbers apart from your reading.

## Acceptance Criteria

- The control: with the new switches at their defaults, the build repeats t0-ion0's Q3 film numbers of rect-bay (+0.47 / +0.23) within the run-to-run difference.
- A record in docs/research/<this ticket>-drift-cut-cost/: README.md with the three answers, what the pictures show first, the tables, the scratch patch and the scripts. Reuse the scripts of t0-ion0 and copy only what changes.
- Each picture opened with the Read tool and described by the user's bar: no blotches, daubs, bright specks, trail or flicker.
- Timing takes on the Mac under the exclusive GPU lock, at most about 20 minutes per take, each side alone, in turn (for example M D4 D8, D8 D4 M). Give the spread of one side's runs beside each difference.
- No change under src, tests or tools.


## Notes

**2026-11-21T19:30:42Z**

Step note (ticket lead, 2026-11-21): the build stage is done. The record is docs/research/t0-xdck-drift-cut-cost/README.md.

The control passes: D4 repeats t0-ion0's Q3 film, +0.47 / +0.23, blotch 0.122 / 0.148.

Measured, film rect-bay at 1920 x 1080, two frames in flight, four timing rounds on the Mac under the exclusive GPU lock, each side alone, every clock 1292 to 1296 MHz. Median frame span in ms, spread of the four run medians in brackets:

| Side | Frame span | plan..rays | Level mid / last | Specks mid / last | bay-sun lag error |
|---|---|---|---|---|---|
| M | 16.05 (0.12) | 4.57 | +1.17 / +0.54 | 0.18 / 1.28 | 0.267 |
| X | 16.55 (0.10) | 4.80 | +1.86 / +1.91 | 0.16 / 0.10 | 0.281 |
| Xt | 28.25 (0.27) | 4.80 | not taken | not taken | not taken |
| D4 | 33.87 (0.72) | 10.24 | +0.47 / +0.23 | 0.22 / 0.18 | 0.102 |
| D8 | 28.10 (0.58) | 4.63 | +0.86 / +0.31 | 0.20 / 0.63 | 0.131 |
| D4s | 27.66 (0.46) | 4.18 | +0.47 / +0.22 | 0.44 / 3.61 | 0.091 |
| C4 | 20.37 (0.38) | 8.53 | +0.43 / +0.18 | 0.21 / 0.29 | 0.090 |

Xt is X with the drift test computed but not acting. It shows that the scratch drift test itself costs +11.7 ms over X: the probe computes the region's light at every render pixel. D4's action costs +5.6 ms over Xt, nearly all in the ray passes. Its own rays reach 54 % of the render pixels over the film. D8's action costs -0.15 ms against Xt, and D4s's -0.59 ms. In the quiet rounds 2 to 4, each side's spread is at most 0.12 ms.

D4s uses TERTIUS_X_FULL_RATE=4, as the ticket's design says. It lowers the own-ray threshold for every pixel, so its own-ray share is 0.2 %, below X's 6 to 7 %. Its film and still show about three times main's bright specks.

Reading: D8 is the action for the remedy. It is better than main in every measure and by the eye, and its action costs no measurable frame span. The remedy's design (t0-acxm) must build the test per block and measure its cost, because the scratch test's 11.7 ms is a per-pixel implementation.
