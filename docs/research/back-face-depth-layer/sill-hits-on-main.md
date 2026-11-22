# The false sill hits on main, before and after the back-face layer

This is the record of the last step of ticket `ter-ukw6`, of 2026-11-22. It measures the false sill hits on main before the back-face layer merged (`c4c1f1f2f`, "before") and after the merge (`821481a5c`, "after"). Both sides hold the fix of the sun's shadow leak (`t8-rjno`). Before uses the 10 % rule: a pixel's slab reaches 10 % of its distance behind its surface. After uses the layer: a pixel's slab ends at its real back face. The method comes from the ticket's note of 2026-11-22. The terms are those of [stage 1b](stage-1b/pictures.md), part 1, and of [the acceptance takes](acceptance.md).

Each section keeps the measured facts and my reading apart. A sentence that starts with "Reading:" holds my interpretation. Every other sentence states a measurement, a count or what an image shows.

## The result in short

| Part | Before | After | What an eye sees |
|---|---|---|---|
| 1. rect-lamps as the scripts give it | 0 bright samples of any class | 0 bright samples of any class | The two stills look the same. |
| 2. A sunlit nose sill seen almost edge-on | The sill's false hits hold 29.2 % of the ceiling cloud's bright reflection light: 46,788 samples, light 10,503,279 | 0 false hits on the sill. Up to 2.6 % of the bright light is of a kind that the census cannot decide. | The ceiling's speckle cloud is sparser and dimmer. It stays, because its other hits are real. |

- The false sill hits of the ticket are gone among the bright samples in both parts. So the ticket can close.
- The speckle cloud on the ceiling of the sunlit view stays after the merge. In the cloud box after the merge, 83.4 % of its bright reflection light is real hits on the sunlit floor patch, and 16.6 % is real hits on the sunlit sill. It is a visible bug of its own, with the mechanism of ticket `ter-12xp`: rare reflection rays that reach a small, very bright surface. `ter-12xp` holds a note with these numbers.

## How the measurement was made

- Before is a Release demo built from a second tree, `/private/tmp/ter-ukw6/before` at `c4c1f1f2f`. After is a Release demo built from this worktree at `821481a5c`. Between `d8559eb58`, the main side of the acceptance takes, and `c4c1f1f2f`, nothing under `src` changed.
- The census builds apply [census-port.patch](acceptance-tools/census-port.patch) to each side. The patch applies to `821481a5c` as it is. It applies to `c4c1f1f2f` with `git apply -3`, because the context of its hunk in `VulkanRenderer.cs` moved. No engine code changed in the record's commit: each census build had its patch only while it built.
- All runs took place on the M1 Max, under the GPU lock's shared mode. Other work loaded the Mac: the load at the end of each run is in the tables of the commands. A still or a census does not measure time, so the load does not change its numbers.
- A still is frame 239 of a film of 240 frames, at 1920 × 1080 with bloom off, at one fixed exposure for both sides. The exposure is the automatic exposure of frame 239 on the before side.
- The census counts draws 200 to 599 of a film of 600 frames, at 1920 × 1080 (render 1280 × 720), with bloom off and the fixed exposure. The census builds use the same exposure as the stills of the same view, except in part 1, which keeps stage 1b's 2.796 stops.
- The classes are those of stage 1b. The census takes each reflection sample and follows its ray to the hit. It then projects the ray's crossing of the hit pixel's plane onto the screen. Class 0, an opening: there the eye sees the sky, or a surface more than 2 % farther than the crossing. Class 1, the hit surface: the eye sees a surface within 2 %. Class 2: a surface more than 2 % nearer hides the crossing. Class 3: undetermined. The bright census takes only the samples of luminance 50 or more, pre-exposed. The every-sample census takes every sample.
- The tools are in [sill-hits-tools](sill-hits-tools). They are tool code. `census.sh` runs both sides of one census. `stills.sh` makes the stills of both sides. `video.sh` makes the side-by-side video. `sources.py` splits a census by class and by the place of each sample's hit pixel, and `test_sources.py` tests it. `pair.py` makes a crop with before on the left and after on the right.

## Part 1: rect-lamps as the scripts give it

The view is the person's camera at the start of `orbit`. The sun stands straight above the ship, so no sunlight comes in. The boxes are stage 1b's, in render pixels: the glow area `587:107:693:200` on the ceiling, and a quiet patch `507:93:547:160`.

### The census

The bright census counted no sample in either box on either side over 400 draws: 0 samples of any class.

A census that counts nothing can also be a census that does not run. So the every-sample census ran too. The glow area, samples / light:

| Side | Class 0 | Class 1 | Class 2 | Walk misses again | Total light | Class 0's share of the light |
|---|---|---|---|---|---:|---:|
| Before | 868,767 / 127,545 | 2,696,141 / 381,025 | 27,179 / 4,552 | 351,113 / 12,421 | 525,544 | 24.27 % |
| After | 742,689 / 114,338 | 2,692,960 / 385,264 | 27,150 / 4,467 | 480,401 / 21,792 | 525,861 | 21.74 % |

- After equals the acceptance's run of the branch exactly, in every class.
- The before counts are within 0.03 % of stage 1b's counts of the 10 % rule (868,891 class-0 samples). Their light is 3.9 % of stage 1b's (3,241,853), because the shadow fix removed the leaked sunlight that lit the sill.
- The mean class-0 sample is 0.147 before and 0.154 after. The bright threshold is 50.
- The class-0 light on the edge rows 375 and 376, the first rows below the end window, falls from 16,828 to 2,345. The far wall below the window holds most of the rest: rows 380 to 419, 109,832 before and 111,502 after. Stage 1b names those samples as the census's blind spot: real hits through the sill's top face.
- The quiet patch: class 0 192,322 / 24,966 before and 190,230 / 25,254 after. Class 1 818,834 / 95,912 before and 818,740 / 97,209 after.

### The stills

The fixed exposure is 3.823005 stops, the same as the acceptance takes. Both films held it on every frame.

[sill-hits-rect-lamps.png](sill-hits-rect-lamps.png): output pixels x 640 to 1279, y 0 to 619, at full size. It shows the ceiling, the end wall and the end window. Before is on the left and after is on the right. The two halves look the same.

- 669 of the 2,073,600 output pixels (0.032 %) differ by more than 8 of 255 in a channel. The largest difference is 64.
- Those pixels lie on the side windows' reveals and on the edges of the openings, at the far end of the room. A view of the difference at six times its contrast shows only those edges.

Reading: in today's rect-lamps no bright light lies near the sill, under either rule. So part 1 cannot show the layer's effect on the false hits. The dim class-0 light at the edge rows falls by 86 %. That light is too dim to see.

## Part 2: a sunlit nose sill seen almost edge-on

### The view

The script [stills-sunlit-nose.json](sill-hits-tools/stills-sunlit-nose.json) is rect-lamps with one more step: a run of 277,243 ticks before the film. The script format cannot set the sun's direction, so the world's time moves the sun. No demo patch was needed.

- The ship's orbit is a circle of radius 6,771 km, with a period of 5,544.9 s. The ship turns once per orbit, with its belly toward Earth and its nose along its motion. At the start, the sun stands straight above the ship, and the sun's direction lies in the orbit's plane. So in the ship's axes the sun turns in the plane of the ship's up and forward axes.
- 277,243 ticks at 60 a second are 4,620.7 s, 300 degrees of the orbit. Then the sun stands 30 degrees above the ship's horizon, straight ahead of the nose.
- So sunlight comes in through the nose window only. It lights the floor from 1.1 m to 4.6 m in front of the nose wall, and the sill's top face, which is 0.5 m deep. The side windows' reveals get no sun, because the light runs parallel to them.

What lights the glow area. The glow area is the same ceiling box as in part 1, inside the speckle cloud:

- The sun lights no pixel of the ceiling directly, because its light comes in downward. The lamps hang under the ceiling and face down, so they do not light it directly either.
- The ceiling's light is bounce light and reflections of four things: the sunlit floor patch, the sunlit top face of the nose sill, the walls that the lamps light, and the window's sky.
- The sky through the nose window is black space. The sun's disk stands 30 degrees up and Earth about 20 degrees down. Seen from the eye, the window spans about 1.5 degrees above and below.
- From the eye, the sill's top face lies 1 m lower and 39.5 m away. It covers 0.2 render rows: the camera sees it almost edge-on.

[sill-hits-sunlit-sill-close.png](sill-hits-sunlit-sill-close.png) checks that the sill is sunlit. It comes from [look-sunlit-sill.json](sill-hits-tools/look-sunlit-sill.json): the person walks 33.5 m toward the nose and looks about 11.5 degrees down, and the same time passes. The automatic exposure is −7.71 stops. The sill's top face is the brightest strip of the window's rim. The side reveals and the lintel are grey, and the floor patch is bright.

The hit pixel's place names its source. In the census's last frame, the nose window covers render columns 593 to 686 and rows up to 375. Row 376 is the edge row, on the sill or the wall's inner face. Rows 377 to 385 are the wall below the window, at 39.5 m to 39.6 m from the eye. Rows 387 to 389 are the sunlit floor patch, at 37.9 m, 36.5 m and 35.3 m. `sources.py` names these regions:

| Region | Render pixels, x0:y0:x1:y1, x1 and y1 excluded |
|---|---|
| window-edge | `590:374:690:377` |
| wall-below | `590:377:690:386` |
| floor-patch | `570:386:710:390` |

The census ran on two boxes: the glow area `587:107:693:200`, and the cloud `500:0:780:220`, which holds most of the speckle cloud. The quiet patch ran too.

### The census

The bright census at the exposure of the stills, −1.7507356 stops. The cloud, samples / light, with the census's tolerance of 2 %:

| Class and source | Before | After |
|---|---|---|
| Class 0 on the window's edge: false hits on the sill's slab | 46,788 / 10,503,279 | 0 / 0 |
| Class 1 on the window's edge: real hits on the sunlit sill | 15,162 / 6,356,278 | 9,138 / 3,247,583 |
| Class 0 on the floor patch | 126,639 / 8,846,651 | 117,703 / 8,026,542 |
| Class 1 on the floor patch | 144,669 / 9,830,585 | 114,463 / 7,843,233 |
| Class 2 on the floor patch | 5,226 / 403,972 | 5,183 / 396,604 |
| Others | 71 / 8,450 | 1 / 73 |
| All | 338,555 / 35,949,215 | 246,488 / 19,514,034 |
| Class 0's share of the light, as the census counts it | 53.83 % | 41.13 % |

The floor patch's class 0 needed a check. The eye sees the floor almost edge-on there, at about 2.6 degrees. One render row then covers about 1.3 m of the floor's depth, at a distance of about 36 m: 3.6 %. That is more than the tolerance of 2 %. So the same bright census ran again with a tolerance of 5 % and of 15 % (`TERTIUS_PROBE_CROSSING_TOLERANCE=50` and `150`):

| Tolerance | Side | Class 1 on the floor patch | Class 0 on the floor patch | Class 0 on the window's edge | Class 1 on the window's edge |
|---|---|---|---|---|---|
| 5 % | Before | 274,399 / 18,951,754 | 2,135 / 129,455 | 46,788 / 10,503,279 | 15,203 / 6,362,398 |
| 5 % | After | 235,484 / 16,155,534 | 1,865 / 110,844 | 0 / 0 | 9,138 / 3,247,583 |
| 15 % | Before | 276,534 / 19,081,209 | 0 / 0 | 46,788 / 10,503,279 | 15,214 / 6,363,610 |
| 15 % | After | 237,349 / 16,266,379 | 0 / 0 | 0 / 0 | 9,138 / 3,247,583 |

- At 5 %, 98 % of the floor patch's class-0 samples move to class 1. At 15 %, all of them move. The sill's class 0 does not change at any tolerance: 46,788 samples before, and 0 after.
- At 15 %, the floor patch holds 83.36 % of the cloud's bright light after the merge, and the sill's real hits hold 16.64 %.
- The census cannot decide two small groups after the merge. The first is the floor patch's class 2 at 2 % tolerance: 5,183 / 396,604, 2.03 % of the light. The second is its class 0 at 5 % tolerance: 1,865 / 110,844, 0.57 %. Both are real hits seen through a neighbouring row, or rays that left through the window and fell into a floor pixel's slab. So at most 2.6 % of the bright light after the merge can be false.

The glow area tells the same, with half the samples. At 2 %: class 0 on the window's edge 23,399 / 5,275,407 before (29.69 % of 17,770,099) and 0 after. At 5 %, after the merge: the floor patch's class 1 holds 122,715 / 7,778,487 (83.06 % of 9,364,632), the sill's class 1 holds 4,475 / 1,535,771 (16.40 %), and the floor patch's class 0 holds 925 / 50,374 (0.54 %).

The every-sample census, at 2 %, adds the dim samples:

| Box | Side | Class 0 on the window's edge | Class 1 on the window's edge | Class 0 elsewhere (most on the floor patch and the far wall) | Total light of the hits |
|---|---|---|---|---|---:|
| Glow | Before | 139,952 / 6,200,943 | 22,982 / 3,291,570 | 729,065 / 5,808,788 | 28,256,557 |
| Glow | After | 18,616 / 110,105 | 20,059 / 1,702,215 | 724,758 / 5,483,843 | 18,870,264 |
| Cloud | Before | 281,541 / 12,365,639 | 47,851 / 6,786,338 | 3,831,106 / 15,159,747 | 77,800,990 |
| Cloud | After | 37,248 / 219,327 | 41,888 / 3,599,832 | 3,808,491 / 14,156,125 | 56,784,275 |

- After the merge, 18,616 dim class-0 samples stay on the window's edge in the glow area. Their mean light is 5.9, against 44.3 before and a bright threshold of 50.

Reading: the layer removes every bright false hit on the sill's slab: 10,503,279 of light in the cloud before, 0 after. That is the ticket's bug, now with real sunlight on the sill. The dim class-0 samples that stay on the edge row match stage 1b's blind spot. Such a ray passes the wall's inner face above the sill, then falls through the sill's top face. That is a real hit, which the census names class 0 because the sill's top is 0.2 rows tall. I did not check this reading with a separate census.

Reading: the real hits read less light after the merge. The sill's real hits fall from 6,356,278 to 3,247,583, and the floor patch's from 19,081,209 to 16,266,379 (at 15 %). A sample's light is the mix of the miss light and the hit light by the fade, `edgeFade(hit) * (1 - entryShare)`, where `entryShare` is how deep the ray enters the slab as a share of the slab's thickness. The edge row's slab is 0.5 m after the merge against about 3.95 m before. So a real hit at the same depth gets a larger `entryShare` and a smaller fade. I did not measure the fades by source.

### What the layer removed and what stays

| Share of the cloud's bright light before the merge (35,949,215) | Light |
|---|---:|
| Removed: the false hits on the sill's slab | 10,503,279 (29.2 %) |
| Removed: the fall of the real hits' light (reading above) | 5,931,902 (16.5 %) |
| Stays: real hits on the floor patch and the sill (15 % tolerance) | 19,513,962 (54.3 %) |

So the layer removed 45.7 % of the cloud's bright light. Of what stays, 97.4 % or more is real.

### The stills and the video

The fixed exposure is −1.7507356 stops. Both films held it on every frame.

[sill-hits-sunlit-ceiling.png](sill-hits-sunlit-ceiling.png): output pixels x 640 to 1279, y 0 to 419, at full size. It shows the ceiling's speckle cloud between the near lamp and the end wall. Before is on the left and after is on the right. Both halves hold a cloud of bright specks. After, the cloud is sparser and the ceiling is darker.

- The mean 8-bit luminance of the crop falls from 160.0 to 152.0.
- Pixels more than 40 of 255 above the median of their 5 × 5 neighbours: 281 before and 154 after. More than 80: 15 before and 6 after.
- Over the whole image, 33.4 % of the pixels differ by more than 8 of 255 in a channel. The mean 8-bit luminance falls from 100.8 to 93.8.

[sill-hits-sunlit-nose-wall.png](sill-hits-sunlit-nose-wall.png): output pixels x 820 to 1099, y 360 to 599, at twice their size. It shows the nose window and the wall around it. In both halves, the floor patch is a white line under the window. Before, the wall beside and below the window glows brighter. After, that glow is dimmer.

[sill-hits-sunlit-ceiling.mp4](sill-hits-sunlit-ceiling.mp4): the side-by-side video of the view, before on the left and after on the right. It holds the frames 60 to 239 at 60 a second, 3 s, at the same fixed exposure. Each side is cropped to output pixels x 480 to 1439, y 0 to 639. The encoding is H.264 at CRF 12. The specks of the cloud change from frame to frame on both sides. They sparkle, with fewer specks after.

Reading: an eye sees the ticket's bug as the brighter, denser cloud and the glow on the nose wall of the left side. The layer removes it. What stays is a sparkle of real light: about 1 % of the cloud's samples reach a small sunlit surface, many times brighter than the rest of the room. This is the mechanism of `ter-12xp`, "single bright pixels" from rare rays that reach a bright surface. `ter-bdoh`, the sparkle of rect-lamps' glossy floor, has the same signal and scene. The specks on the side walls of the stills were not in a census box.

## What was not measured

- The specks on the side walls of the sunlit view. They lie outside the census boxes.
- A sun that lights a side window's reveal. The orbit's sun turns in the ship's up and forward plane, so no time of the orbit lights a reveal. A reveal needs a patch of the demo that moves the sun out of that plane. The nose sill already tests the ticket's case: a thin sunlit face seen almost edge-on, with an opening behind it.
- The fade of each source, which would check the reading on the real hits' fall.
- The dim class-0 samples on the edge row after the merge, with a census of the sill's top face.
- A census on the cloud machine. Both sides ran on the M1 Max, so their numbers compare.

## The commands

Run them from the worktree's root. `<evidence>` is `/Users/alexispurslane/.t3/worktrees/tertius/t3-b149a58c-eb3542ff177f4d65-evidence`, and `<tools>` is `docs/research/back-face-depth-layer/sill-hits-tools`.

The four demos, one build at a time (about 20 s each):

```bash
git worktree add --detach /private/tmp/ter-ukw6/before c4c1f1f2f
git -C /private/tmp/ter-ukw6/before apply -3 <worktree>/docs/research/back-face-depth-layer/acceptance-tools/census-port.patch
(cd /private/tmp/ter-ukw6/before && nix develop --command bash -c 'dotnet build src/Tertius.Demo -c Release -o /private/tmp/ter-ukw6/bin-before-census')
git -C /private/tmp/ter-ukw6/before reset -q && git -C /private/tmp/ter-ukw6/before checkout -- src
(cd /private/tmp/ter-ukw6/before && nix develop --command bash -c 'dotnet build src/Tertius.Demo -c Release -o /private/tmp/ter-ukw6/bin-before')
nix develop --command bash -c 'dotnet build src/Tertius.Demo -c Release -o /private/tmp/ter-ukw6/bin-after'
git apply docs/research/back-face-depth-layer/acceptance-tools/census-port.patch
nix develop --command bash -c 'dotnet build src/Tertius.Demo -c Release -o /private/tmp/ter-ukw6/bin-after-census'
git apply -R docs/research/back-face-depth-layer/acceptance-tools/census-port.patch
```

The census, the stills and the video of each part. `MODES=bright` or `MODES=all` picks one census mode, and `TERTIUS_PROBE_CROSSING_TOLERANCE` sets the tolerance in thousandths:

```bash
nix develop --command bash -c "MODES=bright bash tools/gpu-lock.sh --shared <evidence>/part1/lock-bright.log bash <tools>/census.sh <evidence>/part1/census docs/research/back-face-depth-layer/stage-1b/tools/pictures/census-rect-lamps.json 'glow:587:107:693:200;quiet:507:93:547:160' 2.796"
nix develop --command bash -c "bash tools/gpu-lock.sh --shared <evidence>/part1/lock-stills.log bash <tools>/stills.sh <evidence>/part1/stills rect-lamps docs/research/back-face-depth-layer/stage-1b/tools/pictures/stills-rect-lamps.json 3.823005"
nix develop --command bash -c "bash tools/gpu-lock.sh --shared <evidence>/part2/lock-stills.log bash <tools>/stills.sh <evidence>/part2/stills sunlit-nose <tools>/stills-sunlit-nose.json"
nix develop --command bash -c "bash tools/gpu-lock.sh --shared <evidence>/part2/lock-census.log bash <tools>/census.sh <evidence>/part2/census <tools>/census-sunlit-nose.json 'glow:587:107:693:200;cloud:500:0:780:220;quiet:507:93:547:160' -1.7507356"
nix develop --command bash -c "bash tools/gpu-lock.sh --shared <evidence>/part2/lock-video.log bash <tools>/video.sh <evidence>/part2/video <tools>/stills-sunlit-nose.json -1.7507356 60 480,0,1440,640"
python3 -I docs/research/back-face-depth-layer/stage-1b/tools/pictures/sill_report.py cloud <evidence>/part2/census/before-bright <evidence>/part2/census/after-bright
python3 -I <tools>/sources.py cloud 'window-edge:590:374:690:377;wall-below:590:377:690:386;floor-patch:570:386:710:390' <evidence>/part2/census/before-bright <evidence>/part2/census/after-bright
(cd <tools> && python3 test_sources.py)
```

The load of the Mac at the end of each run:

| Run | Before | After |
|---|---|---|
| Part 1, bright census | 9.71 | 9.06 |
| Part 1, every-sample census | 7.69 | 7.60 |
| Part 1, stills | 5.45 | 6.08 |
| Part 2, stills | 7.52 | 6.80 |
| Part 2, bright census | 7.59 | 6.97 |
| Part 2, every-sample census | 6.03 | 30.55 |
| Part 2, bright census at 5 % | 153.22 | 102.41 |
| Part 2, bright census at 15 % | 59.30 | 57.86 |
| Part 2, video | 60.51 | 19.51 |

## Changes to the method

- Part 2 moves the sun with the world's time, in a scratch script, not with a patch of the demo. The script format has no step that sets the sun, but a run step moves the orbit.
- The sunlit view is the nose sill, not a side window's reveal. At this orbit no time of day lights a reveal (see "What was not measured").
- The census ran at two more tolerances, because the floor patch is seen at a grazing angle. Stage 1b's check used 0.3 % and 1 %.
- `sources.py` is new. It splits the census by the place of each hit pixel, so that the false hits and the real hits of each source have their own counts.
- Part 2 has a video, by the user's rule for a fault that shows over time: the ceiling's specks sparkle.
