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
