---
id: ter-q58r
status: in_progress
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


## Notes

**2026-11-04T16:14:45Z**

2026-11-04: started as a workflow, run bca67615-1e98-4d37-b9d5-e16e4c3859a4 (version 869b0b07), branch ter-q58r-fsr-held-clock from eca76cff2. Settled: the comparison uses only the batched measurement (no flag), the batched test prints the timed-span markers, analyze.py reads its times, the 3 % tolerance stays, and acceptance is three rounds of base against control within the tolerance.
