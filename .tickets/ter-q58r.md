---
id: ter-q58r
status: open
deps: []
links: [ter-ty4s]
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

8 of 10 rounds are within the tolerance. Rounds 4 and 8 are not. In each, one run held 1293 to 1296 MHz for about 1.1 s. Then the GPU's busy share fell to 0.67 to 0.93, and the clock fell to 791 to 1254 MHz and rose again over about 1.5 s. The batch times of that run rose with it (90th percentile 2.600 and 2.593 ms against about 2.03 ms). (Corrected in the note below: the cause is not another process on the GPU. The busy share fell, so the measuring process stopped feeding the GPU.)

A summary of the output of analyze.py /private/tmp/q58r-fsr-timing-2 base control, not the verbatim output:

    10 rounds, batches per run [150], frames per batch [8]
    base | pooled median 1.998 | median of run medians 1.996 | 1296.0 MHz, the median of 10 runs (p10 1295.4, p90 1296.0)
    control | pooled median 1.996 | median of run medians 1.996 | 1296.0 MHz, the median of 10 runs (p10 1294.1, p90 1296.0)
    control against base: 8 rounds at the same clock, 2 at different clocks, 0 unchecked.
    median: pooled frames -0.002 (-0.007 to +0.003), paired runs -0.002 (-0.006 to +0.001), 6/8 rounds faster
    p10: pooled frames -0.001 (-0.004 to +0.002), paired runs -0.000 (-0.005 to +0.001), 5/8 rounds faster

Every interval holds zero, so base and control do not differ.

**2026-11-04T17:55:00Z**

Review fixes and new acceptance runs.

The cause of the refused rounds 4 and 8 above was wrong. gpu-clock's busy share is the share of time in which the GPU is not off (tools/gpu-calibration/gpu-clock.c), and work from another process can only raise it. Over the timed span, run 4-base had a mean busy share of 0.917 (lowest sample 0.709) and run 8-control 0.943 (lowest 0.668), against 0.996 to 0.999 in the other 18 runs. Their spans also took 29 to 30 samples against 24 to 25. So the measuring process stopped feeding the GPU, most likely because other processes took the CPU between two batches, and the governor then lowered the clock. Each batch went through record, submit and a fence wait in sequence, so the GPU idled for every stall of the CPU.

A rerun at a lower load did not remove it. 2 rounds in /private/tmp/q58r-fsr-timing-3 at a 1-minute load of 4.0 to 5.2: round 0 ran at 1291.2 (base) and 1233.6 MHz (control), 4.5 % apart. The control's busy share fell to a mean of 0.962 (lowest 0.895), and its clock to 1013.2 MHz. Round 1 ran at 1292.1 and 1291.4 MHz.

So the batched measurement now keeps two batches on the queue: Fsr3Bench.TimedUpscales takes batchesInFlight, and it records batch n+1 while the GPU runs batch n. The batched test passes 2, with 16 frames in flight. The per-frame test and the other callers pass 1, which waits for each batch as before. TimedUpscales refuses batchSize times batchesInFlight above the upscaler's frames in flight.

7 rounds in /private/tmp/q58r-fsr-timing-4, with fresh base and control copies (tools/README.md, "FSR 3.1 timing"), at a 1-minute load of 3.02 to 6.50 (median 4.13). Every run's busy share had a mean of 0.998 to 1.000 over its timed span.

| Round | Base, MHz | Control, MHz | Difference |
|---|---|---|---|
| 0 | 1295.9 | 1296.0 | 0.0 % |
| 1 | 1295.9 | 1295.9 | 0.0 % |
| 2 | 1295.9 | 1296.0 | 0.0 % |
| 3 | 1295.9 | 1295.9 | 0.0 % |
| 4 | 1296.0 | 1295.9 | 0.0 % |
| 5 | 1295.9 | 1295.9 | 0.0 % |
| 6 | 1295.9 | 1296.0 | 0.0 % |

Every round is within the tolerance of 3 %, which did not change. The verbatim output of analyze.py /private/tmp/q58r-fsr-timing-4 base control:

    7 rounds, batches per run [150], frames per batch [8]
    1-minute load at the run starts: least 3.02, median 4.13, most 6.5
    The clock of a build is the median of its runs' clocks over their timed spans (p10 and p90 of the runs). The estimate at the windowed clock, 1296 MHz, is the median of the run medians, each times its run's clock over 1296 MHz, within about 15 %, to hold a time against a budget. One build's estimates spread by about 8 %, so they do not compare two builds.
    build | pooled median | pooled p10 | pooled p90 | median of run medians | clock | median of run medians at the windowed clock, estimate
    base | 1.959 | 1.943 | 1.975 | 1.958 | 1295.9 MHz, the median of 7 runs (p10 1295.9, p90 1296.0 MHz of the runs) | 1.958
    control | 1.959 | 1.943 | 1.977 | 1.960 | 1295.9 MHz, the median of 7 runs (p10 1295.9, p90 1296.0 MHz of the runs) | 1.960

    control against base: 7 rounds at the same clock, 0 at different clocks, 0 unchecked. Pooled frames: Same clock: clocks 1295.9 MHz, the median of 7 runs (p10 1295.9, p90 1296.0 MHz of the runs) and 1295.9 MHz, the median of 7 runs (p10 1295.9, p90 1296.0 MHz of the runs) differ by 0.0 %, within the tolerance of 3 %.

    median: build minus base, middle and 90 % interval, ms
    build | pooled frames | paired runs at the same clock or unchecked | rounds faster | unchecked rounds in the paired reading
    control | +0.000 (-0.004 to +0.004) | +0.001 (-0.002 to +0.006) | 3/7 | 0 unchecked

    p10: build minus base, middle and 90 % interval, ms
    build | pooled frames | paired runs at the same clock or unchecked | rounds faster | unchecked rounds in the paired reading
    control | +0.000 (-0.005 to +0.003) | +0.001 (-0.002 to +0.006) | 2/7 | 0 unchecked

Every interval holds zero, so base and control do not differ. The batched median fell from about 1.996 ms to about 1.959 ms at the same clock of 1296 MHz with two batches in flight. A batch's span runs from its first stamp to its last, so the gap between batches is not in it. Why the span shrank is not established.

Checks of the trace tools with the new lengths: fsr-trace.sh into /private/tmp/q58r-fsr-trace-0 kept 1200 frames, exactly the 150 timed batches of 8, at a span clock of 1296.0 MHz (25 samples). fsr-passes.py now reads the count of warm-up batches from the measurement's line "warm-up batches: 60, timed batches: 150" in run.log. The shader-dump command of tools/README.md, "GPU calibration", ran under the lock and wrote 18 dumped shaders.

New ticket for work found along the way: ter-ty4s, the GPU lock vanished while a run of gpu-lock.sh held it.
