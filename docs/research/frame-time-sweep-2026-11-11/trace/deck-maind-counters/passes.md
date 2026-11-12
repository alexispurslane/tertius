Apple M1 Max, 1920 x 1080: 151 frames, 0 other command buffers.
GPU clock over the run's timed span: 1291.3 MHz (p10 1290.5, p90 1292.3 MHz, 31 samples)
Load averages when the recording started, after the wait for the GPU lock (1, 5 and 15 minutes): 14.70, 8.62, 7.56
The busy time of a pass is the GPU time of its encoders, in milliseconds. Read each pass's own time here: on MoltenVK the pass times of gpu-timings.json hold only for the span and for sums of neighbouring passes (tools/README.md, "GPU trace").

The time alone is the part of the busy time in which no other encoder that the table counts runs, in milliseconds: no other pass, no encoder before the first pass or after the last pass, in any frame. The timestamp encoders and the other command buffers do not count. The busy time minus the time alone is the time that the pass shares with other passes, and passes.json gives its summary as "shared".

The frame span row and the window row give a wall-clock GPU time per frame in the busy columns, not a busy time.

| Pass | Frames | Busy mean (ms) | Busy median (ms) | Busy p95 (ms) | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) | Top limiter | Second limiter | Read GB/s | Write GB/s |
|---|---|---|---|---|---|---|---|---|---|---|---|
| (before the first pass) | 151 | 0.009 | 0.007 | 0.008 | 0.009 | 0.007 | 0.008 | Buffer Read 2 % | Buffer Write 2 % | 1.2 | 0.9 |
| shadow maps | 151 | 0.171 | 0.176 | 0.184 | 0.164 | 0.176 | 0.184 | GPU Last Level Cache 7 % | MMU 6 % | 8.1 | 9.4 |
| local shadows | 119 | 0.458 | 0.417 | 0.444 | 0.381 | 0.325 | 0.430 | ALU 13 % | GPU Last Level Cache 7 % | 11.8 | 5.3 |
| environment faces | 151 | 0.569 | 0.455 | 0.972 | 0.501 | 0.362 | 0.887 | ALU 12 % | GPU Last Level Cache 4 % | 5.0 | 1.9 |
| environment prefilter | 151 | 0.328 | 0.324 | 0.327 | 0.328 | 0.324 | 0.327 | ALU 10 % | Texture Sample 2 % | 1.7 | 0.3 |
| G-buffer | 151 | 0.197 | 0.198 | 0.206 | 0.151 | 0.149 | 0.163 | Texture Write 42 % | ALU 23 % | 11.3 | 28.1 |
| surface test | 151 | 0.226 | 0.227 | 0.231 | 0.226 | 0.227 | 0.231 | ALU 43 % | GPU Last Level Cache 22 % | 25.1 | 21.0 |
| depth pyramid | 151 | 0.267 | 0.266 | 0.269 | 0.267 | 0.266 | 0.269 | ALU 62 % | Texture Sample 16 % | 7.8 | 5.1 |
| direct light | 151 | 4.096 | 4.079 | 4.161 | 4.050 | 4.038 | 4.129 | ALU 40 % | Texture Sample 5 % | 1.1 | 1.4 |
| light pyramid | 151 | 0.109 | 0.107 | 0.121 | 0.109 | 0.107 | 0.121 | Texture Sample 22 % | Texture Filtering 17 % | 31.0 | 8.8 |
| ray plan | 151 | 0.897 | 0.898 | 0.912 | 0.897 | 0.898 | 0.912 | ALU 30 % | Texture Sample 15 % | 15.6 | 2.6 |
| reflection rays | 151 | 3.031 | 2.643 | 9.361 | 3.030 | 2.643 | 9.361 | ALU 42 % | GPU Last Level Cache 15 % | 33.8 | 0.9 |
| diffuse rays | 151 | 1.641 | 1.407 | 5.768 | 1.641 | 1.407 | 5.768 | ALU 60 % | GPU Last Level Cache 21 % | 20.2 | 0.8 |
| screen trace resolve | 151 | 0.225 | 0.219 | 0.328 | 0.225 | 0.219 | 0.328 | ALU 44 % | Texture Sample 17 % | 20.2 | 17.1 |
| accumulate indirect diffuse | 151 | 0.516 | 0.517 | 0.531 | 0.516 | 0.517 | 0.531 | ALU 41 % | GPU Last Level Cache 33 % | 30.3 | 9.9 |
| filter indirect diffuse | 151 | 4.182 | 4.153 | 4.207 | 4.182 | 4.153 | 4.207 | ALU 83 % | Texture Sample 52 % | 11.7 | 3.0 |
| accumulate indirect specular | 151 | 1.394 | 1.402 | 1.425 | 1.394 | 1.402 | 1.425 | ALU 39 % | GPU Last Level Cache 25 % | 21.5 | 6.5 |
| composite | 151 | 0.166 | 0.166 | 0.176 | 0.166 | 0.166 | 0.176 | Texture Sample 52 % | Texture Cache 46 % | 58.2 | 20.1 |
| scatter rows | 151 | 0.033 | 0.033 | 0.035 | 0.033 | 0.033 | 0.034 | MMU 18 % | GPU Last Level Cache 16 % | 25.0 | 23.4 |
| scatter columns | 151 | 0.017 | 0.017 | 0.018 | 0.017 | 0.017 | 0.018 | MMU 10 % | GPU Last Level Cache 8 % | 23.4 | 0.2 |
| transparent | 151 | 2.062 | 2.061 | 2.306 | 0.354 | 0.264 | 0.903 | ALU 26 % | GPU Last Level Cache 4 % | 1.2 | 0.7 |
| FSR 3.1 | 151 | 3.064 | 2.999 | 3.579 | 1.752 | 1.752 | 1.779 | ALU 47 % | Texture Sample 18 % | 11.4 | 6.9 |
| bloom | 151 | 1.032 | 1.040 | 1.253 | 0.291 | 0.287 | 0.308 | ALU 31 % | Texture Sample 15 % | 5.2 | 0.5 |
| tone map | 151 | 0.127 | 0.126 | 0.131 | 0.123 | 0.123 | 0.126 | ALU 72 % | Texture Filtering 60 % | 30.6 | 2.7 |
| capture | 119 | 0.032 | 0.032 | 0.033 | 0.030 | 0.030 | 0.031 | ALU 28 % | Texture Write 22 % | 6.5 | 27.2 |
| (after the last pass) | 151 | 0.004 | 0.004 | 0.004 | 0.002 | 0.002 | 0.002 | ALU 22 % | Texture Write 18 % | 5.0 | 22.8 |
| frame span | 151 | 24.927 | 24.249 | 34.271 | | | | | | | |

Passes with no GPU work in any frame: deformation, haze, outlines.
