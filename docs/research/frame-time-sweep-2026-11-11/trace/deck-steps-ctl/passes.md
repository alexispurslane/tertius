Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.4 MHz (p10 1290.7, p90 1292.3 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 17.07, 14.55, 11.19
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.173 | 0.176 | 0.192 | 0.166 | 0.176 | 0.192 | - | - | - | - |
| local shadows | 119 | 0.458 | 0.415 | 0.445 | 0.390 | 0.352 | 0.430 | - | - | - | - |
| environment faces | 151 | 0.566 | 0.445 | 0.949 | 0.504 | 0.369 | 0.885 | - | - | - | - |
| environment prefilter | 151 | 0.330 | 0.325 | 0.337 | 0.330 | 0.325 | 0.337 | - | - | - | - |
| G-buffer | 151 | 0.200 | 0.199 | 0.210 | 0.152 | 0.149 | 0.167 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.230 | 0.225 | 0.226 | 0.230 | - | - | - | - |
| depth pyramid | 151 | 0.269 | 0.267 | 0.275 | 0.269 | 0.267 | 0.275 | - | - | - | - |
| direct light | 151 | 4.095 | 4.079 | 4.176 | 4.047 | 4.033 | 4.122 | - | - | - | - |
| light pyramid | 151 | 0.107 | 0.106 | 0.114 | 0.107 | 0.106 | 0.114 | - | - | - | - |
| ray plan | 151 | 0.895 | 0.897 | 0.904 | 0.895 | 0.897 | 0.904 | - | - | - | - |
| reflection rays | 151 | 3.020 | 2.633 | 9.401 | 3.019 | 2.632 | 9.401 | - | - | - | - |
| diffuse rays | 151 | 1.635 | 1.400 | 5.760 | 1.635 | 1.400 | 5.760 | - | - | - | - |
| screen trace resolve | 151 | 0.221 | 0.214 | 0.325 | 0.221 | 0.214 | 0.325 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.512 | 0.515 | 0.521 | 0.512 | 0.515 | 0.521 | - | - | - | - |
| filter indirect diffuse | 151 | 4.172 | 4.147 | 4.167 | 4.172 | 4.147 | 4.167 | - | - | - | - |
| accumulate indirect specular | 151 | 1.388 | 1.394 | 1.413 | 1.388 | 1.394 | 1.413 | - | - | - | - |
| composite | 151 | 0.163 | 0.163 | 0.169 | 0.162 | 0.162 | 0.168 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.034 | 0.035 | 0.033 | 0.034 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.018 | 0.017 | 0.019 | 0.017 | 0.017 | 0.019 | - | - | - | - |
| transparent | 151 | 2.063 | 2.068 | 2.336 | 0.340 | 0.256 | 0.911 | - | - | - | - |
| FSR 3.1 | 151 | 3.124 | 3.085 | 3.603 | 1.750 | 1.749 | 1.782 | - | - | - | - |
| bloom | 151 | 1.034 | 1.052 | 1.276 | 0.291 | 0.286 | 0.310 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.134 | 0.123 | 0.123 | 0.125 | - | - | - | - |
| capture | 119 | 0.031 | 0.031 | 0.033 | 0.029 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.851 | 24.158 | 34.104 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
