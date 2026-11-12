Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1286.6 MHz (p10 1284.0, p90 1292.1 MHz, 35 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 32.50, 30.18, 30.95
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.174 | 0.178 | 0.193 | 0.166 | 0.177 | 0.193 | - | - | - | - |
| local shadows | 119 | 0.450 | 0.407 | 0.441 | 0.376 | 0.326 | 0.420 | - | - | - | - |
| environment faces | 151 | 0.580 | 0.458 | 0.979 | 0.514 | 0.365 | 0.898 | - | - | - | - |
| environment prefilter | 151 | 0.335 | 0.324 | 0.328 | 0.335 | 0.324 | 0.328 | - | - | - | - |
| G-buffer | 151 | 0.205 | 0.199 | 0.207 | 0.154 | 0.147 | 0.162 | - | - | - | - |
| surface test | 151 | 0.223 | 0.224 | 0.231 | 0.223 | 0.224 | 0.231 | - | - | - | - |
| depth pyramid | 151 | 0.271 | 0.268 | 0.271 | 0.271 | 0.268 | 0.271 | - | - | - | - |
| direct light | 151 | 4.149 | 4.106 | 4.247 | 4.098 | 4.058 | 4.190 | - | - | - | - |
| light pyramid | 151 | 0.118 | 0.117 | 0.130 | 0.118 | 0.117 | 0.130 | - | - | - | - |
| ray plan | 151 | 0.895 | 0.893 | 0.913 | 0.895 | 0.893 | 0.913 | - | - | - | - |
| reflection rays | 151 | 3.089 | 2.682 | 9.537 | 3.089 | 2.681 | 9.535 | - | - | - | - |
| diffuse rays | 151 | 1.675 | 1.425 | 5.830 | 1.675 | 1.425 | 5.830 | - | - | - | - |
| screen trace resolve | 151 | 0.232 | 0.227 | 0.318 | 0.232 | 0.227 | 0.318 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.495 | 0.496 | 0.510 | 0.495 | 0.496 | 0.510 | - | - | - | - |
| filter indirect diffuse | 151 | 4.274 | 4.232 | 4.388 | 4.274 | 4.232 | 4.388 | - | - | - | - |
| accumulate indirect specular | 151 | 0.611 | 0.611 | 0.633 | 0.611 | 0.611 | 0.633 | - | - | - | - |
| composite | 151 | 0.180 | 0.179 | 0.192 | 0.180 | 0.179 | 0.192 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.034 | 0.036 | 0.033 | 0.034 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.017 | 0.017 | 0.017 | 0.017 | - | - | - | - |
| transparent | 151 | 2.051 | 2.059 | 2.289 | 0.330 | 0.239 | 0.752 | - | - | - | - |
| FSR 3.1 | 151 | 3.166 | 3.147 | 3.631 | 1.770 | 1.766 | 1.818 | - | - | - | - |
| bloom | 151 | 1.065 | 1.061 | 1.261 | 0.292 | 0.290 | 0.307 | - | - | - | - |
| tone map | 151 | 0.129 | 0.128 | 0.138 | 0.125 | 0.124 | 0.129 | - | - | - | - |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.399 | 23.495 | 33.647 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
