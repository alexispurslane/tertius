Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1294.8 MHz (p10 1293.5, p90 1296.0 MHz, 41 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 8.00, 13.61, 24.29
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 147 | 0.015 | 0.005 | 0.026 | 0.010 | 0.005 | 0.026 | - | - | - | - |
| shadow maps | 147 | 0.183 | 0.176 | 0.245 | 0.169 | 0.172 | 0.211 | - | - | - | - |
| local shadows | 119 | 0.468 | 0.427 | 0.480 | 0.395 | 0.342 | 0.448 | - | - | - | - |
| environment faces | 147 | 0.610 | 0.462 | 1.000 | 0.542 | 0.385 | 0.936 | - | - | - | - |
| environment prefilter | 147 | 0.386 | 0.337 | 0.649 | 0.385 | 0.337 | 0.649 | - | - | - | - |
| G-buffer | 147 | 0.206 | 0.190 | 0.271 | 0.166 | 0.148 | 0.228 | - | - | - | - |
| surface test | 147 | 0.236 | 0.224 | 0.327 | 0.234 | 0.224 | 0.327 | - | - | - | - |
| depth pyramid | 147 | 0.304 | 0.273 | 0.504 | 0.302 | 0.273 | 0.504 | - | - | - | - |
| direct light | 147 | 4.281 | 4.113 | 4.738 | 4.222 | 4.071 | 4.687 | - | - | - | - |
| light pyramid | 147 | 0.156 | 0.130 | 0.275 | 0.156 | 0.130 | 0.275 | - | - | - | - |
| ray plan | 147 | 1.004 | 0.917 | 1.198 | 1.004 | 0.917 | 1.198 | - | - | - | - |
| reflection rays | 147 | 3.457 | 2.974 | 6.142 | 3.418 | 2.936 | 6.043 | - | - | - | - |
| diffuse rays | 147 | 1.833 | 1.512 | 2.952 | 1.832 | 1.512 | 2.952 | - | - | - | - |
| screen trace resolve | 147 | 0.871 | 0.808 | 1.090 | 0.871 | 0.808 | 1.090 | - | - | - | - |
| accumulate indirect diffuse | 147 | 0.669 | 0.582 | 1.067 | 0.669 | 0.582 | 1.067 | - | - | - | - |
| filter indirect diffuse | 147 | 5.021 | 4.924 | 5.600 | 5.021 | 4.924 | 5.600 | - | - | - | - |
| accumulate indirect specular | 147 | 1.582 | 1.461 | 2.049 | 1.582 | 1.461 | 2.049 | - | - | - | - |
| composite | 147 | 0.219 | 0.182 | 0.411 | 0.185 | 0.178 | 0.210 | - | - | - | - |
| scatter rows | 147 | 0.047 | 0.035 | 0.117 | 0.045 | 0.035 | 0.117 | - | - | - | - |
| scatter columns | 147 | 0.026 | 0.019 | 0.046 | 0.025 | 0.019 | 0.044 | - | - | - | - |
| transparent | 147 | 2.217 | 2.124 | 2.925 | 0.373 | 0.244 | 1.092 | - | - | - | - |
| FSR 3.1 | 147 | 3.597 | 3.524 | 4.508 | 2.007 | 1.882 | 2.427 | - | - | - | - |
| bloom | 147 | 0.834 | 0.790 | 1.663 | 0.324 | 0.305 | 0.385 | - | - | - | - |
| tone map | 151 | 0.151 | 0.145 | 0.184 | 0.147 | 0.142 | 0.179 | - | - | - | - |
| capture | 119 | 0.049 | 0.033 | 0.106 | 0.048 | 0.033 | 0.106 | - | - | - | - |
| (after the last pass) | 147 | 0.004 | 0.004 | 0.007 | 0.003 | 0.003 | 0.006 | - | - | - | - |
| frame span | 151 | 30.667 | 28.824 | 42.001 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
