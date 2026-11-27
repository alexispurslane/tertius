NVIDIA RTX 2000 Ada Generation, 1920x1080, 1190 frames per run, 4 runs per tree

| Tree | Median (ms) | p95 (ms) | Run medians (ms) | Change of median vs merged | Change of p95 | Separation |
|---|---:|---:|---|---:|---:|---|
| four | 6.668 | 6.817 | 6.643, 6.653, 6.687, 6.682 | +0.371 ms (+5.9%) | +0.412 ms (+6.4%) | every run differs |
| legacy | 7.799 | 7.977 | 7.627, 7.782, 7.822, 7.816 | +1.502 ms (+23.9%) | +1.572 ms (+24.5%) | every run differs |
| merged | 6.297 | 6.405 | 6.592, 6.279, 6.295, 6.299 | — | — | — |
| outer | 6.423 | 6.567 | 6.392, 6.409, 6.439, 6.436 | +0.126 ms (+2.0%) | +0.162 ms (+2.5%) | runs overlap |

| Pass median (ms) | four | legacy | merged | outer |
|---|---:|---:|---:|---:|
| directional light volume | 0.960 | — | 0.719 | 0.737 |
| diffuse rays | — | 0.639 | — | — |
| accumulate indirect diffuse | — | 0.380 | — | — |
| filter indirect diffuse | — | 1.230 | — | — |
| ray plan | 0.557 | 0.478 | 0.504 | 0.542 |
| reflection rays | 0.700 | 0.678 | 0.684 | 0.703 |
| screen trace resolve | 0.123 | 0.179 | 0.122 | 0.123 |
| accumulate indirect specular | 0.638 | 0.655 | 0.636 | 0.639 |
| composite | 0.314 | 0.162 | 0.269 | 0.304 |
| FSR 3.1 | 1.005 | 1.024 | 1.005 | 1.004 |
| capture | 2.299 | 2.302 | 2.305 | 2.302 |

SM clock: mean 1897 MHz (210 to 2535); temperature 33 to 72 °C over 711 samples.
