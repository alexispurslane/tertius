# Stage 1b pictures: the 10 % rule against the layer as the slab

This is the record of ticket `t1-gxzg`, of 2026-11-14. It measures the probe of [probe.md](probe.md) in three parts. Part 1 counts the false sill hits. Part 2 compares stills of three views for contact darkening and thick walls. Part 3 looks at surfaces with no back face. The terms are those of [probe.md](probe.md), of ticket `t1-n8x9` and of [the record of stage 1](../README.md).

Each part keeps the measured facts and my reading apart. A paragraph that starts with "Reading:" holds my interpretation. Every other sentence states a measurement, a count or what an image shows.

## The result in short

1. The chosen design removes the false sill hits. Under the layer as the slab, the glow area's bright false light falls to 0.016 % of the 10 % rule's. Stage 1 estimated that about 8 % stays. That estimate does not hold.
2. The model has a misbehavior with a name: the borrowed back face. A surface that the back-face pass does not draw, such as a hair card, takes the back face of the solid behind it. Its slab then spans the empty space between the two objects. In the lamp hall, 131,399 card pixels get a slab with a median of 4.1 m. 24 % of the reflection rays then hit, against 2 % under the 10 % rule. Most of those hits start behind the plane that they hit.
3. The stills of rect-lamps change most: the ceiling, the far wall, the upper side walls, the window jambs and the floor's patches all become darker. Part 1 shows that most of that light was false.
4. The cabin of `demo-exit` changes in both directions by about 7 of 255. The fade switch reverses the sign of that change, so the fade by the entry share sets its direction.
5. The deck changes little: 46 tiles darker by about 5.5 of 255, against a noise floor of 4.66.
6. The hair views show no change above their noise floor, apart from a thin strip at a panel's base in `hair-rugs`.
7. A ray can hit a slab of zero thickness. But the sheets keep only 20 % to 37 % of the real hits that the 10 % rule finds on them. The rule `NoBackSheetPlane` keeps 41 % to 66 %.

## How the runs were made

All runs of this ticket ran in the cloud, on Runpod's RTX 2000 Ada, through `tools/runpod/gpu_job.py`. One comparison never mixes two machines. The Mac ran one smoke test of the census build, and one figure comes from it: see Part 3.

The trees are scratch commits on the probe's tree `refs/scratch/ter-kh4g-1b-off`:

| Ref | Commit | What it adds |
|---|---|---|
| `refs/scratch/ter-kh4g-1b-sill-stills` | `30e6754ff` | The scripts and job tools of [tools/pictures](tools/pictures). No source change. |
| `refs/scratch/ter-kh4g-1b-sill-census` | `b4cabf475` | The census build: the sill census and the sheet census ([census.patch](census.patch)). |
| `refs/scratch/ter-kh4g-1b-sill-census-tolerance` | `98b7bb447` | A switch for the crossing's distance tolerance. |
| `refs/scratch/ter-kh4g-1b-sill-census-all` | `0d579c2c7` | A switch to count every sample, bright or not. [census.patch](census.patch) is this tree against the probe. |

The probe's mode comes from `TERTIUS_BACK_FACE_PROBE` in each run's command, so one tree holds every mode.

### The stills

- Each view has a script of its own in [tools/pictures](tools/pictures): the view's camera, then a film of 240 frames.
- The settled frame is the film's frame 239: 240 draws after the first. A capture after a film is a still, which resets the histories and draws 33 times. So I used no capture step. The stills of probe.md, control 5, are such stills.
- The noise floor's partner is the frame 231 of the same film: a settled frame 8 draws earlier.
- `job-stills.sh` runs the mode `ten` first with the automatic exposure, and reads the stops of frame 239 from `exposure.json`. Every column then runs with `--exposure <stops>`, `--size 1920x1080` and `--bloom off`.
- The left column is the probe's mode `ten`. A control job built main (`ab54c785e`) and the probe's tree in one job. Main and the mode `ten` gave the same PNG, byte for byte, at frames 0, 19 and 39 of a 40-frame film of rect-lamps.

### The measure of a change

`regions.py` computes the signed luminance difference of the 8-bit sRGB values, column minus `ten`. It averages that difference over tiles of 16 × 16 output pixels. The noise floor is the largest absolute tile difference between the frames 231 and 239 of `ten`. A region is a connected set of tiles above the noise floor. The unit is 1/255.

In the deck, a person walks between the frames 231 and 239, and the noise floor there is 23.41. Without the 112 tiles around the person, it is 4.66. I report the deck with those tiles left out.

## Part 1: the false sill hits

### What the census counts

The sill census ports the true mode of the sill probe (`hittrue-final.patch` of `t0-adau`) to the probe's tree. The sill probe's patches do not apply to main's tree, because its survey patch belongs to the other line's tree. So [census.patch](census.patch) holds a new port of the same rule:

- The resolve takes each specular sample of luminance 50 or more in a box. It walks the pixel's reflection ray again with the inputs of its record.
- It takes the walk's hit pixel and the plane through the hit point with the hit pixel's geometric normal. It projects the ray's crossing of that plane onto the screen.
- Class 0, an opening: the eye sees the sky there, or a surface more than 2 % farther than the crossing. Class 1, the hit surface: the eye sees a surface within 2 %. Class 2: a nearer surface hides the crossing. Class 3: undetermined.
- The census stores the codes in the specular sample's age and in the diffuse sample's alpha, which no reader of the diffuse signal reads. The C# side reads them back at every counted draw.

The runs: rect-lamps, the person's camera, a film of 600 frames, the draws 200 to 599 counted, `--exposure 2.796` in every rule. The boxes are those of the sill probe, in render pixels: the glow area `587:107:693:200` and a quiet patch `507:93:547:160`.

### The table

The glow area's bright samples, over 400 draws. Light is the sum of the samples' luminance, pre-exposed.

| Rule | Class 0: samples / light | Class 1 | Class 2 | Total | False share | False light against the 10 % rule |
|---|---|---|---|---|---:|---:|
| 10 % rule (`ten`) | 6,783 / 3,011,681 | 1,260 / 1,036,322 | 8 / 3,402 | 8,051 / 4,051,405 | 74.34 % | 100 % |
| Layer as the slab (`layer`) | 4 / 483 | 602 / 488,239 | 0 | 606 / 488,723 | 0.099 % | 0.016 % |
| Trimmed slab (`trim`) | 4 / 483 | 602 / 488,239 | 0 | 606 / 488,723 | 0.099 % | 0.016 % |

- The quiet patch: the 10 % rule has 93 samples of class 0 (41,718) and 42 of class 1 (38,424). The layer and the trimmed slab have no class 0, and 18 samples of class 1 (19,377).
- The trimmed slab's census file equals the layer's, line for line. At the sill both rules end at the back face, 0.5 m behind the front.
- No bright sample lacked a reflection ray, and no walk missed again.
- The 10 % rule's shares agree with the saved run of the sill probe, `hits9`: 74.2 % class 0 and 25.5 % class 1. That run used another tree and the automatic exposure.

### Where the hits land

- Under the 10 % rule, 99.5 % of the class-0 light lands on the render row 375 (2,997,185 of 3,011,681). That row is the edge row: the first row below the end window's opening.
- The layer's 4 bright false samples land on the row 383.
- In the census's last frame, the row 375 shows the sky at the hit columns. The Mac's dump of draw 39 shows the sky on the row 375. Its row 376 shows the wall's inner face at 39.51 m, with a slab of 0.500 m. So the edge row's sample changes with the jitter.
- Geometry from `DemoScenes.cs`: the nose window spans 0.65 m to 2.65 m above the floor, and the eye stands 1.65 m above it. One render row covers 6.25 cm at 39.5 m: the 2 m opening spans 32 rows. The sill's top face, 0.5 m deep and 1 m below the eye, spans 0.2 rows.

### Two checks of the classes

The every-sample census (`TERTIUS_PROBE_SILL_ALL=1`) counts every sample of the glow area, bright or not:

| Rule | Class 0 | Class 1 | Class 2 | Walk misses again |
|---|---|---|---|---|
| 10 % rule | 868,891 / 3,241,853 | 2,695,727 / 1,722,456 | 27,222 / 11,682 | 351,360 / 12,758 |
| Layer as the slab | 742,533 / 121,311 | 2,692,654 / 896,657 | 27,208 / 4,723 | 480,805 / 22,366 |

- The layer keeps the class-1 hits: 2,692,654 against 2,695,727 samples, 0.1 % fewer. Their light falls by 48 %.
- The class-0 samples on the edge rows 375 and 376 fall from 140,031 to 18,456.
- Most of the layer's remaining class-0 samples land on the far wall below the window, rows 380 to 419 and columns 480 to 799. Their light is 3.7 % of the 10 % rule's class-0 light.

The tolerance check ran the bright census again with a tolerance of 0.3 % and of 1 %. Class 0 does not change in either rule. Under the 10 % rule, part of class 1 moves to class 2: 425 samples at 0.3 %, 73 at 1 %.

Reading: the bright class-1 light falls by half because the room is darker under the layer, not because real hits go. The every-sample census keeps the same class-1 hits, and the hit surfaces' light falls with the removed false bounce light. Part 2 shows that darkening.

### What makes the 10 % rule's hits false, and why the layer keeps none

Reading, from the geometry above. A reflection ray from the glow area falls about 0.30 m per meter toward the sill. It passes through the window above the sill and leaves the room. The edge row's sample lies on the wall's inner face, on the sill's top or on the sky, by the jitter. Under the 10 % rule, the edge row's slab reaches 3.95 m behind it, so the leaving ray meets it. Under the layer, the slab ends at the outer face, 0.5 m behind the inner face. A sample on the sill's top ends it sooner.

Stage 1 assumed that the 10 % rule's class-0 hits of the fade steps 28 to 31 enter within the real 0.5 m. Those steps hold 8.1 % of the light here too. But their entry share is measured against the edge row's own sample. So "within 0.5 m of that sample" is not "inside the wall". The layer census shows that those rays no longer hit.

The finding: the chosen design removes the false sill hits. No other mechanism is needed for them. The census has one blind spot. It names a hit on the sill's top face class 0, because the hit pixel's plane is the inner face's. The bright census holds 4 such samples at most.

## Part 2: contact darkening and thick walls

Each image holds three columns. They are the 10 % rule (`ten`), the layer as the slab (`layer`), and `layer+fade`. The last is the layer with the fade over at most the 10 % slab. Each crop is at full size from the 1920 × 1080 still.

### rect-lamps (the cabin of `orbit`, the person's camera)

Stops 2.7868438. Noise floor 50.94: the ceiling's specks move between frames. Under `layer`, 1,972 of 8,040 tiles lie above it, and all are darker. The per-pixel largest channel difference passes 8 of 255 on 97.25 % of the pixels, against 5.08 % for the noise pair.

| Place | Tiles (output px) | Mean | Peak | Class |
|---|---|---:|---:|---|
| The ceiling, the far wall and the upper side walls | 1,766 tiles in x 272–1632, y 0–624 | −64.1 | −131.9 | A removed false hit |
| The floor's patches under the side windows | 48, 43, 27 and 20 tiles at the four nearest windows | −64 to −68 | −90.5 | A removed false hit |
| The window jambs: the side faces of the openings | 19, 15, 9 and 8 tiles | −72 to −85 | −140.2 | A removed false hit (reading) |

- `layer+fade` has the same regions: 2,047 darker tiles, with the same means within 2.
- [pictures-rect-lamps-ceiling.png](pictures-rect-lamps-ceiling.png) shows the far ceiling. The 10 % column has a bright ceiling with a cloud of specks and a glow above the far lamp. The `layer` and `layer+fade` columns show a dark ceiling with a few dozen specks.
- [pictures-rect-lamps-side-window.png](pictures-rect-lamps-side-window.png) shows the nearest right window, a thick wall of 0.5 m. In the 10 % column, the jamb is white, and the floor below holds a bright sparkling patch. In the other two columns, the jamb is gray with a gradient, and the floor patch is dimmer.
- [pictures-rect-lamps-regions.png](pictures-rect-lamps-regions.png) shows the regions over the dimmed 10 % still, at half size. Blue is darker.

Reading: part 1 shows that 74 % of the glow area's bright light is false. The windows show black space, so a ray that leaves through them brings almost no light. The darker room is the room without the false bounce light. The probe's control 5 hid this darkening, because the automatic exposure rose by 0.7 stops.

### The cabin of `demo-exit` (`cabin-camera`)

Stops −0.3901896. Noise floor 5.27. Under `layer`, 792 tiles lie above it: 603 brighter, 189 darker.

| Place | Tiles | Mean | Peak | Class |
|---|---|---:|---:|---|
| The left wall's window frames and pillars, and the beam above them | 394 tiles in x 416–944, y 80–880 | +7.3 | +43.8 | Contact darkening weaker |
| A round patch of the floor, in the middle | 153 tiles in x 720–976, y 720–928 | +7.0 | +9.8 | Another class: more of a reflection's hit light |
| The partition wall with the doorway, on the right | 123 tiles in x 1440–1664, y 448–768 | +6.4 | +11.7 | Another class: more bounce light |
| The far floor before the end window | 10 tiles in x 880–1024, y 336–400 | −6.2 | −6.9 | Contact darkening stronger |

- Under `layer+fade`, 738 tiles lie above the floor: 42 brighter and 696 darker. The largest region is 595 tiles in x 624–1536, y 288–816, darker by 7.4 on the mean.
- [pictures-cabin-window-frames.png](pictures-cabin-window-frames.png) shows the left window frames: thick pillars and jambs. In the 10 % column, the jamb between two pillars is a black strip. In the `layer` column, it is mid-gray. In the `layer+fade` column, it is dark again, but lighter than the 10 % column.
- [pictures-cabin-floor.png](pictures-cabin-floor.png) shows the floor patch. The `layer` column is bluer and brighter. The other two columns look alike.
- [pictures-cabin-partition.png](pictures-cabin-partition.png) shows the partition wall. The `layer` column's wall is lighter and bluer. The other two columns look alike.
- [pictures-cabin-regions.png](pictures-cabin-regions.png) shows the regions of `layer` over the dimmed 10 % still. Red is brighter.

Reading: the hit decision is the same in `layer` and `layer+fade`. Only the fade differs, and the two columns move in opposite directions. So the fade by the entry share sets the direction of the cabin's change. On a slab deeper than 10 %, a hit at the same depth keeps more of its light. I cannot tell which column is right without a reference. I name no leak and no hole here.

### The deck of `demo-exit` (`deck-camera`)

Stops 0.7126267. Noise floor 4.66 without the person's 112 tiles (x 672–800, y 416–640). Under `layer`, 46 tiles lie above it, all darker.

| Place | Tiles | Mean | Peak | Class |
|---|---|---:|---:|---|
| The ship's interior seen through its glass, right | 23 tiles in x 1376–1504, y 256–336 | −5.5 | −6.6 | Another class: less light inside the ship |
| The same, center | 12 tiles in x 1104–1216, y 320–368 | −5.8 | −7.7 | The same |
| The hangar wall left of the person | 6 tiles in x 640–672, y 368–416 | −4.9 | −5.0 | Contact darkening stronger (reading) |

`layer+fade` gives 59 darker tiles at the same places. [pictures-deck-ship-frames.png](pictures-deck-ship-frames.png) shows the ship's frames and glass. The three columns look alike to me. The change lies at the noise floor.

## Part 3: surfaces with no back face

### The sheets of each view

The sheet census reads the back share and the front distance of every counted draw: the same images as the probe's dump. The share is the mean over 32 draws.

| View | Surface pixels | Sheets | Share |
|---|---:|---:|---:|
| `lamp-hall-16-hair`, `hair-camera` | 605,660 | 174,764 | 28.9 % |
| `skin-and-hair`, `hair-camera` | 380,324 | 8,982 | 2.4 % |
| `hair-rugs`, `low-camera` | 764,691 | 17 | 0.0 % |
| `demo-meshes`, the default camera after 180 ticks | 641,644 | 10 | 0.0 % |

The lamp hall's sheets are the hair cards' pixels that have only the sky behind them. They lie 0.89 m to 1.21 m from the eye (1st to 99th percentile), in the rows above the floor's horizon. No floor and no wall is a sheet there: the floor is a box. The rugs of `hair-rugs` lie on the floor, so they take the floor's back face and are no sheets.

### The borrowed back face

The card pixels in front of the floor or a pillar are no sheets. Their first back face behind the front surface is the floor's or the pillar's. The Mac's dump of the lamp hall holds 131,399 surface pixels nearer than 1.5 m with a back face. Their median slab is 4.12 m. The cards are the only surfaces that near.

The sheet census counts each traced ray once, at the pixel that started it. Even draws count the reflection rays and odd draws the diffuse rays. Over the draws 16 to 47 of the lamp hall:

| Rule | Reflection rays | Their hits | Hits whose start lies behind the hit's plane |
|---|---:|---:|---:|
| 10 % rule | 3,646,708 | 74,176 | 101 |
| Layer as the slab | 3,635,373 | 860,419 | 815,413 |

The diffuse rays: 61,879 hits under the 10 % rule and 466,835 under the layer, of which 430,146 start behind the hit's plane.

A ray whose start lies behind the hit's plane, moving away from it, cannot reach the plane. Each of those hits is false. [pictures-lamp-hall-hits-map.png](pictures-lamp-hall-hits-map.png) shows where the hits land under the layer. It comes from the Mac's smoke test of the census build, draws 16 to 47. Gray is the front distance, red the hits per hit pixel on a log scale, and blue the sheets. The red lies on the cards below the horizon, in front of the floor, and on the floor in front of the pillars.

[pictures-lamp-hall-hair.png](pictures-lamp-hall-hair.png) shows the lower cards and the floor in three columns: the 10 % rule, `layer` and `layer/plane`. Stops 5.4094524 and noise floor 3.72. Only 8 tiles pass the noise floor, all darker by at most 4.8. I see no difference between the columns.

Reading: this is a misbehavior of the model as stated. The model says that the slab runs to the first back face behind the front surface. A card that the pass does not draw has the floor's back face behind it. So the model joins the card, the air and the floor into one slab. The pass leaves out the two-sided and cutout runs, and any open mesh acts the same. The lamp hall hides the effect, because the hit light and the miss light behind the cards are alike. It needs a named other thing: a mark for each surface that is not closed, so that the build ignores the back faces behind it. The G-buffer pass or the material is a candidate owner of that mark.

### Can a ray hit a slab of zero thickness

Yes. The walk compares the ray's span of w across a cell with the slab. So a slab of no depth meets a ray that crosses its w inside the cell. The counts over the same frames, with the share of the 10 % rule's count:

| View | Kind | 10 % rule: hits on a sheet / of class 1 | `NoBackSheet` | `NoBackSheetPlane` |
|---|---|---|---|---|
| Lamp hall | Reflection | 6,511 / 3,275 | 816 / 687 (12.5 % / 21.0 %) | 1,526 / 1,333 (23.4 % / 40.7 %) |
| Lamp hall | Diffuse | 24,066 / 12,607 | 2,935 / 2,485 (12.2 % / 19.7 %) | 5,936 / 5,273 (24.7 % / 41.8 %) |
| skin-and-hair | Reflection | 731 / 241 | 83 / 77 (11.4 % / 32.0 %) | 167 / 153 (22.8 % / 63.5 %) |
| skin-and-hair | Diffuse | 4,123 / 685 | 288 / 255 (7.0 % / 37.2 %) | 510 / 454 (12.4 % / 66.3 %) |

- Class 1 is the sill census's class: the ray crosses the hit pixel's plane where the eye sees that surface. A hit on a sheet of another class passed through a gap in the card, or beside it.
- The lamp hall's sheets face the camera. On 99.7 % of them, the front distance changes by less than 0.3 % to any neighbor pixel. So the lost hits are not at a sheet seen at a grazing angle. They are rays that travel nearly along the card's plane.
- `hair-rugs` and `demo-meshes` hold too few sheets for a count: 17 and 10 pixels.

[pictures-skin-hair.png](pictures-skin-hair.png) shows one card fan of `skin-and-hair` in three columns: the 10 % rule, `layer` and `layer/plane`. Stops −7.9722414 and noise floor 12.72. No tile passes it: the mean absolute difference is 0.004 of 255, against 0.138 for the noise pair. I see no difference.

[pictures-hair-rugs-low.png](pictures-hair-rugs-low.png) shows the right rug and the base of the glowing panel in the same three columns. Stops −12.749431 and noise floor 4.55. 52 tiles are darker, by 6.6 on the mean. They form a strip of the floor at the panel's base: 28 tiles at x 1504–1840 and 26 at x 96–416, y 432–496. The rug itself does not change. `layer` and `layer/plane` give the same image there, byte for byte.

Reading: the sheets lose most of their real hits, and the plane rule wins back about half of the loss. The loss is a stair case of the ray: a ray that travels nearly along a card changes its w so little per pixel that it crosses the card's w between two cells. The stills show no effect in these views, because the cards' hit light and the miss light behind them are alike. A bright object behind a curtain of cards could show it.

## The images that show the most

1. [pictures-rect-lamps-ceiling.png](pictures-rect-lamps-ceiling.png): the removed false light of the ceiling.
2. [pictures-rect-lamps-side-window.png](pictures-rect-lamps-side-window.png): a thick wall's jamb and the floor patch below it.
3. [pictures-cabin-window-frames.png](pictures-cabin-window-frames.png): the fade sets the direction of the change on thick pillars.
4. [pictures-lamp-hall-hits-map.png](pictures-lamp-hall-hits-map.png): the borrowed back face, as the hits that it adds.

## What I did not check

- No reference image: no path tracer ran. So a class of part 2 that needs "right" or "wrong" is my reading.
- The census of part 1 cannot see the sill's top face, which is 0.2 rows tall. It names a hit through that face class 0.
- The every-sample census writes its codes into every pixel's age, so its light values come from a disturbed accumulation. Its counts of hits and classes hold. The bright census disturbs only the bright samples.
- Part 3's map comes from the Mac and its counts from the cloud. Each is one measurement, not a comparison of the two.
- I did not run a view where a bright object lies behind hair cards, so the visible size of the borrowed back face and of the sheets' lost hits is open.
- The deck's noise floor leaves out the walking person. Its contact with the floor is not measured.
- Every run used the RTX 2000 Ada and its NVIDIA driver. None ran on RADV, ANV or lavapipe.

## The runs and the raw data

Every output is under `/private/tmp/ter-kh4g/1b/pictures/`:

| Folder | Job | What it holds |
|---|---|---|
| `cloud/rect-lamps` | `1d9f93df` | Stills of `walls`: `out/pack/<column>-000239.webp`, `ten-000231.webp`, the exposure files. |
| `cloud/rect-lamps-timeout` | `f3084524` | The first try with the main control, which passed the job limit of 840 s. |
| `cloud/cabin`, `cloud/deck` | `76981df9`, `2d7fb6d5` | Stills of `walls`. |
| `cloud/lamp-hall-hair`, `cloud/skin-hair`, `cloud/hair-rugs-low` | `fc9145da`, `5d51c87d`, `c505ee3e` | Stills of `hair`. |
| `cloud/control` | `8c8466c6` | Main against the mode `ten`. |
| `cloud/sill` | `473bb34c` | The sill census: `out/sill-rect-lamps-<mode>/census.txt`. |
| `cloud/sill-tolerance` | `3c3b45a4` | The tolerance check. Its last step failed after the four census files came back. |
| `cloud/sill-all`, `cloud/sill-all-ten` | `10e128fa`, `90a6e7d9` | The every-sample census. |
| `cloud/sheet` | `6b46817e` | The sheet census: `census.txt` per view and mode, the slope tables and the maps. |
| `smoke` | Mac | The census build's smoke test and its maps. |
| `sill-src` | none | The tree of the census build. |

The commands, run from the worktree:

```bash
python3 -I tools/runpod/gpu_job.py submit --agent t1-gxzg-pictures --tree t=refs/scratch/ter-kh4g-1b-sill-stills \
  --run 'bash t/tools/bf1b/job-stills.sh walls tools/bf1b/stills-rect-lamps.json' \
  --output 'out/pack/*.webp' --output 'out/summary.txt' --output 'out/*/exposure.json' --timeout 800 --out <folder>
python3 -I tools/runpod/gpu_job.py submit --agent t1-gxzg-pictures --tree t=refs/scratch/ter-kh4g-1b-sill-census \
  --run 'bash t/tools/bf1b/job-census.sh sill' --output 'out/*/census.txt' --timeout 800 --out <folder>
```

- The other stills jobs change the set (`walls` or `hair`) and the script. The sheet census runs `job-census.sh sheet`.
- The tolerance check and the every-sample census run `job-census.sh sill-tolerance` and `sill-all` on the later trees.
- `job-census.sh` names the environment variables of each run.

The analysis, run in the dev shell from [tools/pictures](tools/pictures):

```bash
python3 -I regions.py /private/tmp/ter-kh4g/1b/pictures/cloud/cabin layer
python3 -I regions.py /private/tmp/ter-kh4g/1b/pictures/cloud/deck layer 16 672,416,800,640
python3 -I crops.py /private/tmp/ter-kh4g/1b/pictures/cloud/cabin 520 300 760 900 pictures-cabin-window-frames.png ten layer layerfade
python3 -I sill_report.py glow <ten run folder> <layer run folder> <trim run folder>
python3 -I sheet_report.py /private/tmp/ter-kh4g/1b/pictures/cloud/sheet/out
```

The cloud cost of this ticket, by the job log's estimates: $0.93 for 13 jobs. The timed-out first rect-lamps job counted $0.15 of it.

## Changes to the method

- The sill probe's patches do not apply to the probe's tree, so I ported their true mode by hand. The port keeps the classes and the 2 % tolerance. It drops the dominant part and the miss-wins bit.
- The settled frame is the film's frame 239, not the capture after the film. That capture is a still that resets the histories.
- The left column is the probe's mode `ten`, with one control job against main on the card.
- Part 1 adds two checks: a tighter tolerance and the every-sample census. Part 1 also fixes the exposure, because the census's threshold of 50 is in pre-exposed units.
- Part 3 counts the hits with a census build, not from a dump. The census reads the same back share image, at every counted draw.
- The main subject of part 3 became the borrowed back face. The lamp hall's sheets are the cards against the sky, not a floor or a wall.
