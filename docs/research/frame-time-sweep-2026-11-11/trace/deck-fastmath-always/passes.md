Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.4 MHz (p10 1290.9, p90 1292.3 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 5.85, 5.48, 6.49
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.175 | 0.180 | 0.193 | 0.167 | 0.180 | 0.193 | - | - | - | - |
| local shadows | 119 | 0.454 | 0.410 | 0.443 | 0.381 | 0.336 | 0.431 | - | - | - | - |
| environment faces | 151 | 0.575 | 0.445 | 0.972 | 0.509 | 0.368 | 0.891 | - | - | - | - |
| environment prefilter | 151 | 0.333 | 0.324 | 0.331 | 0.333 | 0.324 | 0.331 | - | - | - | - |
| G-buffer | 151 | 0.202 | 0.198 | 0.209 | 0.153 | 0.148 | 0.162 | - | - | - | - |
| surface test | 151 | 0.224 | 0.225 | 0.229 | 0.224 | 0.225 | 0.229 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.266 | 0.272 | 0.268 | 0.266 | 0.272 | - | - | - | - |
| direct light | 151 | 4.108 | 4.081 | 4.152 | 4.058 | 4.035 | 4.112 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.107 | 0.113 | 0.108 | 0.107 | 0.113 | - | - | - | - |
| ray plan | 151 | 0.892 | 0.893 | 0.904 | 0.892 | 0.893 | 0.904 | - | - | - | - |
| reflection rays | 151 | 3.037 | 2.639 | 9.393 | 3.037 | 2.638 | 9.392 | - | - | - | - |
| diffuse rays | 151 | 1.639 | 1.402 | 5.777 | 1.639 | 1.402 | 5.777 | - | - | - | - |
| screen trace resolve | 151 | 0.221 | 0.215 | 0.322 | 0.221 | 0.215 | 0.322 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.514 | 0.516 | 0.524 | 0.514 | 0.516 | 0.524 | - | - | - | - |
| filter indirect diffuse | 151 | 4.174 | 4.147 | 4.177 | 4.174 | 4.147 | 4.177 | - | - | - | - |
| accumulate indirect specular | 151 | 1.390 | 1.398 | 1.415 | 1.390 | 1.398 | 1.415 | - | - | - | - |
| composite | 151 | 0.165 | 0.164 | 0.171 | 0.164 | 0.164 | 0.171 | - | - | - | - |
| scatter rows | 151 | 0.032 | 0.032 | 0.034 | 0.032 | 0.032 | 0.034 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.063 | 2.072 | 2.282 | 0.323 | 0.261 | 0.806 | - | - | - | - |
| FSR 3.1 | 151 | 3.141 | 3.148 | 3.636 | 1.751 | 1.752 | 1.773 | - | - | - | - |
| bloom | 151 | 1.043 | 1.056 | 1.260 | 0.290 | 0.286 | 0.306 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.134 | 0.123 | 0.123 | 0.126 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.030 | 0.030 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.877 | 24.146 | 34.356 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
