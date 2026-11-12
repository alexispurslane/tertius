Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.1 MHz (p10 1290.8, p90 1291.5 MHz, 30 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 34.89, 16.31, 11.51
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.172 | 0.173 | 0.194 | 0.164 | 0.173 | 0.194 | - | - | - | - |
| local shadows | 119 | 0.456 | 0.411 | 0.442 | 0.388 | 0.351 | 0.430 | - | - | - | - |
| environment faces | 151 | 0.570 | 0.437 | 0.948 | 0.509 | 0.368 | 0.881 | - | - | - | - |
| environment prefilter | 151 | 0.333 | 0.323 | 0.338 | 0.333 | 0.323 | 0.338 | - | - | - | - |
| G-buffer | 151 | 0.200 | 0.198 | 0.210 | 0.153 | 0.149 | 0.170 | - | - | - | - |
| surface test | 151 | 0.223 | 0.225 | 0.228 | 0.223 | 0.225 | 0.228 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.266 | 0.277 | 0.268 | 0.266 | 0.277 | - | - | - | - |
| direct light | 151 | 4.098 | 4.083 | 4.182 | 4.050 | 4.041 | 4.129 | - | - | - | - |
| light pyramid | 151 | 0.106 | 0.105 | 0.116 | 0.106 | 0.105 | 0.116 | - | - | - | - |
| ray plan | 151 | 0.891 | 0.892 | 0.900 | 0.891 | 0.892 | 0.900 | - | - | - | - |
| reflection rays | 151 | 2.099 | 1.802 | 6.596 | 2.099 | 1.801 | 6.596 | - | - | - | - |
| diffuse rays | 151 | 1.636 | 1.400 | 5.767 | 1.636 | 1.400 | 5.767 | - | - | - | - |
| screen trace resolve | 151 | 0.220 | 0.212 | 0.327 | 0.220 | 0.212 | 0.327 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.514 | 0.516 | 0.524 | 0.514 | 0.516 | 0.524 | - | - | - | - |
| filter indirect diffuse | 151 | 4.183 | 4.159 | 4.187 | 4.183 | 4.159 | 4.187 | - | - | - | - |
| accumulate indirect specular | 151 | 1.388 | 1.394 | 1.411 | 1.388 | 1.394 | 1.411 | - | - | - | - |
| composite | 151 | 0.164 | 0.164 | 0.171 | 0.164 | 0.163 | 0.170 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.034 | 0.035 | 0.033 | 0.034 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.019 | 0.017 | 0.017 | 0.019 | - | - | - | - |
| transparent | 151 | 2.080 | 2.083 | 2.293 | 0.358 | 0.266 | 0.986 | - | - | - | - |
| FSR 3.1 | 151 | 3.099 | 3.034 | 3.556 | 1.753 | 1.753 | 1.782 | - | - | - | - |
| bloom | 151 | 1.068 | 1.077 | 1.294 | 0.290 | 0.286 | 0.306 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.132 | 0.123 | 0.123 | 0.125 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 23.954 | 23.338 | 31.623 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
