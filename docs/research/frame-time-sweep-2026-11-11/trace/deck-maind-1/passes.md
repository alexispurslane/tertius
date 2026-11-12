Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1290.4 MHz (p10 1290.1, p90 1292.2 MHz, 34 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 26.10, 30.71, 32.26
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.177 | 0.192 | 0.166 | 0.177 | 0.192 | - | - | - | - |
| local shadows | 119 | 0.449 | 0.404 | 0.436 | 0.379 | 0.332 | 0.426 | - | - | - | - |
| environment faces | 151 | 0.570 | 0.433 | 1.004 | 0.507 | 0.368 | 0.893 | - | - | - | - |
| environment prefilter | 151 | 0.328 | 0.324 | 0.327 | 0.328 | 0.324 | 0.327 | - | - | - | - |
| G-buffer | 151 | 0.199 | 0.197 | 0.207 | 0.151 | 0.147 | 0.161 | - | - | - | - |
| surface test | 151 | 0.224 | 0.224 | 0.233 | 0.224 | 0.224 | 0.233 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.268 | 0.270 | 0.268 | 0.268 | 0.270 | - | - | - | - |
| direct light | 151 | 4.118 | 4.092 | 4.213 | 4.070 | 4.048 | 4.164 | - | - | - | - |
| light pyramid | 151 | 0.114 | 0.112 | 0.129 | 0.114 | 0.112 | 0.129 | - | - | - | - |
| ray plan | 151 | 0.894 | 0.894 | 0.911 | 0.894 | 0.894 | 0.911 | - | - | - | - |
| reflection rays | 151 | 3.052 | 2.667 | 9.500 | 3.051 | 2.666 | 9.500 | - | - | - | - |
| diffuse rays | 151 | 1.654 | 1.417 | 5.797 | 1.654 | 1.417 | 5.797 | - | - | - | - |
| screen trace resolve | 151 | 0.228 | 0.223 | 0.322 | 0.228 | 0.223 | 0.322 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.521 | 0.523 | 0.537 | 0.521 | 0.523 | 0.537 | - | - | - | - |
| filter indirect diffuse | 151 | 4.230 | 4.192 | 4.295 | 4.230 | 4.192 | 4.295 | - | - | - | - |
| accumulate indirect specular | 151 | 1.394 | 1.397 | 1.441 | 1.394 | 1.397 | 1.441 | - | - | - | - |
| composite | 151 | 0.172 | 0.171 | 0.180 | 0.171 | 0.171 | 0.180 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.035 | 0.032 | 0.033 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.017 | 0.017 | 0.017 | 0.017 | - | - | - | - |
| transparent | 151 | 2.077 | 2.069 | 2.365 | 0.356 | 0.286 | 0.953 | - | - | - | - |
| FSR 3.1 | 151 | 3.104 | 3.056 | 3.651 | 1.762 | 1.759 | 1.804 | - | - | - | - |
| bloom | 151 | 1.045 | 1.065 | 1.255 | 0.291 | 0.288 | 0.307 | - | - | - | - |
| tone map | 151 | 0.128 | 0.127 | 0.136 | 0.124 | 0.124 | 0.128 | - | - | - | - |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 25.016 | 24.323 | 34.515 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
