### Mac: face cost per variant

| Face | Cull | Runs | Load at the lock (1 min) | Face cost (ms) | Range over runs (ms) | 216 faces in 60 draws (ms per draw) |
|---:|---|---:|---|---:|---|---:|
| 128 | off | 3 | 3.6, 2.9, 4.0 | 0.722 | 0.714 to 0.728 | 2.60 |
| 128 | on | 3 | 6.3, 4.9, 4.4 | 0.704 | 0.667 to 0.731 | 2.53 |
| 32 | off | 3 | 3.6, 4.8, 3.2 | 0.546 | 0.524 to 0.566 | 1.96 |
| 32 | on | 3 | 4.6, 5.4, 2.9 | 0.566 | 0.563 to 0.569 | 2.04 |

### Mac: the passes per map (mean pair differences, ms)

| Face | Cull | Map | Span | environment faces | environment prefilter | Faces + prefilter | Kept, culled instances | Triangles |
|---:|---|---:|---:|---:|---:|---:|---|---:|
| 128 | off | 0 | 0.612 | 0.175 | 0.348 | 0.523 | 0.8, 0.0 | 152,177 |
| 128 | off | 1 | 0.830 | 0.399 | 0.341 | 0.740 | 17.4, 0.0 | 158,834 |
| 128 | on | 0 | 0.588 | 0.183 | 0.342 | 0.524 | 0.8, 0.0 | 152,177 |
| 128 | on | 1 | 0.817 | 0.402 | 0.331 | 0.734 | 15.9, 1.5 | 158,816 |
| 32 | off | 0 | 0.464 | 0.091 | 0.308 | 0.400 | 0.8, 0.0 | 152,177 |
| 32 | off | 1 | 0.626 | 0.252 | 0.296 | 0.548 | 17.4, 0.0 | 158,834 |
| 32 | on | 0 | 0.493 | 0.092 | 0.311 | 0.402 | 0.8, 0.0 | 152,177 |
| 32 | on | 1 | 0.638 | 0.251 | 0.298 | 0.549 | 13.1, 4.3 | 158,782 |

### Mac: the part of a face's cost that does not shrink with its size (cost = a + b W², no cull)

| Quantity | At 128 (ms) | At 32 (ms) | a, the fixed part (ms) | Fixed share at 32 |
|---|---:|---:|---:|---:|
| Span, both maps | 0.722 | 0.546 | 0.534 | 98% |
| environment faces, map 1 | 0.399 | 0.252 | 0.242 | 96% |
| environment faces, map 0 | 0.175 | 0.091 | 0.086 | 94% |
| environment prefilter, map 1 | 0.341 | 0.296 | 0.293 | 99% |
