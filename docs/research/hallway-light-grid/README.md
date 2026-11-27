# Hallway walk and longer light-grid coverage (issue #21)

## Summary

- The film walks the full gray-box deck of the `orbit` scene, from the aft wall to 3.8 m from the nose wall. Legacy ray tracing and the merged light grid (the directional light volume of #18) give a similar hallway. Merged is darker overall, and the nose wall drops by 24 % when it enters the merged grid at about 7 m.
- A 32 m outer cube built from 2 m cells leaks strong outdoor light into the cabin, as built on the merged grid of 90a6be90d. The cause is the light patch, which spans 4 × 4 × 4 cells and so 8 m at 2 m cells. One 8 m patch can hold a cabin surface and a hull surface that face the same way, and both then get one light value.
- With 4 m patches at the 2 m level (a diagnostic, not a candidate), the leak disappears. The longer range moves the visible step on the nose wall from about 7 m to about 14 m. It does not remove the step: the wall drops by 49 %, against 24 % in merged.
- On one Linux RTX 2000 Ada, merged costs 6.29 ms of GPU frame span without the readback. Legacy costs 1.53 ms more (+24 %). The 0.25 / 0.5 / 2 m prototype costs 0.14 ms more (+2 %), and four levels to 2 m cost 0.39 ms more (+6 %). The leak fix adds no measurable cost to four levels.
- No variant is ready for production. The user chooses if a longer range is worth +6 % once the leak and the step have a design.

## Variants

All variants run the same commit of the scene and the same script. Only the renderer differs. Each prototype is a patch on 90a6be90d, kept separate from production. Per the parent issue, the range prototypes keep the merged grid's behavior of 90a6be90d and do not include #20.

| Name | Code | Levels (cell width) | Outer cube side | Light patch at the outer level | Grid buffer |
|---|---|---|---:|---:|---:|
| legacy | 860685c71, the last production before #18 | none: screen-traced diffuse rays | — | — | — |
| merged | 90a6be90d, current `main` | 0.25, 0.5, 1 m | 16 m | 4 m | 12.77 MiB |
| outer | 90a6be90d + [outer-2m.patch](outer-2m.patch) | 0.25, 0.5, 2 m | 32 m | 8 m | 12.77 MiB |
| four | 90a6be90d + [four-level.patch](four-level.patch) | 0.25, 0.5, 1, 2 m | 32 m | 8 m | 17.02 MiB |
| tiles (diagnostic) | four + [coarse-patches.patch](coarse-patches.patch) | 0.25, 0.5, 1, 2 m | 32 m | 4 m | 17.19 MiB |

The grid buffer is `LightVolumePass.BufferBytes`: 4,462,592 bytes per level (surfaces 1,179,648, field 2,097,152, filtered 786,432, bounce 393,216 and light patches 6,144), plus 64 or 80 bytes of header. The diagnostic adds 43,008 bytes of patches per level. Each level holds 16 × 16 × 16 cells, so a level of cell width w covers a cube of 16 w per side, centered on the camera.

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

- Legacy and merged: no light disappears, and no wall leaks. Merged's nose wall darkens by a step when the camera comes within about 7 m. Legacy's floor and far walls are brighter than merged's along the whole walk.
- outer: the near floor, the side walls and the ceiling turn bright blue. The floor reaches luma 200 to 218, against 80 to 89 for merged. Light patches of the 2 m level feed the 0.5 m level at its boundary, so the leak also covers the floor near the camera.
- four: the 1 m level hides the leak within 8 m of the camera. Past that edge the floor shows a bright band, and the side walls and the ceiling corners turn blue. At 40 m the far floor shows orange and blue blotches.
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
| Light of its patches set to 0 | Gone |
| Patches of 2 × 2 × 2 cells (4 m) instead of 4 × 4 × 4 (8 m) | Gone |

The volume shading pass lights one patch per side of each tile of 4 × 4 × 4 cells. It evaluates the scene's lights at the area-weighted center of that side's surfaces in the tile, 0.5 cells out along their mean normal. At 2 m cells a tile spans 8 m. The cabin floor (+y, at −3.5 m) and the roof's top (+y, at 4.0 m) lie 7.5 m apart, so one patch can hold both. The same holds for the ceiling and the belly. One light value then lights the cabin surface with light from outside the hull, or the reverse. At 1 m cells a tile spans 4 m and cannot hold both. The tests do not explain why the leaked light is blue at the aft wall and orange on the far floor. The sun is the only light outside, and the tests did not isolate where its color changes.

## Same surface inside and outside the old limit

[analyze.py](analyze.py) projects two fixed surfaces into every frame and averages their 8-bit sRGB pixels, inside a box shrunk by 15 % on each side:

- A wall patch right of the nose window (x from 4 to 7 m, y from −2.6 to −0.4 m).
- A floor patch 1 to 3 m in front of the nose wall.

The charts plot luma, 0.2126 R + 0.7152 G + 0.0722 B of the 8-bit values, against the distance to the nose wall: [nose-wall.svg](analysis/nose-wall.svg), [floor.svg](analysis/floor.svg). [surfaces.csv](analysis/surfaces.csv) has every frame.

| Distance to nose wall | legacy | merged | outer | four | tiles |
|---:|---:|---:|---:|---:|---:|
| 20 m | 84.9 | 62.0 | 62.7 | 62.5 | 61.8 |
| 15 m | 76.9 | 70.0 | 70.9 | 70.7 | 69.8 |
| 14 m | 74.8 | 69.4 | 109.6 | 109.0 | 47.3 |
| 10 m | 63.0 | 62.4 | 154.9 | 154.0 | 35.8 |
| 7.5 m | 60.4 | 61.5 | 180.0 | 179.4 | 35.3 |
| 7 m | 60.6 | 46.7 | 180.1 | 58.8 | 35.1 |
| 5 m | 66.2 | 51.6 | 180.6 | 51.7 | 40.1 |

The table gives the luma of the nose-wall patch.

- merged: the wall drops from 61.5 to 46.7 (−24 %) between 7.5 and 7 m, where the 1 m level starts to cover it. The floor patch shows no step.
- Any 32 m variant: the step moves to between 15 and 14 m. In outer and four the leak makes it a rise to 2.6 times. In the diagnostic it is a drop from 69.8 to 35.3 (−49 %). Inside the old limit the wall also stays darker than merged's. Merged's finer levels receive full environment transmission (no light, transmission 1) from outside its cube. In the diagnostic they receive the 2 m level's own light and transmission instead.
- Extra range moves the transition farther away. It does not fix it.

## Movement of the light

The nose-wall patch also shows how the light moves while the camera walks. Between consecutive frames, merged's patch changes by more than 0.6 luma 38 times, in steps of 0.6 to 2.1 luma. The steps lie 0.9 m of camera travel apart and come in groups about every 6 m, the spacing of the side windows. Legacy shows one such step over the whole walk. The steps are the same in every grid variant, and they occur from 58 m to 16 m, where merged's grid does not reach the nose wall. So the grid's own cells do not cause them, and this experiment did not find their cause. At the start of the walk (58.3 m), merged's patch also jumps by 9.3 luma in one frame.

## Differences and noise

The mean absolute difference of the 8-bit RGB values over all pixels ([summary.json](analysis/summary.json)), by distance to the nose wall:

| Pair | Whole walk | 40 to 60 m | 17 to 40 m | 9 to 17 m | 0 to 9 m |
|---|---:|---:|---:|---:|---:|
| merged vs legacy | 15.8 | 18.1 | 18.0 | 9.4 | 10.1 |
| outer vs merged | 74.7 | 71.1 | 58.8 | 85.9 | 116.9 |
| four vs merged | 38.5 | 47.2 | 29.6 | 53.8 | 27.2 |
| tiles vs merged | 12.8 | 12.9 | 12.6 | 19.1 | 7.2 |

Temporal noise is the per-pixel standard deviation over a still hold, averaged over the image, in 8-bit levels. The holds are frames 10 to 30 and frames 1150 to 1189. All variants lie from 0.51 to 0.57: legacy 0.57 and 0.54, merged 0.52 and 0.51, outer 0.53 and 0.53, four 0.54 and 0.51, tiles 0.54 and 0.51. No variant adds visible temporal noise in this gray-box scene.

## GPU cost on Linux

Three jobs on Runpod serverless RTX 2000 Ada workers ran `tools/runpod/ab_run.sh` with the walk at 1920 × 1080 and `--gray-box --video none --exposure 3.79 --gpu-run deformation..outlines`. Each run timed all 1,190 film frames with the renderer's defaults otherwise. Each job built every tree's Release demo on one worker and ran one untimed warm-up run of its first tree. Then it ran paired rounds, and the order of the trees reverses each round.

The frame span below is the per-frame sum of all passes from `deformation` to `outlines`. It leaves out the headless `capture` readback of 2.30 ms, which a window does not do. Its median and 95th percentile come from each run's 1,190 frames. The tables give the median of those per-run values. Every run of each variant lies apart from every run of merged, except where marked.

Job 1 (`e97c0dd4`, 4 rounds) and job 2 (`e976ad8c`, 5 rounds, merged first) ran on the same endpoint and agree to within 0.02 ms. Job 2:

| Variant | Median | p95 | Run medians | Change of median vs merged | Change of p95 |
|---|---:|---:|---|---:|---:|
| legacy | 7.817 ms | 8.000 ms | 8.206, 7.731, 7.812, 7.827, 7.817 | +1.529 ms (+24.3 %) | +1.602 ms (+25.0 %) |
| merged | 6.288 ms | 6.398 ms | 6.239, 6.286, 6.288, 6.306, 6.297 | — | — |
| outer | 6.425 ms | 6.575 ms | 6.391, 6.413, 6.425, 6.441, 6.445 | +0.137 ms (+2.2 %) | +0.177 ms (+2.8 %) |
| four | 6.674 ms | 6.823 ms | 6.642, 6.650, 6.674, 6.679, 6.683 | +0.387 ms (+6.2 %) | +0.425 ms (+6.6 %) |

In job 1 the first merged run was slow (6.592 ms against 6.279 to 6.299 for the others), so outer's runs overlap merged's there. Its medians are legacy 7.799, merged 6.297, outer 6.423 and four 6.668 ms. In job 2 the first legacy run was slow (8.206 ms). Each job's slow run is the first run of a tree that the warm-up did not run. The medians ignore it.

Job 3 (`cfb2f98f`, 5 rounds) adds the leak-free diagnostic. Its worker was 0.09 ms slower on merged, so compare changes, not absolute times:

| Variant | Median | p95 | Change of median vs merged |
|---|---:|---:|---:|
| merged | 6.376 ms | 6.487 ms | — |
| four | 6.767 ms | 6.919 ms | +0.391 ms (+6.1 %) |
| tiles | 6.764 ms | 6.891 ms | +0.387 ms (+6.1 %) |

Pass medians of job 2, in ms:

| Pass | legacy | merged | outer | four |
|---|---:|---:|---:|---:|
| directional light volume | — | 0.719 | 0.737 | 0.960 |
| diffuse rays | 0.640 | — | — | — |
| accumulate indirect diffuse | 0.381 | — | — | — |
| filter indirect diffuse | 1.234 | — | — | — |
| ray plan | 0.479 | 0.503 | 0.542 | 0.557 |
| reflection rays | 0.679 | 0.683 | 0.703 | 0.701 |
| screen trace resolve | 0.179 | 0.122 | 0.123 | 0.123 |
| composite | 0.162 | 0.269 | 0.304 | 0.314 |

- Legacy pays 2.255 ms for its three diffuse passes. Merged replaces them with 0.719 ms of volume plus 0.107 ms more composite.
- Outer keeps three levels, so the volume pass grows only 0.018 ms. Its cost comes from the receivers: `composite` (+0.035 ms) and `ray plan` (+0.039 ms) read the grid at more pixels, because more of the view now lies inside it.
- Four adds a level: the volume pass grows 0.241 ms (+34 %), close to the 33 % more cells, plus the same receiver cost.
- The diagnostic's 512 patches per level cost nothing measurable (+0.003 ms on the volume pass in job 3).

Per-job tables, every run's `gpu-timings.json`, the card's clock log and the job results are in [runpod/](runpod). The three jobs used 1,729 s of execution, counted as $0.31.

Not the same scene: #19 measured merged against legacy on `scripts/temporal-upscale-timing.json` (120 frames of the `demo-exit` deck), with 6.836 and 8.064 ms (−1.229 ms, −15.2 %). That scene and its pass mix differ, so those numbers do not compare with the ones above.

## What a production change would need

These are open design points for the user, not decisions:

1. Light patches at the coarse level must not mix surfaces on both sides of a thin wall. 4 m patches did this here at no measurable cost. A rule that splits a patch by connected surfaces would hold for any wall thickness.
2. The step at the outer edge does not go away with range. Two separate causes need a design: outside the cube the environment is fully open, and inside it the grid's own light is darker than legacy's.
3. The diagnostic still shows a black near-ceiling and bright ceiling–wall edges at 32 m. Both need a diagnosis before any range change.
4. For the same 32 m range, four levels cost 0.25 ms more than three levels with a 4:1 step. The 4:1 step leaks through the 0.5 m level's boundary, and it was not tested with 4 m patches.

## Reproduce

From the repository root, inside the dev shell:

```bash
bash docs/research/hallway-light-grid/render.sh /tmp/hall          # films of all five variants
bash docs/research/hallway-light-grid/compose.sh /tmp/hall out.mp4 legacy merged outer four
python3 -I docs/research/hallway-light-grid/analyze.py /tmp/hall /tmp/hall/analysis legacy,merged,outer,four,tiles
python3 -I docs/research/hallway-light-grid/timing.py docs/research/hallway-light-grid/runpod/job2
```

`tests/Tertius.Host.Tests/HallwayWalkTests.cs` pins the film's frame counts. Its explicit test pins the camera path that `analyze.py` and `compose.sh` assume: `dotnet test --project tests/Tertius.Host.Tests -c Checked --filter-class '*HallwayWalkPathTests' --explicit only`, about 5 minutes.

The Runpod jobs used the commands in each job's `result.json`. The trees were the prototype branches `issue-21-outer-grid-prototype`, `issue-21-four-level-prototype` and `issue-21-fine-tile-diagnostic`, which hold the same commits as the patches.
