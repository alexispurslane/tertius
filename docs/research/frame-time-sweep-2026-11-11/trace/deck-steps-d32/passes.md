Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1290.6 MHz (p10 1289.7, p90 1292.3 MHz, 32 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 19.81, 14.75, 11.16
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.007 | 0.008 | 0.009 | 0.007 | 0.008 | - | - | - | - |
| shadow maps | 151 | 0.173 | 0.176 | 0.193 | 0.165 | 0.176 | 0.193 | - | - | - | - |
| local shadows | 119 | 0.452 | 0.408 | 0.443 | 0.379 | 0.333 | 0.427 | - | - | - | - |
| environment faces | 151 | 0.575 | 0.437 | 0.982 | 0.510 | 0.371 | 0.881 | - | - | - | - |
| environment prefilter | 151 | 0.332 | 0.324 | 0.328 | 0.332 | 0.324 | 0.328 | - | - | - | - |
| G-buffer | 151 | 0.202 | 0.199 | 0.209 | 0.153 | 0.148 | 0.166 | - | - | - | - |
| surface test | 151 | 0.224 | 0.224 | 0.228 | 0.224 | 0.224 | 0.228 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.266 | 0.270 | 0.268 | 0.266 | 0.270 | - | - | - | - |
| direct light | 151 | 4.116 | 4.086 | 4.177 | 4.067 | 4.038 | 4.138 | - | - | - | - |
| light pyramid | 151 | 0.111 | 0.110 | 0.123 | 0.111 | 0.110 | 0.123 | - | - | - | - |
| ray plan | 151 | 0.891 | 0.892 | 0.905 | 0.891 | 0.892 | 0.905 | - | - | - | - |
| reflection rays | 151 | 3.047 | 2.652 | 9.407 | 3.047 | 2.651 | 9.406 | - | - | - | - |
| diffuse rays | 151 | 1.115 | 0.952 | 3.857 | 1.113 | 0.952 | 3.857 | - | - | - | - |
| screen trace resolve | 151 | 0.223 | 0.217 | 0.313 | 0.223 | 0.217 | 0.313 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.518 | 0.521 | 0.533 | 0.518 | 0.521 | 0.533 | - | - | - | - |
| filter indirect diffuse | 151 | 4.206 | 4.169 | 4.302 | 4.206 | 4.169 | 4.302 | - | - | - | - |
| accumulate indirect specular | 151 | 1.391 | 1.399 | 1.416 | 1.391 | 1.399 | 1.416 | - | - | - | - |
| composite | 151 | 0.170 | 0.169 | 0.179 | 0.168 | 0.167 | 0.178 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.035 | 0.032 | 0.033 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.064 | 2.076 | 2.310 | 0.350 | 0.276 | 0.908 | - | - | - | - |
| FSR 3.1 | 151 | 3.079 | 3.049 | 3.496 | 1.755 | 1.756 | 1.783 | - | - | - | - |
| bloom | 151 | 1.071 | 1.069 | 1.300 | 0.292 | 0.288 | 0.309 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.131 | 0.123 | 0.123 | 0.127 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.467 | 23.742 | 32.248 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
