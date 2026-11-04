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


## Notes

**2026-11-04T16:36:43Z**

Acceptance runs, 2026-11-04 (time-round.sh under the GPU lock, base and control by tools/README.md, "FSR 3.1 timing").

time-round.sh now runs FSR's batched measurement, Fsr3UpscalerTests.AnUpscaleFrom1280x720To1920x1080ReportsItsGpuTimePerFrameWhenEightFramesShareASubmission (TERTIUS_FSR_TIMING=1), and analyze.py reads each batch's frame time from the run's log. The tolerance of 3 % did not change.

First try, with the measurement's old lengths (8 warm-up batches, 37 timed batches, 6 to 8 samples per span), 6 rounds in /private/tmp/q58r-fsr-timing: span clocks base/control 1264.1/1238.6, 1262.0/1292.6, 1140.0/1115.4, 1251.2/1146.0, 1292.5/1280.2, 1237.8/1285.1 MHz. Rounds 3 (9.2 %) and 5 (3.8 %) fell outside the tolerance. The clock still rose over the first 0.4 s of the span (run 5-base: 1094.8, 1197.1, 1250.6, 1288.8, 1296.0 MHz), because 8 batches warm up for only about 130 ms.

So the measurement now warms up for 60 batches (about 1 s) and times 150 batches (about 2.4 s, 24 to 25 samples). fsr-passes.py skips 60 warm-up buffers to match. 10 rounds in /private/tmp/q58r-fsr-timing-2, at a 1-minute load of 4.57 to 14.88 (median 6.57):

| Round | Base, MHz | Control, MHz | Difference |
|---|---|---|---|
| 0 | 1296.0 | 1296.0 | 0.0 % |
| 1 | 1296.0 | 1296.0 | 0.0 % |
| 2 | 1296.0 | 1296.0 | 0.0 % |
| 3 | 1295.9 | 1296.0 | 0.0 % |
| 4 | 1147.1 | 1294.1 | 12.8 % |
| 5 | 1295.8 | 1296.0 | 0.0 % |
| 6 | 1296.0 | 1296.0 | 0.0 % |
| 7 | 1296.0 | 1296.0 | 0.0 % |
| 8 | 1295.4 | 1148.7 | 12.8 % |
| 9 | 1296.0 | 1296.0 | 0.0 % |

8 of 10 rounds are within the tolerance. Rounds 4 and 8 are not. In each, one run held 1293 to 1296 MHz for about 1.1 s. Then the GPU's busy share fell to 0.67 to 0.93, and the clock fell to 791 to 1254 MHz and rose again over about 1.5 s. The batch times of that run rose with it (90th percentile 2.600 and 2.593 ms against about 2.03 ms). An Android emulator and a QEMU guest ran on the machine at the time, and they use the GPU outside the lock. So the cause is most likely another process on the GPU, not the measurement.

analyze.py /private/tmp/q58r-fsr-timing-2 base control:

    10 rounds, batches per run [150], frames per batch [8]
    base | pooled median 1.998 | median of run medians 1.996 | 1296.0 MHz, the median of 10 runs (p10 1295.4, p90 1296.0)
    control | pooled median 1.996 | median of run medians 1.996 | 1296.0 MHz, the median of 10 runs (p10 1294.1, p90 1296.0)
    control against base: 8 rounds at the same clock, 2 at different clocks, 0 unchecked.
    median: pooled frames -0.002 (-0.007 to +0.003), paired runs -0.002 (-0.006 to +0.001), 6/8 rounds faster
    p10: pooled frames -0.001 (-0.004 to +0.002), paired runs -0.000 (-0.005 to +0.001), 5/8 rounds faster

Every interval holds zero, so base and control do not differ.
