Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.2 MHz (p10 1290.5, p90 1292.2 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 26.28, 15.53, 11.34
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.173 | 0.177 | 0.194 | 0.165 | 0.177 | 0.194 | - | - | - | - |
| local shadows | 119 | 0.454 | 0.411 | 0.438 | 0.388 | 0.347 | 0.429 | - | - | - | - |
| environment faces | 151 | 0.569 | 0.445 | 0.932 | 0.509 | 0.371 | 0.889 | - | - | - | - |
| environment prefilter | 151 | 0.332 | 0.323 | 0.331 | 0.332 | 0.323 | 0.331 | - | - | - | - |
| G-buffer | 151 | 0.202 | 0.197 | 0.207 | 0.154 | 0.149 | 0.166 | - | - | - | - |
| surface test | 151 | 0.224 | 0.225 | 0.229 | 0.224 | 0.225 | 0.229 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.266 | 0.272 | 0.268 | 0.266 | 0.272 | - | - | - | - |
| direct light | 151 | 4.119 | 4.094 | 4.152 | 4.071 | 4.048 | 4.111 | - | - | - | - |
| light pyramid | 151 | 0.108 | 0.107 | 0.118 | 0.108 | 0.107 | 0.118 | - | - | - | - |
| ray plan | 151 | 0.892 | 0.893 | 0.902 | 0.892 | 0.893 | 0.902 | - | - | - | - |
| reflection rays | 151 | 3.034 | 2.633 | 9.396 | 3.034 | 2.633 | 9.395 | - | - | - | - |
| diffuse rays | 151 | 1.269 | 1.087 | 4.425 | 1.269 | 1.087 | 4.425 | - | - | - | - |
| screen trace resolve | 151 | 0.221 | 0.214 | 0.329 | 0.221 | 0.214 | 0.329 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.513 | 0.516 | 0.524 | 0.513 | 0.516 | 0.524 | - | - | - | - |
| filter indirect diffuse | 151 | 4.173 | 4.147 | 4.179 | 4.173 | 4.147 | 4.179 | - | - | - | - |
| accumulate indirect specular | 151 | 1.389 | 1.397 | 1.414 | 1.389 | 1.397 | 1.414 | - | - | - | - |
| composite | 151 | 0.167 | 0.166 | 0.174 | 0.166 | 0.166 | 0.173 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.034 | 0.032 | 0.033 | 0.034 | - | - | - | - |
| scatter columns | 151 | 0.018 | 0.018 | 0.018 | 0.018 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.060 | 2.059 | 2.258 | 0.348 | 0.260 | 0.998 | - | - | - | - |
| FSR 3.1 | 151 | 3.107 | 3.085 | 3.604 | 1.753 | 1.753 | 1.775 | - | - | - | - |
| bloom | 151 | 1.043 | 1.040 | 1.267 | 0.290 | 0.286 | 0.307 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.133 | 0.123 | 0.123 | 0.126 | - | - | - | - |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.515 | 23.819 | 33.011 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
