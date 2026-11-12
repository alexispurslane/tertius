Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.4 MHz (p10 1290.8, p90 1292.3 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 19.36, 8.86, 7.61
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.178 | 0.191 | 0.166 | 0.178 | 0.191 | - | - | - | - |
| local shadows | 119 | 0.453 | 0.410 | 0.446 | 0.383 | 0.340 | 0.428 | - | - | - | - |
| environment faces | 151 | 0.572 | 0.451 | 0.979 | 0.510 | 0.369 | 0.885 | - | - | - | - |
| environment prefilter | 151 | 0.333 | 0.324 | 0.334 | 0.333 | 0.324 | 0.334 | - | - | - | - |
| G-buffer | 151 | 0.202 | 0.197 | 0.210 | 0.152 | 0.147 | 0.164 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.231 | 0.225 | 0.226 | 0.231 | - | - | - | - |
| depth pyramid | 151 | 0.269 | 0.266 | 0.273 | 0.269 | 0.266 | 0.273 | - | - | - | - |
| direct light | 151 | 4.120 | 4.088 | 4.177 | 4.070 | 4.044 | 4.122 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.106 | 0.118 | 0.108 | 0.106 | 0.118 | - | - | - | - |
| ray plan | 151 | 0.894 | 0.895 | 0.904 | 0.894 | 0.895 | 0.904 | - | - | - | - |
| reflection rays | 151 | 3.038 | 2.637 | 9.425 | 3.038 | 2.637 | 9.425 | - | - | - | - |
| diffuse rays | 151 | 1.639 | 1.401 | 5.777 | 1.639 | 1.401 | 5.777 | - | - | - | - |
| screen trace resolve | 151 | 0.295 | 0.285 | 0.437 | 0.295 | 0.285 | 0.437 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.514 | 0.516 | 0.522 | 0.514 | 0.516 | 0.522 | - | - | - | - |
| filter indirect diffuse | 151 | 4.171 | 4.151 | 4.173 | 4.171 | 4.151 | 4.173 | - | - | - | - |
| accumulate indirect specular | 151 | 1.389 | 1.397 | 1.418 | 1.389 | 1.397 | 1.418 | - | - | - | - |
| composite | 151 | 0.164 | 0.164 | 0.170 | 0.164 | 0.164 | 0.170 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.035 | 0.032 | 0.033 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.060 | 2.072 | 2.306 | 0.353 | 0.273 | 0.953 | - | - | - | - |
| FSR 3.1 | 151 | 3.084 | 3.043 | 3.497 | 1.753 | 1.753 | 1.775 | - | - | - | - |
| bloom | 151 | 1.051 | 1.050 | 1.270 | 0.291 | 0.286 | 0.308 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.133 | 0.123 | 0.123 | 0.125 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.029 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.979 | 24.245 | 34.229 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
