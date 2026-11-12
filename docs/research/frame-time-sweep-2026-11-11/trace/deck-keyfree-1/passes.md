Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1280.9 MHz (p10 1251.0, p90 1292.1 MHz, 38 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 30.47, 29.35, 30.77
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.175 | 0.177 | 0.196 | 0.167 | 0.177 | 0.196 | - | - | - | - |
| local shadows | 119 | 0.453 | 0.408 | 0.444 | 0.383 | 0.341 | 0.422 | - | - | - | - |
| environment faces | 151 | 0.579 | 0.438 | 0.978 | 0.516 | 0.374 | 0.895 | - | - | - | - |
| environment prefilter | 151 | 0.335 | 0.325 | 0.328 | 0.335 | 0.325 | 0.328 | - | - | - | - |
| G-buffer | 151 | 0.205 | 0.200 | 0.212 | 0.153 | 0.148 | 0.174 | - | - | - | - |
| surface test | 151 | 0.224 | 0.223 | 0.231 | 0.224 | 0.223 | 0.231 | - | - | - | - |
| depth pyramid | 151 | 0.273 | 0.268 | 0.276 | 0.273 | 0.268 | 0.276 | - | - | - | - |
| direct light | 151 | 4.171 | 4.113 | 4.519 | 4.119 | 4.064 | 4.481 | - | - | - | - |
| light pyramid | 151 | 0.121 | 0.119 | 0.134 | 0.121 | 0.119 | 0.134 | - | - | - | - |
| ray plan | 151 | 0.900 | 0.894 | 0.929 | 0.900 | 0.894 | 0.929 | - | - | - | - |
| reflection rays | 151 | 3.112 | 2.698 | 9.533 | 3.111 | 2.698 | 9.531 | - | - | - | - |
| diffuse rays | 151 | 1.678 | 1.431 | 5.860 | 1.678 | 1.431 | 5.860 | - | - | - | - |
| screen trace resolve | 151 | 0.234 | 0.228 | 0.316 | 0.234 | 0.228 | 0.316 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.332 | 0.331 | 0.347 | 0.332 | 0.331 | 0.347 | - | - | - | - |
| filter indirect diffuse | 151 | 4.257 | 4.219 | 4.477 | 4.257 | 4.219 | 4.477 | - | - | - | - |
| accumulate indirect specular | 151 | 0.900 | 0.900 | 0.928 | 0.900 | 0.900 | 0.928 | - | - | - | - |
| composite | 151 | 0.183 | 0.182 | 0.193 | 0.182 | 0.182 | 0.192 | - | - | - | - |
| scatter rows | 151 | 0.035 | 0.035 | 0.036 | 0.034 | 0.035 | 0.036 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.091 | 2.091 | 2.351 | 0.371 | 0.279 | 0.927 | - | - | - | - |
| FSR 3.1 | 151 | 3.142 | 3.105 | 3.633 | 1.778 | 1.773 | 1.824 | - | - | - | - |
| bloom | 151 | 1.035 | 1.040 | 1.261 | 0.295 | 0.292 | 0.311 | - | - | - | - |
| tone map | 151 | 0.132 | 0.130 | 0.138 | 0.128 | 0.126 | 0.132 | - | - | - | - |
| capture | 119 | 0.032 | 0.032 | 0.034 | 0.030 | 0.030 | 0.032 | - | - | - | - |
| (after the last pass) | 151 | 0.003 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.582 | 23.786 | 34.114 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
