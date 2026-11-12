# Light pairs: every trace of ticket t2-mlvy

Each row is one run of `tools/gpu-trace.sh --no-counters --start 1 --frames 120` on 2026-11-11, on the M1 Max at about 1291 MHz. The deck is `scripts/temporal-upscale-timing.json`, the hall is `scripts/lamp-hall-64-moving.json`, and the stars scene is `scripts/stars-1.json`. The times are busy medians in ms. The exposure is that of the first timed frame, in stops.

A deck variant without a fixed exposure whose exposure differs from main's 0.704 stops proves nothing about its own change. The exposure moves each lamp's reach, so the variant shades other pairs than main (`docs/plans/light-pairs.md`, "How the cost was measured").

A trace is lost when xctrace does not end after the run. The run's own frame span then stands in the last column.

| Scene | Run | What it changes | Direct light | Transparent | Environment faces | Shadow maps | Frame span | Exposure | Note |
|---|---|---|---:|---:|---:|---:|---:|---:|---|
| deck | deck-cb-1 | the bounds of the cascades | 4.088 | 2.151 | 0.440 | 0.609 | 24.676 | 0.704 |  |
| deck | deck-cb2-1 | the bounds of the cascades, taps in loops with a run-time count | 4.113 | 2.160 | 0.429 | 0.614 | 24.615 | 0.704 |  |
| deck | deck-floor11-1 | floor of 2^-11 | 4.751 | 3.013 | 0.467 | 0.177 | 26.133 | 0.706 |  |
| deck | deck-floor8-1 | floor of 2^-8 | 4.386 | 2.677 | 0.491 | 0.176 | 25.345 | 0.748 |  |
| deck | deck-floor9-1 | floor of 2^-9 | 4.600 | 2.758 | 0.474 | 0.178 | 25.657 | 0.725 |  |
| deck | deck-flooroff-1 | the floor's code, floor off | 4.701 | 2.666 | 0.495 | 0.177 | 25.633 | 0.704 |  |
| deck | deck-main-1 | main | 4.190 | 2.321 | 0.429 | 0.178 | 24.732 | 0.704 |  |
| deck | deck-maind-1 | main, exposure fixed | 4.190 | 2.335 | 0.426 | 0.178 | 24.700 | 0.704 |  |
| deck | deck-maind-2 | main, exposure fixed | 4.191 | 2.311 | 0.450 | 0.177 | 24.833 | 0.704 |  |
| deck | deck-t6d-1 | change 1 of t6-h5e3-six-frame-cuts, exposure fixed | 4.091 | 2.064 | 0.435 | 0.176 | 24.322 | 0.704 |  |
| deck | deck-x1-1 | every third light only | 2.204 | 1.423 | 0.410 | 0.177 | 21.755 | 3.319 |  |
| deck | deck-x10-1 | one raw read settles a clear cascade lookup | 4.088 | 2.137 | 0.446 | 0.175 | 24.633 | 0.704 |  |
| deck | deck-x11-1 | one read of a cascade in place of the 16 | 4.233 | 2.090 | 0.444 | 0.177 | 24.550 | 0.704 |  |
| deck | deck-x11d-1 | one read of a cascade in place of the 16, exposure fixed | 4.225 | 2.087 | 0.440 | 0.178 | 24.471 | 0.704 |  |
| deck | deck-x12-1 | the cascade lookup's code kept and never run | 1.504 | 0.936 | 0.355 | 0.177 | 21.681 | -8.960 |  |
| deck | deck-x13d-1 | the sun in a loop of its own, exposure fixed |  |  |  |  |  | 0.704 | trace lost, the run's own frame span median 25.826 ms |
| deck | deck-x16d-1 | every lamp skipped, exposure fixed | 1.470 | 1.057 | 0.411 | 0.178 | 19.905 | 0.704 |  |
| deck | deck-x17d-1 | a rectangle's response without its shine, exposure fixed | 3.148 | 1.880 | 0.379 | 0.177 | 23.124 | 0.704 |  |
| deck | deck-x18d-1 | every rectangle lamp responds as a point light, exposure fixed | 2.530 | 1.873 | 0.386 | 0.175 | 22.518 | 0.704 |  |
| deck | deck-x19d-1 | t6 with the point pairs in a loop of their own, exposure fixed | 3.875 | 2.531 | 0.449 | 0.175 | 24.794 | 0.704 |  |
| deck | deck-x2-1 | every pair stops after its step |  |  |  |  |  | 6.407 | trace lost, the run's own frame span median 19.805 ms |
| deck | deck-x2d-1 | every pair stops after its step, exposure fixed | 1.100 | 0.692 | 0.184 | 0.177 | 19.579 | 0.704 |  |
| deck | deck-x3-1 | no shadow lookup | 0.748 | 0.570 | 0.188 | 0.178 | 19.039 | -10.362 |  |
| deck | deck-x3d-1 | no shadow lookup, exposure fixed | 3.142 | 1.830 | 0.290 | 0.175 | 23.272 | 0.704 |  |
| deck | deck-x4-1 | every response is a constant | 1.783 | 1.232 | 0.349 | 0.179 | 21.273 | -0.482 |  |
| deck | deck-x4d-1 | every response is a constant, exposure fixed | 1.825 | 1.242 | 0.348 | 0.175 | 21.143 | 0.704 |  |
| deck | deck-x5-1 | shadow lookups in a first loop, responses in a second | 6.690 | 2.770 | 0.639 | 0.176 | 28.100 | 0.704 |  |
| deck | deck-x6-1 | no lookup of the lamps' shadow tiles | 3.533 | 2.098 | 0.418 | 0.177 | 23.804 | 0.574 |  |
| deck | deck-x6d-1 | no lookup of the lamps' shadow tiles, exposure fixed | 3.534 | 2.108 | 0.422 | 0.175 | 24.677 | 0.704 |  |
| deck | deck-x7-1 | no lookup of the sun's cascades | 1.011 | 0.616 | 0.326 | 0.177 | 19.406 | -10.362 |  |
| deck | deck-x7d-1 | no lookup of the sun's cascades, exposure fixed | 4.158 | 2.063 | 0.430 | 0.175 | 24.503 | 0.704 |  |
| deck | deck-x8-1 | no far shadows of the sun's sphere occluders | 4.042 | 2.342 | 0.335 | 0.177 | 24.628 | 0.704 |  |
| deck | deck-x9-1 | x4 with the response's code kept and never run |  |  |  |  |  | -0.482 | trace lost, the run's own frame span median 22.469 ms |
| hall | hall-floor9-1 | floor of 2^-9 |  |  |  |  |  | 4.665 | trace lost, the run's own frame span median 19.009 ms |
| hall | hall-floor9-2 | floor of 2^-9 | 3.654 | 0.032 | 0.526 |  | 18.748 | 4.665 |  |
| hall | hall-main-1 | main | 3.802 | 0.031 | 0.560 |  | 18.900 | 4.472 |  |
| hall | hall-x3-1 | no shadow lookup | 2.773 | 0.032 | 0.376 |  | 17.819 | 4.442 |  |
| hall | hall-x4-1 | every response is a constant | 1.755 | 0.031 | 0.320 |  | 17.076 | 10.000 |  |
| hall | hall-x6-1 | no lookup of the lamps' shadow tiles | 3.110 | 0.031 | 0.412 |  | 18.093 | 4.442 |  |
| stars | stars-cb-1 | the bounds of the cascades | 0.289 | 0.029 | 0.062 | 0.546 | 12.908 | -9.698 |  |
| stars | stars-main-1 | main |  |  |  |  |  | -9.698 | trace lost, the run's own frame span median 14.623 ms |

## Key and fill

Each row is one run of the same trace on 2026-11-11, with the exposure fixed by `--exposure`: 0.7041458 stops on the deck and 4.4676704 stops in the hall. The corner and tile pass is the pass "key and fill". Version 1 is `key-fill-v1.patch`: the corners hold a fill at the grid of 8 pixels. The run `deck-m2d-1` is main after the merge of `t6-h5e3`, with the deck's exposure compiled in.

The two frame span columns come from the run's own `gpu-timings.json`, which holds only the 119 or 120 timed frames. The trace report's frame span also holds the warm-up frames, so its 95th percentile is no measure of the budget.

| Scene | Run | Version | Key lights | Corner and tile pass | Direct light | Transparent | Frame span, median | Frame span, 95th percentile |
|---|---|---|---:|---:|---:|---:|---:|---:|
| deck | deck-m2d-1 | main | | | 4.092 | 2.046 | 24.281 | 24.806 |
| deck | deck-kfoff-1 | 1, key and fill off | | | 4.089 | 2.076 | 24.364 | 24.786 |
| deck | deck-kf2-1 | 1 | 2 | 0.805 | 1.084 | 2.068 | 22.325 | 23.044 |
| deck | deck-kf3-1 | 1 | 3 | 0.823 | 1.452 | 2.088 | 22.753 | 23.272 |
| deck | deck-kf4-1 | 1 | 4 | 0.840 | 1.762 | 2.098 | 22.961 | 23.644 |
| hall | hall-kfoff-1 | 1, key and fill off | | | 4.143 | 0.032 | 19.133 | 19.663 |
| hall | hall-kf2-1 | 1 | 2 | 1.264 | 0.375 | 0.031 | 16.681 | 17.197 |
| hall | hall-kf4-1 | 1 | 4 | 1.314 | 0.538 | 0.032 | 16.950 | 17.599 |
| deck | deck-v2off-1 | 2, key and fill off | | | 4.091 | 2.079 | 24.364 | 25.027 |
| deck | deck-v2k2-1 | 2 | 2 | 0.632 | 3.384 | 2.116 | 24.390 | 28.028 |
| deck | deck-v2k3-1 | 2 | 3 | 0.636 | 3.396 | 2.094 | 24.378 | 27.096 |
| deck | deck-v2sk3-1 | 2, spread corners | 3 | 1.316 | 3.404 | 2.085 | 25.052 | 25.808 |
| hall | hall-v2k2-1 | 2 | 2 | 1.157 | 3.257 | 0.031 | 19.687 | 20.417 |
| hall | hall-v2k4-1 | 2 | 4 | 1.266 | 3.257 | 0.031 | 19.778 | 20.590 |
| deck | deck-t16k3-1 | 2, tiles of 16 pixels | 3 | 0.912 | 3.408 | 2.097 | 24.562 | 25.130 |
| hall | hall-nvk4-1 | 2, no corner shadows | 4 | 1.015 | 3.247 | 0.032 | 19.345 | 20.054 |
| hall | hall-t16k4-1 | 2, tiles of 16 pixels | 4 | 2.250 | 3.259 | 0.032 | 20.632 | 21.117 |

The rows below are cost-only variants of version 2 (`variants/parts.py`): the corner pass without the walk of the rings around a corner on the sky, without the loop over the lights, or without both. Their images are wrong by design, and only the corner and tile pass's time counts.

| Scene | Run | Variant | Key lights | Corner and tile pass |
|---|---|---|---:|---:|
| deck | deck-noring-1 | no ring walk | 3 | 0.614 |
| deck | deck-nolights-1 | no light loop | 3 | 0.230 |
| deck | deck-noboth-1 | neither | 3 | 0.085 |
| hall | hall-noring-1 | no ring walk | 4 | 1.126 |
| hall | hall-nolights-1 | no light loop | 4 | 0.408 |
| hall | hall-noboth-1 | neither | 4 | 0.064 |

The rows below are cost-only variants of version 2 without the ring walk (`variants/density.py`). A tile size changes the count of corners. A count of copies draws that many copies of the corner grid side by side, and each copy ranks only its own share of the lights, so each fragment does a part of a corner's work. The base rows are `deck-noring-1` and `hall-noring-1`.

| Scene | Run | Tile size | Copies | Lights per fragment | Corner and tile pass |
|---|---|---:|---:|---|---:|
| hall | hall-d16-1 | 16 | 1 | all | 0.624 |
| hall | hall-noring-1 | 8 | 1 | all | 1.126 |
| hall | hall-d4-1 | 4 | 1 | all | 1.213 |
| hall | hall-c2-1 | 8 | 2 x 2 | a quarter | 0.548 |
| hall | hall-c4-1 | 8 | 4 x 4 | a sixteenth | 0.516 |
| deck | deck-noring-1 | 8 | 1 | all | 0.614 |
| deck | deck-d4-1 | 4 | 1 | all | 0.963 |
| deck | deck-c4-1 | 8 | 4 x 4 | a sixteenth | 0.429 |
| hall | hall-d16c2-1 | 16 | 2 x 2 | a quarter | 0.349 |
| hall | hall-d16c4-1 | 16 | 4 x 4 | a sixteenth | 0.265 |
| deck | deck-d16c4-1 | 16 | 4 x 4 | a sixteenth | 0.252 |

The rows below are the whole corner and tile pass of version 2 with fixed slots for the greatest losses (`variants/slots.py`): five named values and a fixed chain of compare and carry, in place of two local arrays that a run-time index read. The chosen keys and shares are the same. The offline count of stack instruction sites stayed about the same (22 loads and 13 stores before, 23 and 11 after, with r127 as the highest register in both), so the trace gives the gain and the offline count does not explain it.

| Scene | Run | Key lights | Corner and tile pass | Direct light | Frame span, median | Frame span, 95th percentile |
|---|---|---:|---:|---:|---:|---:|
| deck | deck-v2k3-1 | 3 | 0.636 | 3.396 | 24.378 | 27.096 |
| deck | deck-u-1 | 3 | 0.607 | 3.386 | 24.181 | 24.642 |
| hall | hall-v2k4-1 | 4 | 1.266 | 3.257 | 19.778 | 20.590 |
| hall | hall-u-1 | 4 | 1.036 | 3.261 | 19.340 | 19.888 |

Version 2 is the tree of the commit that adds this table: every lamp keeps its step and its shadow lookup at the pixel, and only a key lamp takes its full response. The spread-corners variant is `variants/kfs.py`. The variant without corner shadows is `variants/nv.py`, and the variant with tiles of 16 pixels is `variants/t16.py`. The deck trace of the variant without corner shadows gave no report, because `xctrace` did not end (t2-d75t). In the runs `deck-v2k2-1` and `deck-v2k3-1` the GPU clock fell for a part of the run (10th percentile 1081 MHz and 880 MHz), which shows in their 95th percentile of the frame span and not in the busy medians.

## How to build and trace a variant

The scripts in `variants/` are the scratch tools of the ticket, as they ran. They name the scratch folder `/private/tmp/t2-mlvy` and the worktree of the ticket, so set those paths before a new run.

1. `build-sed.sh <name> <commit> <edit.py>` builds main with one edit of `lighting.glsl`. `build-main-edit.sh` takes an edit of the whole tree. `build-d.sh` also fixes the exposure at the deck's 0.704 stops (`fixexp.py`).
2. `measure-lib.sh` gives `trace <name> <binary> <script>`, which traces a built binary through `gpu-trace-bin.sh`.
3. Take the GPU lock for the traces with the main checkout's `tools/gpu-lock.sh`.
