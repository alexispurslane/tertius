---
id: ter-q58r
status: open
deps: []
links: []
created: 2026-11-04T15:03:35Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, tools]
---
# Compare FSR builds at a held clock

Found in ter-qk78. FSR's timing test (Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTime) waits for each frame, so the GPU clock over its timed span moves from run to run: on 2026-11-04 four runs of one build and its byte copy ran at 886.6 to 1008.5 MHz. tools/fsr3-timing/analyze.py refuses a round whose two clocks differ by more than 3 %, so it refused both rounds of that comparison, and it will refuse nearly every round. The calibration report (docs/research/temporal-renderer/gpu-calibration.md) recommends FSR's batched measurement (GpuCalibrationTests), whose timed batches held 1241.0 to 1272.4 MHz over five runs, 2.5 % apart. Give the FSR comparison that held clock: for example, tools/fsr3-timing/time-round.sh runs the batched measurement, or a flag chooses it, and analyze.py reads its frame times. Then the comparison of two FSR builds has rounds within the tolerance.

