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

**2026-11-04T15:52:54Z**

2026-11-04, the hardening:

Coverage. The changed C# lines of src/ against main, in Debug and Release: 12/12 lines and 4/4 branches, all in src/Tertius.Host/ScriptCommand.cs. coverage.py is not in the dev shell, so the Python tools were measured with the standard library's `trace` module over tools/test_gpu_clock.py, test_fsr3_timing.py, test_gpu_trace.py and test_gpu_calibration.py (151 tests, then 152). The changed executable lines that no test ran:

- tools/gpu_clock.py `record()`: now tested by `Record.test_recording_a_log_writes_its_spans_clock_into_the_timings_and_keeps_the_runs_times`, red on a copy whose `record()` dropped the run's entries.
- tools/gpu_clock.py `main()`: the command line, which gpu-clock.sh, demo-timing.sh and gpu-trace.sh run in a subprocess, outside the trace. The build's real runs ran `span` and `record`.
- tools/fsr3-timing/analyze.py: no changed line left out.
- tools/gpu-calibration/analyze.py (the clock columns of the `upscalers` and `rounds` reports), tools/gpu-calibration/fsr-passes.py (the clock line of a trace) and tools/gpu-trace.py (`report()` and its argument parser): report code over the folders of real runs and xctrace exports, which no test reached before this ticket either. The clock that they print comes from `gpu_clock.span_clock`, `to_json` and `from_json`, which tools/test_gpu_clock.py tests.

Mutation. The branch changes no C# in Core, Simulation or Physics, so no Stryker run applies. tools/mutate.py runs only `dotnet test` on a project with a .csproj, so the Python tools have no mutation run, and their decision logic (the span's closing rule, the tolerance, the windowed estimate and the unknown case) stays tested by tools/test_gpu_clock.py alone. The Host list, the final run:

| id | place | change | result | killed by | s |
|---|---|---|---|---|---:|
| H01 | src/Tertius.Host/ScriptCommand.cs:431 | timed-end guard > 0 -> >= 0 | killed | ScriptCommandCaptureTests.AScriptWithDumpsOnlyPrintsNoMarkersOfATimedSpan | 17.3 |
| H02 | src/Tertius.Host/ScriptCommand.cs:431 | timed-end guard > 0 -> > 1 | killed | ScriptCommandCaptureTests.AFilmOfOneFrameStillPrintsBothMarkersSoThatEveryTimedStartHasItsEnd | 17.1 |
| H03 | src/Tertius.Host/ScriptCommand.cs:432 | no timed-end line | killed | ScriptCommandCaptureTests.AFilmOfOneFrameStillPrintsBothMarkersSoThatEveryTimedStartHasItsEnd, ScriptCommandCaptureTests.AHeadlessFilmPrintsTheMarkersOfItsTimedSpanOnceEachBeforeTheFilesAndTheGpuTimings | 16.2 |
| H04 | src/Tertius.Host/ScriptCommand.cs:501 | timed-start on every frame | killed | ScriptCommandCaptureTests.AHeadlessFilmPrintsTheMarkersOfItsTimedSpanOnceEachBeforeTheFilesAndTheGpuTimings | 17.5 |
| H05 | src/Tertius.Host/ScriptCommand.cs:501 | timed-start after the second frame | killed | ScriptCommandCaptureTests.AFilmOfOneFrameStillPrintsBothMarkersSoThatEveryTimedStartHasItsEnd | 16.9 |
| H06 | src/Tertius.Host/ScriptCommand.cs:502 | no timed-start line | killed | ScriptCommandCaptureTests.AFilmOfOneFrameStillPrintsBothMarkersSoThatEveryTimedStartHasItsEnd, ScriptCommandCaptureTests.AHeadlessFilmPrintsTheMarkersOfItsTimedSpanOnceEachBeforeTheFilesAndTheGpuTimings | 16.9 |
| H07 | src/Tertius.Host/ScriptCommand.cs:405 | playback writes timed-start nowhere | killed | ScriptCommandCaptureTests.AFilmOfOneFrameStillPrintsBothMarkersSoThatEveryTimedStartHasItsEnd, ScriptCommandCaptureTests.AHeadlessFilmPrintsTheMarkersOfItsTimedSpanOnceEachBeforeTheFilesAndTheGpuTimings | 16.7 |
| H08 | src/Tertius.Host/ScriptCommand.cs:81 | start marker text | survived |  | 19.1 |
| H09 | src/Tertius.Host/ScriptCommand.cs:84 | end marker text | survived |  | 19.1 |
9 rows: killed 7, timeout 0, survived 2, no-coverage 0, build-failed 0, no-tests 0, no-mutant 0, removed 0

The answers:

- H02 and H05 survived the first run: no test filmed a single frame. The new test `AFilmOfOneFrameStillPrintsBothMarkersSoThatEveryTimedStartHasItsEnd` kills both.
- H06 first deleted the line, which left `output` unused and failed the build (IDE0060). Its final form writes an empty string instead.
- H08 and H09, judgment call: the marker text is a contract with tools/gpu_clock.py's START and END, which no .NET test can read. A wrong text shows on the first timed run, because every tool then prints the clock as unknown with the reason that the log has no marker.
