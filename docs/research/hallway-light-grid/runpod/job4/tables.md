NVIDIA RTX 2000 Ada Generation, 1920x1080, 1190 frames per run, 4 runs per tree

| Tree | Median (ms) | p95 (ms) | Run medians (ms) | Change of median vs merged | Change of p95 | Separation |
|---|---:|---:|---|---:|---:|---|
| four | 6.671 | 6.819 | 6.646, 6.660, 6.682, 6.685 | +0.377 ms (+6.0%) | +0.420 ms (+6.6%) | every run differs |
| legacy | 7.906 | 8.090 | 8.257, 7.873, 7.888, 7.923 | +1.612 ms (+25.6%) | +1.692 ms (+26.5%) | every run differs |
| merged | 6.294 | 6.398 | 6.240, 6.294, 6.294, 6.310 | — | — | — |
| outer | 6.426 | 6.570 | 6.386, 6.422, 6.435, 6.430 | +0.132 ms (+2.1%) | +0.172 ms (+2.7%) | every run differs |
| tiles | 6.666 | 6.792 | 6.648, 6.653, 6.681, 6.679 | +0.372 ms (+5.9%) | +0.394 ms (+6.2%) | every run differs |

| Pass median (ms) | four | legacy | merged | outer | tiles |
|---|---:|---:|---:|---:|---:|
| directional light volume | 0.960 | — | 0.720 | 0.737 | 0.963 |
| diffuse rays | — | 0.641 | — | — | — |
| accumulate indirect diffuse | — | 0.381 | — | — | — |
| filter indirect diffuse | — | 1.234 | — | — | — |
| ray plan | 0.557 | 0.479 | 0.504 | 0.542 | 0.556 |
| reflection rays | 0.700 | 0.681 | 0.683 | 0.703 | 0.698 |
| screen trace resolve | 0.123 | 0.179 | 0.122 | 0.123 | 0.126 |
| accumulate indirect specular | 0.639 | 0.658 | 0.635 | 0.639 | 0.636 |
| composite | 0.314 | 0.162 | 0.269 | 0.304 | 0.314 |
| FSR 3.1 | 1.007 | 1.029 | 1.004 | 1.006 | 1.007 |
| capture | 2.303 | 2.306 | 2.307 | 2.306 | 2.304 |

SM clock: mean 1989 MHz (210 to 2535); temperature 32 to 72 °C over 836 samples.
