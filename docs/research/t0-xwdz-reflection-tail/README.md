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

The temporal dispatch reprojects the length the same way, then adds 1 in the blend (`historyLength + 1.0`). The spatial
dispatch's anti-lag then shortens the length by the clamp's move:

```glsl
        float antiLagged = max(fastCap, historyLength * (1.0 - min(1.0, lag / parameters.antiLagShare)));
        historyLength = evidence == LightEvidence || evidence == RegionEvidence ? min(antiLagged, FullRateFrames) : antiLagged;
```

where

- `historyLength` = the length after the temporal blend, in draws. It is the reprojected length plus 1.
- `fastCap` = 4 draws for the reflections (`SignalParameters.Specular`). The anti-lag runs only where `historyLength >
  fastCap`, so from draw 4 on.
- `lag` = the luminance of the clamp's move of the slow history into the box of the 3 × 3 fast histories, divided by the
  luminance of the box's mean. The reflections' box is the mean ± 1.5 σ (`ClampSigmas`), with no retained widening.
- `parameters.antiLagShare` = 0.25 for the reflections: a lag of a quarter of the mean cuts the length to the fast cap.

The default specular history (`SpecularHistoryMode.PreciseUncapped`) has no cap and stores 32-bit floats (RGBA32F), and
`slowHistoryValue` returns the value unchanged. So a length of 7.9999 stays under 8 from draw to draw.

## The take

- Code: the line's head c768e1ed8 with its survey probe patch, t0-gumm's `scratch-first-filmed.patch` and
  `scratch-frozen.patch`, and this record's tail log (`tools/build.sh`). The tail log reads the specular accumulation
  trace (`TERTIUS_REFLECTION_HISTORY_AGREEMENT=control-trace`, `TemporalSignal.TraceLayers`) and the own-ray lists after
  every draw. `scratch-tail-log.patch` is its last version (v3 with the v4 weights log). `scratch-tail-log-v2.patch` made
  the v2 logs. The comment in each patch defines each count.
- Scenes and events: t0-gumm's fair test (`tools/tailrun.sh`), frozen films at 1920 × 1080 and the headless render scale
  2/3 (1280 × 720). The cabin is orbit's person camera with the switch's lamps off. The bay is demo-surface's bay camera
  lit by one sunbeam. The start films 120 draws. The cut films 100 draws, one draw on another camera and 120 draws after
  the cut back.
- In a frozen world, the start and the cut back give the same log draw for draw from draw 1 on (`cmp` of the two logs).
  The cut back restarts every history from the same state as the start. So each table below holds for both events. The
  only difference is at draw 0: after a cut, the own texel still holds the other camera's history.
- Each version repeats the counts of the version before it exactly, and the v3 cut takes ran twice with the same log. The
  runs time nothing. They held the GPU lock in its shared mode.

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

## Draw 8's own rays by the length after draw 7

The v3 log sorts each own ray of draw 8 by the length that its own texel held after draw 7 (trace layer 0), and by the
least length of its weighted taps (trace layer 3). `tools/v3split.py` prints this table and the two below.

| own rays of draw 8 | cabin | share | bay | share |
|---|---:|---:|---:|---:|
| own texel in [7.99, 8) | 66,035 | 37.4 % | 76,773 | 34.1 % |
| own texel in [7.5, 7.99) | 24,370 | 13.8 % | 16,914 | 7.5 % |
| own texel under 7.5 | 76,673 | 43.5 % | 122,601 | 54.5 % |
| own texel 8 or more, a weighted tap under 8 | 5,939 | 3.4 % | 5,756 | 2.6 % |
| every tap 8 or more, the mean falls under 8 | 20 | 0.0 % | 0 | 0.0 % |
| reprojected 8 or more: the block's ray does not fit the lobe | 3,366 | 1.9 % | 2,820 | 1.3 % |
| all | 176,403 | | 224,864 | |

A test of "length >= 7.5", or of a length rounded to the nearest draw, saves the first two rows: 90,405 own rays in the
cabin (51.2 %) and 93,687 in the bay (41.7 %), and perhaps part of the fourth row. A test with a tolerance of 0.01 draws
saves the first row only: 37.4 % and 34.1 %.

## Where the lengths just under an integer come from

A length "near below" an integer lies less than 1e-3 under it, as 7.9999 does. Such a length grows by exactly 1 per draw,
so a pixel that gets one in draws 5 to 7 reaches draw 8 just under 8. At draw 7, the cabin holds 82,866 such lengths and
the bay 104,037. The v3 log finds which step made each one. A store cannot make one: the 32-bit store keeps the value
unchanged.

| draw (cabin) | the mean fell below its least tap | the blend added less than 1 | a cut of at most 1e-3 | new, from none of these three | inherited from the own texel | near below, all |
|---:|---:|---:|---:|---:|---:|---:|
| 3 | 239 | 0 | 0 | 0 | 0 | 239 |
| 4 | 0 | 0 | 366 | 0 | 0 | 366 |
| 5 | 125 | 0 | 298 | 26,601 | 284 | 27,308 |
| 6 | 170 | 0 | 270 | 25,895 | 26,289 | 52,624 |
| 7 | 166 | 0 | 292 | 31,777 | 50,631 | 82,866 |

The columns 2 to 4 count only the near-below lengths of the draw that had the event in that draw (`v3split.py` table 1). The
new near-below lengths without any of the three events appear at draw 5, the draw after the first cuts. They match the
count of pixels whose reprojected length fell below their own texel's length by any amount: 59 at draw 4, 28,739 at draw 5
and 62,675 at draw 7. So a weighted tap with a shorter length pulled the mean down by less than 1e-3. A pull that small
from a neighbor that a cut made shorter by a fraction of a draw needs a tap weight under about 1e-3.

The v4 log shows such weights. It sorts the surface pixels by the weight share of the three taps other than the heaviest
(trace layer 1):

| draw 5, cabin | exactly 0 | under 1e-6 | [1e-6, 1e-5) | [1e-5, 1e-4) | [1e-4, 1e-3) | [1e-3, 1e-2) | 1e-2 or more |
|---|---:|---:|---:|---:|---:|---:|---:|
| all surface pixels | 472,945 | 5,014 | 36,550 | 350,372 | 37,676 | 0 | 1,964 |
| reprojected below own | 64 | 195 | 2,116 | 22,727 | 3,281 | 0 | 277 |

In the still frame, 430,000 of the 904,500 surface pixels give their other three taps a weight share from 1e-7 to 1e-3, and
473,000 give them exactly 0. No pixel lies between 1e-3 and 1e-2. So these weights are rounding, not motion: 28,319 of the
28,660 losses of draw 5 (98.8 %) come from them. The bay gives the same picture.

The place goes from a normalized screen place back to texels in `reprojectionOf` (`signals.glsl`):

```glsl
    vec2 texelPlace = place * frame.previousViewport.xy - 0.5;
    ...
    vec2 f = texelPlace - vec2(r.first);
```

where

- `place` = the pixel's last place in normalized screen coordinates, from 0 to 1.
- `frame.previousViewport.xy` = the last draw's render size in pixels (1280 × 720).
- `texelPlace` = the last place in texels of the last image.
- `f` = the fraction of a texel by which the place lies past the first tap, which sets the weights of the other three taps.
  In a still frame it should be 0.

A 32-bit float near 1 has a spacing of about 6e-8. Times 1280 that gives about 7.6e-5 texel, the size of most of the
weights above. The log does not show this step itself, so this is an inference from the size.

Over draws 3 to 7, the events that made a near-below length split like this:

| event | cabin | bay |
|---|---:|---:|
| the reprojection's tiny tap weights next to a shorter neighbor | 84,273 (97.8 %) | 106,945 (98.7 %) |
| a cut of at most 1e-3 | 1,226 (1.4 %) | 717 (0.7 %) |
| the mean fell below its least tap | 700 (0.8 %) | 685 (0.6 %) |
| the blend added less than 1 | 0 | 0 |
| the store | 0 | 0 |

The sums exceed the near-below count at draw 7, because a later cut of more than 1e-3 takes a length out of the class.

## The cuts of the tail, draws 9 to 12

`logs/v4-*-cut/cuts.txt` counts the cuts by their length before the cut ("from") and after it ("to"), in bins of 1 draw.
In draw d no length exceeds d + 1.

### Cabin

| draw | side | [4, 5) | [5, 6) | [6, 7) | [7, 8) | [8, 9) | [9, 10) | [10, 11) | [11, 12) | [12, 13) | 13 or more |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 9 | from | 1 | 832 | 2,182 | 4,812 | 8,555 | 20,750 | 32,700 | 0 | 0 | 0 |
| 9 | to | 3,469 | 5,328 | 9,171 | 13,229 | 16,918 | 21,717 | 0 | 0 | 0 | 0 |
| 10 | from | 0 | 756 | 1,826 | 3,725 | 6,792 | 10,362 | 28,541 | 37,750 | 0 | 0 |
| 10 | to | 3,783 | 4,846 | 8,757 | 12,680 | 16,402 | 19,413 | 23,871 | 0 | 0 | 0 |
| 11 | from | 1 | 752 | 1,387 | 2,965 | 5,132 | 7,873 | 12,030 | 32,952 | 37,313 | 0 |
| 11 | to | 3,439 | 3,974 | 7,330 | 10,937 | 14,301 | 17,661 | 19,689 | 23,074 | 0 | 0 |
| 12 | from | 0 | 572 | 1,034 | 2,301 | 4,073 | 6,071 | 9,296 | 13,007 | 35,945 | 33,591 |
| 12 | to | 3,032 | 3,224 | 5,880 | 8,827 | 12,199 | 15,195 | 17,408 | 18,717 | 21,408 | 0 |

### Bay

| draw | side | [4, 5) | [5, 6) | [6, 7) | [7, 8) | [8, 9) | [9, 10) | [10, 11) | [11, 12) | [12, 13) | 13 or more |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 9 | from | 0 | 13,501 | 4,480 | 7,257 | 10,530 | 21,453 | 26,434 | 0 | 0 | 0 |
| 9 | to | 22,927 | 6,886 | 10,294 | 13,193 | 14,662 | 15,693 | 0 | 0 | 0 | 0 |
| 10 | from | 0 | 14,896 | 3,678 | 5,735 | 8,506 | 11,160 | 25,073 | 26,636 | 0 | 0 |
| 10 | to | 24,088 | 5,713 | 9,201 | 12,150 | 13,996 | 15,005 | 15,531 | 0 | 0 | 0 |
| 11 | from | 0 | 15,271 | 3,083 | 4,655 | 6,753 | 9,107 | 11,972 | 27,706 | 24,717 | 0 |
| 11 | to | 23,862 | 4,737 | 7,455 | 10,642 | 13,130 | 14,247 | 14,612 | 14,579 | 0 | 0 |
| 12 | from | 0 | 14,870 | 2,770 | 3,580 | 5,548 | 7,257 | 9,638 | 12,092 | 28,817 | 21,017 |
| 12 | to | 23,391 | 3,917 | 6,024 | 8,704 | 11,209 | 12,775 | 13,612 | 13,118 | 12,839 | 0 |

- Most cuts of the tail start from the two longest bins, the histories that are still on schedule: in the cabin, 53,450 of
  draw 9's 69,832 cuts (76.5 %) start in [9, 11).
- The cuts end at every length from 4 up. The share that ends under 8 falls as the histories grow: in the cabin 44.7 % of
  the cuts at draw 9 and 19.8 % at draw 12, in the bay 63.7 % and 39.8 %.
- In the bay, about 15,000 pixels per draw start in [5, 6) and end in [4, 5). The anti-lag cuts these pixels back to the
  fast cap in every draw, so they never reach 8. This loop makes most of the bay's "cut to 4" of 2.2 % per draw.

## The verdict

The inferred cause explains a bit over half of draw 8's own rays, and in a weaker form than stated. The anti-lag does cut
young reflection histories in draws 4 to 7, 5 to 12 % of the pixels per draw. But most cuts are partial and few reach the
fast cap of 4. The own rays whose own texel lies under 7.99 at draw 7 are 57.3 % of draw 8's own rays in the cabin and
62.0 % in the bay. The rest of draw 8's own rays, 37.4 % in the cabin and 34.1 % in the bay, come from a rounding defect.
In a still frame, the place that the reprojection computes lies a float rounding off its own texel. So the other three
taps take weights from 1e-7 to 1e-3 in place of 0. Next to a neighbor that a real cut shortened, these weights pull the
reprojected length less than 1e-3 under its schedule. The 32-bit history keeps that deficit, so the length reaches draw 8
as 7.9999 and fails the plan's test `>= FullRateFrames`. The mean, the blend and the store do not round. Tiny cuts cause
1.4 % of the near-below lengths at most. The tail after draw 9 is a third effect. The narrow box's anti-lag keeps cutting
8 to 12 % of the pixels per draw, mostly histories of 9 to 13 draws that are on schedule, and 20 to 64 % of the cuts end
under 8. In the bay, about 15,000 pixels per draw stay at the fast cap.

What I would change first is the rounding defect, at its source: a still frame's reprojection should read its own texel
with a weight of exactly 1. Two ways would do it: snap a place that lies within about 1e-3 texel of a texel's center onto
that center, or give no weight to a tap under a small floor, as the plan's light record already does with
`LeastFootprintWeight`. Only the defect has no reason to exist, and a fix of it changes no decision of the anti-lag. The
log puts its gain at 37.4 % (cabin) and 34.1 % (bay) of draw 8's own rays, and probably most of the 3.4 % with a weighted
tap under 8. A tolerance in the plan's test would hide the same deficit, but the light's reprojection would still read the
neighbors. The young-history cuts and the tail's cuts are design questions for step 2.

## Other findings

- The block's ray does not fit the lobe of 1,700 to 3,400 own-ray pixels per draw whose length is 8 or more. This is the
  settled share of own rays.
- The diffuse own rays fall to 0.3 % of the pixels at draw 8 (2,307 in the cabin). The diffuse light has no part in this
  tail.
- The first finding gave 50 % own rays at draw 8 after a start. That start was t0-gumm's live start on X5's code. On the
  line's head, the frozen start and the frozen cut give the same 26 % of the pixels that can trace one (cabin).
- The mean of four equal taps falls below its least tap at about 240 pixels in draw 3 and 100 to 200 per draw later. This
  is float rounding of the mean, and too small to matter here.

## Repeating the take

```powershell
bash docs/research/t0-xwdz-reflection-tail/tools/build.sh
bash -c 'for s in cabin bay; do for e in start cut; do bash docs/research/t0-xwdz-reflection-tail/tools/tailrun.sh $s $e $s-$e; done; done'
python3 docs/research/t0-xwdz-reflection-tail/tools/tailtable.py /private/tmp/t0-xwdz/runs/cabin-cut 4 12
python3 docs/research/t0-xwdz-reflection-tail/tools/v3split.py /private/tmp/t0-xwdz/runs/cabin-cut
```

`build.sh` takes the last patch. `PATCH=scratch-tail-log-v2.patch` builds the v2 log instead. A full build needs 22 GiB of
free disk. The v3 and v4 builds of this record were incremental builds in the tree of the first v3 build. Each run takes
about 30 seconds to 1.5 minutes. It writes only small logs: `tail.txt`, `hist.txt`, `cuts.txt`, `weights.txt` and
`frames.series`. `tailrun.sh` refuses to start under 18 GiB of free disk. It sets `TERTIUS_PROBE_SERIES`, so that the
keep list `none` writes no PNG. The v2 runs had no series, so they wrote PNGs, which were deleted after the runs.

The kept logs:

- `logs/v1-cabin-start`: the first version of the log, which is not kept as a patch. Its columns after "<frame> <draw
  index> <width> <height>" are: surface pixels, reprojected under 8, before the anti-lag under 8, after it under 8, the
  clamp ran, cut, cut from under 8, cut from 8 or more, cut to 4 or less, reprojected below own by more than 1e-3, after
  under draw index + 1 − 0.001, own reflection rays, of them reprojected 8 or more, own diffuse rays.
- `logs/v2-*`: the four takes of the v2 log.
- `logs/v3-*-start`: the two starts of the v3 log.
- `logs/v4-*-cut`: the two cuts of the v4 log. Its counts equal the v3 cuts on every v3 column.
