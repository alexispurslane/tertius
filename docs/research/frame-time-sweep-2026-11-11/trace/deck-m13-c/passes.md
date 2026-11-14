Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1290.8 MHz (p10 1289.5, p90 1292.5 MHz, 26 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 6.62, 6.52, 8.43
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.007 | 0.009 | 0.009 | 0.007 | 0.009 | MMU 0 % | GPU Last Level Cache 0 % | 0.2 | 0.2 |
| shadow maps | 151 | 0.171 | 0.176 | 0.184 | 0.163 | 0.176 | 0.184 | GPU Last Level Cache 7 % | MMU 6 % | 8.4 | 9.5 |
| local shadows | 119 | 0.458 | 0.412 | 0.450 | 0.380 | 0.324 | 0.424 | ALU 13 % | GPU Last Level Cache 8 % | 12.3 | 5.5 |
| environment faces | 151 | 0.579 | 0.452 | 0.990 | 0.510 | 0.372 | 0.893 | ALU 12 % | GPU Last Level Cache 4 % | 5.0 | 1.9 |
| environment prefilter | 151 | 0.320 | 0.312 | 0.319 | 0.320 | 0.312 | 0.319 | ALU 12 % | Texture Sample 3 % | 2.2 | 0.4 |
| G-buffer | 151 | 0.203 | 0.199 | 0.208 | 0.155 | 0.150 | 0.170 | Texture Write 43 % | ALU 23 % | 11.4 | 28.3 |
| surface test | 151 | 0.225 | 0.226 | 0.230 | 0.225 | 0.226 | 0.230 | ALU 43 % | GPU Last Level Cache 22 % | 25.3 | 20.9 |
| depth pyramid | 151 | 0.253 | 0.252 | 0.257 | 0.253 | 0.252 | 0.257 | ALU 70 % | Texture Sample 20 % | 11.4 | 7.1 |
| direct light | 151 | 3.822 | 3.793 | 3.865 | 3.774 | 3.747 | 3.815 | ALU 43 % | Texture Sample 7 % | 1.1 | 1.5 |
| light pyramid | 151 | 0.094 | 0.093 | 0.102 | 0.094 | 0.093 | 0.102 | Texture Sample 35 % | GPU Last Level Cache 28 % | 51.2 | 15.8 |
| ray plan | 151 | 1.301 | 1.297 | 1.320 | 1.301 | 1.297 | 1.320 | ALU 26 % | Texture Sample 12 % | 15.0 | 4.4 |
| reflection rays | 151 | 1.859 | 1.626 | 5.558 | 1.858 | 1.626 | 5.558 | ALU 57 % | GPU Last Level Cache 15 % | 16.7 | 0.8 |
| diffuse rays | 151 | 1.484 | 1.271 | 5.008 | 1.484 | 1.271 | 5.008 | ALU 69 % | GPU Last Level Cache 19 % | 16.7 | 0.8 |
| screen trace resolve | 151 | 0.300 | 0.292 | 0.408 | 0.300 | 0.292 | 0.408 | ALU 39 % | Texture Sample 14 % | 21.2 | 13.8 |
| accumulate indirect diffuse | 151 | 0.515 | 0.516 | 0.529 | 0.515 | 0.516 | 0.529 | ALU 43 % | GPU Last Level Cache 33 % | 31.6 | 10.6 |
| filter indirect diffuse | 151 | 2.009 | 1.982 | 2.013 | 2.009 | 1.982 | 2.013 | ALU 76 % | Texture Sample 49 % | 23.5 | 7.3 |
| accumulate indirect specular | 151 | 1.391 | 1.396 | 1.425 | 1.391 | 1.396 | 1.425 | ALU 39 % | GPU Last Level Cache 25 % | 21.7 | 6.6 |
| composite | 151 | 0.167 | 0.166 | 0.176 | 0.166 | 0.165 | 0.176 | Texture Sample 52 % | Texture Cache 47 % | 57.8 | 20.0 |
| scatter rows | 151 | 0.034 | 0.033 | 0.035 | 0.031 | 0.033 | 0.035 | MMU 17 % | GPU Last Level Cache 15 % | 23.1 | 21.4 |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | MMU 22 % | GPU Last Level Cache 19 % | 39.6 | 16.8 |
| transparent | 151 | 2.015 | 2.007 | 2.301 | 1.167 | 1.083 | 1.974 | ALU 26 % | GPU Last Level Cache 4 % | 1.2 | 0.8 |
| exposure | 151 | 0.254 | 0.200 | 0.719 | 0.025 | 0.022 | 0.026 | ALU 30 % | Texture Sample 6 % | 4.4 | 0.4 |
| FSR 3.1 | 151 | 1.728 | 1.723 | 1.738 | 1.721 | 1.723 | 1.738 | ALU 62 % | Texture Sample 31 % | 22.1 | 13.3 |
| exposure adapt | 151 | 0.012 | 0.012 | 0.013 | 0.012 | 0.012 | 0.013 | Texture Sample 1 % | ALU 1 % | 0.6 | 0.1 |
| bloom | 151 | 1.079 | 1.227 | 1.507 | 0.299 | 0.293 | 0.324 | ALU 39 % | Texture Sample 17 % | 5.9 | 0.9 |
| tone map | 151 | 0.128 | 0.127 | 0.132 | 0.122 | 0.122 | 0.126 | ALU 73 % | Texture Filtering 59 % | 30.7 | 2.6 |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.032 | ALU 29 % | Texture Write 22 % | 7.3 | 26.2 |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | ALU 17 % | Texture Filtering 11 % | 6.0 | 9.0 |
| frame span | 151 | 20.750 | 20.198 | 26.884 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.
