Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.3 MHz (p10 1290.5, p90 1292.3 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 15.19, 14.27, 11.17
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.008 | 0.009 | 0.008 | 0.008 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.178 | 0.193 | 0.166 | 0.178 | 0.193 | - | - | - | - |
| local shadows | 119 | 0.457 | 0.415 | 0.442 | 0.392 | 0.353 | 0.430 | - | - | - | - |
| environment faces | 151 | 0.568 | 0.429 | 0.979 | 0.508 | 0.367 | 0.878 | - | - | - | - |
| environment prefilter | 151 | 0.330 | 0.325 | 0.336 | 0.330 | 0.325 | 0.336 | - | - | - | - |
| G-buffer | 151 | 0.201 | 0.199 | 0.210 | 0.152 | 0.148 | 0.167 | - | - | - | - |
| surface test | 151 | 0.224 | 0.225 | 0.229 | 0.224 | 0.225 | 0.229 | - | - | - | - |
| depth pyramid | 151 | 0.267 | 0.266 | 0.274 | 0.267 | 0.266 | 0.274 | - | - | - | - |
| direct light | 151 | 4.103 | 4.088 | 4.193 | 4.054 | 4.040 | 4.140 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.106 | 0.117 | 0.108 | 0.106 | 0.117 | - | - | - | - |
| ray plan | 151 | 0.895 | 0.895 | 0.905 | 0.895 | 0.895 | 0.905 | - | - | - | - |
| reflection rays | 151 | 3.025 | 2.638 | 9.396 | 3.025 | 2.638 | 9.396 | - | - | - | - |
| diffuse rays | 151 | 1.635 | 1.400 | 5.766 | 1.635 | 1.400 | 5.766 | - | - | - | - |
| screen trace resolve | 151 | 0.220 | 0.213 | 0.326 | 0.220 | 0.213 | 0.326 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.511 | 0.514 | 0.521 | 0.511 | 0.514 | 0.521 | - | - | - | - |
| filter indirect diffuse | 151 | 4.173 | 4.148 | 4.171 | 4.173 | 4.148 | 4.171 | - | - | - | - |
| accumulate indirect specular | 151 | 1.387 | 1.395 | 1.413 | 1.387 | 1.395 | 1.413 | - | - | - | - |
| composite | 151 | 0.164 | 0.163 | 0.170 | 0.164 | 0.163 | 0.169 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.034 | 0.035 | 0.033 | 0.034 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.018 | 0.017 | 0.019 | 0.017 | 0.017 | 0.019 | - | - | - | - |
| transparent | 151 | 2.079 | 2.083 | 2.428 | 0.353 | 0.273 | 0.876 | - | - | - | - |
| FSR 3.1 | 151 | 3.133 | 3.118 | 3.659 | 1.752 | 1.753 | 1.770 | - | - | - | - |
| bloom | 151 | 1.028 | 1.026 | 1.266 | 0.290 | 0.286 | 0.308 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.134 | 0.123 | 0.123 | 0.125 | - | - | - | - |
| capture | 119 | 0.031 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.884 | 24.169 | 34.424 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
