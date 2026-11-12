Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1288.3 MHz (p10 1289.9, p90 1291.7 MHz, 32 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 11.48, 9.82, 9.10
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.008 | 0.009 | 0.009 | 0.008 | 0.009 | - | - | - | - |
| shadow maps | 151 | 0.173 | 0.177 | 0.192 | 0.165 | 0.177 | 0.192 | - | - | - | - |
| local shadows | 119 | 0.456 | 0.410 | 0.440 | 0.389 | 0.342 | 0.433 | - | - | - | - |
| environment faces | 151 | 0.572 | 0.447 | 0.967 | 0.511 | 0.365 | 0.889 | - | - | - | - |
| environment prefilter | 151 | 0.332 | 0.324 | 0.328 | 0.332 | 0.324 | 0.328 | - | - | - | - |
| G-buffer | 151 | 0.201 | 0.198 | 0.209 | 0.152 | 0.149 | 0.165 | - | - | - | - |
| surface test | 151 | 0.224 | 0.225 | 0.229 | 0.224 | 0.225 | 0.229 | - | - | - | - |
| depth pyramid | 151 | 0.268 | 0.266 | 0.269 | 0.268 | 0.266 | 0.269 | - | - | - | - |
| direct light | 151 | 4.118 | 4.079 | 4.185 | 4.069 | 4.032 | 4.133 | - | - | - | - |
| light pyramid | 151 | 0.109 | 0.108 | 0.117 | 0.109 | 0.108 | 0.117 | - | - | - | - |
| ray plan | 151 | 0.893 | 0.893 | 0.904 | 0.893 | 0.893 | 0.904 | - | - | - | - |
| reflection rays | 151 | 2.411 | 2.080 | 7.552 | 2.410 | 2.080 | 7.552 | - | - | - | - |
| diffuse rays | 151 | 1.652 | 1.407 | 5.762 | 1.652 | 1.407 | 5.762 | - | - | - | - |
| screen trace resolve | 151 | 0.225 | 0.218 | 0.324 | 0.225 | 0.218 | 0.324 | - | - | - | - |
| accumulate indirect diffuse | 151 | 0.519 | 0.520 | 0.530 | 0.519 | 0.520 | 0.530 | - | - | - | - |
| filter indirect diffuse | 151 | 4.202 | 4.169 | 4.235 | 4.202 | 4.169 | 4.235 | - | - | - | - |
| accumulate indirect specular | 151 | 1.393 | 1.399 | 1.419 | 1.393 | 1.399 | 1.419 | - | - | - | - |
| composite | 151 | 0.168 | 0.167 | 0.175 | 0.167 | 0.167 | 0.175 | - | - | - | - |
| scatter rows | 151 | 0.033 | 0.033 | 0.035 | 0.033 | 0.033 | 0.035 | - | - | - | - |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | - | - | - | - |
| transparent | 151 | 2.056 | 2.071 | 2.278 | 0.364 | 0.272 | 1.002 | - | - | - | - |
| FSR 3.1 | 151 | 3.037 | 2.983 | 3.384 | 1.755 | 1.753 | 1.785 | - | - | - | - |
| bloom | 151 | 1.051 | 1.055 | 1.262 | 0.291 | 0.287 | 0.307 | - | - | - | - |
| tone map | 151 | 0.127 | 0.127 | 0.135 | 0.124 | 0.123 | 0.125 | - | - | - | - |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.031 | - | - | - | - |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | - | - | - | - |
| frame span | 151 | 24.317 | 23.604 | 32.541 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.

The trace holds no GPU counters, so the table has no limiters (tools/README.md, "GPU trace").
