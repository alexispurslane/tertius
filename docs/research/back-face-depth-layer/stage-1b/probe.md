# The probe of stage 1b: the back layer as the slab, stored in the depth pyramid

This is the record of the throwaway probe of ticket `t1-n8x9`, of 2026-11-14. It models the design that Alexis chose: the slab of a pixel runs from its front surface to the first back face behind it, and the depth pyramid stores it. The pictures ticket `t1-gxzg` and the cost ticket `t1-stwp` measure with this probe. The terms are those of the ticket and of [the record of stage 1](../README.md).

The probe is not on the branch. The file [probe-slab-in-pyramid.patch](probe-slab-in-pyramid.patch) holds it, against the review base `b8fe11032`. It changes a specialization constant count and a format that the Rendering tests pin, so the branch keeps only the patch.

## What the probe changes

| Part | Change |
|---|---|
| `thickness.glsl` | The slab rules, the rules for a pixel with no back face, two specialization constants (ids 12 and 13), and `slabBack(w, share)`. |
| `depth_pyramid_finest.comp` | The finest level becomes RG32F. R is the center w, as before. G is `slabBack(w, share)`. Sky holds (0, 0). |
| `depth_pyramid_first.comp` | Each pixel's far bound becomes `slabBack(farBound, share)` before the merge. The levels above merge as before. |
| `screen_walk.glsl` | The walk reads `near` and `back` from the texel at every level. It multiplies by no constant. The fade switch is a specialization constant (id 14). |
| `BackFacePass` | The mode parser, and the pass draws the one-sided solid runs only. |
| `PyramidImages`, `RenderTargets`, `DepthPyramidPass`, `PyramidBuild` | The depth pyramid's finest image takes the pyramid's format, RG32F. Binding 0 of the finest set and of the level-1 set holds the back share. The depth build gets the constants. |
| `ShaderConstants`, `VulkanRenderer` | Three constants from the mode. The dump also writes the finest level, level 1 and the draw counts. |

The back share reaches the build through binding 0 of the pyramid's own sets. The depth pyramid's finest shader and level-1 shader never read that binding, because they read the G-buffer. So no layout gains a binding.

## The modes

One binary holds every rule. The variable `TERTIUS_BACK_FACE_PROBE` selects the mode, and specialization constants carry it into the shaders.

| Mode | The pass | Slab rule | Rule for a pixel with no back face |
|---|---|---|---|
| `off` (the default) | Not drawn | `SlabTenPercent` | None |
| `ten` | Drawn | `SlabTenPercent` | None |
| `trim` | Drawn | `SlabTrimmed` | None: the 10 % rule |
| `layer` or `layer/sheet` | Drawn | `SlabLayer` | `NoBackSheet` |
| `layer/plane` | Drawn | `SlabLayer` | `NoBackSheetPlane` |
| `layer/ten` | Drawn | `SlabLayer` | `NoBackTenPercent` |
| `layer/solid` | Drawn | `SlabLayer` | `NoBackSolid` |

A last `+fade` adds the fade switch to any mode, such as `layer+fade`. With it, the walk computes the entry share against `max(back, near / (1 + ThicknessShare))`. The slab that decides a hit does not change.

The mode `off` is main's frame through the new store: RG32F at the finest level, without the pass. The cost ticket can compare it with main for the cost of the store alone.

Under `TERTIUS_BACK_FACE_DUMP=<folder>`, every capture writes these raw files, row by row from the top:

- `front-<n>-<W>x<H>.f32`: the front distance in m.
- `back-<n>-<W>x<H>.f32`: the back share.
- `finest-<n>-<W>x<H>.rg32`: the finest level, R and G per pixel.
- `level1-<n>-<w>x<h>.rg32`: level 1, padding included, at the size of the image's mip.
- `draws-<n>.txt`: the draw commands of each kind.

The dump does not run in the mode `off`.

## How to build and run

The frozen binaries are on the Mac, under `/private/tmp/ter-kh4g/1b/`:

| Folder | Source |
|---|---|
| `main/` | Main, `ab54c785e`, built from a `git archive` export in `src-main/`. |
| `probe/` | The patch on `b8fe11032`, which is the tree of `refs/scratch/ter-kh4g-1b-off`. One binary for every mode. |

Run a binary from the worktree's root, so that the script paths resolve. Take the GPU lock in shared mode, inside the dev shell:

```bash
nix develop --command bash -c 'TERTIUS_BACK_FACE_PROBE=layer bash /Users/alexispurslane/Development/tertius/tools/gpu-lock.sh --shared <out>/run.log /private/tmp/ter-kh4g/1b/probe/Tertius.Demo --script scripts/rect-lamps.json --headless --out <out>'
```

The helper `/private/tmp/ter-kh4g/1b/tools/still.sh <binary folder> <out> <mode> <script>` runs the same command. The checker `/private/tmp/ter-kh4g/1b/tools/slab_check.py` computes controls 3 and 4 from one dump. It needs numpy, which the dev shell holds.

The folder [tools](tools) holds copies of these helpers and of the one-capture scripts, because `/private/tmp` does not survive a restart.

To build the probe again, apply the patch to `b8fe11032` and build `src/Tertius.Demo` in Release.

A cloud job runs every run of a job in one environment. So each mode has a scratch commit on `b8fe11032`, whose only difference from the patch is the default mode in `BackFacePass.DefaultMode`. The script `/private/tmp/ter-kh4g/1b/tools/scratch-refs.sh` made them:

| Ref | Commit | Default mode |
|---|---|---|
| `refs/scratch/ter-kh4g-1b-off` | `e224992b94ba34dff32e5d20f00fca87f540943d` | `off` |
| `refs/scratch/ter-kh4g-1b-ten` | `a1bd38179e3fa84cd1d3e5d9cf8f06cbe678c005` | `ten` |
| `refs/scratch/ter-kh4g-1b-trim` | `500441177677c3e3af20a63f046440197dcfe375` | `trim` |
| `refs/scratch/ter-kh4g-1b-layer` | `1c8fbbdd064237f863b5aae1c403fc509384700b` | `layer` |
| `refs/scratch/ter-kh4g-1b-layer-plane` | `90019b38e05b93dd0d6ce777ca74ebe5dfd4b990` | `layer/plane` |
| `refs/scratch/ter-kh4g-1b-layer-ten` | `34d1f0a22f7124ac4083bdc90b04bfca5adaacdb` | `layer/ten` |
| `refs/scratch/ter-kh4g-1b-layer-solid` | `6e446a7bb20b11e01a79bc645c374c6e0ebe2fbd` | `layer/solid` |
| `refs/scratch/ter-kh4g-1b-layer-fade` | `f3aba963ea5927a636cb39ae0245100af84e0c75` | `layer+fade` |

The scratch refs live in the shared repository's refs. A cloud job must push its commit as the job tools describe.

## The controls

Every output below came from the frozen binaries on the Mac (MoltenVK, M1 Max). The outputs are under `/private/tmp/ter-kh4g/1b/`.

The first runs of controls 2 to 5 used an earlier build of the probe. Its source differed from the patch in two shader comments only. The review found that, so I ran control 2, every dump and the three stills of control 5 again on the frozen `probe/`. Each new output equals the first output byte for byte. The first outputs are in `first-build/`, and `stills-frozen/` holds the new stills.

### Control 1: main twice

Two runs of `main/` with `scripts/rect-lamps.json` at 1280 × 720 gave the same `cabin.png` and the same 240 film frames, byte for byte. The tolerance of control 2 is therefore zero. Outputs: `c1/main-1`, `c1/main-2`.

### Control 2: the 10 % rule through the new store against main

The modes `ten` and `off` of `probe/`, with the same script, gave main's `cabin.png` and main's 240 film frames, byte for byte. So the pass changes nothing that the frame reads, and the new store computes main's slab. Outputs: `c2/ten`, `c2/off`.

The dump shows the exact slab. In the mode `ten`, G equals `R * 0.909090936` on all 399,704 surface pixels. That constant is `1.0 / (1.0 + 0.1)`, which glslc folds in double precision. A check in single precision gives 0.90909088 and then reports a false difference of one or two units in the last place. At level 1, the same rounding commutes with the minimum, because a multiply by a positive constant keeps the order.

### Control 3: the finest level under the layer rule

One capture of `rect-lamps` (`scripts/rect-lamps-one.json`: the person's camera, one capture) gives a render size of 848 × 480. The results by mode:

| Mode | Surface pixels | With a back face | Sheets | G against the rule |
|---|---:|---:|---:|---|
| `ten` | 399,704 | 399,681 | 23 | Equal on every surface pixel |
| `trim` | 399,704 | 399,681 | 23 | Equal on every surface pixel |
| `layer` | 399,704 | 399,681 | 23 | G / R within 9.1e-8 of the share, relative. G = R on all 23 sheets. |
| `layer/ten` | 399,704 | 399,681 | 23 | The same, and G = R · 0.909090936 on the 23 sheets |
| `layer/solid` | 399,704 | 399,681 | 23 | The same, and G = 0 on the 23 sheets |

- No surface pixel has G above R, and every sky pixel holds G = 0.
- The sill: 2,466 pixels lie 39.45 to 39.70 m from the eye and have a back face. Under `layer` and `trim`, their slab along the view ray is 0.501 to 0.502 m (25th to 75th percentile). Under `ten` it is 3.957 to 3.966 m.

The 23 sheets of `rect-lamps` show no effect of the rule `layer/plane`. So a hair view tests it: `scripts/lamp-hall-16-hair.json` reduced to one capture after 10 ticks (`hair-one.json`). There, 80,465 of 270,728 surface pixels are sheets.

- Under `layer`, G = R on all 80,465 sheets.
- Under `layer/plane`, G is below R on 54,395 sheets and equal on the rest. R is the same in both modes.
- Level 1 is identical, byte for byte, under `layer` and `layer/plane`. The plane acts at the finest level only.

Outputs: `dump/<mode>`, `dump-hair/layer`, `dump-hair/layer-plane`.

### Control 4: the invariant

For every cell of level 1 that holds a surface, the checker compares its G with the smallest G of the pixels under it. It also compares its R with the largest R under it.

| View and mode | Cells with a surface | Cells whose G is above a pixel's G | Cells whose R is below a pixel's R |
|---|---:|---:|---:|
| `rect-lamps`, each of the six modes | 100,031 | 0 | 0 |
| The hair view, `layer` and `layer/plane` | 74,943 | 0 | 0 |
| The deck of `demo-exit`, `layer` | 216,773 | 0 | 0 |
| The cabin of `demo-exit`, `layer` | 230,400 | 0 | 0 |

So the slab of each cell holds the slab of every pixel under it. Outputs: `dump/<mode>`, `dump-hair`, `dump-deck`, `dump-cabin`.

### Control 5: a still of each slab rule

One still of `scripts/rect-lamps.json` per slab rule, after the script's 240 draws. The demo argument `--size 1920x1080` sets the output size, and FSR 3.1 renders at 1280 × 720. I opened each still. Outputs: `stills/ten/cabin.png`, `stills/trim/cabin.png`, `stills/layer/cabin.png`, and `stills/layer-fade/cabin.png` for the fade switch.

- The 10 % still shows main's look: bright clouds and many specks on the ceiling, sparkling patches on the floor, and glow around the side windows.
- The trimmed still and the layer still lose the ceiling's clouds and most of its specks. The floor's patches and the windows' glow become weaker.
- The layer still differs from the trimmed still by more than 8 of 255 on 1.1 % of its pixels. They spread over the ceiling, walls and floor, and nearly all are darker.
- At this size I find no new hole and no new leak in the layer still.

The automatic exposure differs: 3.05 stops for `ten`, 3.72 for `trim` and 3.74 for `layer`. So these stills do not compare levels of light. The pictures ticket uses a fixed exposure.

### The fade switch

The mode `layer+fade` gave a still that differs from the `layer` still, so the switch reaches the walk. The difference is small in this view. The measure is the largest difference of a pixel's three channels. It is above 8 of 255 on only 0.01 % of the pixels, and its mean is 0.56 of 255. The changed pixels lie on the walls and the floor. By the sum of the three channels, 123 pixels are brighter by more than 24 and 144 are darker. The exposure is 3.75 stops against 3.74. Output: `stills/layer-fade/cabin.png`.

### The draw commands that the pass draws

The pass draws the one-sided solid runs only. The counts are sums over the three depth ranges of one captured frame.

| View | One-sided solid | One-sided cutout | Two-sided solid | Two-sided cutout |
|---|---:|---:|---:|---:|
| `rect-lamps` | 6 | 0 | 0 | 0 |
| The deck of `demo-exit` | 20 | 0 | 0 | 0 |
| The cabin of `demo-exit` | 20 | 0 | 0 | 0 |
| The hair view of `lamp-hall-16-hair` | 20 | 0 | 0 | 1 |

So only the hair view holds a run that the pass leaves out. In the deck 8 of 866,742 surface pixels are sheets, and in the cabin none of 921,600.

## The readers of ThicknessShare that the probe leaves

| Reader | What it does today | What it does under the layer as the slab |
|---|---|---|
| `TargetDepthShare` in `screen_trace.glsl`, κ / 2 | A guided diffuse ray aims at a box from a bright pixel's center depth to 5 % behind it. | The box does not follow the slab. Behind a thin surface, such as the sill, the box reaches past the back face, so the walk rejects some guided rays. A rejected guided ray counts as a miss, so the estimate stays unbiased and only the guiding's gain falls. Behind a thick wall the box lies inside the slab, as today. |
| The plane tolerance in `screen_trace_plan.comp` | For a block's reflection ray to fit a pixel, the pixel's point must lie within κ times its view depth of the block's plane. | Unchanged: it is a tolerance of a plane, not a thickness. |
| The environment map's near entry | The comment of `thickness.glsl` names it. | No shader and no C# file reads `ThicknessShare` for it in this tree. A search finds only the walk, `TargetDepthShare`, the plan's tolerance and the C# copies. The comment is stale. |

## The limits of the probe

- The C# copies (`ScreenRay`, `DepthPyramid`, `CellBounds`) still compute the 10 % rule. With the patch applied, the Rendering tests that pin the constant count, the finest format and the walk fail. The probe adds no test.
- The level-1 rule multiplies a pixel's far bound by the share of the pixel's center ray. The real back face along the far corner's ray can lie elsewhere. The invariant holds anyway, because the far bound is at most the center w and both get the same share.
- The rule `layer/plane` deepens a sheet only at the finest level. Its level-1 bound is the same as under `layer`.
- The finest texel grows from 4 to 8 bytes. The cost ticket measures what that costs.
- Two owners write binding 0 of the depth pyramid's level-1 set. `PyramidImages` writes the finest level there, and `RenderTargets.WriteBackShareSets` then writes the back share over it. Each `Pyramid.Create` must be followed by that second write. The real mechanism gives the pyramid's owner the image that its build reads, so that one writer remains.
- Four comments in `PyramidImages.cs` are stale under the patch. They name an R32F finest level, binding 0 of the finest set as unwritten, and binding 0 of level 1 as unread.
- The mode parser writes the numbers of the rules in C# beside the constants of `thickness.glsl`, and it reads the variable on each call. A probe accepts that. The real mechanism has one rule and no switch.
- Every run used MoltenVK on the M1 Max. The probe did not run on NVIDIA, RADV, ANV or lavapipe.
- The stills come from one frame of one view per rule, with automatic exposure.
- The hair view holds 30 % sheets. They come from surfaces with no back face behind them, not only from the hair cards. The pictures ticket can look at what they are.

## The settled details

- The 10 % rule multiplies by `1.0 / (1.0 + ThicknessShare)`, as main's walk did, and does not divide as the ticket writes. A divide rounds differently: in the dump of the mode `ten`, R / 1.1 differs from main's product on 244,261 of 399,704 surface pixels.
- The switches are specialization constants, not one tree per rule. So one frozen binary holds every rule, and a timing run can interleave the modes as stage 1 did. A cloud job gets a tree per mode through the scratch refs.
- The probe keeps a mode `off`, without the pass, beside `ten`. The cost ticket can then separate the cost of the store from the cost of the pass.
