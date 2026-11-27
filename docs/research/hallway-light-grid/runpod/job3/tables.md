NVIDIA RTX 2000 Ada Generation, 1920x1080, 1190 frames per run, 5 runs per tree

| Tree | Median (ms) | p95 (ms) | Run medians (ms) | Change of median vs merged | Change of p95 | Separation |
|---|---:|---:|---|---:|---:|---|
| four | 6.767 | 6.919 | 6.742, 6.764, 6.767, 6.769, 6.769 | +0.391 ms (+6.1%) | +0.432 ms (+6.7%) | every run differs |
| merged | 6.376 | 6.487 | 6.344, 6.374, 6.376, 6.378, 6.379 | — | — | — |
| tiles | 6.764 | 6.891 | 6.751, 6.757, 6.764, 6.764, 6.765 | +0.387 ms (+6.1%) | +0.405 ms (+6.2%) | every run differs |

| Pass median (ms) | four | merged | tiles |
|---|---:|---:|---:|
| directional light volume | 0.979 | 0.734 | 0.982 |
| diffuse rays | — | — | — |
| accumulate indirect diffuse | — | — | — |
| filter indirect diffuse | — | — | — |
| ray plan | 0.564 | 0.509 | 0.564 |
| reflection rays | 0.713 | 0.695 | 0.711 |
| screen trace resolve | 0.124 | 0.123 | 0.127 |
| accumulate indirect specular | 0.642 | 0.637 | 0.639 |
| composite | 0.319 | 0.273 | 0.319 |
| FSR 3.1 | 1.012 | 1.008 | 1.013 |
| capture | 2.299 | 2.301 | 2.301 |

SM clock: mean 2078 MHz (210 to 2475); temperature 32 to 71 °C over 589 samples.
