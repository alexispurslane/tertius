Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.3 MHz (p10 1290.6, p90 1292.4 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 13.33, 10.02, 9.15
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.178 | 0.191 | 0.166 | 0.178 | 0.191 | - | - | - | - |
| local shadows | 119 | 0.455 | 0.411 | 0.441 | 0.394 | 0.363 | 0.437 | - | - | - | - |
| environment faces | 151 | 0.567 | 0.432 | 0.959 | 0.512 | 0.367 | 0.889 | - | - | - | - |
| environment prefilter | 151 | 0.329 | 0.324 | 0.331 | 0.329 | 0.324 | 0.331 | - | - | - | - |
| G-buffer | 151 | 0.197 | 0.196 | 0.207 | 0.150 | 0.146 | 0.166 | - | - | - | - |
| surface test | 151 | 0.223 | 0.224 | 0.229 | 0.223 | 0.224 | 0.229 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.267 | 0.273 | 0.268 | 0.267 | 0.273 | - | - | - | - |
| direct light | 151 | 4.096 | 4.082 | 4.165 | 4.049 | 4.036 | 4.117 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.108 | 0.115 | 0.108 | 0.108 | 0.115 | - | - | - | - |
| ray plan | 151 | 0.892 | 0.893 | 0.904 | 0.892 | 0.893 | 0.904 | - | - | - | - |
| reflection rays | 151 | 3.033 | 2.644 | 9.409 | 3.033 | 2.643 | 9.408 | - | - | - | - |
| diffuse rays | 151 | 1.640 | 1.405 | 5.774 | 1.640 | 1.405 | 5.774 | - | - | - | - |
| screen trace resolve | 151 | 0.222 | 0.215 | 0.320 | 0.222 | 0.215 | 0.320 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.516 | 0.518 | 0.528 | 0.516 | 0.518 | 0.528 | - | - | - | - |
| filter indirect diffuse | 151 | 4.191 | 4.165 | 4.206 | 4.191 | 4.165 | 4.206 | - | - | - | - |
| accumulate indirect specular | 151 | 1.389 | 1.396 | 1.416 | 1.389 | 1.396 | 1.416 | - | - | - | - |
| composite | 151 | 0.165 | 0.164 | 0.170 | 0.165 | 0.164 | 0.170 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.034 | 0.035 | 0.033 | 0.034 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.055 | 2.057 | 2.319 | 0.328 | 0.252 | 0.819 | - | - | - | - |
| FSR 3.1 | 151 | 3.071 | 2.991 | 3.574 | 1.753 | 1.754 | 1.784 | - | - | - | - |
| bloom | 151 | 1.056 | 1.068 | 1.264 | 0.291 | 0.287 | 0.311 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.135 | 0.123 | 0.123 | 0.126 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.909 | 24.196 | 34.470 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
