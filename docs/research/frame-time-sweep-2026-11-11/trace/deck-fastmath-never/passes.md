Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1293.4 MHz (p10 1292.6, p90 1293.8 MHz, 56 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 3.98, 5.16, 6.46
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.008 | 0.009 | 0.008 | 0.008 | - | - | - | - |
| shadow maps | 151 | 0.176 | 0.180 | 0.196 | 0.168 | 0.180 | 0.196 | - | - | - | - |
| local shadows | 119 | 0.450 | 0.412 | 0.444 | 0.380 | 0.340 | 0.431 | - | - | - | - |
| environment faces | 151 | 0.906 | 0.627 | 1.726 | 0.842 | 0.564 | 1.683 | - | - | - | - |
| environment prefilter | 151 | 0.444 | 0.436 | 0.446 | 0.444 | 0.436 | 0.446 | - | - | - | - |
| G-buffer | 151 | 0.293 | 0.292 | 0.305 | 0.241 | 0.238 | 0.254 | - | - | - | - |
| surface test | 151 | 0.246 | 0.247 | 0.250 | 0.246 | 0.247 | 0.250 | - | - | - | - |
| depth pyramid | 151 | 0.471 | 0.470 | 0.475 | 0.471 | 0.470 | 0.475 | - | - | - | - |
| direct light | 151 | 7.699 | 7.677 | 7.857 | 7.647 | 7.636 | 7.809 | - | - | - | - |
| light pyramid | 151 | 0.112 | 0.111 | 0.118 | 0.112 | 0.111 | 0.118 | - | - | - | - |
| ray plan | 151 | 0.931 | 0.933 | 0.944 | 0.931 | 0.933 | 0.944 | - | - | - | - |
| reflection rays | 151 | 3.361 | 2.908 | 11.072 | 3.361 | 2.908 | 11.071 | - | - | - | - |
| diffuse rays | 151 | 2.280 | 1.987 | 7.433 | 2.280 | 1.987 | 7.433 | - | - | - | - |
| screen trace resolve | 151 | 0.287 | 0.281 | 0.380 | 0.287 | 0.281 | 0.380 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.767 | 0.771 | 0.778 | 0.767 | 0.771 | 0.778 | - | - | - | - |
| filter indirect diffuse | 151 | 12.256 | 12.219 | 12.247 | 12.256 | 12.219 | 12.247 | - | - | - | - |
| accumulate indirect specular | 151 | 1.979 | 1.992 | 2.009 | 1.979 | 1.992 | 2.009 | - | - | - | - |
| composite | 151 | 0.165 | 0.164 | 0.171 | 0.164 | 0.164 | 0.170 | - | - | - | - |
| scatter rows | 151 | 0.035 | 0.035 | 0.035 | 0.034 | 0.034 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.019 | 0.019 | 0.020 | 0.019 | 0.019 | 0.020 | - | - | - | - |
| transparent | 151 | 3.892 | 3.876 | 4.359 | 0.681 | 0.488 | 1.977 | - | - | - | - |
| FSR 3.1 | 151 | 9.529 | 9.465 | 10.223 | 7.160 | 7.183 | 7.197 | - | - | - | - |
| bloom | 151 | 2.307 | 2.323 | 2.832 | 0.884 | 0.880 | 0.903 | - | - | - | - |
| tone map | 151 | 0.190 | 0.190 | 0.193 | 0.187 | 0.187 | 0.190 | - | - | - | - |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.005 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 47.248 | 46.337 | 58.738 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
