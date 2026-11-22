Apple M1 Max, 1920 x 1080: 152 frames, 0 other command buffers.
GPU clock over the run's timed span: 1296.0 MHz (p10 1296.0, p90 1296.0 MHz, 21 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 7.58, 6.68, 6.20
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 152 | 0.007 | 0.005 | 0.005 | 0.007 | 0.005 | 0.005 | - | - | - | - |
| shadow maps | 152 | 0.151 | 0.150 | 0.153 | 0.115 | 0.112 | 0.148 | - | - | - | - |
| local shadows | 120 | 0.439 | 0.395 | 0.425 | 0.355 | 0.314 | 0.382 | - | - | - | - |
| environment faces | 152 | 0.400 | 0.334 | 0.547 | 0.356 | 0.254 | 0.480 | - | - | - | - |
| environment prefilter | 152 | 0.314 | 0.309 | 0.311 | 0.314 | 0.309 | 0.311 | - | - | - | - |
| G-buffer | 152 | 0.198 | 0.198 | 0.210 | 0.148 | 0.145 | 0.153 | - | - | - | - |
| surface test | 152 | 0.230 | 0.231 | 0.234 | 0.230 | 0.231 | 0.234 | - | - | - | - |
| depth pyramid | 152 | 0.253 | 0.253 | 0.255 | 0.253 | 0.253 | 0.255 | - | - | - | - |
| key and fill | 152 | 0.248 | 0.247 | 0.262 | 0.197 | 0.197 | 0.205 | - | - | - | - |
| direct light | 152 | 3.253 | 3.244 | 3.324 | 3.253 | 3.244 | 3.324 | - | - | - | - |
| light pyramid | 152 | 0.095 | 0.094 | 0.098 | 0.095 | 0.094 | 0.098 | - | - | - | - |
| ray plan | 152 | 1.345 | 1.345 | 1.362 | 1.345 | 1.345 | 1.362 | - | - | - | - |
| reflection rays | 152 | 1.683 | 1.488 | 4.942 | 1.682 | 1.488 | 4.942 | - | - | - | - |
| diffuse rays | 152 | 1.464 | 1.258 | 5.044 | 1.464 | 1.258 | 5.044 | - | - | - | - |
| screen trace resolve | 152 | 0.311 | 0.302 | 0.430 | 0.311 | 0.302 | 0.430 | - | - | - | - |
| accumulate indirect diffuse | 152 | 0.638 | 0.641 | 0.648 | 0.638 | 0.641 | 0.648 | - | - | - | - |
| filter indirect diffuse | 152 | 2.060 | 2.036 | 2.050 | 2.060 | 2.036 | 2.050 | - | - | - | - |
| accumulate indirect specular | 152 | 1.466 | 1.475 | 1.486 | 1.466 | 1.475 | 1.486 | - | - | - | - |
| composite | 152 | 0.172 | 0.171 | 0.176 | 0.171 | 0.170 | 0.176 | - | - | - | - |
| scatter rows | 152 | 0.032 | 0.032 | 0.033 | 0.032 | 0.032 | 0.033 | - | - | - | - |
| scatter columns | 152 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 152 | 2.014 | 2.011 | 2.212 | 1.172 | 1.084 | 1.933 | - | - | - | - |
| exposure | 152 | 0.280 | 0.031 | 1.024 | 0.022 | 0.022 | 0.022 | - | - | - | - |
| FSR 3.1 | 152 | 1.723 | 1.715 | 1.727 | 1.713 | 1.715 | 1.726 | - | - | - | - |
| exposure adapt | 152 | 0.010 | 0.010 | 0.011 | 0.010 | 0.010 | 0.011 | - | - | - | - |
| bloom | 152 | 1.083 | 1.215 | 1.468 | 0.297 | 0.293 | 0.320 | - | - | - | - |
| tone map | 152 | 0.126 | 0.126 | 0.127 | 0.121 | 0.121 | 0.122 | - | - | - | - |
| capture | 120 | 0.031 | 0.031 | 0.031 | 0.031 | 0.031 | 0.031 | - | - | - | - |
| (after the last pass) | 152 | 0.003 | 0.003 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 152 | 20.233 | 19.774 | 26.049 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
