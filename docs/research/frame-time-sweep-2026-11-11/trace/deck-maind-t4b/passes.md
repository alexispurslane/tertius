Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.2 MHz (p10 1290.5, p90 1292.4 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 6.32, 5.72, 6.49
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.010 | 0.008 | 0.009 | 0.010 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.179 | 0.194 | 0.167 | 0.179 | 0.194 | - | - | - | - |
| local shadows | 119 | 0.457 | 0.412 | 0.444 | 0.388 | 0.338 | 0.433 | - | - | - | - |
| environment faces | 151 | 0.572 | 0.437 | 0.965 | 0.510 | 0.369 | 0.881 | - | - | - | - |
| environment prefilter | 151 | 0.333 | 0.324 | 0.333 | 0.333 | 0.324 | 0.333 | - | - | - | - |
| G-buffer | 151 | 0.201 | 0.197 | 0.208 | 0.154 | 0.149 | 0.168 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.229 | 0.225 | 0.226 | 0.229 | - | - | - | - |
| depth pyramid | 151 | 0.269 | 0.267 | 0.271 | 0.269 | 0.267 | 0.271 | - | - | - | - |
| direct light | 151 | 4.100 | 4.082 | 4.154 | 4.052 | 4.042 | 4.108 | - | - | - | - |
| light pyramid | 151 | 0.107 | 0.106 | 0.113 | 0.107 | 0.106 | 0.113 | - | - | - | - |
| ray plan | 151 | 0.894 | 0.895 | 0.904 | 0.894 | 0.895 | 0.904 | - | - | - | - |
| reflection rays | 151 | 3.034 | 2.638 | 9.407 | 3.034 | 2.636 | 9.407 | - | - | - | - |
| diffuse rays | 151 | 1.637 | 1.403 | 5.777 | 1.637 | 1.403 | 5.777 | - | - | - | - |
| screen trace resolve | 151 | 0.222 | 0.215 | 0.327 | 0.222 | 0.215 | 0.327 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.511 | 0.514 | 0.521 | 0.511 | 0.514 | 0.521 | - | - | - | - |
| filter indirect diffuse | 151 | 4.173 | 4.148 | 4.179 | 4.173 | 4.148 | 4.179 | - | - | - | - |
| accumulate indirect specular | 151 | 1.390 | 1.396 | 1.419 | 1.390 | 1.396 | 1.419 | - | - | - | - |
| composite | 151 | 0.163 | 0.163 | 0.169 | 0.163 | 0.162 | 0.168 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.034 | 0.035 | 0.033 | 0.034 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.019 | 0.017 | 0.017 | 0.019 | - | - | - | - |
| transparent | 151 | 2.079 | 2.078 | 2.400 | 0.383 | 0.287 | 0.932 | - | - | - | - |
| FSR 3.1 | 151 | 3.061 | 3.022 | 3.430 | 1.751 | 1.751 | 1.773 | - | - | - | - |
| bloom | 151 | 1.050 | 1.052 | 1.236 | 0.290 | 0.286 | 0.307 | - | - | - | - |
| tone map | 151 | 0.126 | 0.126 | 0.132 | 0.123 | 0.123 | 0.126 | - | - | - | - |
| capture | 119 | 0.031 | 0.031 | 0.033 | 0.029 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.904 | 24.188 | 34.260 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
