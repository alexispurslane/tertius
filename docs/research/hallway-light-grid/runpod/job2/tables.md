NVIDIA RTX 2000 Ada Generation, 1920x1080, 1190 frames per run, 5 runs per tree

| Tree | Median (ms) | p95 (ms) | Run medians (ms) | Change of median vs merged | Change of p95 | Separation |
|---|---:|---:|---|---:|---:|---|
| four | 6.674 | 6.823 | 6.642, 6.650, 6.674, 6.679, 6.683 | +0.387 ms (+6.2%) | +0.425 ms (+6.6%) | every run differs |
| legacy | 7.817 | 8.000 | 8.206, 7.731, 7.812, 7.827, 7.817 | +1.529 ms (+24.3%) | +1.602 ms (+25.0%) | every run differs |
| merged | 6.288 | 6.398 | 6.239, 6.286, 6.288, 6.306, 6.297 | — | — | — |
| outer | 6.425 | 6.575 | 6.391, 6.413, 6.425, 6.441, 6.445 | +0.137 ms (+2.2%) | +0.177 ms (+2.8%) | every run differs |

| Pass median (ms) | four | legacy | merged | outer |
|---|---:|---:|---:|---:|
| directional light volume | 0.960 | — | 0.719 | 0.737 |
| diffuse rays | — | 0.640 | — | — |
| accumulate indirect diffuse | — | 0.381 | — | — |
| filter indirect diffuse | — | 1.234 | — | — |
| ray plan | 0.557 | 0.479 | 0.503 | 0.542 |
| reflection rays | 0.701 | 0.679 | 0.683 | 0.703 |
| screen trace resolve | 0.123 | 0.179 | 0.122 | 0.123 |
| accumulate indirect specular | 0.639 | 0.657 | 0.634 | 0.640 |
| composite | 0.314 | 0.162 | 0.269 | 0.304 |
| FSR 3.1 | 1.008 | 1.028 | 1.003 | 1.006 |
| capture | 2.295 | 2.302 | 2.294 | 2.299 |

SM clock: mean 1973 MHz (210 to 2535); temperature 33 to 71 °C over 833 samples.
