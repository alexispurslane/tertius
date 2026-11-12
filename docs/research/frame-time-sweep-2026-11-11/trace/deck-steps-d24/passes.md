Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.1 MHz (p10 1290.7, p90 1291.6 MHz, 30 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 8.83, 8.07, 7.45
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.008 | 0.009 | 0.008 | 0.008 | - | - | - | - |
| shadow maps | 151 | 0.175 | 0.178 | 0.194 | 0.167 | 0.178 | 0.194 | - | - | - | - |
| local shadows | 119 | 0.457 | 0.411 | 0.448 | 0.384 | 0.330 | 0.432 | - | - | - | - |
| environment faces | 151 | 0.574 | 0.433 | 0.963 | 0.509 | 0.369 | 0.898 | - | - | - | - |
| environment prefilter | 151 | 0.332 | 0.323 | 0.338 | 0.332 | 0.323 | 0.338 | - | - | - | - |
| G-buffer | 151 | 0.199 | 0.197 | 0.207 | 0.151 | 0.148 | 0.163 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.230 | 0.225 | 0.226 | 0.230 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.267 | 0.273 | 0.268 | 0.267 | 0.273 | - | - | - | - |
| direct light | 151 | 4.100 | 4.082 | 4.203 | 4.052 | 4.037 | 4.159 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.107 | 0.116 | 0.108 | 0.107 | 0.116 | - | - | - | - |
| ray plan | 151 | 0.893 | 0.894 | 0.903 | 0.893 | 0.894 | 0.903 | - | - | - | - |
| reflection rays | 151 | 3.023 | 2.629 | 9.380 | 3.022 | 2.628 | 9.379 | - | - | - | - |
| diffuse rays | 151 | 0.891 | 0.769 | 3.037 | 0.891 | 0.769 | 3.037 | - | - | - | - |
| screen trace resolve | 151 | 0.219 | 0.212 | 0.332 | 0.219 | 0.212 | 0.332 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.515 | 0.518 | 0.526 | 0.515 | 0.518 | 0.526 | - | - | - | - |
| filter indirect diffuse | 151 | 4.174 | 4.148 | 4.173 | 4.174 | 4.148 | 4.173 | - | - | - | - |
| accumulate indirect specular | 151 | 1.388 | 1.394 | 1.413 | 1.388 | 1.394 | 1.413 | - | - | - | - |
| composite | 151 | 0.164 | 0.164 | 0.170 | 0.164 | 0.163 | 0.170 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.034 | 0.032 | 0.033 | 0.034 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.067 | 2.069 | 2.240 | 0.349 | 0.273 | 0.968 | - | - | - | - |
| FSR 3.1 | 151 | 3.102 | 3.016 | 3.644 | 1.754 | 1.754 | 1.775 | - | - | - | - |
| bloom | 151 | 1.056 | 1.058 | 1.264 | 0.290 | 0.286 | 0.308 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.135 | 0.123 | 0.123 | 0.126 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.117 | 23.544 | 31.742 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
