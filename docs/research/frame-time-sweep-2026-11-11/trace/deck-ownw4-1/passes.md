Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.6 MHz (p10 1290.9, p90 1292.4 MHz, 32 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 6.78, 5.76, 6.53
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.008 | 0.009 | 0.008 | 0.008 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.178 | 0.195 | 0.166 | 0.178 | 0.195 | - | - | - | - |
| local shadows | 119 | 0.453 | 0.413 | 0.442 | 0.385 | 0.351 | 0.430 | - | - | - | - |
| environment faces | 151 | 0.572 | 0.447 | 0.941 | 0.511 | 0.369 | 0.893 | - | - | - | - |
| environment prefilter | 151 | 0.333 | 0.324 | 0.337 | 0.333 | 0.324 | 0.337 | - | - | - | - |
| G-buffer | 151 | 0.200 | 0.198 | 0.210 | 0.152 | 0.149 | 0.165 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.230 | 0.225 | 0.226 | 0.230 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.266 | 0.273 | 0.268 | 0.266 | 0.273 | - | - | - | - |
| direct light | 151 | 4.096 | 4.080 | 4.160 | 4.048 | 4.037 | 4.112 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.107 | 0.116 | 0.108 | 0.107 | 0.116 | - | - | - | - |
| ray plan | 151 | 0.894 | 0.895 | 0.906 | 0.894 | 0.895 | 0.906 | - | - | - | - |
| reflection rays | 151 | 3.163 | 2.779 | 9.373 | 3.163 | 2.779 | 9.372 | - | - | - | - |
| diffuse rays | 151 | 1.640 | 1.404 | 5.758 | 1.640 | 1.404 | 5.758 | - | - | - | - |
| screen trace resolve | 151 | 0.807 | 0.795 | 0.989 | 0.807 | 0.795 | 0.989 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.517 | 0.519 | 0.530 | 0.517 | 0.519 | 0.530 | - | - | - | - |
| filter indirect diffuse | 151 | 4.181 | 4.156 | 4.197 | 4.181 | 4.156 | 4.197 | - | - | - | - |
| accumulate indirect specular | 151 | 1.395 | 1.403 | 1.423 | 1.395 | 1.403 | 1.423 | - | - | - | - |
| composite | 151 | 0.163 | 0.163 | 0.169 | 0.163 | 0.162 | 0.169 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.034 | 0.035 | 0.033 | 0.033 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.065 | 2.070 | 2.293 | 0.364 | 0.267 | 0.920 | - | - | - | - |
| FSR 3.1 | 151 | 3.091 | 3.040 | 3.583 | 1.753 | 1.753 | 1.771 | - | - | - | - |
| bloom | 151 | 1.045 | 1.047 | 1.282 | 0.289 | 0.285 | 0.306 | - | - | - | - |
| tone map | 151 | 0.126 | 0.126 | 0.130 | 0.123 | 0.123 | 0.126 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 25.626 | 24.959 | 35.118 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
