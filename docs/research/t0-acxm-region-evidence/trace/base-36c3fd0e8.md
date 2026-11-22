Apple M1 Max, 1920 x 1080: 152 frames, 0 other command buffers.
GPU clock over the run's timed span: 1296.0 MHz (p10 1296.0, p90 1296.0 MHz, 22 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 7.33, 6.54, 6.12
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 152 | 0.007 | 0.005 | 0.005 | 0.007 | 0.005 | 0.005 | - | - | - | - |
| shadow maps | 152 | 0.151 | 0.150 | 0.153 | 0.115 | 0.112 | 0.148 | - | - | - | - |
| local shadows | 120 | 0.442 | 0.398 | 0.426 | 0.352 | 0.312 | 0.382 | - | - | - | - |
| environment faces | 152 | 0.407 | 0.354 | 0.568 | 0.359 | 0.258 | 0.484 | - | - | - | - |
| environment prefilter | 152 | 0.318 | 0.309 | 0.314 | 0.318 | 0.309 | 0.314 | - | - | - | - |
| G-buffer | 152 | 0.200 | 0.195 | 0.208 | 0.149 | 0.145 | 0.153 | - | - | - | - |
| surface test | 152 | 0.230 | 0.231 | 0.234 | 0.230 | 0.231 | 0.234 | - | - | - | - |
| depth pyramid | 152 | 0.254 | 0.252 | 0.254 | 0.254 | 0.252 | 0.254 | - | - | - | - |
| key and fill | 152 | 0.248 | 0.245 | 0.263 | 0.197 | 0.196 | 0.204 | - | - | - | - |
| direct light | 152 | 3.259 | 3.247 | 3.330 | 3.259 | 3.247 | 3.330 | - | - | - | - |
| light pyramid | 152 | 0.094 | 0.093 | 0.096 | 0.094 | 0.093 | 0.096 | - | - | - | - |
| ray plan | 152 | 1.348 | 1.347 | 1.363 | 1.348 | 1.347 | 1.363 | - | - | - | - |
| reflection rays | 152 | 1.689 | 1.491 | 4.940 | 1.689 | 1.490 | 4.939 | - | - | - | - |
| diffuse rays | 152 | 1.467 | 1.261 | 5.051 | 1.467 | 1.261 | 5.051 | - | - | - | - |
| screen trace resolve | 152 | 0.312 | 0.304 | 0.431 | 0.312 | 0.304 | 0.431 | - | - | - | - |
| accumulate indirect diffuse | 152 | 0.598 | 0.601 | 0.607 | 0.598 | 0.601 | 0.607 | - | - | - | - |
| filter indirect diffuse | 152 | 2.056 | 2.032 | 2.046 | 2.056 | 2.032 | 2.046 | - | - | - | - |
| accumulate indirect specular | 152 | 1.417 | 1.425 | 1.436 | 1.417 | 1.425 | 1.436 | - | - | - | - |
| composite | 152 | 0.173 | 0.173 | 0.177 | 0.173 | 0.172 | 0.177 | - | - | - | - |
| scatter rows | 152 | 0.032 | 0.032 | 0.034 | 0.032 | 0.032 | 0.034 | - | - | - | - |
| scatter columns | 152 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 152 | 2.040 | 2.032 | 2.348 | 1.203 | 1.119 | 1.983 | - | - | - | - |
| exposure | 152 | 0.262 | 0.031 | 0.954 | 0.022 | 0.022 | 0.023 | - | - | - | - |
| FSR 3.1 | 152 | 1.718 | 1.713 | 1.726 | 1.710 | 1.713 | 1.725 | - | - | - | - |
| exposure adapt | 152 | 0.010 | 0.010 | 0.011 | 0.010 | 0.010 | 0.011 | - | - | - | - |
| bloom | 152 | 1.087 | 1.213 | 1.521 | 0.298 | 0.292 | 0.323 | - | - | - | - |
| tone map | 152 | 0.126 | 0.125 | 0.127 | 0.121 | 0.121 | 0.122 | - | - | - | - |
| capture | 120 | 0.031 | 0.031 | 0.032 | 0.031 | 0.031 | 0.032 | - | - | - | - |
| (after the last pass) | 152 | 0.003 | 0.003 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 152 | 20.197 | 19.706 | 26.125 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
