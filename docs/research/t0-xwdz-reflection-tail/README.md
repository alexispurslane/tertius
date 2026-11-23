# Why reflection pixels keep tracing their own ray after a reset (t0-xwdz, step 1)

Step 1 of ticket t0-xwdz measures only. It holds no design and no fix.

## The question

After a reset, 23 to 50 % of the pixels still traced their own reflection ray in draw 8, and the tail lasted to about draw
12 (the first finding: t0-gumm's own-ray logs, on X5's code). The inferred cause was this: the anti-lag of
`accumulate_spatial.comp` cuts a young, noisy reflection history back to the fast cap of 4 in draws 5 to 7, because the
reflections' narrow box takes no retained widening.

The ray plan (`screen_trace_plan.comp`) gives a pixel its own reflection ray when its reprojected history length is under
`FullRateFrames` (8), or when the block's ray does not fit the pixel's lobe:

```glsl
            if (historyLengthOf(r, kind) >= FullRateFrames && fits(p, c, kind))
                continue;
```

where

- `historyLengthOf(r, kind)` = the slow history's length at the pixel's last place: the weighted mean of the lengths of the
  four bilinear taps that hold weight, `Σ w · L / Σ w` (`HISTORY_READER` in `signals.glsl`).
- `FullRateFrames` = 8 draws.
- `fits(p, c, kind)` = whether the block's chosen ray lies inside the pixel's reflection lobe.

The anti-lag in the spatial dispatch shortens the length by the clamp's move:

```glsl
        float antiLagged = max(fastCap, historyLength * (1.0 - min(1.0, lag / parameters.antiLagShare)));
        historyLength = evidence == LightEvidence || evidence == RegionEvidence ? min(antiLagged, FullRateFrames) : antiLagged;
```

where

- `historyLength` = the length after the temporal blend, in draws. It is the reprojected length plus 1 after a reset.
- `fastCap` = 4 draws for the reflections (`SignalParameters.Specular`). The anti-lag runs only where `historyLength >
  fastCap`, so from draw 4 on.
- `lag` = the luminance of the clamp's move of the slow history into the box of the 3 × 3 fast histories, divided by the
  luminance of the box's mean. The reflections' box is the mean ± 1.5 σ (`ClampSigmas`), with no retained widening.
- `parameters.antiLagShare` = 0.25 for the reflections: a lag of a quarter of the mean cuts the length to the fast cap.

The default specular history (`SpecularHistoryMode.PreciseUncapped`) has no cap and stores 32-bit floats (RGBA32F). So a
length does not round to a half float, and a length of 7.9995 stays under 8.

## The take

- Code: the line's head c768e1ed8 with its survey probe patch, t0-gumm's `scratch-first-filmed.patch` and
  `scratch-frozen.patch`, and this record's `scratch-tail-log.patch` (`tools/build.sh`). The tail log reads the specular
  accumulation trace (`TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace`, `TemporalSignal.TraceLayers`) and the own-ray
  lists after every draw. The patch's comment defines each count.
- Scenes and events: t0-gumm's fair test (`tools/tailrun.sh`), frozen films at 1920 × 1080 and the headless render scale
  2/3 (1280 × 720). The cabin is orbit's person camera with the switch's lamps off. The bay is demo-surface's bay camera
  lit by one sunbeam. The start films 120 draws. The cut films 100 draws, one draw on another camera and 120 draws after
  the cut back.
- In a frozen world, the start and the cut back give the same log draw for draw (`cmp` of the two logs from the reset on).
  The cut back restarts every history from the same state as the start. So each table below holds for both events.
- The runs time nothing. They held the GPU lock in its shared mode.

Every share is a percent of the surface pixels of the draw: 904,500 in the cabin and about 890,000 in the bay. Only 3 of 4
pixels can trace an own ray, because one pixel of each 2 × 2 block traces the block's ray. So 75.0 % means every pixel
that can.

## The per-draw table

The columns, made by `tools/tailtable.py` from `logs/v2-*/tail.txt`:

- "under 8 at the plan" = the reprojected length is under 8, as the plan of the draw reads it.
- "cut" = the anti-lag shortened the length in this draw by more than 1e-3 draws. "cut young" = of them, from a length
  under 8. "cut to 4" = the cut ended at the fast cap of 4 or less.
- "behind" = the length after this draw is under the draw index + 1 − 0.01. Some cut or loss happened since the reset.
  It is meaningful only in the first draws: every length falls behind sooner or later.
- "own" = the reflection own rays of the draw.

### Cabin

| draw | under 8 at the plan | cut | cut young | cut to 4 | behind | own | own count |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 4 | 100.0 | 6.2 | 6.2 | 3.2 | 6.1 | 75.0 | 678,169 |
| 5 | 100.0 | 5.0 | 5.0 | 1.1 | 9.7 | 75.0 | 678,169 |
| 6 | 100.0 | 4.6 | 4.6 | 0.4 | 12.7 | 75.0 | 678,114 |
| 7 | 100.0 | 4.8 | 2.1 | 0.2 | 15.6 | 75.0 | 678,200 |
| 8 | 28.2 | 5.5 | 1.1 | 0.1 | 18.7 | 19.5 | 176,403 |
| 9 | 9.1 | 7.7 | 0.9 | 0.1 | 22.9 | 6.8 | 61,818 |
| 10 | 6.5 | 9.9 | 0.7 | 0.2 | 28.3 | 5.2 | 46,803 |
| 11 | 5.7 | 11.1 | 0.6 | 0.2 | 33.9 | 4.5 | 41,044 |
| 12 | 5.2 | 11.7 | 0.4 | 0.2 | 39.4 | 4.1 | 36,855 |

### Bay

| draw | under 8 at the plan | cut | cut young | cut to 4 | behind | own | own count |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 4 | 100.0 | 11.7 | 11.7 | 9.5 | 11.7 | 75.0 | 667,485 |
| 5 | 100.0 | 7.1 | 7.1 | 3.8 | 15.2 | 75.0 | 667,486 |
| 6 | 100.0 | 6.3 | 6.3 | 2.3 | 18.1 | 75.0 | 667,484 |
| 7 | 100.0 | 6.5 | 3.8 | 1.9 | 21.2 | 75.0 | 667,484 |
| 8 | 36.2 | 7.6 | 3.0 | 1.8 | 24.4 | 25.3 | 224,864 |
| 9 | 14.4 | 9.4 | 2.8 | 2.1 | 28.3 | 10.8 | 95,915 |
| 10 | 11.0 | 10.7 | 2.7 | 2.2 | 32.4 | 8.4 | 74,929 |
| 11 | 9.8 | 11.6 | 2.6 | 2.2 | 36.6 | 7.5 | 67,011 |
| 12 | 9.1 | 11.9 | 2.4 | 2.2 | 40.6 | 7.0 | 62,369 |

The settled state, at draw 118 of the cabin: 0.5 % of the lengths under 8, 7.9 % cut per draw, and 5,347 own rays.

### Why each own ray of draw 8 saw a length under 8

The log sorts each own ray by the trace's layer 0 (the length that the last draw left at the pixel's own texel) and layer
3 (the least length of the weighted taps):

| scene | own rays | own texel under 8 | own texel 8 or more, a tap under 8 | every tap 8 or more, the mean rounds under 8 | reprojected 8 or more: the lobe fit |
|---|---:|---:|---:|---:|---:|
| cabin | 176,403 | 167,078 | 5,939 | 20 | 3,366 |
| bay | 224,864 | 216,288 | 5,756 | 0 | 2,820 |

## The verdict on the inferred cause

The inferred cause is partly confirmed.

1. Confirmed: the anti-lag cuts young reflection histories in draws 4 to 7. It cuts 5 to 6 % of the cabin's pixels per draw
   and 6 to 12 % of the bay's. At draw 7, 15.6 % of the cabin and 21.2 % of the bay are behind schedule by more than 0.01
   draws. Each of them traces its own ray in draw 8.
2. Refuted in part: most cuts do not reach the fast cap of 4. A cut to 4 or less takes 3.2 % of the cabin's pixels at draw
   4 and 1.1 % or less after it. In the bay it takes 9.5 % at draw 4 and 2 to 4 % after it. Most cuts are partial. A pixel
   that loses a fraction of a draw reaches 7.x at draw 7 and 8.x at draw 8, so it traces its own ray in draw 8 only. This
   is why the own rays fall from 19.5 % at draw 8 to 6.8 % at draw 9 in the cabin.
3. Not in the brief: at draw 7, the length after the anti-lag lies in [7.999, 8) at 82,525 of the cabin's pixels (9.1 %,
   from the first log `logs/v1-cabin-start`: 226,765 under 8 against 144,240 under 7.999). The 32-bit history keeps such
   a length under 8, so the plan's test `>= FullRateFrames` fails. These pixels give about a third of draw 8's own rays in
   the cabin. Two causes are ruled out:
   - The rounding of the bilinear mean in draw 8: where every tap holds 8 or more, the mean falls under 8 at only 20
     pixels.
   - Tiny cuts from 8 in draw 7: draw 7 has 24,401 lengths that a cut took from 8 or more to under 8 (after under 8
     minus before under 8, because a cut never raises a length), against 24,150 cuts by more than 1e-3. So only about 250
     cuts in draw 7 were smaller than 1e-3.

   So these lengths were already under 8 before draw 7's anti-lag: cuts smaller than 1e-3 in draws 4 to 6, or rounding
   in the reprojection or the blend of those draws. The v3 log tells these apart (see "The open step").
4. The tail after draw 9 has a different cause. The young cuts stop (under 1 % per draw in the cabin), but the anti-lag
   cuts 8 to 12 % of the pixels per draw, most with a length of 8 or more. The ones that fall under 8 trace their own ray
   again. In the bay, 2.2 % of the pixels per draw are cut to 4 or less, also late in the tail.

## Two follow-up questions from the v2 logs

### (a) Draw 8's own rays from a length of 7.5 or more at draw 7

The v2 log does not split the lengths at 7.5, so the logs give bounds only. In the cabin, 167,078 of draw 8's 176,403 own
rays have an own length under 8, from the 226,765 surface pixels under 8 at draw 7. That is 0.737 own rays per such pixel,
close to the 3 of 4 pixels that can trace one. With that ratio, the own rays come from these lengths at draw 7 (the
histogram `logs/v2-cabin-cut/hist.txt` and the v1 counts):

| length at draw 7 | surface pixels | own rays in draw 8, about |
|---|---:|---:|
| [7.999, 8) | 82,525 | 60,800 |
| [7.99, 7.999) | about 3,000 | 2,200 |
| [7, 7.99) | about 80,800 | 59,600 |
| under 7 | 60,394 | 44,500 |
| a tap under 8, own 8 or more | | 5,939 |
| every tap 8 or more, the mean rounds under 8 | | 20 |
| 8 or more, the lobe fit | | 3,366 |

So a test of "length >= 7.5", or of a length rounded to the nearest draw, saves at least about 63,000 of the 176,403 own
rays (36 %, the lengths of 7.99 or more) and at most about 122,000 (70 %, all lengths of 7 or more). The v3 log counts the
split at 7.5.

### (b) The cuts of the tail: how many end under 8

A cut never raises a length. So the cuts that take a length from 8 or more to under 8 are "after under 8" minus "before
under 8", an exact count. "Of all cuts" adds the young cuts, which start under 8 and so end under 8.

| draw | cabin: cuts | of them from 8 or more | of those, ending under 8 | of all cuts, ending under 8 | bay: of all cuts, ending under 8 |
|---:|---:|---:|---:|---:|---:|
| 8 | 49,684 | 39,611 | 21,556 (54.4 %) | 63.7 % | 78.5 % |
| 9 | 69,832 | 62,005 | 23,371 (37.7 %) | 44.7 % | 63.7 % |
| 10 | 89,752 | 83,445 | 23,759 (28.5 %) | 33.5 % | 53.5 % |
| 11 | 100,405 | 95,300 | 20,575 (21.6 %) | 25.6 % | 45.2 % |
| 12 | 105,890 | 101,983 | 17,056 (16.7 %) | 19.8 % | 39.8 % |

The v2 log has no histogram of the lengths that the cuts start from. It gives these bounds: in draw d, a cut from 8 or
more starts from a length in [8, d + 1], because no length exceeds d + 1. The histogram of all lengths after the anti-lag
(`hist.txt`) shows that the lengths of 8 or more lie in the two top bins: at draw 9 of the cabin, 229,660 in [8, 9) and
560,820 in [9, 10). The v3 log writes the histogram of each cut's length before and after it (`cuts.txt`).

## The open step: the v3 log

`scratch-tail-log.patch` (v3) is written and applies, but it is not built yet: the disk was under the line's limit of 22
GiB free for a build (19.9 GiB at 12:35). It adds:

- the lengths before the anti-lag in [7.99, 8), and the lengths that a cut took from 8 or more into [7.99, 8);
- the cuts by any amount, beside the cuts by more than 1e-3;
- the reprojected lengths below the own texel's length by any amount;
- the own rays whose own length lies in [7.99, 8), in [7.5, 7.99) and under 7.99;
- the surface pixels with a length in [7.5, 8);
- `cuts.txt`: the histogram, in steps of 1 draw, of each cut's length before it and after it.

It splits point 3 into tiny cuts and rounding, answers question (a) at 7.5, and gives question (b)'s histogram of the
starting lengths.

## Other findings

- The block's ray does not fit the lobe of 1,700 to 3,400 own-ray pixels per draw whose length is 8 or more. This is the
  settled share of own rays.
- The diffuse own rays fall to 0.3 % of the pixels at draw 8 (2,307 in the cabin). The diffuse light has no part in this
  tail.
- The first finding gave 50 % own rays at draw 8 after a start. That start was t0-gumm's live start on X5's code. On the
  line's head, the frozen start and the frozen cut give the same 26 % of the pixels that can trace one (cabin).

## Repeating the take

```powershell
& { $env:PATCH = 'scratch-tail-log-v2.patch'; bash docs/research/t0-xwdz-reflection-tail/tools/build.sh }
bash -c 'for s in cabin bay; do for e in start cut; do bash docs/research/t0-xwdz-reflection-tail/tools/tailrun.sh $s $e $s-$e; done; done'
python3 docs/research/t0-xwdz-reflection-tail/tools/tailtable.py /private/tmp/t0-xwdz/runs/cabin-cut 4 12
```

Without `PATCH`, the build takes the v3 patch. The build needs 22 GiB of free disk. Each run takes about 1 to 1.5 minutes
and writes only small logs: `tail.txt`, `hist.txt` and `frames.series` (v3 adds `cuts.txt`). The v2 runs had no
`TERTIUS_PROBE_SERIES`, so their PNGs were written and deleted after the run. `tailrun.sh` now sets it, so the keep list
`none` writes no PNG.

`logs/v1-cabin-start` comes from the first version of the log, which is not kept as a patch. Its columns after "<frame>
<draw index> <width> <height>" are: surface pixels, reprojected under 8, before the anti-lag under 8, after it under 8,
the clamp ran, cut, cut from under 8, cut from 8 or more, cut to 4 or less, reprojected below own by more than 1e-3, after
under draw index + 1 − 0.001, own reflection rays, of them reprojected 8 or more, own diffuse rays. Its numbers equal the
v2 cabin start where both count the same thing.
