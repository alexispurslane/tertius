# The acceptance record of the reflection step cap of 128 (ticket ter-w1o3)

A reflection ray of the screen tracer walks the depth image in steps. The step cap is the largest number of steps of one walk. `ScreenTrace.Default` now sets the reflection step cap to 128. Before 2026-11-12 it was 256. This folder keeps the measurements behind that change. The change is decision 41 of `docs/plans/temporal-renderer.md`.

## The result

| Question | Answer |
|---|---|
| GPU time of the deck's frame on an RTX 2000 Ada | 8.918 ms at 256 steps and 8.703 ms at 128 steps: 0.215 ms less, 2.4 % |
| GPU time of the deck's frame on the M1 Max | 0.33 ms less with pass time stamps, and 0.49 to 0.69 ms less without them |
| Stills outside the hair scenes, on the M1 Max | 145 captures: the mean of a capture changes by at most 0.067 % |
| Stills of the hair scenes, on the M1 Max | 15 captures: the mean changes by at most 0.231 %, and no fault shows |
| A film with a moving camera, on the M1 Max | 180 frames: the mean of a frame changes by at most 0.017 % |
| Stills on Linux, on the RTX 2000 Ada | 2 captures: the mean changes by at most 0.018 % |
| The tests after the merge of main | Checked: 10,780 passed. Release: 10,767 passed. No test failed |

A level is one step of the 255 steps of an 8-bit color channel. The mean of a capture is the mean of its luminance in linear light.

## The timing on the GPU workbench

The GPU workbench is the queue of cloud GPU jobs that `tools/runpod/WORKBENCH.md` describes. One job built two trees on one RTX 2000 Ada. The tree `main` is main at `8a71d1c12`. The tree `cut` is that commit with the cap of 128. The job ran the deck script `scripts/temporal-upscale-timing.json` five times per tree, with the trees in turn.

```text
GPU frame span without the capture pass, ms, per run:
  cut: median 8.703, runs 8.700, 8.688, 8.703, 8.703, 8.712, spread 0.024
  main: median 8.918, runs 8.906, 8.921, 8.918, 8.912, 8.920, spread 0.016

cut against main: -0.215 ms (-2.4%), every run differs. Estimate on the Mac: -0.52 ms.
  reflection rays: 0.900 -> 0.684 ms (-0.216)
  environment faces: 0.084 -> 0.084 ms (-0.001)
  direct light: 1.765 -> 1.766 ms (+0.000)
```

Every run at 128 steps lies below every run at 256 steps. The whole gain is in the pass `reflection rays`. No other pass moves by more than 0.001 ms.

## The timed take on the M1 Max

`tools/runpod/WORKBENCH.md` asks for one timed Mac run of the final candidate before the merge. Take 7 is that run. It recorded the deck with Instruments under the exclusive GPU lock, and it took 200 s. Its binary is main's Release build of `ad095e599` with the scratch edit `../edits/steps.py`. With that edit, the variable `SWEEP_RSTEPS` sets the reflection step cap, and the exposure has the deck's fixed value.

| Trace | Step cap | Pass time stamps | Frame span, median | GPU clock, mean | Counts |
|---|---:|---|---:|---:|---|
| `deck-w3-ctl` | 256 | on | 20.963 ms | 1291 MHz | yes |
| `deck-w3-r128` | 128 | on | 20.631 ms | 1292 MHz | yes |
| `deck-w3-ctl-ns` | 256 | off | 20.784 ms | 1294 MHz | yes |
| `deck-w3-r128-ns` | 128 | off | 20.096 ms | 1293 MHz | yes |
| `deck-w3-ctl-ns-b` | 256 | off | 20.588 ms | 1293 MHz | yes |
| `deck-w3-r128-ns-b` | 128 | off | 21.097 ms | 1151 MHz | no |
| `deck-w3-ctl-b` | 256 | on | 21.421 ms | 1222 MHz | no |
| `deck-w3-r128-b` | 128 | on | 22.101 ms | 1176 MHz | no |

The frame span is the median over 111 settled frames of the trace, from `../tools/idle.py`. The GPU clock fell in the last three traces, while the load of the machine rose to 44. A trace with a fallen clock measures the clock and not the change. So those three traces do not count.

- With pass time stamps, the frame took 0.33 ms less: 20.963 ms against 20.631 ms.
- Without them, as in a window, the frame took 0.49 to 0.69 ms less: 20.588 and 20.784 ms against 20.096 ms.
- The two stamped traces split the time between the two ray passes differently. The sum of both ray passes compares: 3.26 ms at 256 steps and 2.78 ms at 128 steps.

The workbench's estimate for the Mac was 0.52 ms. It lies inside the measured range of 0.49 to 0.69 ms.

## The stills on the M1 Max

Each script ran twice with the binary of take 7, at 256 and at 128 steps. Each scene kept its own automatic exposure. `capdiff2.py` compared each pair of captures. `capdiff-stills.txt` holds the row of every capture.

| Group | Captures | Identical | Largest change of the mean | Largest share of pixels over 4 levels |
|---|---:|---:|---|---|
| Outside the hair scenes | 145 | 18 | +0.067 %: `mirror-balls` at 2560 × 1440, `plate` | 0.104 %: `mirror-balls`, `balls` |
| The hair scenes | 15 | 3 | -0.231 %: `hair-bent-cards`, `front` | 1.097 %: `hair-rugs` at 2560 × 1440, `low` |

The first group holds 24 capture scripts and the deck's low view at 1280 × 720. It also holds six scripts at 1920 × 1080 and at 2560 × 1440.

### Outside the hair scenes

Five of the 145 captures change by more than 0.04 % in the mean:

| Capture | Change of the mean |
|---|---:|
| `mirror-balls` at 2560 × 1440, `plate` | +0.067 % |
| `rigs`, `twist` | -0.064 % |
| `demo-shuttle`, `nose` | +0.054 % |
| `mirror-balls` at 1920 × 1080, `belly` | +0.040 % |
| `mirror-balls` at 2560 × 1440, `belly` | +0.040 % |

In `mirror-balls`, single pixels on the rim of a ball change by up to 107 levels. `pics/mirror-balls-pair.png` shows the capture `balls` at both caps, and `pics/mirror-balls-rim-crop.png` enlarges one rim. The two crops show the same picture.

### The larger render sizes

At a larger render size, a ray crosses more pixels, so it needs more steps for the same path. Six scripts ran at 1920 × 1080 and at 2560 × 1440: `deck`, `reflections-stills`, `mirror-balls`, `doorway`, `hair-rugs` and `demo-surface`. Their 36 captures outside the hair scenes change by at most 0.067 % in the mean. This is no more than at 1280 × 720. `pics/deck-pair.png` shows the deck at 1920 × 1080 at both caps, where the mean changes by 0.010 %. It closes item 1 of the ticket.

### The hair scenes

The cap of 256 came from ticket `ter-r5hb`, for hair rays at a grazing angle. So the four hair scripts are the hard case.

| Capture | Change of the mean | Pixels over 4 levels | Largest change |
|---|---:|---:|---:|
| `hair-bent-cards`, `front` | -0.231 % | 0.001 % | 6 levels |
| `hair-bent-cards`, `bent` | -0.163 % | 0.001 % | 8 levels |
| `lamp-hall-16-hair`, `hair` | -0.064 % | 0.198 % | 39 levels |
| `hair-rugs`, `low` | -0.005 % | 0.956 % | 41 levels |
| `hair-rugs`, `rugs` | -0.003 % | 0.123 % | 62 levels |
| `hair-rugs` at 2560 × 1440, `low` | -0.016 % | 1.097 % | 73 levels |
| `skin-and-hair`, 6 captures | 0.000 % | 0.000 % | 4 levels |

- `hair-bent-cards` changes most in the mean. The change is even: no pixel changes by more than 8 levels.
- In `hair-rugs`, about 1 % of the pixels of `low` change by more than 4 levels. This is noise on the dark floor, because the mean changes by 0.016 % at most.
- In blocks of 16 × 16 pixels, the largest change of a hair capture is 3.9 %. No block passes 5 %.
- `pics/lamp-hall-hair-pair.png` and `pics/lamp-hall-hair-crop.png` show the lamp hall at both caps. `pics/hair-rugs-band.png` enlarges the place of the old band fault of `ter-r5hb`. No crop shows a band or another fault.

The hair means pass the figure of about 0.04 % that the ticket named. The pictures show no change, so the build went on with 128 steps.

### One correction of the takes

The first take of part a shared its job list file with a take of ticket `ter-2zga`. Two takes wrote the same folders at once. I deleted the 13 folders from `w3-mirror-balls-1440-r128` on, and one take rendered them again. `stills-wide-w3a.lock.log` is the log of that second take. Since then, each take reads its own job list.

## The film with a moving camera

`../refl/exit-floor.json` films the floor with the follow camera, 180 frames at 1280 × 720. Two runs at 256 steps are equal in all 180 frames (`capdiff-film-control.txt`). So every difference of the third run comes from the cap. At 128 steps the mean of a frame changes by at most 0.017 %. At most 0.034 % of a frame's pixels change by more than 4 levels (`capdiff-film.txt`). This closes item 2 of the ticket.

## The identity of the branch's build

The image tests and take 7 used the scratch binary, whose cap comes from a variable. The branch's own Release build has the default cap of 128. It made 49 frames of five scripts. They equal the scratch binary's frames at 128 steps byte for byte (`framecmp-w3id.txt`). So the measurements describe the merged code.

## The stills on Linux

A second workbench job made stills of both trees at 1280 × 720 on the RTX 2000 Ada. It hit its timeout of 600 s in the fifth script, before its comparison step. It sent back the two pairs of stills that its output list named. `capdiff2.py` compared them on the Mac:

```text
script                 capture                   mean %    any %   >1 lv %  >4 lv %  max
doorway                inside                    -0.000     0.68    0.000    0.000    3
hair-bent-cards        front                     +0.018     8.61    0.044    0.001    6
```

Both stills show their lit scene. The scene `doorway` is the one that a cap of 64 breaks. Together with the timing job, this closes item 3 of the ticket.

## After the merge of main

During the build, main gained a change of the reflection resolve (ticket `ter-xo83`, commit `220e1b546`). The resolve is the pass that turns the rays' results into the reflected light of each pixel. The branch took main at `b1ce2701b` by a merge, commit `9e8256255`. Two more workbench jobs compared that main with the merged branch.

| Check on the merged code | Result |
|---|---|
| GPU time of the deck's frame, five runs per tree | 8.865 ms to 8.652 ms: 0.213 ms less, 2.4 %, and every run differs |
| The pass `reflection rays` | 0.895 ms to 0.680 ms |
| 9 stills of `doorway`, `mirror-balls` and `hair-rugs` at 1280 × 720 | The mean changes by at most 0.116 % |
| Every suite in Checked | 10,780 passed, 26 skipped, none failed (`tests/checked8.lock.log`) |
| Every suite in Release | 10,767 passed, 26 skipped, none failed (`tests/release8.lock.log`) |

The gain is the same as before the merge. The stills agree with the M1 Max results above (`workbench/stills2-capdiff.txt`):

- The four captures of `doorway` change by 0.000 % in the mean, and by at most 7 levels at one pixel.
- In `hair-rugs`, 0.993 % of the pixels of `low` change by more than 4 levels. The M1 Max gave 0.956 %.
- The capture `plate` of `mirror-balls` changes by +0.116 % in the mean. The capture is nearly black. Only 0.01 % of its pixels change, by at most 14 levels.

The byte comparison of the branch's build belongs to the code before this merge. After the merge, the resolve differs from the scratch binary's, so the frames differ too.

## The tests

The first four items ran on the code before the merge of main. The table above holds the suite runs after it.

- The test `TheRendererOptionsDefaultToTheDesignsTracerAndThirtyTwoWarmUpDraws` of `ScreenTraceSettingTests` names the new default. It failed first with "Expected options.ScreenTrace to be ... ReflectionSteps = 128 ..., but found ... ReflectionSteps = 256". It passed after the change.
- The change adds no other test. `ScreenWalkBoundaryTests` and `ScreenRayTests` test the cap itself with explicit step counts. The default value is a measured trade, and the options test pins it.
- The full Checked suite passed on the changed code: 10,778 passed, 26 skipped, none failed (`tests/checked7.lock.log`). No snapshot test failed, so no reference image changed.
- The full Release suite passed on the changed code: 10,765 passed, 26 skipped, none failed (`tests/release7.lock.log`).

### The mutants

The change has one code line, in the Rendering module. The official Stryker run covers Core, Simulation and Physics only, so it had no line to mutate. `tools/mutate.py` ran three listed mutants of the line (`tests/mutants.json`, `tests/mutate.lock.log`).

| id | Place | Change | Result | Killed by |
|---|---|---|---|---|
| C01 | `src/Tertius.Rendering/ScreenTraceSetting.cs:32` | The old cap of 256 steps | killed | `TheRendererOptionsDefaultToTheDesignsTracerAndThirtyTwoWarmUpDraws` |
| C02 | `src/Tertius.Rendering/ScreenTraceSetting.cs:32` | One step fewer, 127 | killed | `TheRendererOptionsDefaultToTheDesignsTracerAndThirtyTwoWarmUpDraws` |
| C03 | `src/Tertius.Rendering/ScreenTraceSetting.cs:32` | The cap of 64 steps, which breaks the floor of `doorway` | killed | `TheRendererOptionsDefaultToTheDesignsTracerAndThirtyTwoWarmUpDraws` |

All three mutants died, so a test runs the changed line and pins its value. No mutant survived, and the line needs no further test.

## What the folder holds

| Path | Content |
|---|---|
| `workbench/ab-report.txt`, `workbench/ab/` | The report of the timing job, its result and the timings of its ten runs |
| `workbench/stills-result.json`, `workbench/stills-capdiff.txt` | The result of the stills job, with its log, and the comparison of its two pairs |
| `workbench/ab2-report.txt`, `workbench/ab2/`, `workbench/stills2-*` | The timing job and the stills job on the code after the merge of main |
| `take7.sh`, `take7.jobs`, `take7.lock.log`, `take7-idle.txt` | The timed take on the M1 Max and the table of its traces |
| `stills-wide-w3a.jobs`, `stills-wide-w3b.jobs`, `stills-wide-w3id.jobs` | The job lists of the image tests, each with its lock log |
| `hairscripts/`, `bigscripts/` | The demo scripts of the hair scenes and of the larger render sizes |
| `capdiff2.py` | The tool that compares the captures of two runs |
| `capdiff-stills.txt`, `capdiff-film.txt`, `capdiff-film-control.txt` | The comparison of every still and of every frame of the film |
| `framecmp-w3id.txt` | The byte comparison of the branch's build with the scratch binary |
| `pics/` | The pictures that this text names |
| `tests/` | The commands and the logs of the test runs, and the list and the table of the mutants |

`../refl/stills4.sh` runs a job list of stills, and `../measure-lib.sh` records a trace. The folder does not hold the stills, the film frames or the traces.
