Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.2 MHz (p10 1290.4, p90 1292.3 MHz, 30 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 4.51, 5.33, 6.55
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.175 | 0.180 | 0.194 | 0.168 | 0.180 | 0.194 | - | - | - | - |
| local shadows | 119 | 0.452 | 0.409 | 0.436 | 0.387 | 0.344 | 0.430 | - | - | - | - |
| environment faces | 151 | 0.569 | 0.442 | 0.951 | 0.510 | 0.372 | 0.895 | - | - | - | - |
| environment prefilter | 151 | 0.315 | 0.306 | 0.312 | 0.315 | 0.306 | 0.312 | - | - | - | - |
| G-buffer | 151 | 0.200 | 0.197 | 0.208 | 0.155 | 0.147 | 0.165 | - | - | - | - |
| surface test | 151 | 0.225 | 0.225 | 0.229 | 0.225 | 0.225 | 0.229 | - | - | - | - |
| depth pyramid | 151 | 0.254 | 0.252 | 0.255 | 0.254 | 0.252 | 0.255 | - | - | - | - |
| direct light | 151 | 4.104 | 4.085 | 4.165 | 4.059 | 4.039 | 4.115 | - | - | - | - |
| light pyramid | 151 | 0.091 | 0.090 | 0.095 | 0.091 | 0.090 | 0.095 | - | - | - | - |
| ray plan | 151 | 0.894 | 0.895 | 0.902 | 0.894 | 0.895 | 0.902 | - | - | - | - |
| reflection rays | 151 | 3.026 | 2.629 | 9.374 | 3.026 | 2.629 | 9.374 | - | - | - | - |
| diffuse rays | 151 | 1.638 | 1.403 | 5.770 | 1.638 | 1.402 | 5.770 | - | - | - | - |
| screen trace resolve | 151 | 0.221 | 0.214 | 0.326 | 0.221 | 0.214 | 0.326 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.508 | 0.511 | 0.516 | 0.508 | 0.511 | 0.516 | - | - | - | - |
| filter indirect diffuse | 151 | 4.164 | 4.139 | 4.163 | 4.164 | 4.139 | 4.163 | - | - | - | - |
| accumulate indirect specular | 151 | 1.385 | 1.393 | 1.413 | 1.385 | 1.393 | 1.413 | - | - | - | - |
| composite | 151 | 0.165 | 0.165 | 0.170 | 0.165 | 0.165 | 0.170 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.033 | 0.035 | 0.033 | 0.033 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.101 | 2.094 | 2.428 | 0.431 | 0.336 | 1.010 | - | - | - | - |
| FSR 3.1 | 151 | 2.918 | 2.897 | 3.256 | 1.735 | 1.736 | 1.754 | - | - | - | - |
| bloom | 151 | 1.056 | 1.059 | 1.335 | 0.290 | 0.286 | 0.307 | - | - | - | - |
| tone map | 151 | 0.126 | 0.126 | 0.132 | 0.123 | 0.123 | 0.124 | - | - | - | - |
| capture | 119 | 0.031 | 0.031 | 0.033 | 0.029 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.158 | 23.435 | 33.394 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
