---
id: ter-qk78
status: open
deps: []
links: []
created: 2026-11-04T01:46:03Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, performance, tools]
---
# Record the GPU clock beside every timed run, and compare builds only at the same clock

Found in ter-3ug8 (docs/research/temporal-renderer/gpu-calibration.md). The GPU's clock on the development Mac moves with the GPU's duty cycle: headless demo runs at 1280 x 720 run at a mean of 800 MHz, at 1920 x 1080 at about 1050 MHz, and the sustained clock is 1293 MHz. A pass whose time follows the clock takes a time inversely proportional to the clock. So two timed runs compare only at the same clock, and a timed run that does not record its clock cannot be converted to the clock at which section 11 of docs/plans/temporal-renderer.md states its budgets. Make the timing tools record the clock beside every timed run: tools/gpu-trace.sh, the headless demo's GPU timings, FSR's timing test and the spikes' rounds. tools/gpu-calibration/gpu-clock.c reads it through IOReport without root, on macOS only. A run on another driver records that its clock is unknown.

## Notes

**2026-11-04T03:25:10Z**

2026-11-03 (ter-3ug8's review fix): record the clock over the timed span, not over the whole run. The run's mean holds the setup, which runs at 648 MHz: FSR's batched measurement ran at 1208 MHz over the whole run and at 1257 MHz over its timed batches. The measurement prints the lines timed-start and timed-end around its timed work, and tools/gpu-calibration/analyze.py's span_clock averages gpu-clock's samples between them. Every timing tool takes the GPU lock through tools/gpu-lock.sh, the one helper of the lock, whose waiters take turns in the order in which they came.


**2026-11-04T14:34:10Z**

2026-11-04, the build: every timing tool records the GPU clock over its timed span (tools/README.md, "The GPU clock of a timed run").

- The timed work prints `timed-start` and `timed-end`: the headless demo (after its first shown frame, until the script's steps end), FSR's timing test, FSR's batched measurement, the Vulkan probes, `probes.swift` and `metalfx.swift`.
- `tools/gpu-clock.sh` is the one launcher of gpu-clock. Where gpu-clock cannot run, it prints `clock unknown: <reason>` and runs the work alone. gpu-clock itself does the same when IOReport gives no GPU clock.
- `tools/gpu_clock.py` owns the record (mean MHz, p10 and p90 of the samples, sample count, or the reason that the clock is unknown), the check of two clocks and the estimate at the windowed clock. `span_clock` moved there from `tools/gpu-calibration/analyze.py`, and averages over each span between a start and the next end.
- The tolerance is 3 %: five runs of one build of FSR's batched measurement ran at 1241.0 to 1272.4 MHz over their spans, 2.5 % apart, and their medians did not follow the clock.
- The tools: `tools/demo-timing.sh` (new) and `tools/gpu_clock.py compare` for the cost recipe, `tools/gpu-trace.sh` (now under the lock), `tools/fsr3-timing/time-round.sh` and `analyze.py` (rounds at different clocks leave the paired reading, and the pooled reading is marked), and `rounds.sh`, `upscalers.sh`, `fsr-trace.sh` and `window.sh` of `tools/gpu-calibration/`.
- Found: xctrace's list of encoders stays empty when a process sits between xctrace and the traced process. So gpu-trace.sh and fsr-trace.sh run gpu-clock outside xctrace, with `--target-stdout -`.

The real machine, under the lock: two FSR timing rounds of one build against a byte copy ran at 886.6, 913.8, 1008.5 and 954.8 MHz over their spans, and analyze.py refused both rounds. One timed demo run of the deck at 1280 x 720 ran at 1035.9 MHz (p10 743.0, p90 1294.0, 17 samples) over its span and 1055.1 MHz over the whole run. A short gpu-trace.sh run ran at 1253.2 MHz.

**2026-11-04T15:08:33Z**

2026-11-04, the review fix:

- The estimate at the windowed clock is for budget checks only. One build's estimates spread by about 8 % between runs at different clocks (FSR rounds of one build and its byte copy: 1.893 to 2.058 ms; ter-3ug8's seven deck runs: 6.81 to 7.34 ms). gpu_clock.ESTIMATE_SPREAD holds it. An INVALID line now says that the runs do not compare and must run again. tools/README.md, docs/plans/rendering-upgrade.md and docs/plans/temporal-renderer.md ('How each ticket measures its part') say the same. The epic's recipe now runs each run through tools/demo-timing.sh and leaves out the pairs at different clocks.
- One model of a clock: tools/gpu_clock.py's samples() and clock_of() serve the timed span and the window summary of tools/gpu-calibration/analyze.py, whose copy of the parser and the mean is gone. A clock is Known or Unknown, and a build's clock in tools/fsr3-timing/analyze.py is a BuildClock that says 'of the runs'.
- span_clock leaves out a span that no timed-end closes.
- gpu-trace.sh takes the load at the start from the lock's uptime-start line, after the wait. A real trace on 2026-11-04 waited 185 s, and passes.md gave the load of that line.
- fsr3-timing/analyze.py counts the unchecked rounds of each paired reading, and refuses a build whose checked rounds all ran at different clocks, even with unchecked rounds. tools/test_fsr3_timing.py tests it.
- tests/Tertius.Rendering.Tests/TimedSpan.cs is the one writer of the markers in that project.
- New ticket ter-q58r: compare FSR builds at a held clock, from FSR's batched measurement, because the timing test's clock moves and the tool refuses nearly every round.
