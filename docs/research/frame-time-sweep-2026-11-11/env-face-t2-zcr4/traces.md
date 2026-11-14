# The traces of the environment face investigation

Ticket `t2-zcr4`, takes 14 to 16, on main at `b93ab3570`, on the M1 Max. Each row is one trace of 119 frames of the deck. The times are the engine's timer of the pass `environment faces`. `tools/passrow.py` prints the table.

A row counts only with a GPU clock of 1290.5 to 1291.2 MHz. The other rows ran under load or lost their clock record. Take 14 ran with the automatic exposure, and takes 15 and 16 at the deck's fixed exposure of 0.704 stops.

| Run | environment faces, mean ms | environment faces, median ms | Frame span, median ms | GPU clock, MHz |
|---|---:|---:|---:|---:|
| `deck-t14-env-ctl` | 0.463 | 0.383 | 20.347 | none |
| `deck-t14-env-draws1` | 0.466 | 0.387 | 20.272 | 1290.9 |
| `deck-t14-env-draws2` | 0.622 | 0.613 | 20.383 | 1290.9 |
| `deck-t14-env-nolight` | 0.120 | 0.123 | 20.861 | 1291.1 |
| `deck-t14-env-noshadow` | 0.305 | 0.258 | 16.258 | 1263.7 |
| `deck-t15-pre0-a` | 0.568 | 0.526 | 23.791 | none |
| `deck-t15-pre1-a` | 0.468 | 0.390 | 20.107 | 1290.9 |
| `deck-t15-helper` | 0.462 | 0.389 | 20.152 | 1290.5 |
| `deck-t15-nolight` | 0.120 | 0.124 | 20.676 | 1291.2 |
| `deck-t15-pre0-b` | 0.376 | 0.344 | 21.578 | 1295.9 |
| `deck-t15-pre1-b` | 0.491 | 0.391 | 20.318 | 1238.6 |
| `deck-t16-ctl-a` | 0.470 | 0.399 | 20.135 | 1291.0 |
| `deck-t16-syn-a` | 0.172 | 0.171 | 20.909 | 1173.3 |
| `deck-t16-nolight` | 0.124 | 0.120 | 20.789 | 1243.0 |
| `deck-t16-pre0` | 0.469 | 0.387 | 20.129 | 1290.5 |
| `deck-t16-pre1` | 0.468 | 0.389 | 20.097 | 1290.9 |
| `deck-t16-ctl-b` | 0.468 | 0.387 | 20.161 | 1290.6 |
| `deck-t16-syn-b` | 0.177 | 0.187 | 20.810 | 1290.7 |

| Name part | Build |
|---|---|
| `env-ctl`, `ctl` | Main |
| `draws1`, `draws2` | The faces compare their depth with greater-or-equal and draw each range once or twice |
| `nolight` | A face's fragment writes its glow alone |
| `noshadow` | A face reads no shadow map. Its automatic exposure went to −9.29 stops, so the row is void |
| `pre0`, `pre1` | The depth-only first pass of each range, off and on |
| `helper` | A helper fragment returns before the light loop |
| `syn` | No light in the surfaces, and one made-up light loop per texel in the sky's fullscreen triangle |

## Takes 17 to 19: the built pass

Takes 17 to 19 ran on 2026-11-12 to 2026-11-14, at the deck's fixed exposure. Take 17 compares main at `657642432` with the built pass on that main. Takes 18 and 19 run on main `7093f45f6`. A row counts only with a GPU clock of 1290 to 1294 MHz.

| Run | environment faces, mean ms | environment faces, median ms | Frame span, median ms | GPU clock, MHz |
|---|---:|---:|---:|---:|
| `deck-t17-main-a` | 0.461 | 0.382 | 20.320 | 1290.9 |
| `deck-t17-face-a` | 0.391 | 0.366 | 23.204 | 1089.9 |
| `deck-t17-face-b` | 0.364 | 0.356 | 20.447 | 1227.6 |
| `deck-t17-main-b` | 0.457 | 0.371 | 20.613 | 1292.1 |
| `deck-t17-main-c` | 0.483 | 0.405 | 20.563 | 1247.1 |
| `deck-t17-face-c` | 0.338 | 0.316 | 20.717 | 1291.9 |
| `deck-t18-face3-a` | 0.352 | 0.343 | 20.096 | 1290.6 |
| `deck-t18-face4-a` | 0.160 | 0.117 | 20.013 | none |
| `deck-t18-geo1-a` | 0.445 | 0.410 | 11.544 | 951.2 |
| `deck-t18-geo1-b` | 0.428 | 0.400 | 11.197 | 951.7 |
| `deck-t18-face4-b` | 0.156 | 0.118 | 19.861 | 1290.8 |
| `deck-t18-face3-b` | 0.367 | 0.330 | 19.956 | 1290.4 |
| `deck-t18-face3-c` | 0.346 | 0.352 | 20.073 | 1290.7 |
| `deck-t18-geo1-c` | 0.440 | 0.383 | 11.202 | 935.0 |
| `deck-t18-face4-c` | 0.254 | 0.115 | 20.113 | 1291.0 |
| `deck-t19-face3-a` | 0.350 | 0.340 | 19.940 | 1290.8 |
| `deck-t19-face5-a` | 0.328 | 0.342 | 20.061 | 1291.9 |
| `deck-t19-geo2-a` | 0.358 | 0.353 | 19.962 | 1290.8 |
| `deck-t19-geo2-b` | 0.359 | 0.346 | 19.981 | 1290.9 |
| `deck-t19-face5-b` | 0.326 | 0.334 | 19.921 | 1291.3 |
| `deck-t19-face3-b` | 0.356 | 0.343 | 19.934 | 1291.0 |
| `deck-t19-face3-c` | 0.350 | 0.347 | 19.935 | 1290.9 |
| `deck-t19-geo2-c` | 0.369 | 0.348 | 20.199 | 1292.3 |
| `deck-t19-face5-c` | 0.333 | 0.343 | 20.428 | 1293.9 |

| Name part | Build |
|---|---|
| `t17-main` | Main at `657642432` |
| `t17-face`, `face3` | The built pass: a face lights each texel once |
| `face4` | The built pass with the three depth ranges of a face in one rendering. Its image is wrong, so its rows are void |
| `geo1` | The built pass with the three depth ranges of the main view's G-buffer pass in one rendering. Its image is wrong, so its rows are void |
| `face5` | The built pass, and a face renders only the depth ranges that hold surfaces |
| `geo2` | The built pass, and the main view's G-buffer pass renders only the depth ranges that hold something |
