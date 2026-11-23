# The cost of one environment face: 32 against 128, with and without a cull of small objects (ter-yglw)

Experiment E3 of [the lighting review](../lighting-system-review-2026-11-22/README.md), section 12. Design P of that
review needs about 216 environment faces for a cabin. This record times the existing face pass on the deck at a face
size of 32 and of 128, each with and without a cull of objects smaller than one texel.

**Base: main 65a24d189, after Design X.** The probe is a scratch change (`probe.patch`). It never entered `src` on main.

**Section 10 (t1-6tn2) has its own base: main ce871714a**, after this record's merge. It splits the fixed part of
today's face cost into its sources, and measures the cost floor of a lean field face of design P.

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
  started, so the failure was passing. The cause: at about 02:45 another session's Nix garbage collection deleted the
  dev shell's MoltenVK from the store, and the next run fetched it again. The takes of section 10 run in a dev shell
  with a garbage collection root (`nix develop --profile <evidence folder>/nix/dev-profile`), whose closure holds
  MoltenVK.

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

## 10. The fixed part, split (t1-6tn2)

**Base: main ce871714a.** Takes on 2026-11-23. The probe is the same scratch change with more switches
(`probe.patch`, section 10.9).

Sections 4 and 5 found that 98 % of a face's cost does not shrink with its size. This section splits today's face
(128, no cull, one face per draw) into its sources: the Earth, the irradiance convolution, the rest of the prefilter, and
what is left. Then it measures a lean field face, the face that a field point of design P would draw.

### 10.1 The answer in short

| Source of today's face cost | Mac, span | Mac, passes | Card, span |
|---|---:|---:|---:|
| Today, one face at 128 (ms) | 0.696 | 0.631 | 0.174 |
| The Earth | 0.016 (2 %) | 0.038 (6 %) | 0.038 (22 %) |
| of it: a 4,096-triangle Earth would save | −0.033 (−5 %) | 0.005 (1 %) | 0.016 (9 %) |
| of it: its draws in the near depth range | 0.066 (9 %) | 0.017 (3 %) | 0.012 (7 %) |
| The irradiance convolution | 0.159 (23 %) | 0.182 (29 %) | 0.034 (19 %) |
| The rest of the prefilter | 0.230 (33 %) | 0.155 (25 %) | 0.040 (23 %) |
| What is left: the room, the light pass, the passes' own fixed cost | 0.292 (42 %) | 0.255 (40 %) | 0.051 (29 %) |
| Interaction: today less the Earth, the prefilter and what is left | −0.001 | 0.002 | 0.011 (6 %) |

- **On the Mac the prefilter is the lever: 0.39 ms, 55 % of today's face.** The irradiance convolution is 0.16 to 0.18
  ms of it. Its other dispatches (the radiance and distance mips, the specular levels, the irradiance downsample and
  their barriers) are 0.16 to 0.23 ms.
- **On the Mac the Earth is not a lever: 0.02 to 0.04 ms, 2 to 6 %.** A 4,096-triangle Earth saves nothing measurable,
  and leaving out its near-range draws saves 0.02 to 0.07 ms inside the spread between runs. The Mac pays for the
  Earth's covered texels, not its triangles: it costs 0.10 ms in the root map's face pass, where it covers most of five
  faces, and nothing in the ship's map, where the walls hide it (section 10.4).
- **On the card the Earth is 22 % and the prefilter 42 %.** A coarse Earth saves 9 %.
- **What is left is 0.26 to 0.29 ms on the Mac, 40 %.** A face that draws nothing at all still costs 0.08 to 0.10 ms in
  the face pass. Per face, that pass records three depth-range renderings and one light rendering with five barrier
  flushes, even when a range has no draws, and on MoltenVK each is a Metal encoder of its own.
- **The lean field face does not fit 0.7 ms per draw on the Mac by the span**: 216 faces in 60 draws cost 1.11 ms per
  draw at 16 and 1.48 ms at 32 (section 10.6). By the pass sum they cost 0.57 and 0.96 ms. On the card they cost 0.16
  to 0.17 ms per draw.
- **With the empty depth ranges skipped, the lean field face fits at 16**: 0.35 ms per draw by the span, and 0.63 ms at
  32 (section 10.6, "Skipping the empty depth ranges"). The skip saves nothing for today's face.

### 10.2 Terms

- **Parts**: the parts of a face's work that a variant switches off (`TERTIUS_PROBE_FACE_PARTS`, `FaceProbe.cs`):
  - `earth-off`: the faces leave out the Earth. The Earth is the instance of a mesh whose farthest vertex lies 6,000 to
    7,000 km from its origin: `Primitives.Sphere(EarthRadius, 256, 128)`, 65,536 triangles.
  - `earth-coarse`: the faces draw the Earth as `Primitives.Sphere(radius, 64, 32)`, 4,096 triangles. The main view keeps
    its own mesh.
  - `earth-reach`: a face draws an instance only in the depth ranges that its surface can reach (section 10.3).
  - `irr-off`: the prefilter skips the irradiance convolution's dispatch. The irradiance downsample still runs.
  - `prefilter-off`: the prefilter pass records nothing.
  - `own`: a map's faces draw only the instances of the map's own frame.
  - `specular-off`: the prefilter skips the specular levels. The mips and the irradiance still run.
  - `ranges-skip`: a face begins at its first depth range that has draws and skips the empty ranges after it.
  The prefilter switches act only in a frame that draws fewer than six faces of a map. The first draw, which refreshes
  every face, prefilters as today, and the variants' maps then keep that draw's levels. Those variants measure cost only.
  Their picture is not judged.
- **Saving**: today's face cost less the variant's, on one machine, by one measure, from runs of the same session.
- **Two measures on the Mac.** On MoltenVK a pass's own time is not valid (section 2), so the Mac has two:
  - **span**: the face cost as in section 2, the pair difference of the frame's GPU span. It counts everything that a
    face adds to the draw, also in other passes.
  - **passes**: the pair difference of `environment faces` + `environment prefilter`, a sum of neighbouring passes,
    which is valid on MoltenVK, as the mean over the two maps. It leaves out what a face adds to other passes.
  On the card each pass's own time is valid, and the span and the passes agree within 0.01 ms.
- **What is left**: the face cost with `earth-off+prefilter-off`. It holds the room's surfaces, the light pass and the
  fixed cost of the face pass's renderings.
- **Empty face**: a face that draws no instance. In the lean runs the root map's faces are empty, because the Earth is
  the root frame's only object and the lean face leaves it out.
- **Lean field face**: the face that a field point of design P would draw (the orchestrator's definition, ter-6fka):
  `own+earth-off`, the frame's own objects only, with the light pass and without the prefilter, six faces per pass, at
  16 and at 32. A texel that sees none of the frame's objects stays empty: here the parent frame's map would serve it.
  Its second row, `own+earth-off+specular-off`, adds today's irradiance convolution (a 16 × 16 output per face from level
  0) with today's radiance and distance mips, the form that the engine has now.

### 10.3 Method

1. The probe of sections 3 and 7 with the switches of section 10.2 (`tools/apply-face.py`, `tools/FaceProbe.cs`).
2. **No variant draws the Earth in one depth range only**, because that gives a wrong picture. On the deck the eye is
   400 km above the ground. In a face's 90° frustum a point at distance d has a view depth of at least d / √3, so the
   Earth's surface lies at view depths from 231 km to past the 2,020 km boundary of the middle range: it needs the
   middle and the far range. It never reaches the near range (below 2.02 km), which today draws it anyway, because the
   range rule tests the Earth's bounding sphere. `earth-reach` drops a range whose far boundary lies below
   (|center| − radius) / √3, for every instance. That is exact, and it drops the Earth's near range only.
3. **`ranges-skip` is exact**: the dumped faces with and without it are bit-identical, 12 on the deck and 18 in the cabin
   (`tools/counts.sh`, compared with `cmp`).
4. **The Mac**: `tools/queue-mac.sh`, 3 rounds of each variant, a run set aside at a load of 8 or more at the lock. The
   split runs at a spacing of 2 (`results/t1-6tn2/mac2-facecost.jsonl`), the lean faces at 6 faces per pass and a
   spacing of 4 (`lean-facecost.jsonl`), and the range skip in a later session (`mac2b-facecost.jsonl`,
   `lean2-facecost.jsonl`). The split at a spacing of 4 was not completed (end of section 10.4). Every take ran in a
   dev shell with a garbage collection root (`nix develop --profile <evidence folder>/nix/dev-profile`), whose closure
   holds MoltenVK (section 4.1). 1,288 to 1,296 MHz in every kept run of the split and the first lean runs, and 1,275 to
   1,296 MHz in the range-skip runs.
5. **The card**: one job (f3fbc2c3, 197 s, $0.043) with a discarded warm-up run: the split at a spacing of 2 and the lean
   faces at 6 per pass and a spacing of 4, 2 rounds each (`card-facecost.jsonl`). SM clock 2,130 to 2,460 MHz while
   busy (mean 2,363). The card's runs at a spacing of 4 in section 7.2 showed no difference from a spacing of 2, so the
   card's split has a spacing of 2 only. An earlier job (855d3d70, $0.027) failed after its warm-up, because of a bug in
   `card.sh` that a variant without parts set off. Cloud money of t1-6tn2: $0.070.
6. **The counts**: `tools/counts.sh` on the deck and in the cabin view, untimed (`tables/t1-6tn2-counts.md`).
7. `tools/split.py` makes the tables from facecost.py's lines. facecost.py now counts the instances and triangles of a
   face frame as the mean over its drawn faces (before, the first face's alone, which changes nothing for one face per
   pass).

### 10.4 The split, per variant

`tools/split.py`, means over runs. Saving and share against today's row of the same table.

| Measure | Face | Parts | Runs | Load at the lock | Face cost per run (ms) | Mean (ms) | Saving against today (ms) | Share of today | 216 faces in 60 draws (ms per draw) |
|---|---:|---|---:|---|---|---:|---:|---:|---:|
| Mac, span | 128 | none | 3 | 2.7, 4.0, 4.3 | 0.638, 0.735, 0.716 | 0.696 |  | 100% | 2.51 |
| Mac, span | 128 | earth-coarse | 3 | 5.6, 3.1, 5.4 | 0.731, 0.731, 0.726 | 0.729 | -0.033 | 105% | 2.63 |
| Mac, span | 128 | earth-off | 3 | 3.4, 2.1, 4.8 | 0.686, 0.669, 0.686 | 0.680 | 0.016 | 98% | 2.45 |
| Mac, span | 128 | earth-off+prefilter-off | 3 | 6.6, 4.2, 4.2 | 0.287, 0.294, 0.295 | 0.292 | 0.404 | 42% | 1.05 |
| Mac, span | 128 | earth-reach | 3 | 6.8, 5.5, 3.9 | 0.724, 0.576, 0.591 | 0.630 | 0.066 | 91% | 2.27 |
| Mac, span | 128 | irr-off | 3 | 3.9, 5.3, 6.7 | 0.544, 0.532, 0.534 | 0.537 | 0.159 | 77% | 1.93 |
| Mac, span | 128 | prefilter-off | 3 | 5.1, 5.2, 4.0 | 0.241, 0.336, 0.343 | 0.307 | 0.390 | 44% | 1.10 |
| card, span | 128 | none | 2 | -, - | 0.164, 0.184 | 0.174 |  | 100% | 0.63 |
| card, span | 128 | earth-coarse | 2 | -, - | 0.151, 0.165 | 0.158 | 0.016 | 91% | 0.57 |
| card, span | 128 | earth-off | 2 | -, - | 0.132, 0.139 | 0.136 | 0.038 | 78% | 0.49 |
| card, span | 128 | earth-off+prefilter-off | 2 | -, - | 0.051, 0.051 | 0.051 | 0.123 | 29% | 0.18 |
| card, span | 128 | earth-reach | 2 | -, - | 0.155, 0.169 | 0.162 | 0.012 | 93% | 0.58 |
| card, span | 128 | irr-off | 2 | -, - | 0.126, 0.155 | 0.141 | 0.034 | 81% | 0.51 |
| card, span | 128 | prefilter-off | 2 | -, - | 0.102, 0.099 | 0.100 | 0.074 | 58% | 0.36 |

The passes per map (mean pair differences, ms; on the Mac only the span and the sum of the two passes are valid alone,
and the single passes are shown for their trend):

| Machine | Parts | Map 0 span | Map 0 faces | Map 0 prefilter | Map 1 span | Map 1 faces | Map 1 prefilter |
|---|---|---:|---:|---:|---:|---:|---:|
| Mac | today | 0.556 | 0.178 | 0.347 | 0.834 | 0.400 | 0.337 |
| Mac | earth-off | 0.501 | 0.081 | 0.345 | 0.857 | 0.420 | 0.340 |
| Mac | earth-reach | 0.452 | 0.191 | 0.328 | 0.806 | 0.405 | 0.304 |
| Mac | earth-coarse | 0.618 | 0.176 | 0.344 | 0.838 | 0.394 | 0.339 |
| Mac | irr-off | 0.433 | 0.174 | 0.169 | 0.639 | 0.398 | 0.157 |
| Mac | prefilter-off | 0.223 | 0.179 | 0.002 | 0.389 | 0.399 | 0.010 |
| Mac | earth-off+prefilter-off | 0.105 | 0.080 | -0.001 | 0.476 | 0.420 | 0.010 |
| card | today | 0.146 | 0.071 | 0.082 | 0.202 | 0.127 | 0.083 |
| card | earth-off | 0.094 | 0.009 | 0.081 | 0.177 | 0.089 | 0.083 |
| card | earth-reach | 0.135 | 0.060 | 0.082 | 0.189 | 0.116 | 0.083 |
| card | earth-coarse | 0.131 | 0.040 | 0.082 | 0.185 | 0.098 | 0.083 |
| card | irr-off | 0.110 | 0.071 | 0.040 | 0.171 | 0.128 | 0.041 |
| card | prefilter-off | 0.071 | 0.071 | -0.000 | 0.129 | 0.128 | -0.000 |
| card | earth-off+prefilter-off | 0.009 | 0.009 | -0.000 | 0.092 | 0.090 | -0.000 |

- **The Earth on the Mac**: `earth-off` lowers the root map's face pass from 0.178 to 0.081 ms, and leaves the ship's
  map's at 0.400 to 0.420 ms. `earth-coarse` and `earth-reach` leave the root map's face pass at 0.176 and 0.191 ms. So
  the Earth's cost on the Mac lies in the texels that it covers, which neither a coarse mesh nor fewer ranges remove.
- **The Earth on the card**: `earth-off` lowers the root map's face pass from 0.071 to 0.009 ms and the ship's map's from
  0.127 to 0.089 ms, although the ship's walls hide the Earth in the ship's map on the deck (`pics/deck-map1-lean.png`,
  rows 1 and 2 look the same). `earth-coarse` saves half of the root map's 0.062 ms.
- **The prefilter** costs the same per map on both maps: 0.34 ms on the Mac and 0.08 ms on the card. The irradiance
  convolution is half of it on both machines.

The split at a spacing of 4 was not completed. Two of its runs were kept (round 0 of today's face and of `earth-off`,
`results/t1-6tn2/mac4/`) and four were set aside at a load of 8.4 to 19.9. The line dropped the rest: the spacing of 2
and the card settle the split, and quiet time on the Mac is scarce. No table uses these runs.

### 10.5 The faces' triangles per variant

The first draw's counts, summed over each map's six faces (`tables/t1-6tn2-counts.md`; the size cull is off):

| View | Map | Today | `earth-off` | `earth-reach` | `earth-coarse` | Lean (`own+earth-off`) |
|---|---:|---:|---:|---:|---:|---:|
| deck | 0 (root) | 917,504 | 0 | 655,360 | 57,344 | 0 |
| deck | 1 (ship) | 867,804 | 81,372 | 605,660 | 130,524 | 81,372 |
| cabin | 0 (root) | 917,504 | 0 | 655,360 | 57,344 | 0 |
| cabin | 1 (ship) | 862,464 | 76,032 | 600,320 | 125,184 | 76,032 |
| cabin | 2 (cabin) | 870,352 | 83,920 | 608,208 | 133,072 | 7,888 |

The Earth is 91 % of the ship's map's triangles on the deck. A lean face of the ship's map draws 13,562 triangles on
average on the deck. A lean face of the cabin's map draws 1,315.

### 10.6 The lean field face

Six faces per pass, a spacing of 4. The face cost is the ship's map's (map 1 on the deck), per face, because the
root map's lean faces are empty. Today's row is today's face at 6 per pass, from the same session.

| Measure | Face | Parts | Runs | Load at the lock | Face cost per run (ms) | Mean (ms) | Saving against today (ms) | Share of today | 216 faces in 60 draws (ms per draw) |
|---|---:|---|---:|---|---|---:|---:|---:|---:|
| Mac | 128 | none | 3 | 4.7, 6.1, 2.9 | 0.802, 0.597, 0.597 | 0.665 |  | 100% | 2.39 |
| Mac | 32 | own+earth-off+prefilter-off | 3 | 4.8, 3.9, 2.8 | 0.421, 0.407, 0.401 | 0.410 | 0.256 | 62% | 1.48 |
| Mac | 32 | own+earth-off+specular-off | 3 | 3.9, 5.1, 6.5 | 0.478, 0.459, 0.566 | 0.501 | 0.165 | 75% | 1.80 |
| Mac | 16 | own+earth-off+prefilter-off | 3 | 4.5, 4.6, 6.3 | 0.309, 0.305, 0.313 | 0.309 | 0.356 | 46% | 1.11 |
| Mac | 16 | own+earth-off+specular-off | 3 | 4.0, 7.9, 7.0 | 0.322, 0.321, 0.326 | 0.323 | 0.342 | 49% | 1.16 |
| card | 128 | none | 2 | -, - | 0.136, 0.134 | 0.135 |  | 100% | 0.49 |
| card | 32 | own+earth-off+prefilter-off | 2 | -, - | 0.048, 0.043 | 0.045 | 0.089 | 34% | 0.16 |
| card | 32 | own+earth-off+specular-off | 2 | -, - | 0.061, 0.062 | 0.062 | 0.073 | 46% | 0.22 |
| card | 16 | own+earth-off+prefilter-off | 2 | -, - | 0.047, 0.047 | 0.047 | 0.088 | 35% | 0.17 |
| card | 16 | own+earth-off+specular-off | 2 | -, - | 0.050, 0.055 | 0.053 | 0.082 | 39% | 0.19 |

The same runs by the pass sum (Mac) and the empty faces of the root map:

| Machine | Face | Parts | Ship's map: span | Ship's map: passes | 216 faces in 60 draws by the passes (ms per draw) | Empty face (root map): span | Empty face: passes |
|---|---:|---|---:|---:|---:|---:|---:|
| Mac | 32 | lean | 0.410 | 0.267 | 0.96 | 0.286 | 0.104 |
| Mac | 16 | lean | 0.309 | 0.159 | 0.57 | 0.291 | 0.101 |
| Mac | 32 | lean + irradiance | 0.501 | 0.323 | 1.16 | 0.312 | 0.164 |
| Mac | 16 | lean + irradiance | 0.323 | 0.187 | 0.67 | 0.277 | 0.130 |
| card | 32 | lean | 0.045 | 0.044 | 0.16 | 0.005 | 0.004 |
| card | 16 | lean | 0.047 | 0.046 | 0.17 | 0.004 | 0.004 |
| card | 32 | lean + irradiance | 0.062 | 0.062 | 0.22 | 0.021 | 0.021 |
| card | 16 | lean + irradiance | 0.053 | 0.052 | 0.19 | 0.010 | 0.010 |

- **On the Mac an empty face costs 0.28 to 0.31 ms by the span**, at 16 and at 32 alike, and 0.10 ms in the face pass.
  The 0.19 ms between the two shows as growth in `local_shadows`, `G-buffer` and `transparent` of the face frame and the
  frame after it. This record cannot say whether that is face work that late stamps put in other passes, or a real
  slowdown of those passes, for example a wait on the shadow image that the light pass reads. The draw pays it either
  way. One empty face at a spacing of 2 costs 0.10 ms by the span (section 10.4, `earth-off+prefilter-off`, map 0),
  so the two measures differ most for a frame of six faces.
- **The room adds little at 16**: the ship's lean face costs 0.02 ms more than an empty face by the span at 16, and 0.12
  ms at 32.
- **The irradiance adds 0.01 to 0.09 ms per face on the Mac** and 0.006 to 0.017 ms on the card, in today's form.
- **On the card** the lean face costs 0.045 to 0.047 ms, and an empty face 0.003 to 0.005 ms.

#### Skipping the empty depth ranges

`ranges-skip` on the Mac, in a session after the runs above: today's face at a spacing of 2
(`results/t1-6tn2/mac2b-facecost.jsonl`), and the lean face at 6 faces per pass and a spacing of 4, without and with the
skip (`lean2-facecost.jsonl`). Each session has its own baseline, so the two tables do not mix with the tables above.
Two runs were set aside at a load of 8 or more.

Today's face, both maps:

| Parts | Runs | Load at the lock | Span per run (ms) | Span, mean (ms) | Passes, mean (ms) |
|---|---:|---|---|---:|---:|
| none | 3 | 4.1, 5.3, 7.4 | 0.737, 0.742, 0.734 | 0.738 | 0.626 |
| ranges-skip | 3 | 5.9, 7.8, 3.3 | 0.755, 0.715, 0.727 | 0.732 | 0.620 |
| earth-reach+ranges-skip | 3 | 4.9, 5.4, 6.7 | 0.484, 0.711, 0.718 | 0.638 | 0.621 |

The mean of `earth-reach+ranges-skip` holds one outlier, 0.484 ms. Without it the mean is 0.715 ms.

The lean face (`own+earth-off+prefilter-off`), the ship's map (map 1), and the empty face of the root map (map 0):

| Face | Parts | Load at the lock | Ship's map: span (ms) | Ship's map: passes (ms) | 216 faces in 60 draws by the span (ms per draw) | Empty face: span (ms) | Empty face: passes (ms) |
|---:|---|---|---:|---:|---:|---:|---:|
| 32 | lean | 3.7, 7.2, 7.3 | 0.411 | 0.269 | 1.48 | 0.286 | 0.116 |
| 32 | lean + `ranges-skip` | 4.8, 4.8, 7.0 | 0.175 | 0.209 | 0.63 | 0.028 | 0.053 |
| 16 | lean | 4.4, 4.2, 7.3 | 0.299 | 0.160 | 1.08 | 0.280 | 0.097 |
| 16 | lean + `ranges-skip` | 4.9, 6.9, 7.6 | 0.097 | 0.106 | 0.35 | 0.037 | 0.049 |

- **For today's face the skip saves nothing measurable**: 0.006 ms by the span and by the passes. Today's face has draws
  in every range, because the Earth's bounding sphere reaches the near range.
- **For the lean face the skip removes most of the fixed cost on the Mac.** An empty face falls from 0.28 ms to 0.03
  to 0.04 ms by the span. The lean face at 16 falls from 0.299 to 0.097 ms, and 216 faces in 60 draws then cost 0.35 ms
  per draw. At 32 they cost 0.63 ms per draw.
- The 0.19 ms between an empty face's span and its passes in section 10.6 disappears with the skip. So that cost came
  from the empty renderings.

### 10.7 The pictures

- `pics/deck-map0-earth-coarse.png`: the root map's faces on the deck at −7 stops, today's Earth (top row) and the
  coarse Earth (bottom row). The horizon and the light look the same. In the face that looks down (−Y), the grid's thin
  lines break up and wiggle, because the coarse triangles stretch the grid texture.
- `pics/deck-map1-lean.png`: the ship's map on the deck at +4 stops. Rows: today at 128, `earth-off` at 128, the lean face
  at 32, the lean face at 16. The `earth-off` row looks the same as today's: the walls hide the Earth. At 32 the room
  stays, blocky. At 16 two small lamp panels and most of the rack are gone.

### 10.8 Reading

The levers for today's face, at most, on the Mac by the span and by the passes, and on the card:

| Lever | Mac, at most | Card, at most |
|---|---|---|
| No prefilter for today's face | 0.39 ms (55 %) | 0.074 ms (42 %) |
| No irradiance convolution | 0.16 to 0.18 ms (23 to 29 %) | 0.034 ms (19 %) |
| No Earth in the faces | 0.02 to 0.04 ms (2 to 6 %) | 0.038 ms (22 %) |
| A 4,096-triangle Earth | nothing measurable | 0.016 ms (9 %) |
| No near-range draw of the Earth | 0.02 to 0.07 ms, inside the spread | 0.012 ms (7 %) |
| What no part removes | 0.26 to 0.29 ms (40 to 42 %) | 0.051 ms (29 %) |

- On the Mac the prefilter is worth a design: it is the one part that removes more than half of today's face.
- On the Mac the Earth is not worth a design. On the card it is, but the card is not where the budget binds (workbench
  rule 4).
- The face pass's own structure (the renderings and flushes per face) is the floor of every variant on the Mac,
  including the lean field face. A design that wants many faces per draw on the Mac has to lower the renderings per face.
- Skipping the empty depth ranges is that lowering for the lean field face: at 16 it brings 216 faces in 60 draws to
  0.35 ms per draw by the span, inside the bound of 0.7 ms (section 10.6, "Skipping the empty depth ranges"). It saves
  nothing for today's face.
- This record designs no fix.
- A probe of several faces in one batch of tiles was planned and dropped by the line's decision, unmeasured: the range
  skip settled the store's cost question. Its code is not in `probe.patch` or in `tools/`.

### 10.9 Files of section 10

- `probe.patch`: the probe's diff against main ce871714a (`src` only), all switches of sections 3, 7 and 10.
- `tools/`: `FaceProbe.cs` and `apply-face.py` with the switches of section 10.2; `split.py`; the runners take a variant as
  `size:cull:parts`.
- `tables/t1-6tn2-counts.md`.
- `results/t1-6tn2/`: `mac2-facecost.jsonl`, `lean-facecost.jsonl`, `card-facecost.jsonl`, `mac2b-facecost.jsonl` and
  `lean2-facecost.jsonl`, and per run the face log, the frame log, the run log and `gpu-timings.json` (`mac2/`, `lean/`,
  `card/`, `counts/`, `mac2b/`, `lean2/`, and the kept and set-aside runs at a spacing of 4 in `mac4/`), and the card's
  clocks.
- `pics/deck-map0-earth-coarse.png`, `pics/deck-map1-lean.png`.
