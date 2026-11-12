Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1279.9 MHz (p10 1283.0, p90 1292.2 MHz, 37 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 19.54, 27.54, 30.89
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.008 | 0.009 | 0.008 | 0.008 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.178 | 0.193 | 0.166 | 0.178 | 0.193 | - | - | - | - |
| local shadows | 119 | 0.450 | 0.407 | 0.438 | 0.381 | 0.338 | 0.427 | - | - | - | - |
| environment faces | 151 | 0.575 | 0.443 | 0.982 | 0.513 | 0.366 | 0.889 | - | - | - | - |
| environment prefilter | 151 | 0.334 | 0.325 | 0.329 | 0.334 | 0.325 | 0.329 | - | - | - | - |
| G-buffer | 151 | 0.202 | 0.198 | 0.209 | 0.153 | 0.149 | 0.163 | - | - | - | - |
| surface test | 151 | 0.224 | 0.224 | 0.233 | 0.224 | 0.224 | 0.233 | - | - | - | - |
| depth pyramid | 151 | 0.273 | 0.268 | 0.274 | 0.273 | 0.268 | 0.274 | - | - | - | - |
| direct light | 151 | 4.160 | 4.107 | 4.236 | 4.111 | 4.060 | 4.188 | - | - | - | - |
| light pyramid | 151 | 0.119 | 0.115 | 0.134 | 0.119 | 0.115 | 0.134 | - | - | - | - |
| ray plan | 151 | 0.897 | 0.889 | 0.958 | 0.897 | 0.889 | 0.958 | - | - | - | - |
| reflection rays | 151 | 3.423 | 2.895 | 11.715 | 3.422 | 2.895 | 11.714 | - | - | - | - |
| diffuse rays | 151 | 1.651 | 1.404 | 5.551 | 1.651 | 1.404 | 5.551 | - | - | - | - |
| screen trace resolve | 151 | 0.236 | 0.226 | 0.325 | 0.236 | 0.226 | 0.325 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.528 | 0.525 | 0.556 | 0.527 | 0.525 | 0.556 | - | - | - | - |
| filter indirect diffuse | 151 | 4.314 | 4.234 | 4.977 | 4.314 | 4.234 | 4.977 | - | - | - | - |
| accumulate indirect specular | 151 | 1.400 | 1.398 | 1.445 | 1.400 | 1.398 | 1.445 | - | - | - | - |
| composite | 151 | 0.174 | 0.173 | 0.184 | 0.173 | 0.173 | 0.184 | - | - | - | - |
| scatter rows | 151 | 0.035 | 0.035 | 0.036 | 0.034 | 0.034 | 0.036 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.093 | 2.080 | 2.404 | 0.358 | 0.270 | 0.948 | - | - | - | - |
| FSR 3.1 | 151 | 3.131 | 3.080 | 3.642 | 1.771 | 1.766 | 1.811 | - | - | - | - |
| bloom | 151 | 1.057 | 1.083 | 1.258 | 0.292 | 0.288 | 0.310 | - | - | - | - |
| tone map | 151 | 0.129 | 0.128 | 0.137 | 0.125 | 0.124 | 0.130 | - | - | - | - |
| capture | 119 | 0.033 | 0.032 | 0.034 | 0.031 | 0.030 | 0.032 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 26.118 | 24.586 | 37.195 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
