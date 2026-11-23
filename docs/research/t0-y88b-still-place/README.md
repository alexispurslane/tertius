# A still pixel's last place at its texel's center: the takes of t0-y88b

Ticket t0-y88b moves a still pixel's place in the last frame's image onto its texel's center. Before the fix, a place came back
from its share of the image up to about 2.5e-4 texel off the center at 3,840 pixels wide. The three other taps of the
reprojection then got weights of 1e-7 to 1e-3. t0-xwdz's step 1 found that these weights pulled the history length a hair
under 8, so that the ray plan traced an own reflection ray that bought nothing (docs/research/t0-xwdz-reflection-tail/).

The fix is `lastTexelPlace` in `src/Tertius.Rendering/Shaders/signals.glsl`, commit 19c70fe58 on the branch
t0-y88b-still-place. These takes compare it with its base, main 496e3affc.

## How to make the takes again

All raw data lives in /private/tmp/t0-y88b and is deleted after the analysis. These steps make it again.

1. `bash tools/builds.sh <fix commit> ym yf` builds the two probe builds into /private/tmp/t0-y88b/{ym,yf}/probe-bin. Each
   build takes its commit's survey probe patch, t0-gumm's frozen-film patches, t0-xwdz's tail log and this record's
   `scratch-yaw-drift.patch`. The fix's build here came from `git stash create` of the fix's tracked changes,
   b65047db3, whose src and ARCHITECTURE.md equal those of 19c70fe58. A build from 19c70fe58 compiles the same source.
2. `bash tools/yrun.sh <build> <cabin|bay> <name> <draws> <keep from> [VAR=value]` runs one frozen take into
   /private/tmp/t0-y88b/runs/<name>. The takes of this record:
   - counts: `yrun.sh ym cabin ym-cabin 120 120`, then the same for the bay and for yf;
   - stills: `LOG=0 yrun.sh ym cabin ym-cabin-still 660 640`, then the same for the bay and for yf;
   - slow pans: `LOG=0 yrun.sh ym cabin ym-cabin-p300 600 420 TERTIUS_X_YAW_TEXELS=0.0033333`, and with 0.005 for
     `-p200`, then the same for the bay and for yf.
3. `python3 -I ../t0-xwdz-reflection-tail/tools/v3split.py /private/tmp/t0-y88b/runs/<name>` prints the counts.
4. `python3 -I tools/compare.py ym-<take> yf-<take> <take>` prints the differences and writes the crops.
5. `FFMPEG=<ffmpeg> python3 -I ../t0-qvm1-region-cap-takes/tools/video.py videos/<take>.mp4 420 "<note>" "main 496e3affc|/private/tmp/t0-y88b/runs/ym-<take>|1280 x 720" "fix 19c70fe58|/private/tmp/t0-y88b/runs/yf-<take>|1280 x 720"`
   makes a film.

Every take is t0-gumm's fair scene at 1920 x 1080 and the headless render scale of 2/3 (1280 x 720). The cabin is orbit's
person camera with the switch's lamps off and a fixed exposure of 4.08 stops. The bay is demo-surface's bay camera with a
fixed exposure of -4.0 stops.

## The counts

Frozen start, 120 draws, t0-xwdz's tail log. "Near below" counts the surface pixels whose length lies less than 1e-3 under a
whole number of draws. "Own rays" counts draw 8's own reflection rays.

| Scene | Near below at draw 7, main | Fix | In [7.99, 8) before draw 7, main | Fix | Own rays at draw 8, main | Fix | Change |
|---|---:|---:|---:|---:|---:|---:|---:|
| Cabin | 82,866 | 982 | 87,501 | 3,172 | 176,403 | 106,254 | -39.8 % |
| Bay | 104,037 | 592 | 104,102 | 1,309 | 224,864 | 142,056 | -36.8 % |

Of draw 8's own rays, those whose own texel held a length in [7.99, 8) fall from 66,035 to 2,965 (cabin) and from 76,773 to
1,243 (bay). The rays whose own texel held less than 7.5 stay: 76,673 and 76,595 (cabin), 122,601 and 122,510 (bay).

The fix's remaining lengths just under a whole draw come from the cuts of at most 1e-3 and from the lengths that inherit
them. At draw 7 in the cabin they are 352 tiny cuts, 34 means under their least tap and 647 inherited from the own texel.
On main, the tiny cuts and the means under their least tap were 2.2 % (cabin) and 1.3 % (bay) of the near-below count,
about the size of what remains.

## The stills and the slow pans

Each still is draws 640 to 659 of a frozen take of 660 draws. Each pan is draws 420 to 599 of a frozen take of 600 draws,
with the camera turning 1/300 or 1/200 texel per draw. Main and the fix compare draw by draw in 8-bit gray.

| Take | Mean gray, main | Fix | Mottle, main | Fix | Largest difference | Pixels over 2 in a draw, at most |
|---|---:|---:|---:|---:|---:|---:|
| Cabin still | 46.502 | 46.502 | 0.788 | 0.802 | 13 | 218 |
| Bay still | 105.141 | 105.141 | 0.938 | 0.943 | 19 | 25,312 |
| Cabin, 1/300 | 46.458 | 46.457 | 0.792 | 0.798 | 19 | 279 |
| Cabin, 1/200 | 46.457 | 46.457 | 0.793 | 0.795 | 15 | 193 |
| Bay, 1/300 | 105.331 | 105.327 | 0.929 | 0.929 | 70 | 33,584 |
| Bay, 1/200 | 105.412 | 105.408 | 0.928 | 0.934 | 56 | 33,603 |

The mean gray covers the last 20 draws. The mottle is the standard deviation of the last draw minus its Gaussian blur of
radius 4, on the flat areas (68 % of the bay and 81 % of the cabin). The pans move: on main, 3,242 to 31,028 pixels change by
more than 8 between draws 420 and 599.

The pictures show no visible change. The bay's difference is a new noise pattern at the same strength. The fix sends draw 8's
own rays to other pixels, so the noise lands elsewhere, and the bay's mottle lives in long histories, so even a mean of 20
draws keeps its pattern. The largest difference, 70 of 255 in the bay's 1/300 pan, is one speck at a window's edge. Neither
film shows a step at the snap's edge of 1/256 texel.

- `pictures/<take>-1x.png`: a 1:1 crop of 320 x 220 output pixels around the take's largest difference in its last draw:
  main left, the fix in the middle and the difference times 8 right. `<take>-3x.png` is the same at 3x (nearest).
- `pictures/<scene>-still-main.png` and `-fix.png`: the whole last draw of each still, with its `exposure.json`.
- `videos/<scene>-p300.mp4` and `-p200.mp4`: 3 s films at 60 frames a second, main left and the fix right, one fixed
  exposure, H.264 at CRF 12.

## The listed mutants

`tools/mutate.py` with the list `tools/mutants.json`, on the branch's head 47afcc7c2 and, for S08's test, its successor. Each
mutant's tests ran in Checked under the script's timeout of 120 s. "Lock wait" is the test host's wait for the GPU lock in
its shared mode, from its log in /tmp/tertius-gpu-test-lock: the host prints a wait line only when it waits. A first run at
15:24 timed out at 13 of the 16 mutants with no test result, because the hosts waited behind another lead's exclusive
ticket (ticket tc-5azv). Those 13 and S08 ran again from 16:14 to 16:31, when every host waited at most 1 s.

| Id | Line | Change | Result | Lock wait | Seconds | Killed by (first) |
|---|---|---|---|---:|---:|---|
| S01 | signals.glsl:56 | no snap | killed | ≤ 1 s | 72 | PixelHistoryTests.AStillPixelUnderFsrJitterKeepsItsSurfacePlaceOverItsReflectedPlace |
| S02 | signals.glsl:56 | bound 1/32 texel | killed | ≤ 1 s | 59 | APlaceASixtyFourthOfATexelLeftOfAndAboveACenterKeepsItsBilinearWeights |
| S03 | signals.glsl:56 | bound 1/150 texel | killed | ≤ 1 s | 57 | APlaceATwoHundredthOfATexelOffACenterKeepsItsBilinearWeights |
| S04 | signals.glsl:56 | bound 1/400 texel | killed | ≤ 1 s | 58 | APlaceAThreeHundredthOfATexelOffACenterReadsThatCenter |
| S05 | signals.glsl:65 | the center by floor | killed | ≤ 1 s | 56 | AStillPixelWhoseHistoryNoCutShortenedHoldsALengthOfExactlyEightAfterEightDraws |
| S06 | signals.glsl:66 | snap the far places | killed | ≤ 1 s | 62 | AStillPixelWhoseHistoryNoCutShortenedHoldsALengthOfExactlyEightAfterEightDraws |
| S07 | signals.glsl:66 | mix swapped | killed | 14 s | 69 | AStillPixelWhoseHistoryNoCutShortenedHoldsALengthOfExactlyEightAfterEightDraws |
| S08 | signals.glsl:66 | no abs: every place before a center snaps | killed | ≤ 1 s | 72 | APlaceASixtyFourthOfATexelLeftOfAndAboveACenterKeepsItsBilinearWeights |
| S09 | signals.glsl:66 | no snap along y | killed | ≤ 1 s | 60 | AStillPixelWhoseHistoryNoCutShortenedHoldsALengthOfExactlyEightAfterEightDraws |
| S10 | signals.glsl:78 | the surface taps without the snap | killed | ≤ 1 s | 58 | AStillPixelWhoseHistoryNoCutShortenedHoldsALengthOfExactlyEightAfterEightDraws |
| S11 | signals.glsl:116 | the reprojection without the snap | killed | ≤ 1 s | 61 | AStillPixelWhoseHistoryNoCutShortenedHoldsALengthOfExactlyEightAfterEightDraws |
| S12 | accumulate_temporal.comp:157 | the trace's surface place without the snap | survived | ≤ 1 s | 103 | |
| S13 | accumulate_temporal.comp:188 | the trace's reflected place without the snap | survived | ≤ 1 s | 30 | |
| S14 | accumulate_temporal.comp:199 | the trace's kept reflected place without the snap | survived | ≤ 1 s | 27 | |
| S15 | screen_trace_plan.comp:27 | the footprint floor at 0 | survived | ≤ 1 s | 76 | |
| S16 | screen_trace.glsl:478 | lastPlaceAt half a pixel off (t1-slb9) | killed | 90 s | 143 | AStillPixelsLastPlaceReadsItsOwnTexelWithTheWholeWeight |

S08 survived its first run: every probe lay after the center. The probe then got a place 1/64 texel left of and above the
center, and that test killed it.

The answers to the survivors:

- S12, S13 and S14, judgment calls. The three lines lie inside `#ifdef TERTIUS_ACCUMULATION_TRACE` and set only the trace's
  texel place, which no render reads. Without the snap, the trace's place differs from the reprojection's only where a place
  lies within 1/256 texel before a center. In the trace tests at 1,280 pixels wide, no still place
  lands there (S12 survived), and a still frame's reflected place never wins (S13, S14). A trace test at 3,840 would cost about 30 s of GPU time
  for a diagnostic line.
- S15, a judgment call. The floor works only under motion, at a light edge on one surface, and no test scene has one.
  Ticket tc-5br7 holds the test that would pin it.

S16 is t1-wl7c's mutant S07, the mutant of ticket t1-slb9. This ticket's probe test kills it, so t1-slb9 needs no test of
its own.

The changed C# lines are tests only. The changed lines of src are GLSL, which no coverage tool measures, so the list above
holds a mutant for each changed code line of src.
