Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.4 MHz (p10 1290.9, p90 1292.4 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 7.32, 6.53, 7.30
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.177 | 0.194 | 0.166 | 0.177 | 0.194 | - | - | - | - |
| local shadows | 119 | 0.453 | 0.413 | 0.443 | 0.389 | 0.345 | 0.436 | - | - | - | - |
| environment faces | 151 | 0.570 | 0.435 | 0.949 | 0.511 | 0.370 | 0.892 | - | - | - | - |
| environment prefilter | 151 | 0.332 | 0.323 | 0.336 | 0.332 | 0.323 | 0.336 | - | - | - | - |
| G-buffer | 151 | 0.200 | 0.197 | 0.208 | 0.152 | 0.147 | 0.163 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.230 | 0.225 | 0.226 | 0.230 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.266 | 0.272 | 0.268 | 0.266 | 0.272 | - | - | - | - |
| direct light | 151 | 4.097 | 4.077 | 4.145 | 4.049 | 4.035 | 4.100 | - | - | - | - |
| light pyramid | 151 | 0.107 | 0.107 | 0.113 | 0.107 | 0.107 | 0.113 | - | - | - | - |
| ray plan | 151 | 0.894 | 0.895 | 0.903 | 0.894 | 0.895 | 0.903 | - | - | - | - |
| reflection rays | 151 | 3.037 | 2.637 | 9.438 | 3.037 | 2.636 | 9.438 | - | - | - | - |
| diffuse rays | 151 | 1.638 | 1.405 | 5.771 | 1.638 | 1.405 | 5.771 | - | - | - | - |
| screen trace resolve | 151 | 0.221 | 0.215 | 0.322 | 0.221 | 0.215 | 0.322 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.514 | 0.516 | 0.524 | 0.514 | 0.516 | 0.524 | - | - | - | - |
| filter indirect diffuse | 151 | 4.184 | 4.158 | 4.181 | 4.184 | 4.158 | 4.181 | - | - | - | - |
| accumulate indirect specular | 151 | 1.389 | 1.397 | 1.412 | 1.389 | 1.397 | 1.412 | - | - | - | - |
| composite | 151 | 0.165 | 0.165 | 0.171 | 0.165 | 0.165 | 0.171 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.034 | 0.032 | 0.033 | 0.034 | - | - | - | - |
| scatter columns | 151 | 0.018 | 0.018 | 0.018 | 0.018 | 0.018 | 0.018 | - | - | - | - |
| transparent | 151 | 2.059 | 2.053 | 2.302 | 0.359 | 0.291 | 0.912 | - | - | - | - |
| FSR 3.1 | 151 | 3.081 | 3.014 | 3.611 | 1.751 | 1.752 | 1.777 | - | - | - | - |
| bloom | 151 | 1.045 | 1.053 | 1.272 | 0.292 | 0.287 | 0.309 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.132 | 0.123 | 0.123 | 0.125 | - | - | - | - |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.892 | 24.184 | 34.300 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
