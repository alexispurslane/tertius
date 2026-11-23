# The cost of one environment face: 32 against 128, with and without a cull of small objects (ter-yglw)

Experiment E3 of [the lighting review](../lighting-system-review-2026-11-22/README.md), section 12. Design P of that
review needs about 216 environment faces for a cabin. This record times the existing face pass on the deck at a face
size of 32 and of 128, each with and without a cull of objects smaller than one texel.

**Base: main 65a24d189, after Design X.** The probe is a scratch change (`probe.patch`). It never entered `src` on main.

## 1. The answer in short

- **Design P does not fit as sketched.** On the Mac, one face costs 0.52 to 0.57 ms at 32 with the cull, 2.6 to 2.8
  times the bound of 0.2 ms. A refresh of 216 faces within 60 draws then costs 1.88 to 2.04 ms per draw. The bound of
  0.2 ms per face gives 0.72 ms per draw. The brief's 1.5 ms per draw is the cost of 0.42 ms per face (section 8).
- The table gives the measure with a face in every second draw. A face in every fourth draw gives 11 % less on the Mac at
  128 and 8 % less at 32 with the cull (section 7.1). The record keeps both, as a range of the Mac's face cost.

| Variant | Mac: face cost | Mac: 216 faces in 60 draws | Card: face cost | Card: 216 faces in 60 draws |
|---|---:|---:|---:|---:|
| 128, no cull (today) | 0.722 ms | 2.60 ms per draw | 0.178 ms | 0.64 ms per draw |
| 128, cull | 0.704 ms | 2.53 ms per draw | 0.191 ms | 0.69 ms per draw |
| 32, no cull | 0.546 ms | 1.96 ms per draw | 0.141 ms | 0.51 ms per draw |
| 32, cull | 0.566 ms | 2.04 ms per draw | 0.143 ms | 0.51 ms per draw |

- **A smaller face helps little, because almost all of a face's cost does not shrink with its size.** Fitted as a
  fixed part plus a part per texel, the fixed part is 0.53 ms of the Mac's 0.55 ms at 32 (98 %), and 0.14 ms of the
  card's 0.14 ms. The fixed part alone is 2.7 times the bound on the Mac.
- **The fixed part has two sources:**
  - The face pass draws about 145,000 to 159,000 triangles per face at any size. Most of them are the Earth's sphere,
    65,536 triangles, which a face that sees it draws once in each of up to three depth ranges.
  - The prefilter costs 0.30 ms per face on the Mac at 32 and 0.34 ms at 128. Its irradiance convolution reads the whole
    cube at 32 × 32 for each of its 16 × 16 output texels per face, the same work at every face size of 32 or more. Its
    other dispatches each carry a fixed cost.
- **The cull removes small objects but almost no work.** On the deck it leaves out 9 of 104 instances of the ship's map
  at 128 and 26 at 32, but only 108 and 312 of its 867,804 triangles. Its effect on the face cost lies inside the spread
  between runs on both machines.
- **The cull keeps the lamps and the room.** The face pictures with and without it look the same at each size. A lamp's
  panel is dark in every variant, by design (decision 18 of the environment map plan), and its light reaches the faces
  through the light pass at every size.
- **Six faces in one pass cost less per face than one.** On the Mac at 32 with the cull, a face in a batch of six costs
  0.35 to 0.38 ms in two quiet runs, against 0.52 ms alone. A refresh of 216 faces within 60 draws then costs 1.27 to
  1.37 ms per draw. That is under the brief's 1.5 ms per draw, but still about twice the 0.72 ms that the bound of
  0.2 ms per face gives (section 7). On the card a batched face at 32 with the cull costs 0.085 ms, 45 % less than
  alone, and 216 faces in 60 draws cost 0.31 ms per draw.

## 2. Terms

- **Face**: one of the six square images of an environment map, W × W texels, drawn from the camera's position in one
  of the map's axis directions. W is the face size: 128 today, 32 in the variants.
- **Map 0, map 1**: the maps of the served chain of frames, root first. On the deck, map 0 is the root frame's map (the
  space outside, with the Earth) and map 1 the ship's. In the cabin view a third map, map 2, is the cabin's frame.
- **The cull**: an instance is left out of a face when its bounding sphere covers less than one texel of that face,
  seen from the capture point. In the probe: the sphere's radius over its distance from the eye is below
  sin(atan(1 / W)), the half angle of the widest texel, the one at the face's center. An instance that holds the eye is
  never culled.
- **Face frame, empty frame**: the probe draws the in-turn face only in every second drawn frame, the face frame. The
  drawn frame between two face frames draws no face: the empty frame.
- **GPU span**: the GPU time of a frame from its first timestamp to its last. On MoltenVK a pass's own time can hold
  part of the next pass's work, so only the span and the sums of neighbouring passes are valid there
  (`ScriptCommand.LateStampsNote`). On the card each pass's own time is valid.
- **Pair difference**: a face frame's value minus the mean of the values of its two neighbouring empty frames.
- **Face cost**: the GPU time that one face's refresh adds to a draw: the mean pair difference of the GPU span over the
  face frames of a run, divided by the faces that each face frame draws. It counts every pass that the face adds. Two
  passes change:
  - `environment faces`: the face's surfaces and their light (`EnvironmentFacePass`);
  - `environment prefilter`: the downsample of the radiance and distance cubes, the specular levels and the irradiance
    of the drawn face (`EnvironmentPrefilterPass`).
- **216 faces in 60 draws**: the face cost times 216 / 60 = 3.6, the cost per draw of refreshing 216 faces within 60
  draws, which is the review's sketch of design P.
- **Fixed part**: with a cost fitted as a + b W² through the two sizes, a is the part of a face's cost that does not
  shrink with its size. W² is the face's texel count.
- **Load**: the 1-minute load average of the Mac (10 cores) when the run took the GPU lock, from `uptime` in its log.

## 3. Method

1. **The probe** (`tools/apply-face.py`, which copies `tools/FaceProbe.cs` and `tools/FrameTimeProbe.cs`) makes these
   changes to a tree. `probe.patch` is its diff against main 65a24d189.
   - `TERTIUS_PROBE_FACE_SIZE`: the face size of the default map.
   - `TERTIUS_PROBE_FACE_CULL=1`: the cull, in `SceneBatcher.BatchFace`, after the face's own frustum cull.
   - `TERTIUS_PROBE_FACE_EVERY=2`: in-turn faces in even drawn frames only. Each face still comes in turn. A refresh of
     every face (a still's first draw, a cut) stays as it is.
   - `TERTIUS_PROBE_FACES_PER_FRAME`: the faces that each face frame draws, 1 by default.
   - `TERTIUS_PROBE_FACE_LOG`: per drawn frame, each drawn face's map, face, draw commands, triangles, kept instances
     and instances that only the cull left out.
   - `TERTIUS_PROBE_FRAME_TIMES`: per timed frame, its GPU span and the time of each pass.
   - `TERTIUS_PROBE_FACE_DUMP`: at the end of a run, level 0 of each map's radiance cube.
2. **The deck**: `tools/deck-240.json`, demo-exit's deck camera from frame 399 (`tools/gpu-trace.py trim`), 240 frames,
   1280 × 720, two frames in flight, as tools/README.md times the deck. The faces of the default map: one face per face
   frame, in turn over the 12 faces of the 2 maps.
3. **The Mac**: `tools/queue-mac.sh` runs 3 rounds of the four variants in a rotated order, each run through
   `tools/time.sh` under the GPU lock alone and beside gpu-clock. A run starts at a load below 8. A run that took the
   lock at a load of 8 or more is set aside and runs again. A run's face frames are every second drawn frame
   (`TERTIUS_PROBE_FACE_EVERY=2`), except in section 7. The probe was built in the worktree as an uncommitted
   change, because the disk had 29 GiB free, under the 30 GiB that a build copy needs. It was reverted afterwards.
4. **The card** (RTX 2000 Ada): `tools/card.sh` in one job applies the probe to the tree, builds it, makes one discarded
   warm-up run (t1-80jd) and then the same 3 rounds. Job 2a424f8e, 147 s, $0.035. An earlier job (c75a14a5, $0.012)
   failed because a worker's tree leaves out `docs/`. The second job adds the record's folder to the sparse checkout.
   A third job (3c81fff4, 130 s, $0.032) times the batches of section 7. The record's cloud jobs cost $0.079 in all.
5. `tools/facecost.py` reads a run. `tools/summarize.py` makes the tables in `tables/`.
6. **The counts and the pictures**: `tools/counts.sh` runs each variant untimed on the deck (`tools/deck-40.json`) and in
   one cabin view (`tools/cabin-40.json`, demo-exit's cabin camera from frame 100). The first draw refreshes every face,
   so its line of the face log counts all six faces of each map. `tools/cullcount.py` counts them, and
   `tools/faceview.py` draws the dumped faces.

## 4. The face cost

Means over 3 runs per variant. Each run has about 59 face frames per map. The standard error of a run's face cost is
0.011 to 0.015 ms on the Mac and 0.003 to 0.005 ms on the card. **Measured on main 65a24d189, after Design X.**

### 4.1 The Mac (Apple GPU, MoltenVK), 1,296 MHz in every kept run

| Face | Cull | Runs | Load at the lock | Face cost (ms) | Range over runs (ms) | 216 faces in 60 draws (ms per draw) |
|---:|---|---:|---|---:|---|---:|
| 128 | off | 3 | 3.6, 2.9, 4.0 | 0.722 | 0.714 to 0.728 | 2.60 |
| 128 | on | 3 | 6.3, 4.9, 4.4 | 0.704 | 0.667 to 0.731 | 2.53 |
| 32 | off | 3 | 3.6, 4.8, 3.2 | 0.546 | 0.524 to 0.566 | 1.96 |
| 32 | on | 3 | 4.6, 5.4, 2.9 | 0.566 | 0.563 to 0.569 | 2.04 |

Set-aside runs (`results/mac-facecost.jsonl`, names starting with `x-`):

- Loads of 11.7, 13.7, 8.9, 10.4 and 10.1 at the lock. Their face costs (0.726, 0.483, 0.546, 0.720 and 0.736 ms)
  lie in or near their variants' ranges, except the run at 13.7 (0.483 ms at 32 with the cull, standard error 0.052 ms).
- Two runs at 02:47 failed at the start with `vkCreateInstance failed: ErrorIncompatibleDriver`. A run a minute later
  started, so the failure was passing. It is not explained.

### 4.2 The card (RTX 2000 Ada), SM clock 2,130 to 2,475 MHz while busy (mean 2,372)

| Face | Cull | Runs | Face cost (ms) | Range over runs (ms) | 216 faces in 60 draws (ms per draw) |
|---:|---|---:|---:|---|---:|
| 128 | off | 3 | 0.178 | 0.172 to 0.187 | 0.64 |
| 128 | on | 3 | 0.191 | 0.177 to 0.212 | 0.69 |
| 32 | off | 3 | 0.141 | 0.132 to 0.149 | 0.51 |
| 32 | on | 3 | 0.143 | 0.135 to 0.149 | 0.51 |

The Mac's face cost is 3.7 to 4.1 times the card's, more than the workbench's usual factor of 2.4 for a whole frame.

### 4.3 The passes, per map

Mean pair differences in ms (`tables/mac.md`, `tables/card.md`). On the Mac only the span and the sum of the two
neighbouring passes are valid alone. The single passes are shown for their trend.

| Machine | Face | Cull | Map | Span | environment faces | environment prefilter | Faces + prefilter |
|---|---:|---|---:|---:|---:|---:|---:|
| Mac | 128 | off | 0 | 0.612 | 0.175 | 0.348 | 0.523 |
| Mac | 128 | off | 1 | 0.830 | 0.399 | 0.341 | 0.740 |
| Mac | 32 | off | 0 | 0.464 | 0.091 | 0.308 | 0.400 |
| Mac | 32 | off | 1 | 0.626 | 0.252 | 0.296 | 0.548 |
| Mac | 32 | on | 1 | 0.638 | 0.251 | 0.298 | 0.549 |
| card | 128 | off | 0 | 0.149 | 0.071 | 0.082 | 0.153 |
| card | 128 | off | 1 | 0.206 | 0.128 | 0.084 | 0.211 |
| card | 32 | off | 0 | 0.125 | 0.068 | 0.069 | 0.137 |
| card | 32 | off | 1 | 0.156 | 0.097 | 0.069 | 0.165 |
| card | 32 | on | 1 | 0.160 | 0.098 | 0.069 | 0.167 |

An empty frame's two passes cost 0.012 and 0.022 ms on the Mac and 0.0001 and 0.005 ms on the card. The pair
difference removes them.

## 5. The part that does not shrink with the size

Fitted as cost(W) = a + b W² through W = 128 and W = 32, no cull:

| Machine | Quantity | At 128 (ms) | At 32 (ms) | a, the fixed part (ms) | Fixed share at 32 |
|---|---|---:|---:|---:|---:|
| Mac | span, both maps | 0.722 | 0.546 | 0.534 | 98 % |
| Mac | environment faces, map 1 | 0.399 | 0.252 | 0.242 | 96 % |
| Mac | environment faces, map 0 | 0.175 | 0.091 | 0.086 | 94 % |
| Mac | environment prefilter, map 1 | 0.341 | 0.296 | 0.293 | 99 % |
| card | span, both maps | 0.178 | 0.141 | 0.138 | 98 % |
| card | environment faces, map 1 | 0.128 | 0.097 | 0.095 | 98 % |
| card | environment faces, map 0 | 0.071 | 0.068 | 0.068 | 100 % |
| card | environment prefilter, map 1 | 0.084 | 0.069 | 0.067 | 99 % |

Two points fix a line, so this fit cannot show a curve. It says only that going from 128 to 32 removes about a quarter
of the cost, and that a smaller face cannot remove more than the part per texel that is left.

What the fixed part holds, from the code and the counts:

- **The face pass draws the same triangles at every size.** A face of the deck's ship map draws 144,634 triangles on
  average (867,804 over 6 faces), and a face of the root map 152,917 (917,504 over 6). Those of map 0 are all the
  Earth's: `Primitives.Sphere(EarthRadius, 256, 128)`, 65,536 triangles, which spans all three depth ranges of a face
  and so draws 3 times. Map 1 also draws the Earth through the windows. The size cull never touches it: it is the
  largest object in every face.
- **The prefilter's irradiance work is fixed per face.** `environment_irradiance.comp` writes a 16 × 16 face
  (`IrradianceCube.FaceSize`) and reads the whole cube at 32 × 32 (`IrradianceCube.SourceFaceSize`), 6,144 texels, for
  each output texel. That is about 1.6 million reads per face at any face size of 32 or more.
- **The prefilter's dispatches each carry a fixed cost.** Per refreshed face, from `EnvironmentPrefilterPass`:
  - the downsample of the radiance and distance cubes, in runs of 3 levels: 3 runs at 128 (8 levels) and 2 at 32
    (6 levels), each run 2 dispatches;
  - 1 irradiance convolution and 1 irradiance downsample;
  - the specular levels, in dispatches of 4 levels: 2 at 128 (6 levels) and 1 at 32 (4 levels);
  - so about 10 dispatches at 128 and 7 at 32, with barrier flushes between the runs.
  On MoltenVK each compute dispatch behind a barrier is a Metal encoder of its own, which costs GPU time.

## 6. What the cull removes, and the faces' content

Counts in the first draw, summed over each map's six faces (`tables/cull-counts.md`):

| View | Map | Instances at 128 / 32 without the cull | Left out by the cull at 128 | Left out at 32 | Triangles without the cull | Triangles that the cull removes at 32 |
|---|---:|---:|---:|---:|---:|---:|
| deck | 0 | 5 | 0 | 0 | 917,504 | 0 |
| deck | 1 | 104 | 9 | 26 | 867,804 | 312 |
| cabin | 0 | 5 | 0 | 0 | 917,504 | 0 |
| cabin | 1 | 94 | 9 | 21 | 862,464 | 252 |
| cabin | 2 | 109 | 10 | 25 | 870,352 | 300 |

So the cull leaves out about a quarter of the instances at 32 and a tenth at 128, all small, together 0.04 % of the
triangles. Its draw commands fall from 86 to 79 on the deck's ship map. On both machines its effect on the face cost
(−0.018 to +0.020 ms on the Mac, +0.002 to +0.013 ms on the card) lies inside the range between runs.

The faces' content (each map's six faces in layer order +X, −X, +Y, −Y, +Z, −Z; rows 128 without the cull, 128 with it,
32 without, 32 with):

- `pics/deck-map1-faces.png`, the ship's map on the deck at +4 stops: the room's walls, floor and ceiling, the dark lamp
  panels as small squares, and a rack at the top right. The rows with and without the cull look the same at each size.
  At 32 the rack and the ceiling edges are blocky, but they stay. The lamp
  panels stay at 32.
- `pics/cabin-map2-faces.png`, the cabin's map in the cabin view at +4 stops: the cabin's frames, windows and the
  corridor. The same: no visible loss from the cull. At 32 the frames are blocky.
- `pics/deck-map0-faces.png`, the root map on the deck at −7 stops, rows 128 without the cull and 32 with it: black
  space and the Earth with its grid lines, blocky at 32.

The lamps: a lamp's panel shows dark in every variant, because a face adds no emission for an entity that holds a
light (docs/plans/environment-map.md, decision 18). The lamp's light reaches the face's texels through the light pass
(`environment face light`), which lights every texel by every light at every size. The deck's ship map peaks at 0.26
(128) and 0.19 (32) in its brightest texel, so no face holds a lamp's glow at either size. The cull removes no lamp: a
light is not an instance.

## 7. Several faces in one pass

A refresh of 216 faces need not draw one face per pass. This section times six faces of one map in one pass on the
Mac, against one face, at 128 without the cull and at 32 with it.

**Setup.** `TERTIUS_PROBE_FACE_EVERY=4` makes every fourth drawn frame a face frame. `TERTIUS_PROBE_FACES_PER_FRAME`
gives its faces: 6 (one whole map) or 1. With six faces, the face frame's work spilled into the next frame: that
frame's G-buffer pass grew from 0.16 ms to 0.8 to 1.4 ms. So with a spacing of 4, `facecost.py` takes

    face cost = (span of the face frame + span of the frame after it
                 - span of the 2nd frame after it - span of the 3rd frame after it) / faces of the face frame

where the two later frames are empty and clean. Each run has 30 face frames per map. Same deck script, same quiet
rule (load below 8 at the lock), 3 rounds (`results/mac-batch-facecost.jsonl`).

### 7.1 The Mac

| Faces per pass | Face | Cull | Load at the lock | Face cost per run (ms) | Mean (ms) | 216 faces in 60 draws (ms per draw) |
|---:|---:|---|---|---|---:|---:|
| 1 | 128 | off | 4.6, 4.5, 5.6 | 0.648, 0.624, 0.647 | 0.640 | 2.30 |
| 1 | 32 | on | 4.7, 4.3, 5.5 | 0.525, 0.512, 0.526 | 0.521 | 1.88 |
| 6 | 128 | off | 5.6, 5.1, 3.4 | 0.505, 0.501, 0.544 | 0.517 | 1.86 |
| 6 | 32 | on | 6.6, 4.7, 3.3 | 0.554 (standard error 0.105), 0.378, 0.353 | 0.366 (two runs) | 1.32 |

The first run of six faces at 32 has a standard error of 0.105 ms, ten times the other runs'. Its face frames
spread from −1.2 to 5.1 ms per face on map 0 and from 0.23 to 3.0 ms on map 1, but their medians (0.29 and 0.43 ms)
match the other two runs. It ran at the highest load of the set, 6.6. The mean leaves it out. With it, the
mean is 0.428 ms (1.54 ms per draw).

**Six faces in one pass save 0.12 to 0.16 ms per face.** At 32 with the cull, a face in a batch costs 0.35 to 0.38 ms
against 0.51 to 0.53 ms alone, 30 % less. At 128 it costs 0.50 to 0.54 ms against 0.62 to 0.65 ms, 19 % less. That
fits a fixed cost per pass: the passes' setup and the prefilter's dispatch runs are paid once for six faces. The face
pass's triangles do not shrink, so a batched face at 32 still costs about 1.8 times the bound of 0.2 ms.

**One face at a spacing of 4 costs less than at a spacing of 2.** At 128 without the cull it costs 0.640 ms against
0.722 ms, and at 32 with the cull 0.521 ms against 0.566 ms. The two measures differ in what they subtract: the spacing
of 2 subtracts the mean of both neighbours, the spacing of 4 the two frames after the next. With two frames in flight,
the Apple GPU can overlap the end of one frame with the start of the next, so a frame's span can hold part of its
neighbour's work. This record does not settle which measure is closer. It keeps both as the range of the Mac's face cost
for one face: 0.64 to 0.72 ms at 128 and 0.52 to 0.57 ms at 32 with the cull. Both lie well over the bound.

### 7.2 The card

Job 3c81fff4, 130 s, $0.032: the same probe build and a discarded warm-up run, then 2 rounds of each row
(`results/card-batch-facecost.jsonl`). SM clock 2,130 to 2,460 MHz while busy (mean 2,375).

| Faces per pass | Face | Cull | Face cost per run (ms) | Mean (ms) | 216 faces in 60 draws (ms per draw) |
|---:|---:|---|---|---:|---:|
| 1 | 128 | off | 0.189, 0.179 | 0.184 | 0.66 |
| 1 | 32 | on | 0.161, 0.152 | 0.156 | 0.56 |
| 6 | 128 | off | 0.105, 0.105 | 0.105 | 0.38 |
| 6 | 32 | on | 0.086, 0.085 | 0.085 | 0.31 |

On the card, six faces in one pass save 43 % per face at 128 and 45 % at 32 with the cull, more than on the Mac. One
face at a spacing of 4 costs 3 to 9 % more than at a spacing of 2 (0.184 against 0.178 ms, 0.156 against 0.143 ms),
which is inside two standard errors of a run, so the card shows no sign of the Mac's difference between the two
measures. A batched face at 32 with the cull costs 0.085 ms on the card, under the bound of 0.2 ms, and the Mac's
batched face costs 4.3 times as much.

## 8. Reading

- **Against the bound.** The ticket's bound is about 0.2 ms per face on the Mac with the cull. One face costs 0.52 to
  0.57 ms there, and a face in a batch of six 0.35 to 0.38 ms. Design P does not fit as sketched.
- **The bound in ms per draw.** 216 faces within 60 draws is 3.6 faces per draw. At 0.2 ms per face that is 0.72 ms per
  draw. The brief names 1.5 ms per draw for the same bound, which is 0.42 ms per face. Against 0.72 ms, every variant
  fails. Against 1.5 ms, one face per pass fails (1.88 to 2.04 ms per draw), and six faces per pass at 32 with the cull
  pass (1.27 to 1.37 ms per draw).
- **On the card** the same refresh costs 0.51 ms per draw at 32. But rule 4 of the workbench (tools/runpod/WORKBENCH.md)
  holds the 12 ms goal on the Mac, and the Mac's face costs about 4 times the card's.
- **A smaller face is not the lever.** 98 % of a face's cost at 32 does not depend on its size. Below 32 the irradiance
  convolution's source level would shrink too, but the face pass's triangles and the passes' fixed costs would stay.
- **What could make a face cheap**, untested here:
  - Draw far objects coarser in a face: a level of detail for the Earth's 65,536-triangle sphere, or a far field that a
    face draws once. The size cull cannot do this, because the objects that cost are large.
  - Batch many faces into one pass, so that the fixed cost per dispatch is paid once (section 7).
  - A cheaper irradiance for small faces, whose convolution reads less than the whole cube at 32 × 32.

## 9. Files

- `probe.patch`: the probe's diff against main 65a24d189 (`src` only). It applies to that commit with `git apply`.
- `tools/`: the probe (`apply-face.py`, `FaceProbe.cs`, `FrameTimeProbe.cs`), the scripts (`deck-240.json`,
  `deck-40.json`, `cabin-40.json`), the runners (`time.sh`, `queue-mac.sh`, `card.sh`, `counts.sh`) and the readers
  (`facecost.py`, `summarize.py`, `cullcount.py`, `faceview.py`). Each file's header says what it does and how to run it.
- `tables/`: `mac.md`, `card.md` and `cull-counts.md`.
- `results/`: `mac-facecost.jsonl`, `card-facecost.jsonl`, `mac-batch-facecost.jsonl` and `card-batch-facecost.jsonl`
  (one line per run; batch runs start with `4x1-` or `4x6-`), and per run the face log, the frame
  log, the run log and `gpu-timings.json` (`results/mac/`, `results/card/`, `results/counts/`), and the card's clocks (`results/card/clocks.csv`, `results/card/clocks-batch.csv`).
- `pics/`: the face pictures.
