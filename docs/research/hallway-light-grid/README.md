# Hallway walk and longer light-grid coverage (issue #21)

## Summary

- The film walks the full gray-box deck of the `orbit` scene, from the aft wall to 3.8 m from the nose wall. Legacy ray tracing and the merged light grid (the directional light volume of #18) give a similar hallway. Merged is darker overall. Its nose wall drops by 26 % in one frame when the 1 m level reaches the wall at 7.0 m.
- A 32 m outer cube built from 2 m cells leaks strong outdoor light into the cabin, as built on the merged grid of 90a6be90d. The cause is the tile: the volume shading pass computes one light value, a patch, per side of each tile of 4 × 4 × 4 cells. At 2 m cells a tile spans 8 m. So one patch can hold a cabin surface and a hull surface that face the same way, and both get one light value. Near the old limit the leaking variants also flicker from frame to frame.
- With 2 × 2 × 2-cell tiles (4 m) at the 2 m level (a diagnostic, not a candidate), the leak and the flicker disappear. The longer range moves the visible step on the nose wall from 7.0 m to 14.5 m. It does not remove the step: the wall drops by 31 % there, against 26 % in merged, and by 16 % more at 12.5 m.
- On one Linux RTX 2000 Ada, with every variant on the same scene, merged costs 6.29 ms of GPU frame span without the readback. Legacy costs 1.61 ms more (+26 %). The 0.25 / 0.5 / 2 m prototype costs 0.13 ms more (+2 %), and four levels to 2 m cost 0.38 ms more (+6 %). The smaller tiles add no measurable cost to four levels.
- No variant is ready for production. The user chooses if a longer range is worth +6 % once the leak and the step have a design.

## Variants

All variants run the same `orbit` scene and the same script. Only the renderer differs. 860685c71 lacks the 1 W `airlock-lamp` that #19 added to the scene, so legacy takes the scene file of 90a6be90d through [legacy-scene.patch](legacy-scene.patch). With the lamp, legacy's frames change by 0.048 levels of 8-bit RGB on average over the walk. Its GPU span grows by about 0.09 ms (job 4 against job 2). Each prototype is a patch on 90a6be90d, kept separate from production. Per the parent issue, the range prototypes keep the merged grid's behavior of 90a6be90d and do not include #20.

| Name | Code | Levels (cell width) | Outer cube side | Tile side at the outer level | Grid buffer |
|---|---|---|---:|---:|---:|
| legacy | 860685c71, the last production before #18, + [legacy-scene.patch](legacy-scene.patch) | none: screen-traced diffuse rays | — | — | — |
| merged | 90a6be90d, current `main` | 0.25, 0.5, 1 m | 16 m | 4 m | 12.77 MiB |
| outer | 90a6be90d + [outer-2m.patch](outer-2m.patch) | 0.25, 0.5, 2 m | 32 m | 8 m | 12.77 MiB |
| four | 90a6be90d + [four-level.patch](four-level.patch) | 0.25, 0.5, 1, 2 m | 32 m | 8 m | 17.02 MiB |
| tiles (diagnostic) | four + [coarse-patches.patch](coarse-patches.patch) | 0.25, 0.5, 1, 2 m | 32 m | 4 m | 17.19 MiB |

The grid buffer is `LightVolumePass.BufferBytes`: 4,462,592 bytes per level (surfaces 1,179,648, field 2,097,152, filtered 786,432, bounce 393,216 and patches 6,144), plus 64 or 80 bytes of header. The diagnostic sizes the patch array for 512 tiles at every level, 43,008 bytes more per level, but only the 2 m level uses 512 tiles. The finer levels keep 64. Each level holds 16 × 16 × 16 cells, so a level of cell width w covers a cube of 16 w per side, centered on the camera.

The restriction pass sums the surfaces of the child cells into each parent cell. It assumed that a parent spans 2 children per axis. `outer` jumps from 0.5 m to 2 m. So its patch generalizes the restriction to a ratio of 2 or 4 children per axis, and it scales each face sum by 1 / ratio². A change of the cell width alone sums the wrong children. `four` keeps 2:1 at every step and uses the same generalized code.

## The walk

[scripts/hallway-walk.json](../../../scripts/hallway-walk.json) controls the `person` view of the `orbit` scene at 60 ticks per second:

1. Turn to the aft wall, walk there (z = 29.18 m in the ship's frame) and turn to face the nose.
2. Film 30 still frames.
3. Hold W and film 1,100 frames: the person walks at 3 m/s along x = 0 to z = −25.72 m.
4. Release W and film 60 frames.

The nose wall's inner face lies at z = −29.5 m, so the camera's distance to it falls from 58.68 m to 3.78 m. The walk stops there because, pressed against the nose wall, the camera looks out of the nose window at Earth. The crates at x ≈ 3 m and the airlock at x = −8 m stay beside the path.

Every film uses the gray box at 1280 × 720 with a fixed exposure of 3.79 stops, the exposure of the cabin-cutoff stills of #19. All other settings are the renderer's defaults: screen tracer on, FSR 3.1 at a render scale of 2/3, bloom on. Legacy needs the screen tracer, because its diffuse light comes from screen-traced rays.

## Films and stills

- [hallway-walk-2x2.mp4](hallway-walk-2x2.mp4): legacy, merged, outer and four, synchronized, 2560 × 1440 at 60 fps, 19.8 s. The counter shows the distance to the nose wall.
- [hallway-walk-leak-free.mp4](hallway-walk-leak-free.mp4): legacy, merged and the leak-free diagnostic in one row, 3840 × 720 at 60 fps.
- [stills-40-13-6.5m.jpg](stills-40-13-6.5m.jpg): all five variants at 40, 13 and 6.5 m from the nose wall.

What the films show:

- Legacy and merged: no light disappears, and no wall leaks. Merged's nose wall darkens by a step when the camera comes within 7.0 m. Legacy's floor and far walls are brighter than merged's along the whole walk.
- outer: the near floor, the side walls and the ceiling turn bright blue. The floor reaches luma 200 to 218, against 80 to 89 for merged. Patches of the 2 m level feed the 0.5 m level at its boundary, so the leak also covers the floor near the camera. Within 16 m of the nose wall, the leaked light flickers from frame to frame (see "Movement of the light").
- four: the 1 m level hides the leak within 8 m of the camera. Past that edge the floor shows a bright band, and the side walls and the ceiling corners turn blue. At 40 m the far floor shows orange and blue blotches. The leaked light flickers as in outer.
- tiles: no blue. Two faults remain at 32 m. The ceiling near the camera turns almost black, and bright lines run along the edges between the ceiling and the side walls.

## Why 2 m cells leak

These single-frame tests on `four` at the aft wall localize the leak:

| Change to the 2 m level only | Blue leak |
|---|---|
| Screen tracer off | Stays |
| Window panes included in the grid (the grid normally skips glass) | Stays, brighter |
| Meshes with a bounding radius over 1 km left out (Sun, Earth) | Stays |
| Emission of its surfaces set to 0 | Stays |
| Radiance of its cells set to 0 | Gone |
| Its patches set to 0 | Gone |
| Tiles of 2 × 2 × 2 cells (4 m) instead of 4 × 4 × 4 (8 m) | Gone |

The volume shading pass computes one patch per side of each tile of 4 × 4 × 4 cells. It evaluates the scene's lights at the area-weighted center of that side's surfaces in the tile, 0.5 cells out along their mean normal. At 2 m cells a tile spans 8 m. The cabin floor (+y, at −3.5 m) and the roof's top (+y, at 4.0 m) lie 7.5 m apart, so one tile can hold both, and they share the tile's +y patch. The same holds for the ceiling and the belly. One light value then lights the cabin surface with light from outside the hull, or the reverse. At 1 m cells a tile spans 4 m and cannot hold both. The tests do not explain why the leaked light is blue at the aft wall and orange on the far floor. The sun is the only light outside, and the tests did not isolate where its color changes.

## Same surface inside and outside the old limit

[analyze.py](analyze.py) projects two fixed surfaces into every frame and averages their 8-bit sRGB pixels, inside a box shrunk by 15 % on each side:

- A wall area right of the nose window (x from 4 to 7 m, y from −2.6 to −0.4 m).
- A floor area 1 to 3 m in front of the nose wall.

The charts plot luma, 0.2126 R + 0.7152 G + 0.0722 B of the 8-bit values, against the distance to the nose wall: [nose-wall.svg](analysis/nose-wall.svg), [floor.svg](analysis/floor.svg). [surfaces.csv](analysis/surfaces.csv) has every frame.

| Distance to nose wall | legacy | merged | outer | four | tiles |
|---:|---:|---:|---:|---:|---:|
| 20.0 m (frame 805) | 84.9 | 62.0 | 62.7 | 62.5 | 61.8 |
| 14.53 m (frame 914) | 76.0 | 70.0 | 71.0 | 70.7 | 69.8 |
| 14.48 m (frame 915) | 75.9 | 70.0 | 109.9 | 109.3 | 48.3 |
| 12.53 m (frame 954) | 70.2 | 67.8 | 120.9 | 120.1 | 46.1 |
| 12.48 m (frame 955) | 70.2 | 67.8 | 132.1 | 131.3 | 38.7 |
| 10.0 m (frame 1005) | 63.0 | 62.4 | 154.9 | 154.0 | 35.8 |
| 7.03 m (frame 1064) | 60.6 | 62.9 | 181.5 | 180.9 | 35.5 |
| 6.98 m (frame 1065) | 60.6 | 46.7 | 180.1 | 58.8 | 35.1 |
| 5.0 m (frame 1105) | 66.2 | 51.6 | 180.6 | 51.7 | 40.1 |

The table gives the luma of the nose-wall area. Each step below happens between two consecutive frames:

| Variant | Distance | Before | After | Change |
|---|---:|---:|---:|---:|
| merged | 6.98 m | 62.9 | 46.7 | −25.7 % |
| outer | 14.48 m | 71.0 | 109.9 | +54.9 % |
| outer | 12.48 m | 120.9 | 132.1 | +9.3 % |
| four | 14.48 m | 70.7 | 109.3 | +54.6 % |
| four | 12.48 m | 120.1 | 131.3 | +9.3 % |
| four | 6.98 m | 180.9 | 58.8 | −67.5 % |
| tiles | 14.48 m | 69.8 | 48.3 | −30.9 % |
| tiles | 12.48 m | 46.1 | 38.7 | −16.1 % |

- merged: one step of −25.7 % at 6.98 m, where the 1 m level starts to cover the wall. The floor area shows no step.
- Any 32 m variant: the first step moves to 14.48 m, where the 2 m level reaches the wall. A second step follows at 12.48 m, one 2 m cell later. In outer and four the leak makes both steps rises, and the wall then drifts up to 2.5 times its value before the steps by about 9 m. In the diagnostic both steps are drops, and the wall then drifts down to 35.3 by about 9.5 m: 49 % below its value before the first step. Inside the old limit the wall also stays darker than merged's. Merged's finer levels receive full environment transmission (no light, transmission 1) from outside its cube. In the diagnostic they receive the 2 m level's own light and transmission instead.
- Extra range moves the transition farther away. It does not fix it.

## Movement of the light

The two areas also show how the light moves while the camera walks. `walk_steps` in [summary.json](analysis/summary.json) counts, for frames 32 to 1129, the frame-to-frame changes of each area's luma by more than 0.6:

| Area and distance to the nose wall | legacy | merged | outer | four | tiles |
|---|---:|---:|---:|---:|---:|
| Nose wall, 16 to 60 m | 1 | 30 | 30 | 30 | 30 |
| Nose wall, 7.1 to 16 m | 0 | 4 | 118 | 119 | 2 |
| Floor, 7.1 to 16 m | 16 | 26 | 142 | 121 | 20 |

- From 16 to 60 m, every grid variant shows the same 30 steps on the wall. Merged has 36 steps of 0.6 to 2.1 luma over the whole walk, besides the jump of +9.3 at 58.3 m (the first moving frame) and the drop of −16.1 at 6.98 m. The small steps lie 0.9 m of camera travel apart and come in groups about every 6 m, the spacing of the side windows. Legacy shows one. Merged's grid does not reach the wall there, so the grid's own cells do not cause them, and this experiment did not find their cause.
- From 7.1 to 16 m, outer and four flicker. Their wall luma changes by more than 0.6 in most consecutive frames. The median change is 2.9, the 10th to 90th percentile is 1.1 to 5.5, and the largest is 38.9, the step at 14.48 m. Merged and the diagnostic do not. This is the leaked light: the 2 m tiles that hold the cabin and the hull change as the grid follows the camera. The 4 m tiles of the diagnostic remove the flicker with the leak.
- The far floor area covers only a few pixels beyond about 16 m, so its counts there measure aliasing, and the table leaves them out.

## Differences and noise

The mean absolute difference of the 8-bit RGB values over all pixels ([summary.json](analysis/summary.json)), by distance to the nose wall:

| Pair | Whole walk | 40 to 60 m | 17 to 40 m | 9 to 17 m | 0 to 9 m |
|---|---:|---:|---:|---:|---:|
| merged vs legacy | 15.8 | 18.1 | 18.0 | 9.4 | 10.1 |
| outer vs merged | 74.7 | 71.1 | 58.8 | 85.9 | 116.9 |
| four vs merged | 38.5 | 47.2 | 29.6 | 53.8 | 27.2 |
| tiles vs merged | 12.8 | 12.9 | 12.6 | 19.1 | 7.2 |

Temporal noise is the per-pixel standard deviation over a still hold, averaged over the image, in 8-bit levels. The holds are frames 10 to 30 and frames 1150 to 1189. All variants lie from 0.51 to 0.57: legacy 0.57 and 0.54, merged 0.52 and 0.51, outer 0.53 and 0.53, four 0.54 and 0.51, tiles 0.54 and 0.51. So with a still camera no variant adds visible temporal noise in this gray-box scene. During the walk, outer and four flicker (see "Movement of the light").

## GPU cost on Linux

Four jobs on Runpod serverless RTX 2000 Ada workers ran `tools/runpod/ab_run.sh` with the walk at 1920 × 1080 and `--gray-box --video none --exposure 3.79 --gpu-run deformation..outlines`. Each run timed all 1,190 film frames with the renderer's defaults otherwise. Each job built every tree's Release demo on one worker and ran one untimed warm-up run of its first tree. Then it ran paired rounds, and the order of the trees reverses each round.

The frame span below is the per-frame sum of all passes from `deformation` to `outlines`. It leaves out the headless `capture` readback of 2.30 ms, which a window does not do. Its median and 95th percentile come from each run's 1,190 frames. The tables give the median of those per-run values. [timing.py](timing.py) makes them. Its docstring says why it does not use `tools/runpod/ab_report.py`, whose medians differ by at most 0.015 ms. Every run of each variant lies apart from every run of merged, except where marked.

Job 4 (`a9e256cf`, 4 rounds, merged first) is the headline. It is the only job in which legacy renders the same scene as the grid variants, and it times all five variants on one worker:

| Variant | Median | p95 | Run medians | Change of median vs merged | Change of p95 |
|---|---:|---:|---|---:|---:|
| legacy | 7.906 ms | 8.090 ms | 8.257, 7.873, 7.888, 7.923 | +1.612 ms (+25.6 %) | +1.692 ms (+26.5 %) |
| merged | 6.294 ms | 6.398 ms | 6.240, 6.294, 6.294, 6.310 | — | — |
| outer | 6.426 ms | 6.570 ms | 6.386, 6.422, 6.435, 6.430 | +0.132 ms (+2.1 %) | +0.172 ms (+2.7 %) |
| four | 6.671 ms | 6.819 ms | 6.646, 6.660, 6.682, 6.685 | +0.377 ms (+6.0 %) | +0.420 ms (+6.6 %) |
| tiles | 6.666 ms | 6.792 ms | 6.648, 6.653, 6.681, 6.679 | +0.372 ms (+5.9 %) | +0.394 ms (+6.2 %) |

Pass medians of job 4, in ms:

| Pass | legacy | merged | outer | four | tiles |
|---|---:|---:|---:|---:|---:|
| directional light volume | — | 0.720 | 0.737 | 0.960 | 0.963 |
| diffuse rays | 0.641 | — | — | — | — |
| accumulate indirect diffuse | 0.381 | — | — | — | — |
| filter indirect diffuse | 1.234 | — | — | — | — |
| ray plan | 0.479 | 0.504 | 0.542 | 0.557 | 0.556 |
| reflection rays | 0.681 | 0.683 | 0.703 | 0.700 | 0.698 |
| screen trace resolve | 0.179 | 0.122 | 0.123 | 0.123 | 0.126 |
| composite | 0.162 | 0.269 | 0.304 | 0.314 | 0.314 |

- Legacy pays 2.256 ms for its three diffuse passes. Merged replaces them with 0.720 ms of volume plus 0.107 ms more composite.
- Outer keeps three levels, so the volume pass grows only 0.017 ms. Its cost comes from the receivers: `composite` (+0.035 ms) and `ray plan` (+0.038 ms) read the grid at more pixels, because more of the view now lies inside it.
- Four adds a level: the volume pass grows 0.240 ms (+33 %), close to the 33 % more cells, plus the same receiver cost.
- The diagnostic's 512 tiles at the 2 m level cost nothing measurable: +0.003 ms on the volume pass, against four.

The first run of legacy was slow (8.257 ms). In every job, the slow run is the first run of a tree that the warm-up did not run. The medians ignore it.

Jobs 1 to 3 ran before the scene fix, so their legacy lacks the airlock lamp:

- Job 1 (`e97c0dd4`, 4 rounds) and job 2 (`e976ad8c`, 5 rounds) timed legacy, merged, outer and four on the same endpoint. Their medians agree within 0.02 ms and their changes against merged within 0.03 ms. Job 2 gives legacy +1.529 ms (+24.3 %), outer +0.137 ms (+2.2 %) and four +0.387 ms (+6.2 %). In job 1 the first merged run was slow (6.592 ms), so outer's runs overlap merged's there.
- Job 3 (`cfb2f98f`, 5 rounds) timed merged, four and tiles on a worker that was 0.08 ms slower on merged: four +0.391 ms (+6.1 %) and tiles +0.387 ms (+6.1 %).

The changes of the grid variants against merged agree across the four jobs within 0.02 ms. Per-job tables, every run's `gpu-timings.json`, the card's clock log and the job results are in [runpod/](runpod). The four jobs used 2,433 s of execution, counted as $0.43.

Not the same scene: #19 measured merged against legacy on `scripts/temporal-upscale-timing.json` (120 frames of the `demo-exit` deck), with 6.836 and 8.064 ms (−1.229 ms, −15.2 %). That scene and its pass mix differ, so those numbers do not compare with the ones above.

## What a production change would need

These are open design points for the user, not decisions:

1. Tiles at the coarse level must not mix surfaces on both sides of a thin wall. 4 m tiles did this here at no measurable cost. A rule that splits a tile's patch by connected surfaces would hold for any wall thickness.
2. The step at the outer edge does not go away with range. Two separate causes need a design: outside the cube the environment is fully open, and inside it the grid's own light is darker than legacy's.
3. The diagnostic still shows a black near-ceiling and bright ceiling–wall edges at 32 m. Both need a diagnosis before any range change.
4. For the same 32 m range, four levels cost 0.25 ms more than three levels with a 4:1 step. The 4:1 step leaks through the 0.5 m level's boundary, and it was not tested with 4 m tiles.

## Reproduce

From the repository root, inside the dev shell:

```bash
bash docs/research/hallway-light-grid/render.sh /tmp/hall          # films of all five variants
bash docs/research/hallway-light-grid/compose.sh /tmp/hall/film out.mp4 legacy merged outer four
python3 -I docs/research/hallway-light-grid/analyze.py /tmp/hall/film /tmp/hall/analysis legacy,merged,outer,four,tiles
python3 -I docs/research/hallway-light-grid/timing.py docs/research/hallway-light-grid/runpod/job4
```

`tests/Tertius.Host.Tests/HallwayWalkTests.cs` pins the film's frame counts. Its explicit nested test pins the person's start and end on the center line, which also bounds the heading error to 0.0002 rad: `dotnet test --project tests/Tertius.Host.Tests -c Checked --filter-class '*AlongTheCabin' --explicit only`, about 5 minutes. No test pins the eye height (1.65 m in `DemoScenes`), the field of view (60 degrees, `SceneBatcher`'s default) or the linear z per frame that `analyze.py` and `compose.sh` also assume.

The Runpod jobs used the commands in each job's `result.json`. The prototype trees were the commits 0c7a80289 (legacy scene, job 4 only), 3dded7d23 (outer), fe0cd24e1 (four) and a0ed6aece (tiles), the same commits as the patches. `tools/runpod/gpu_job.py` published them under hidden refs, which it deletes after three days, so the patches are the lasting record.
