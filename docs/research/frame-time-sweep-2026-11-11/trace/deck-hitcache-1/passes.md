Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.1 MHz (p10 1290.1, p90 1292.1 MHz, 34 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 25.74, 29.88, 31.88
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.172 | 0.174 | 0.192 | 0.164 | 0.174 | 0.192 | - | - | - | - |
| local shadows | 119 | 0.452 | 0.410 | 0.437 | 0.383 | 0.340 | 0.425 | - | - | - | - |
| environment faces | 151 | 0.569 | 0.446 | 0.945 | 0.507 | 0.371 | 0.890 | - | - | - | - |
| environment prefilter | 151 | 0.330 | 0.325 | 0.328 | 0.330 | 0.325 | 0.328 | - | - | - | - |
| G-buffer | 151 | 0.202 | 0.200 | 0.208 | 0.154 | 0.151 | 0.165 | - | - | - | - |
| surface test | 151 | 0.224 | 0.224 | 0.233 | 0.224 | 0.224 | 0.233 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.267 | 0.272 | 0.268 | 0.267 | 0.272 | - | - | - | - |
| direct light | 151 | 4.112 | 4.100 | 4.182 | 4.064 | 4.054 | 4.135 | - | - | - | - |
| light pyramid | 151 | 0.120 | 0.118 | 0.135 | 0.120 | 0.118 | 0.135 | - | - | - | - |
| ray plan | 151 | 0.896 | 0.894 | 0.921 | 0.896 | 0.894 | 0.921 | - | - | - | - |
| reflection rays | 151 | 2.400 | 2.116 | 7.218 | 2.399 | 2.116 | 7.218 | - | - | - | - |
| diffuse rays | 151 | 1.485 | 1.281 | 5.060 | 1.485 | 1.281 | 5.060 | - | - | - | - |
| screen trace resolve | 151 | 0.710 | 0.700 | 0.821 | 0.710 | 0.700 | 0.821 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.520 | 0.519 | 0.548 | 0.520 | 0.519 | 0.548 | - | - | - | - |
| filter indirect diffuse | 151 | 4.262 | 4.214 | 4.402 | 4.262 | 4.214 | 4.402 | - | - | - | - |
| accumulate indirect specular | 151 | 1.399 | 1.403 | 1.450 | 1.399 | 1.403 | 1.450 | - | - | - | - |
| composite | 151 | 0.175 | 0.173 | 0.189 | 0.174 | 0.173 | 0.189 | - | - | - | - |
| scatter rows | 151 | 0.034 | 0.033 | 0.035 | 0.033 | 0.033 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.055 | 2.059 | 2.249 | 0.330 | 0.282 | 0.808 | - | - | - | - |
| FSR 3.1 | 151 | 3.160 | 3.138 | 3.624 | 1.768 | 1.763 | 1.823 | - | - | - | - |
| bloom | 151 | 1.047 | 1.058 | 1.277 | 0.292 | 0.289 | 0.309 | - | - | - | - |
| tone map | 151 | 0.130 | 0.128 | 0.142 | 0.126 | 0.125 | 0.137 | - | - | - | - |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.724 | 24.165 | 32.463 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
