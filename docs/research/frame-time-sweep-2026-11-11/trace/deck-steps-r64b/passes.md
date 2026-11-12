Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.2 MHz (p10 1290.7, p90 1291.5 MHz, 30 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 8.29, 7.98, 7.40
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.178 | 0.193 | 0.166 | 0.178 | 0.193 | - | - | - | - |
| local shadows | 119 | 0.459 | 0.413 | 0.445 | 0.393 | 0.364 | 0.434 | - | - | - | - |
| environment faces | 151 | 0.562 | 0.429 | 0.961 | 0.503 | 0.371 | 0.890 | - | - | - | - |
| environment prefilter | 151 | 0.329 | 0.324 | 0.333 | 0.329 | 0.324 | 0.333 | - | - | - | - |
| G-buffer | 151 | 0.199 | 0.197 | 0.209 | 0.151 | 0.148 | 0.164 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.230 | 0.225 | 0.226 | 0.230 | - | - | - | - |
| depth pyramid | 151 | 0.267 | 0.266 | 0.274 | 0.267 | 0.266 | 0.274 | - | - | - | - |
| direct light | 151 | 4.102 | 4.088 | 4.174 | 4.055 | 4.043 | 4.131 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.107 | 0.116 | 0.108 | 0.107 | 0.116 | - | - | - | - |
| ray plan | 151 | 0.895 | 0.896 | 0.906 | 0.895 | 0.896 | 0.906 | - | - | - | - |
| reflection rays | 151 | 1.664 | 1.439 | 5.256 | 1.664 | 1.439 | 5.255 | - | - | - | - |
| diffuse rays | 151 | 1.634 | 1.400 | 5.754 | 1.634 | 1.400 | 5.754 | - | - | - | - |
| screen trace resolve | 151 | 0.222 | 0.215 | 0.326 | 0.222 | 0.215 | 0.326 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.513 | 0.515 | 0.523 | 0.513 | 0.515 | 0.523 | - | - | - | - |
| filter indirect diffuse | 151 | 4.171 | 4.147 | 4.173 | 4.171 | 4.147 | 4.173 | - | - | - | - |
| accumulate indirect specular | 151 | 1.386 | 1.394 | 1.409 | 1.386 | 1.394 | 1.409 | - | - | - | - |
| composite | 151 | 0.164 | 0.163 | 0.169 | 0.163 | 0.163 | 0.168 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.034 | 0.032 | 0.033 | 0.034 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.076 | 2.082 | 2.348 | 0.364 | 0.276 | 0.965 | - | - | - | - |
| FSR 3.1 | 151 | 3.090 | 3.033 | 3.612 | 1.752 | 1.751 | 1.784 | - | - | - | - |
| bloom | 151 | 1.038 | 1.040 | 1.265 | 0.291 | 0.285 | 0.311 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.133 | 0.123 | 0.123 | 0.125 | - | - | - | - |
| capture | 119 | 0.031 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 23.511 | 23.000 | 29.914 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
