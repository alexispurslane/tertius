Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.4 MHz (p10 1290.9, p90 1292.3 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 5.56, 5.44, 6.44
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.008 | 0.009 | 0.008 | 0.008 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.179 | 0.193 | 0.166 | 0.179 | 0.193 | - | - | - | - |
| local shadows | 119 | 0.456 | 0.411 | 0.439 | 0.386 | 0.329 | 0.430 | - | - | - | - |
| environment faces | 151 | 0.574 | 0.441 | 0.948 | 0.511 | 0.368 | 0.880 | - | - | - | - |
| environment prefilter | 151 | 0.330 | 0.323 | 0.330 | 0.330 | 0.323 | 0.330 | - | - | - | - |
| G-buffer | 151 | 0.198 | 0.197 | 0.208 | 0.152 | 0.148 | 0.165 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.230 | 0.225 | 0.226 | 0.230 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.267 | 0.270 | 0.268 | 0.267 | 0.270 | - | - | - | - |
| direct light | 151 | 4.095 | 4.083 | 4.171 | 4.049 | 4.037 | 4.130 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.107 | 0.114 | 0.108 | 0.107 | 0.114 | - | - | - | - |
| ray plan | 151 | 0.894 | 0.895 | 0.903 | 0.894 | 0.895 | 0.903 | - | - | - | - |
| reflection rays | 151 | 3.145 | 2.757 | 9.391 | 3.144 | 2.756 | 9.391 | - | - | - | - |
| diffuse rays | 151 | 1.638 | 1.403 | 5.768 | 1.638 | 1.403 | 5.768 | - | - | - | - |
| screen trace resolve | 151 | 0.757 | 0.745 | 0.934 | 0.757 | 0.745 | 0.934 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.514 | 0.516 | 0.525 | 0.514 | 0.516 | 0.525 | - | - | - | - |
| filter indirect diffuse | 151 | 4.177 | 4.153 | 4.175 | 4.177 | 4.153 | 4.175 | - | - | - | - |
| accumulate indirect specular | 151 | 1.395 | 1.402 | 1.427 | 1.395 | 1.402 | 1.427 | - | - | - | - |
| composite | 151 | 0.164 | 0.163 | 0.170 | 0.163 | 0.162 | 0.170 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.034 | 0.032 | 0.033 | 0.034 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.043 | 2.038 | 2.274 | 0.332 | 0.253 | 0.936 | - | - | - | - |
| FSR 3.1 | 151 | 3.094 | 3.108 | 3.431 | 1.750 | 1.749 | 1.774 | - | - | - | - |
| bloom | 151 | 1.036 | 1.052 | 1.273 | 0.290 | 0.287 | 0.305 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.134 | 0.123 | 0.123 | 0.125 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 25.512 | 24.857 | 34.663 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
