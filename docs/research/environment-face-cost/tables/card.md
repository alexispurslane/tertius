### card: face cost per variant

| Face | Cull | Runs | Load at the lock (1 min) | Face cost (ms) | Range over runs (ms) | 216 faces in 60 draws (ms per draw) |
|---:|---|---:|---|---:|---|---:|
| 128 | off | 3 | -, -, - | 0.178 | 0.172 to 0.187 | 0.64 |
| 128 | on | 3 | -, -, - | 0.191 | 0.177 to 0.212 | 0.69 |
| 32 | off | 3 | -, -, - | 0.141 | 0.132 to 0.149 | 0.51 |
| 32 | on | 3 | -, -, - | 0.143 | 0.135 to 0.149 | 0.51 |

### card: the passes per map (mean pair differences, ms)

| Face | Cull | Map | Span | environment faces | environment prefilter | Faces + prefilter | Kept, culled instances | Triangles |
|---:|---|---:|---:|---:|---:|---:|---|---:|
| 128 | off | 0 | 0.149 | 0.071 | 0.082 | 0.153 | 0.8, 0.0 | 152,177 |
| 128 | off | 1 | 0.206 | 0.128 | 0.084 | 0.211 | 17.4, 0.0 | 158,834 |
| 128 | on | 0 | 0.161 | 0.071 | 0.082 | 0.153 | 0.8, 0.0 | 152,177 |
| 128 | on | 1 | 0.220 | 0.129 | 0.083 | 0.212 | 15.9, 1.5 | 158,816 |
| 32 | off | 0 | 0.125 | 0.068 | 0.069 | 0.137 | 0.8, 0.0 | 152,177 |
| 32 | off | 1 | 0.156 | 0.097 | 0.069 | 0.165 | 17.4, 0.0 | 158,834 |
| 32 | on | 0 | 0.126 | 0.069 | 0.069 | 0.137 | 0.8, 0.0 | 152,177 |
| 32 | on | 1 | 0.160 | 0.098 | 0.069 | 0.167 | 13.1, 4.3 | 158,782 |

### card: the part of a face's cost that does not shrink with its size (cost = a + b W², no cull)

| Quantity | At 128 (ms) | At 32 (ms) | a, the fixed part (ms) | Fixed share at 32 |
|---|---:|---:|---:|---:|
| Span, both maps | 0.178 | 0.141 | 0.138 | 98% |
| environment faces, map 1 | 0.128 | 0.097 | 0.095 | 98% |
| environment faces, map 0 | 0.071 | 0.068 | 0.068 | 100% |
| environment prefilter, map 1 | 0.084 | 0.069 | 0.067 | 99% |
