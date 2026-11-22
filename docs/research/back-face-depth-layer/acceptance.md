# The acceptance takes of the back-face layer

This is the record of ticket `t1-8b4w`, of 2026-11-21. It measures the finished branch `ter-kh4g-back-face-depth-layer` at `dd800356b` against main at `d8559eb58`, the main that the branch holds. Both sides hold the fix of the history store (`t8-69v7`) and the fix of the sun's shadow leak (`t8-rjno`). The method of each point comes from the design's section "The acceptance" and from [stage 1b](stage-1b/pictures.md). The terms are those of [the design](../../plans/back-face-depth-layer.md) and of [the record of stage 1](README.md).

Each section keeps the measured facts and my reading apart. A sentence that starts with "Reading:" holds my interpretation. Every other sentence states a measurement, a count or what an image shows.

## The result in short

| Point | Result |
|---|---|
| 1. The speckle on rect-lamps' window reveals (`t53-yy6p`) | Gone on both sides. The reveals are smooth on main and on the branch, in the ticket's own capture and at frame 239. The speckle needed the base without the shadow fix. |
| 2. The false sill hits | 0 bright samples of any class in the glow area over 400 draws, against 6,783 false ones under the 10 % rule in stage 1b. The every-sample census matches stage 1b's layer within 0.1 %, so the census ran. |
| 3. No new leaks or holes, no visible bug | Passed in rect-lamps, the cabin and the deck. No speck, sparkle, blotch or light through a shadow in any still or sampled film frame. No tile flickers more on the branch. |
| 4. Contact darkening | Small and both ways: weaker on the cabin's thick pillars, stronger by 7 of 255 at the corridor's end. Nothing looks wrong. |
| 5. The room's brightness | Main and the branch now give rect-lamps the same light within 1 %. The shadow fix removed the light that made the layer's darkening of stage 1b. |
| 6. The frame cost | The card: +0.075 to +0.139 ms in the rooms, +0.558 ms in the crowd. The Mac: +0.181 to +0.265 ms in the rooms, +0.747 ms in the crowd. The pass "back faces" takes 0.048 to 0.056 ms in the rooms and 0.482 ms in the crowd on the card, 0.0048 ms per figure. The depth pyramid takes 0.075 to 0.080 ms more in the rooms. |
| 7. The suites | The Mac: Checked 11,043 tests and Release 11,030 tests, 0 failed, 28 skipped in each. The card: 3,310 Rendering tests, 0 failed, 11 skipped. One earlier run of the card's shard 4 crashed once and did not crash again in three runs (`t70-o38b`, outside the acceptance). |
| 8. Linux beside NVIDIA | Lavapipe, Mesa 26.2.4: 3,310 Rendering tests, 6 failed, 13 skipped. The six fail on main with the same values (`t70-0pr5`, outside the acceptance). Every test of the 32 changed test files passes, apart from two timing tests that skip themselves on a noisy CPU. |

## How the takes were made

- Main's side is a Release demo of `d8559eb58`, built from a second tree under `/private/tmp/t1-8b4w/main`. The branch's side is a Release demo of `dd800356b`, built from this worktree. The stills, the films, the census and the Mac timing ran on the M1 Max.
- A still is the frame 239 of a film of 240 frames, at 1920 × 1080 with bloom off. A capture after a film resets the histories, so no still comes from a capture. The one exception is point 1, which repeats the bug ticket's own capture.
- The exposure is fixed per scene. One run of main with the automatic exposure gives the stops of its frame 239. Both sides then run with `--exposure <stops>`.
- Every Mac render took the GPU lock's shared mode. Every Mac timing take held its exclusive mode, one scene per hold.
- The cloud jobs went through `tools/runpod/gpu_job.py` under the agent name `t1-8b4w-takes`. Each timing job compares the branch with the main of the same job.

The tools of this record are in [acceptance-tools](acceptance-tools). They are tool code: no engine code changed.

| File | What it does |
|---|---|
| `mac-stills.sh` | The stills and the film of one view on the Mac, both sides, at one fixed exposure |
| `films.py`, `test_films.py` | The flicker of a film per tile, and its comparison between the sides, with its tests |
| `regions.py`, `crops.py` | Stage 1b's region and crop tools, with main as the base column |
| `brightness.py` | The mean linear luminance of the ticket's three boxes of `rect-lamps` |
| `census-port.patch` | The sill census of stage 1b, ported to the branch. It stays a patch and the scratch commit `refs/scratch/t1-8b4w-census` (`f1939e2e6`). |
| `mac-take.sh` | One Mac timing take of one scene, main against the branch |
| `cloud-cost.sh` | The two cloud timing jobs |
| `cost_report.py` | Stage 1b's cost report, for the two sides |
| `lavapipe-tests.sh` | The Rendering tests on Mesa's lavapipe in a cloud job |

## Point 1: the speckle on rect-lamps' window reveals (bug ticket `t53-yy6p`)

The ticket's crop came from the review base `2f18fd829`, which had no fix of the sun's shadow leak. It showed grey speckle and a bright vertical strip on the reveals of the right side windows. A reveal is a side face of an opening in the wall.

The same script ran on both sides: `scripts/rect-lamps.json`, 1280 × 720, `--exposure 3.341`, the ticket's own settings. The script films 240 frames and then captures the still `cabin`.

[acceptance-reveals.png](acceptance-reveals.png) shows the output pixels x 1000 to 1279, y 290 to 419, at twice their size. The top row is the capture `cabin`, and the bottom row is the film's frame 239. The left column is main, and the right column is the branch. [acceptance-reveals-contrast.png](acceptance-reveals-contrast.png) shows the same crops at three times the contrast.

- On both sides and in both rows, the reveals are a smooth grey. No crop shows speckle or a bright strip, also at three times the contrast.
- The pixel noise of the reveals is the same on both sides. It is the standard deviation of the luminance minus its 3 × 3 mean, in units of 1/255:

| Image | Side | Large reveal, x 1185 to 1208, y 327 to 389 | Small reveal, x 1001 to 1007, y 338 to 381 |
|---|---|---:|---:|
| Capture `cabin` | main | 0.23 | 0.38 |
| Capture `cabin` | branch | 0.23 | 0.37 |
| Frame 239 | main | 0.24 | 0.33 |
| Frame 239 | branch | 0.23 | 0.32 |

Reading: the speckle and the strip needed the base without the shadow fix. That base lit a thin strip of each jamb with leaked sunlight (`t8-rjno`). The bright vertical strip was most likely that leak itself, and the speckle the rare rays of the reveal that hit it. The base's 10 % rule spread the same light as the white glow of the left column. With the fix on both sides, nothing remains to probe. So this record names no cause with a probe of the reveal's rays. The ticket `t53-yy6p` holds a note with this answer.

## Point 2: the false sill hits

The sill census of [stage 1b](stage-1b/pictures.md), part 1, ran on the branch. [census-port.patch](acceptance-tools/census-port.patch) ports it. The port applies stage 1b's `census.patch` to `dd800356b` with one change: the branch keeps no image of the back distance, so the census marks no hit pixel as a sheet. The sill census does not use that mark in its classes.

The run: rect-lamps, the person's camera, a film of 600 frames with the draws 200 to 599 counted, `--size 1920x1080 --bloom off --exposure 2.796`, on the M1 Max. The boxes are stage 1b's, in render pixels: the glow area `587:107:693:200` and a quiet patch `507:93:547:160`.

The bright census counts each specular sample of luminance 50 or more, pre-exposed. Over the 400 draws, it counted no sample in either box: 0 samples of any class.

| Rule | Class 0, the false hits: samples / light | Class 1, the real hits | Source |
|---|---|---|---|
| 10 % rule, stage 1b | 6,783 / 3,011,681 | 1,260 / 1,036,322 | Stage 1b, the RTX 2000 Ada, before the shadow fix |
| Layer as the slab, stage 1b's probe | 4 / 483 | 602 / 488,239 | The same |
| The branch, `dd800356b` | 0 / 0 | 0 / 0 | This record, the M1 Max, with the shadow fix |

A census that counts nothing can also be a census that does not run. So the every-sample mode (`TERTIUS_PROBE_SILL_ALL=1`) ran too. It counts every sample of the glow area, bright or not:

| Run | Class 0 | Class 1 | Class 2 | Walk misses again |
|---|---|---|---|---|
| Stage 1b, layer as the slab | 742,533 / 121,311 | 2,692,654 / 896,657 | 27,208 / 4,723 | 480,805 / 22,366 |
| The branch | 742,689 / 114,338 | 2,692,960 / 385,264 | 27,150 / 4,467 | 480,401 / 21,792 |

- The counts of the branch match stage 1b's layer within 0.1 % in every class. So the census runs, and the branch decides hits as the probe did.
- The class-1 light falls from 896,657 to 385,264. The mean class-1 sample is 0.143. The bright threshold is 50.
- The quiet patch of the every-sample run: class 0 190,230 / 25,254, class 1 818,740 / 97,209, class 2 8,078 / 1,121, misses 54,952 / 397.

Reading: the false sill hits are 0 among the bright samples, against 6,783 under the 10 % rule. The light of the real hits fell too, because the shadow fix removed the leaked sunlight that the hit surfaces carried. The census of main today, the 10 % rule with the shadow fix, did not run: the port targets the branch. Stage 1b's dim class-0 samples below the end window stay, as stage 1b described them: hits through the sill's top face, which the census cannot see.

## Point 3: no new leaks or holes, and no visible bug

Each scene has one still per side, the frame 239 of a film of 240 frames, and one film per side. The fixed exposure is the automatic exposure of main's frame 239:

| Scene | Script | Stops |
|---|---|---:|
| rect-lamps | [stills-rect-lamps.json](stage-1b/tools/pictures/stills-rect-lamps.json) | 3.823005 |
| Cabin | [stills-cabin.json](stage-1b/tools/pictures/stills-cabin.json) | −0.40080005 |
| Deck | [stills-deck.json](stage-1b/tools/pictures/stills-deck.json) | 0.707066 |

Every frame of every film held its fixed exposure.

### The measure of a change

`regions.py` is stage 1b's measure, with main as the base. A tile is 16 × 16 output pixels, and its value is the mean of the signed luminance difference, branch minus main, in units of 1/255. The noise floor is the largest tile difference between main's frames 231 and 239. A region is a connected set of tiles of one sign above the noise floor. In the deck, the 112 tiles around the walking person are left out, as in stage 1b.

`films.py` measures the flicker of each film. A tile's flicker is the mean absolute change of its pixels' luminance between consecutive frames, over the frames 180 to 239. A tile flickers more on the branch when its flicker is at least twice main's and at least 2/255 above it.

| Scene | Mean absolute luminance difference: branch against main / noise pair | Noise floor | Tiles above it: brighter / darker | Mean flicker: main / branch | Tiles that flicker more on the branch |
|---|---|---:|---|---|---:|
| rect-lamps | 0.275 / 0.188 | 1.37 | 0 / 113 | 0.035 / 0.035 | 0 |
| Cabin | 2.144 / 0.527 | 5.16 | 672 / 197 | 0.116 / 0.121 | 0 |
| Deck | 0.551 / 0.226 | 4.74 | 0 / 42 | 0.047 / 0.046 | 0 |

The regions of 10 tiles or more, and for rect-lamps all of its small regions in one row:

| Scene | Place | Tiles | Mean | Peak |
|---|---|---:|---:|---:|
| rect-lamps | The far wall, the openings of the side windows and the floor below them, x 400 to 1504, y 416 to 592 | 113 in 16 regions of 1 to 19 tiles | −1.4 to −4.1 | −5.1 |
| Cabin | The left wall's window frames and the jamb between two pillars, x 560–640, y 368–880 | 120 | +20.1 | +44.6 |
| Cabin | The beam and the upper frames of the left wall, x 416–720, y 80–368 | 119 | +13.4 | +32.0 |
| Cabin | A round patch of the floor, x 720–992, y 720–944 | 171 | +6.9 | +10.0 |
| Cabin | The partition wall and the wall left of it, x 1328–1664, y 400–784 | 186 | +6.3 | +11.7 |
| Cabin | The corridor's far end, x 640–944, y 352–688 | 150 | −7.0 | −16.0 |
| Cabin | Beside the right windows, x 1600–1920, y 336–368, and x 1200–1248, y 352–464 | 21 and 13 | +5.7 and +6.3 | +7.9 |
| Cabin | The far floor before the end window, x 880–1024, y 336–400 | 12 | −6.1 | −6.9 |
| Deck | The ship's interior seen through its glass, right and center, x 1104–1504, y 256–368 | 22 and 11 | −5.5 and −5.9 | −7.7 |

### What the stills and films show

I looked at every still at half size, at four crops at full size, and at frames 5, 30, 90 and 160 of each branch film.

- rect-lamps: both stills show a smooth, dark room. No speck, no sparkle, no blotch and no light through a shadow. The two sides look alike. [acceptance-rect-lamps-side-window.png](acceptance-rect-lamps-side-window.png) shows the nearest right side window and the floor below it, x 1560 to 1919, y 380 to 899. Main's jamb holds one bright pixel, and the branch's has none.
- Cabin: both stills are clean. The branch's left window frames and the jamb between two pillars are lighter and bluer. [acceptance-cabin-window-frames.png](acceptance-cabin-window-frames.png), x 400 to 779, y 60 to 899, shows them. Main's jambs between the pillars are black strips, and the branch's are mid-blue. [acceptance-cabin-corridor-end.png](acceptance-cabin-corridor-end.png), x 620 to 999, y 300 to 699, shows the corridor's far end: the branch's floor there is a little darker.
- Deck: [acceptance-deck-ship-frames.png](acceptance-deck-ship-frames.png), x 1040 to 1559, y 180 to 419, shows the ship's frames and glass. The two sides look alike to me.
- The sampled film frames show no transient speck, blotch or flash on any scene.

Reading: no scene shows a new leak, a hole or a visible bug. The cabin's regions are those of stage 1b's `layer` column, with the same signs and means within 1. Stage 1b named the window frames' change "contact darkening weaker". No path trace explains a difference here: the stage 2 traces of the cabin are of other views and other lights, so none stands beside these stills.

The films are outside the repository, at 1920 × 1080, 30 frames per second, H.264:

- `/Users/alexispurslane/.t3/worktrees/tertius/t3-7428be87-0b475c01d80269e9-evidence/stills/<scene>/film-main.mp4`
- `/Users/alexispurslane/.t3/worktrees/tertius/t3-7428be87-0b475c01d80269e9-evidence/stills/<scene>/film-branch.mp4`

The frames 231 and 239 of each side are lossless WebP files in `.../stills/<scene>/out/pack/`.

## Point 4: contact darkening

The crops of point 3 are the evidence. My judgment by eye:

- Cabin, the thick pillars of the left wall: the branch darkens the jambs between the pillars less. Main's 10 % slab hides them in black, and the branch's slab, the real thickness of the pillar, lets more light reach them. The pillars' edges stay distinct, and no frame looks flat or lit from inside.
- Cabin, the corridor's far end: the branch darkens the floor and the end window's frames slightly more, by 7 of 255 on the mean.
- rect-lamps: the window reveals and the floor's contact with the walls look the same on both sides.
- Deck: no difference that I can see.

Reading: the change of contact darkening is small, and it goes both ways. Nothing in the crops looks wrong to me.

## Point 5: the room's brightness, a note

The mean linear luminance of the ticket's three boxes in rect-lamps' frame 239, in W/(m² sr). `brightness.py` inverts the engine's exposure, tone curve and sRGB encoding at 3.823005 stops, with bloom off.

| Box | Output pixels | Engine before the fixes | Engine with the layer, before the shadow fix | Main today | Branch today | Path trace, lamps alone | Path trace, sun alone |
|---|---|---:|---:|---:|---:|---:|---:|
| Side wall | x 1300 to 1500, y 300 to 440 | 0.0313 | 0.0146 | 0.0127 | 0.0128 | 0.0236 | 0.0086 |
| Jamb | x 1760 to 1798, y 500 to 580 | 0.456 | 0.049 | 0.0074 | 0.0073 | 0.0147 | 0.066 |
| Floor patch | x 1700 to 1900, y 700 to 880 | 0.0283 | 0.0123 | 0.0102 | 0.0101 | 0.0279 | 0.0112 |

Reading: main and the branch now give the same room within 1 %. The shadow fix took the leaked sunlight out of main, and with it the light that the 10 % rule's false hits spread. So the layer no longer darkens rect-lamps by about 64 of 255, as stage 1b measured. Both sides are darker than the path trace's sum, which is lost light and no bug by the user's bar.

## Point 6: the frame cost

### The frame cost per scene and machine

Branch minus main, in ms. The card's values are rounds 2 to 6, and the Mac's are its kept rounds. The spread is main's slowest run minus its fastest over the same rounds. Every mean difference is larger than main's spread, and every round of every scene has the branch slower. So no cell needs the marks † and ‡ of stage 1b.

| Scene | Machine | Main: mean | Branch: mean | Branch minus main: mean (lowest to highest) | Main's spread | Rounds |
|---|---|---:|---:|---|---:|---:|
| Deck | RTX 2000 Ada | 8.456 | 8.563 | +0.107 (+0.088 to +0.133) | 0.024 | 5 |
| Deck | M1 Max | 18.645 | 18.829 | +0.184 (+0.111 to +0.258) | 0.108 | 12 |
| Cabin | RTX 2000 Ada | 9.417 | 9.555 | +0.139 (+0.075 to +0.174) | 0.058 | 5 |
| Cabin | M1 Max | 20.370 | 20.635 | +0.265 (+0.238 to +0.310) | 0.039 | 12 |
| rect-lamps | RTX 2000 Ada | 6.982 | 7.057 | +0.075 (+0.031 to +0.100) | 0.037 | 5 |
| rect-lamps | M1 Max | 14.553 | 14.734 | +0.181 (+0.154 to +0.203) | 0.042 | 12 |
| Crowd | RTX 2000 Ada | 9.206 | 9.764 | +0.558 (+0.454 to +0.637) | 0.114 | 5 |
| Crowd | M1 Max | 15.462 | 16.209 | +0.747 (+0.678 to +0.808) | 0.127 | 12 |

### On the M1 Max

One take per scene, each one exclusive hold of the GPU lock through `mac-take.sh`. A take runs one untimed run of main, then 12 rounds of both sides: even rounds run main first, odd rounds the branch first. Every run used `--video none --in-flight 2 --pass-timestamps off --exposure <stops>` beside `tools/gpu-clock.sh`. A run's value is its median GPU frame span over 120 frames, in ms. `cost_report.py` drops a round whose two runs' mean clocks differ by more than 1 MHz.

| Scene | Script | Stops, from main's first run | The take's time | 1-minute load over the take's runs | Dropped rounds, for the clock |
|---|---|---:|---|---|---|
| Deck | `scripts/temporal-upscale-timing.json` | 0.70300125 | 22:01 to 22:05 | 2.3 to 6.1 | none |
| Cabin | `scripts/ter-kh4g-cabin-timing.json` | −0.43325411 | 22:54 to 22:57 | 2.4 to 3.4 | none |
| rect-lamps | `scripts/ter-kh4g-rect-lamps-timing.json` | 3.87313185 | 22:47 to 22:50 | 1.9 to 3.5 | none |
| Crowd | `scripts/ter-kh4g-crowd-timing.json` | −9.249871 | 22:51 to 22:53 | 2.6 to 4.5 | none |

- Every kept run held 1295.77 to 1296.00 MHz.
- The first takes of the cabin, rect-lamps and the crowd, at 22:06 to 22:22, ran while other work raised the load to 17.6 to 31.9. They dropped 1 to 4 rounds each for the clock. After the Release suite, the Mac was quiet, and the three scenes were taken again at the same stops. The table holds the quiet takes. The first takes gave +0.291, +0.170 and +0.757 ms, within 0.03 ms of the quiet takes' means. They are in `.../cost/mac/`, and the quiet takes in `.../cost/mac-retake/`.
- The Mac has no pass times, because MoltenVK's pass times are not valid alone. So the pass "back faces" and the depth pyramid have no own time on the Mac.
- The four takes before these ran without a clock, because stage 1b's `gpu-clock` binary was gone. They stay out of the tables. They are in `.../cost/mac-noclock/`.

Reading: the layer costs the Mac 0.18 to 0.27 ms in the rooms, 1.0 % to 1.3 % of the frame, and 0.75 ms in the crowd, 4.8 %. The Mac's change is 1.3 to 2.4 times the card's. WORKBENCH.md's usual ratio of 2.4 holds for the rooms more than for the crowd, where the pass's vertex work dominates. Stage 1b's probe cost the Mac 1.014 ms in the crowd, so the finished pass costs the Mac less than the probe did.

### On the RTX 2000 Ada

Two jobs ran `cloud-cost.sh`: job A the deck and the cabin, job B rect-lamps and the crowd. Per scene: one run of main with the automatic exposure, then `ab_run.sh` with that exposure fixed, a warm-up of main, and six rounds of both trees in an order that reverses every round. A run's value is its median GPU frame span without the `capture` pass, in ms. A difference pairs the two sides inside one round. Round 1 is left out, as in stage 1b, because the first run of the branch on the deck was 0.55 ms slower than its later runs. Each cell gives the mean, then the lowest and the highest round.

| Scene | Stops | Main: mean (lowest to highest, spread) | Branch: mean (spread) | Branch minus main, rounds 2 to 6 | Share of main's frame |
|---|---:|---|---|---|---:|
| Deck | 0.70066695 | 8.456 (8.444 to 8.468, 0.024) | 8.563 (0.057) | +0.107 (+0.088 to +0.133) | +1.3 % |
| Cabin | −0.438143655 | 9.417 (9.392 to 9.451, 0.058) | 9.555 (0.065) | +0.139 (+0.075 to +0.174) | +1.5 % |
| rect-lamps | 3.86628305 | 6.982 (6.964 to 7.001, 0.037) | 7.057 (0.073) | +0.075 (+0.031 to +0.100) | +1.1 % |
| Crowd | −9.250481 | 9.206 (9.129 to 9.243, 0.114) | 9.764 (0.172) | +0.558 (+0.454 to +0.637) | +6.1 % |

Every round of every scene has the branch slower. Every mean difference is above main's spread.

The passes: each value is the median over the six runs of a pass's median, then the per-round difference, branch minus main, over rounds 2 to 6, in ms.

| Scene | Back faces, own time | Depth pyramid: main / branch / difference | Surface test: difference | Reflection rays: difference | Diffuse rays: difference |
|---|---:|---|---:|---:|---:|
| Deck | 0.048 | 0.150 / 0.225 / +0.075 | +0.007 | −0.025 | −0.002 |
| Cabin | 0.056 | 0.157 / 0.237 / +0.080 | +0.009 | −0.026 | +0.004 |
| rect-lamps | 0.050 | 0.155 / 0.233 / +0.078 | +0.005 | −0.038 | 0.000 |
| Crowd | 0.482 | 0.106 / 0.145 / +0.039 | 0.000 | −0.007 | +0.016 |

- The pass "back faces" takes 0.048 to 0.056 ms in the rooms and 0.482 ms in the crowd. Each of its five rounds lies within 0.001 ms of its mean.
- The depth pyramid takes 0.075 to 0.080 ms more in the rooms and 0.039 ms more in the crowd. Its finest dispatch now evaluates each pixel's surface. Stage 1b's probe added 0.010 to 0.012 ms there.
- The reflection rays take 0.025 to 0.038 ms less in the rooms.

The figures, the design's decision 8. The crowd holds 100 skinned figures of 20,202 vertices each.

| Quantity on the RTX 2000 Ada | ms |
|---|---:|
| The pass "back faces" in the crowd | 0.482 |
| The same per figure | 0.0048 |
| The crowd's G-buffer pass on main, which draws the same figures | 1.298 |
| The crowd's deformation of the figures, `deform.comp` (the design, from `t1-j03y`) | 1.275 |
| The whole frame's change in the crowd | +0.558 |

Reading: the layer costs 0.08 to 0.14 ms in the rooms on the card, 1.1 % to 1.5 % of the frame. The depth pyramid's new work is about 0.08 ms of that, and the pass about 0.05 ms. In the crowd, the pass adds 37 % to what the G-buffer pass already spends on the figures. Stage 1b's probe saved 0.109 ms in rect-lamps, from 0.044 ms of diffuse rays and 0.071 ms of reflection rays. Today the branch saves no diffuse-ray time there and 0.038 ms of reflection rays, and main's diffuse rays cost 0.566 ms against 0.622 ms in stage 1b. I did not find which change of main between stage 1b and `d8559eb58` did that.

## Point 7: the suites

### The Mac's suites

The full suites ran on the M1 Max on the branch's code, one configuration at a time, from this worktree: `nix develop --command bash -c 'dotnet test --solution Tertius.slnx -c Checked'`, then the same with `-c Release`. The worktree's commits after `dd800356b` change only documents and tools.

| Project | Checked: passed / skipped | Release: passed / skipped |
|---|---|---|
| Core | 1,207 / 0 | 1,175 / 0 |
| Simulation | 2,007 / 0 | 2,026 / 0 |
| Physics | 1,492 / 8 | 1,493 / 8 |
| Host | 2,079 / 8 | 2,079 / 8 |
| Rendering | 3,298 / 12, in 17 min 2 s | 3,297 / 12, in 22 min 35 s |
| Slop | 597 / 0 | 597 / 0 |
| Style | 276 / 0 | 276 / 0 |
| Unions | 59 / 0 | 59 / 0 |
| All | 11,043 tests, 0 failed, 28 skipped | 11,030 tests, 0 failed, 28 skipped |

The Checked suite ran from 21:42 to 22:00, and the Release suite from 22:22 to 22:46. No timing test failed, so no class needed a rerun.

### The card's suites

The Rendering tests of the branch `dd800356b` ran on the RTX 2000 Ada under Linux, NVIDIA driver 595.112.07, in six GPU shards of `tools/runpod/shards.py`, with `-maxThreads 2 -longRunning 60`, as WORKBENCH.md gives them.

| Shard | Job | Tests | Passed | Failed | Skipped |
|---|---|---:|---:|---:|---:|
| 0 | `5a2c6325` | 587 | 587 | 0 | 0 |
| 1 | `57b33e32` | 530 | 529 | 0 | 1 |
| 2 | `2a1f70d1` | 543 | 542 | 0 | 1 |
| 3 | `2efd39c6` | 457 | 449 | 0 | 8 |
| 4, first run | `cd6d064e` | The test host crashed: segmentation fault, exit 139 | | | |
| 4, second and third run | `df47171c`, `b6839276` | 713 each | 712 each | 0 | 1 each |
| 5 | `b34002d9` | 480 | 480 | 0 | 0 |
| All six, with shard 4's second run | | 3,310 | 3,299 | 0 | 11 |

Shard 4's first run discovered 435 test cases and printed one static skip. About 10 s later, the host crashed with no result file. The default reporter names no running test. A fourth run, `d73cf861`, ran shard 4's filters with per-test output on the branch and on main, each for 380 s: 684 and 653 test cases finished, with no failure and no crash. So the crash came once in four runs of the branch, and three runs were clean, one of them partial. Bug ticket `t70-o38b` holds it as a rare fault outside this acceptance.

## Point 8: Linux on a driver beside NVIDIA

The driver is Mesa's lavapipe: llvmpipe, Mesa 26.2.4 with LLVM 21.1.8, 256 bits, Vulkan 1.4.354. The worker's dev shell holds no Linux driver apart from NVIDIA's. So [lavapipe-tests.sh](acceptance-tools/lavapipe-tests.sh) builds the pinned nixpkgs' `mesa`, which the binary cache gives, and names its lavapipe manifest in `VK_ICD_FILENAMES`. The tests ran on Runpod CPU workers of 2 processors and 4 GB, in the Checked configuration, with `-maxThreads 2`.

| Run | Job | Classes | Tests | Failed | Skipped | Time of the tests |
|---|---|---|---:|---:|---:|---:|
| The probe | `90eaab13` | BackFacePassTests, BackFaceSlabWalkTests, PyramidRenderTests | 41 | 0 | 0 | 326 s |
| The changed test files, shard 0 of 3 | `0a7bbd78` | 11 classes | 326 | 0 | 1 | 202 s |
| The same, shard 1 | `edb064ec` | 10 classes | 199 | 0 | 2 | 287 s |
| The same, shard 2 | `a1e4a24a` | 10 classes | 185 | 0 | 0 | 443 s |
| The whole project, shard 0 of 4 | `f36313c1` | every class, by `shards.py`'s rule | 802 | 2 | 1 | 635 s |
| The same, shard 1 | `4920cf72` | | 672 | 1 | 3 | 1,496 s |
| The same, shard 2 | `02e14158` | | 1,041 | 1 | 1 | 1,150 s |
| The same, shard 3 | `5b5233d7` | | 795 | 2 | 8 | 1,217 s |
| The whole project | | | 3,310 | 6 | 13 | |

The changed test files are the 32 files of `tests/Tertius.Rendering.Tests` that the branch changes against main.

The six failures, and the same six tests on main `d8559eb58` in job `b64af51d`:

| Test | The branch | Main |
|---|---|---|
| `ScreenWalkBoundaryTests.TheShaderHitsTheNextWallPixelOnlyWithinItsSegmentAndStepCap(channel: 3)`: a walk of a 4-pixel segment with a cap of 1 step must hit the next wall pixel | Misses: 2 (WalkMiss) where 1 (WalkHit) is expected | The same |
| `HairRenderTests.AHairPixelSendsTheLightThatHairFiberGives(…, lamp: True)` | Off by 0.000196 against a bound of 0.000183 | The same |
| `SkinScatterRenderTests.TheDiffuseImageHoldsDOfSkinZeroAtOtherPixelsAndTheSkinMask` | D of a skin pixel off by 0.0019 against a bound of 0.0018 | The same |
| `OutlineRenderTests.TheOverlayDrawsTheOutlineOfEachKindOfFrameOverTheScene` | 5 pixels above FLIP 0.1 (largest 0.147) | The same |
| `MeteringDrawTests.AStillRightAfterACameraCutAtItsTickShowsTheExposureOfTheNewView` | 5.3726 stops against 5.3884 ± 0.01 | The same |
| `Fsr3UpscalerTests.TheSettledPatternAndTheFramesAfterAChangeOfTheSceneMatchTheirReferenceImage` | 201 pixels above FLIP 0.1 (largest 0.320) | The same |

The 13 skips:

- Eleven skip on every driver. Nine wait for an environment variable that turns on a measurement. Two wait for the bug tickets `t53-eyrv` and `ter-186q`.
- Two skip on lavapipe only: `GpuTimingTests.APassThatFillsABufferAddsMoreThanNearlyNoTimeToTheFramesGpuSpan` and `APassThatRecordsNothingAddsNearlyNoTimeToTheFramesGpuSpan`. The test skips itself when its interval of the frame spans is wider than 0.05 ms. On the shared 2-processor worker, the narrowest interval was 0.27 and 0.31 ms wide.

Reading: every test of the changed files passes on lavapipe, apart from those two self-skips. The six failures are main's on lavapipe, with the same values, so the layer did not cause them. Bug ticket `t70-0pr5` lists them outside this acceptance.

## The commands

Run them from the worktree's root. `<evidence>` is `/Users/alexispurslane/.t3/worktrees/tertius/t3-7428be87-0b475c01d80269e9-evidence`, and `tools` is `docs/research/back-face-depth-layer/acceptance-tools`. The two Release demos are in `/private/tmp/t1-8b4w/bin-main` and `bin-branch`, from `dotnet build src/Tertius.Demo -c Release -o <folder>` in each tree.

The stills and films of one scene (point 3), and the analysis:

```bash
nix develop --command bash -c 'bash tools/gpu-lock.sh --shared <evidence>/stills/rect-lamps-lock.log bash $tools/mac-stills.sh <evidence>/stills rect-lamps docs/research/back-face-depth-layer/stage-1b/tools/pictures/stills-rect-lamps.json'
nix develop --command bash -c 'python3 -I $tools/regions.py <evidence>/stills/deck branch 16 672,416,800,640'
nix develop --command bash -c 'python3 -I $tools/films.py compare <evidence>/stills/deck/flicker-main.npy <evidence>/stills/deck/flicker-branch.npy 672,416,800,640'
nix develop --command bash -c 'python3 -I $tools/brightness.py <evidence>/stills/rect-lamps 3.823005'
```

Point 1 ran `scripts/rect-lamps.json` with each demo under the shared lock, with `--headless --out <evidence>/reveal/<side> --exposure 3.341`.

The census (point 2), from the scratch commit's build in `/private/tmp/t1-8b4w/bin-census`. The every-sample run adds `TERTIUS_PROBE_SILL_ALL=1`:

```bash
TERTIUS_PROBE_SILL_CENSUS=1 TERTIUS_PROBE_CENSUS_BOXES='glow:587:107:693:200;quiet:507:93:547:160' TERTIUS_PROBE_CENSUS_OUT=<evidence>/census/branch TERTIUS_PROBE_CENSUS_FROM=200 TERTIUS_PROBE_CENSUS_TO=600 \
  nix develop --command bash -c 'bash tools/gpu-lock.sh --shared <evidence>/census/lock.log /private/tmp/t1-8b4w/bin-census/Tertius.Demo --script docs/research/back-face-depth-layer/stage-1b/tools/pictures/census-rect-lamps.json --headless --out <evidence>/census/run --size 1920x1080 --bloom off --video none --exposure 2.796'
```

The frame cost (point 6):

```bash
nix develop --command bash -c 'bash $tools/mac-take.sh <evidence>/cost/mac-retake ter-kh4g-cabin-timing 0 12'
python3 -I $tools/cost_report.py mac <evidence>/cost/mac-retake
bash $tools/cloud-cost.sh <evidence>/cost/cloud dd800356bffd8f0b6280ab9afd72869a4de03587 A B
python3 -I $tools/cost_report.py cloud <evidence>/cost/cloud/jobA <evidence>/cost/cloud/jobB
```

The card's shard i of 6 (point 7), as WORKBENCH.md gives it, with the tree `main=dd800356b`. The two full reruns of shard 4 added `-reporter verbose`. A lavapipe shard i of 4 (point 8), from a commit that holds the tool:

```bash
python3 -I tools/runpod/gpu_job.py submit --agent t1-8b4w-takes --kind cpu --tree t=2035e46c1 \
  --run "git -C t show HEAD:$tools/lavapipe-tests.sh > lavapipe-tests.sh && CLASS_STEMS='*' SHARD='4 i' bash lavapipe-tests.sh t out" \
  --output 'out/steps.txt' --output 'out/tests.json' --output 'out/tests.log' --timeout 3600 --out <evidence>/cloud/lavapipe-full<i>
```

The job's checkout leaves out `docs/`, so the command reads the tool from git. The changed-file shards set `CLASS_STEMS` to the 32 file names, and the six on main ran the tool twice in one job, with `-method` filters.

## The cloud dollars

By the job log's estimates, `~/.local/state/tertius/gpu-jobs/jobs.jsonl`, agent `t1-8b4w-takes`: 22 jobs, $1.0724.

| Work | Jobs | Dollars |
|---|---:|---:|
| The cloud timing, jobs A and B | 2 | 0.1097 |
| The card's Rendering shards, with the crashed shard 4 | 6 | 0.4719 |
| Shard 4's rerun with per-test output on both sides, which met the job's time limit | 1 | 0.1452 |
| Shard 4's two full runs, by the line orchestrator's order, which added $0.35 to the budget | 2 | 0.1885 |
| Lavapipe: two failed starts, the probe, the changed files, the whole project and the six on main | 11 | 0.1571 |

So the ticket's own work counted $0.8839 of its $1.00, and the order counted $0.1885 of its $0.35.

## Changes to the method

- Main's side is `d8559eb58`, the main that the branch holds, not main at the time of the take. The ticket's note of 2026-11-21 allows it.
- The stills ran on the Mac, not in a cloud job, to keep the cloud money for the suites. Both sides ran on the one Mac, so their images compare.
- Point 1 needed no probe of the reveal's rays, because the speckle is gone on both sides.
- The census ran on the Mac. Stage 1b's census ran on the card, so its counts are another machine's. The every-sample counts agree within 0.1 % anyway.
- `films.py` is new: stage 1b had no measure of flicker.
- The first four Mac takes ran without a GPU clock: stage 1b's `gpu-clock` binary in `/private/tmp/tertius-survey-20261109/clockbin` is gone. I built it again from `tools/gpu-calibration/gpu-clock.c` and took all four scenes again. The first takes stay out of the tables.
- With two sides, the Mac order alternates by round, in place of a Williams square of four sides.
- The cloud timing ran six rounds per side in two jobs of two scenes each, because a GPU job runs 840 s at most.
- The Linux run used a CPU worker, because lavapipe runs on the CPU and a CPU worker costs an eighth of a GPU worker. A job builds the pinned nixpkgs' `mesa` there, because the image has no lavapipe.
- The first Linux plan was the classes of the changed test files, because the probe's 41 heavy tests took 326 s. The changed files then ran in 930 s, so the whole project ran too.
