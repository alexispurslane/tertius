# The retained deviation's rounding weight (lin-4ffc): stills and mutants

Ticket lin-4ffc changed the rounding test of the retained deviation σr in `accumulate_temporal.comp`. The test now weighs a sample's step with the fast history's blend weight 1 / min(L, 4), not with σr's weight 1 / min(L, 32). This folder holds the evidence of the change on the stills and the list of mutants.

## The stills

The survey's still part rendered two cases at the line's head 2f9461158 (before) and at the fix a4c472728 (after). The probe build used `tools/renderer-survey/probe.patch` with its `LeastMotion` hunk moved to `motion.glsl`. The command was:

```bash
nix develop --command python3 -I tools/renderer-survey.py run <out> --parts still --only bay-sunbeam exit-cabin --no-build
```

| Case | Build | Specks per 10,000 pixels | Blotch (90th percentile) | Blotch RMS | Level |
|---|---|---:|---:|---:|---:|
| bay-sunbeam | before | 0.0694 | 0.02985 | 0.01888 | +0.00048 |
| bay-sunbeam | after | 0.0694 | 0.02985 | 0.01888 | +0.00047 |
| exit-cabin | before | 0.2074 | 0.01530 | 0.00958 | −0.00051 |
| exit-cabin | after | 0.1977 | 0.01530 | 0.00958 | −0.00051 |

The 8-bit shown stills differ very little. In bay-sunbeam, 668 pixels differ, 2 of them by more than 2 levels of 255, and none by more than 6. In exit-cabin, 11 pixels differ, each by 1 level. The design expected no change or a slightly wider clamp box at long histories. The clamp box of these views rarely binds, so the wider box changes almost no pixel.

Each crop shows the before still on the left and the after still on the right. The crops are at full size: 1 pixel of the still is 1 pixel of the crop.

| Crop | Place in the still | What it shows |
|---|---|---|
| `bay-sunbeam-wall.png` | x 160 to 544, y 96 to 448 | The sun strip on the left wall and the lit back wall. This region holds the most changed pixels. The strip's edge is clean, and the wall's gradient is smooth with a fine grain. The two sides look the same. |
| `bay-sunbeam-floor.png` | x 520 to 904, y 600 to 920 | The floor under the sunbeam. A smooth gradient, with a fine grain in the darker lower left. The two sides look the same. |
| `exit-cabin-floor.png` | x 760 to 1144, y 520 to 840 | The floor with soft bands of sunlight through the window frames, the box and the tilted pillar. A faint blue mottle on the upper left floor is in both. The two sides look the same. |
| `exit-cabin-wall.png` | x 0 to 384, y 260 to 580 | The metal beams and frames of the left wall. A fine grain on the beams, the same in both. |

By the user's bar, the fix adds no blotch, daub, bright speck, trail or flicker to these stills. Leave out rect-lamps and the cabin switch: that room holds a leak of the sun's shadow until another line's fix lands.

## The mutants

`mutants.json` lists the mutants for `tools/mutate.py`. Rows L01 to L04 are the line's rows of the half-float store fix (t8-69v7). Rows R01 to R05 are this fix's rows. Run the list in the dev shell from the repository root:

```bash
nix develop --command python3 -I tools/mutate.py docs/research/lin-4ffc-rounding-weight/mutants.json <out>
```

The first run killed every row except R02 and R03. The test `ASampleWithoutNoiseLeavesNoDeviationWhenItsFastHistoryStallsShortOfIt` came after it, and kills both. The table of results is in the notes of tickets lin-4ffc and t8-69v7.
