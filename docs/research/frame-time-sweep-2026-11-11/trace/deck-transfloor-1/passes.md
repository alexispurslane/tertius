Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1295.1 MHz (p10 1294.7, p90 1296.0 MHz, 34 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 16.12, 15.19, 23.79
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 144 | 0.022 | 0.005 | 0.032 | 0.013 | 0.005 | 0.031 | - | - | - | - |
| shadow maps | 144 | 0.183 | 0.176 | 0.228 | 0.172 | 0.172 | 0.223 | - | - | - | - |
| local shadows | 119 | 0.464 | 0.422 | 0.491 | 0.401 | 0.346 | 0.460 | - | - | - | - |
| environment faces | 144 | 0.616 | 0.449 | 1.025 | 0.557 | 0.384 | 0.966 | - | - | - | - |
| environment prefilter | 144 | 0.396 | 0.333 | 0.668 | 0.395 | 0.332 | 0.668 | - | - | - | - |
| G-buffer | 144 | 0.216 | 0.194 | 0.360 | 0.175 | 0.148 | 0.282 | - | - | - | - |
| surface test | 144 | 0.237 | 0.225 | 0.292 | 0.234 | 0.225 | 0.292 | - | - | - | - |
| depth pyramid | 144 | 0.302 | 0.272 | 0.470 | 0.298 | 0.271 | 0.470 | - | - | - | - |
| direct light | 144 | 4.379 | 4.115 | 5.492 | 4.325 | 4.075 | 5.461 | - | - | - | - |
| light pyramid | 144 | 0.165 | 0.124 | 0.336 | 0.163 | 0.124 | 0.333 | - | - | - | - |
| ray plan | 144 | 1.006 | 0.920 | 1.493 | 1.005 | 0.920 | 1.493 | - | - | - | - |
| reflection rays | 144 | 3.397 | 2.816 | 6.052 | 3.360 | 2.776 | 6.048 | - | - | - | - |
| diffuse rays | 144 | 1.844 | 1.497 | 3.501 | 1.843 | 1.497 | 3.501 | - | - | - | - |
| screen trace resolve | 144 | 0.300 | 0.263 | 0.527 | 0.300 | 0.263 | 0.527 | - | - | - | - |
| accumulate indirect diffuse | 144 | 0.643 | 0.562 | 1.077 | 0.642 | 0.562 | 1.077 | - | - | - | - |
| filter indirect diffuse | 144 | 5.083 | 4.926 | 6.045 | 5.082 | 4.926 | 6.045 | - | - | - | - |
| accumulate indirect specular | 144 | 1.608 | 1.450 | 2.196 | 1.608 | 1.450 | 2.196 | - | - | - | - |
| composite | 144 | 0.215 | 0.184 | 0.365 | 0.188 | 0.178 | 0.217 | - | - | - | - |
| scatter rows | 144 | 0.048 | 0.035 | 0.100 | 0.046 | 0.035 | 0.096 | - | - | - | - |
| scatter columns | 144 | 0.030 | 0.018 | 0.113 | 0.027 | 0.018 | 0.105 | - | - | - | - |
| transparent | 144 | 0.095 | 0.078 | 0.169 | 0.065 | 0.055 | 0.125 | - | - | - | - |
| FSR 3.1 | 144 | 2.118 | 1.895 | 2.896 | 2.006 | 1.813 | 2.798 | - | - | - | - |
| bloom | 144 | 0.448 | 0.430 | 0.686 | 0.351 | 0.329 | 0.405 | - | - | - | - |
| tone map | 151 | 0.147 | 0.142 | 0.225 | 0.144 | 0.138 | 0.190 | - | - | - | - |
| capture | 119 | 0.049 | 0.033 | 0.105 | 0.048 | 0.032 | 0.104 | - | - | - | - |
| (after the last pass) | 144 | 0.005 | 0.004 | 0.007 | 0.003 | 0.003 | 0.005 | - | - | - | - |
| frame span | 151 | 28.331 | 25.784 | 49.557 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
