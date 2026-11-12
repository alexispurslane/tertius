Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1292.2 MHz (p10 1291.3, p90 1292.6 MHz, 38 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 11.18, 8.40, 7.52
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.011 | 0.009 | 0.008 | 0.011 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.178 | 0.194 | 0.166 | 0.178 | 0.194 | - | - | - | - |
| local shadows | 119 | 0.454 | 0.411 | 0.452 | 0.380 | 0.328 | 0.433 | - | - | - | - |
| environment faces | 151 | 0.577 | 0.446 | 0.956 | 0.510 | 0.367 | 0.883 | - | - | - | - |
| environment prefilter | 151 | 0.330 | 0.324 | 0.334 | 0.330 | 0.324 | 0.334 | - | - | - | - |
| G-buffer | 151 | 0.198 | 0.197 | 0.210 | 0.150 | 0.146 | 0.166 | - | - | - | - |
| surface test | 151 | 0.225 | 0.226 | 0.230 | 0.225 | 0.226 | 0.230 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.267 | 0.276 | 0.268 | 0.267 | 0.276 | - | - | - | - |
| direct light | 151 | 4.099 | 4.084 | 4.185 | 4.050 | 4.036 | 4.140 | - | - | - | - |
| light pyramid | 151 | 0.107 | 0.106 | 0.119 | 0.107 | 0.106 | 0.119 | - | - | - | - |
| ray plan | 151 | 0.483 | 0.484 | 0.490 | 0.483 | 0.484 | 0.490 | - | - | - | - |
| reflection rays | 151 | 9.549 | 9.531 | 9.634 | 9.548 | 9.531 | 9.631 | - | - | - | - |
| diffuse rays | 151 | 1.655 | 1.419 | 5.794 | 1.655 | 1.419 | 5.794 | - | - | - | - |
| screen trace resolve | 151 | 0.264 | 0.260 | 0.328 | 0.264 | 0.260 | 0.328 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.513 | 0.516 | 0.522 | 0.513 | 0.516 | 0.522 | - | - | - | - |
| filter indirect diffuse | 151 | 4.172 | 4.147 | 4.172 | 4.172 | 4.147 | 4.172 | - | - | - | - |
| accumulate indirect specular | 151 | 1.398 | 1.407 | 1.429 | 1.398 | 1.407 | 1.429 | - | - | - | - |
| composite | 151 | 0.165 | 0.164 | 0.171 | 0.164 | 0.164 | 0.170 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.035 | 0.032 | 0.033 | 0.034 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.019 | 0.017 | 0.017 | 0.019 | - | - | - | - |
| transparent | 151 | 2.049 | 2.043 | 2.310 | 0.341 | 0.252 | 0.852 | - | - | - | - |
| FSR 3.1 | 151 | 3.094 | 3.071 | 3.568 | 1.752 | 1.751 | 1.786 | - | - | - | - |
| bloom | 151 | 1.053 | 1.056 | 1.276 | 0.291 | 0.286 | 0.310 | - | - | - | - |
| tone map | 151 | 0.127 | 0.126 | 0.133 | 0.123 | 0.123 | 0.126 | - | - | - | - |
| capture | 119 | 0.032 | 0.031 | 0.033 | 0.030 | 0.029 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 31.033 | 30.691 | 33.894 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
