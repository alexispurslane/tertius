---
id: ter-9hie
status: open
deps: []
links: []
created: 2026-10-31T21:34:03Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: show each pass's time alone beside its busy time

Found by ter-0y0z, 2026-10-31. tools/gpu-trace.py reports a pass's busy time as the union of its own encoders' intervals (line 254). An interval that a pass shares with another pass counts in the busy time of both, and passes.md has no column that shows it. On ter-0y0z's above-alt export of mars-air, these are the medians of the time per frame that also lies in another pass: G-buffer 0.177 of 0.479 ms, bloom 0.107 of 0.571 ms, haze 0.141 of 0.164 ms, direct light 0.052 ms, and each SMAA pass about 0.015 ms. This class of misreading gave ter-6vhj its unexplained exposure row and cost ter-0y0z nine traces.

## Acceptance Criteria

passes.md has a column with each pass's time alone: the segments where open_segments (tools/gpu-trace.py) names only that pass. tools/README.md, 'GPU trace', says that the busy time minus the time alone is the time that the pass shares with other passes. A test or a fixture export makes sure that two overlapping passes give the expected time alone.


## Notes

**2026-10-31T21:36:09Z**

2026-10-31: the orchestrator had filed the same finding on main as ter-v4gw while ter-0y0z's fixer filed this ticket on its branch. ter-v4gw is removed in ter-0y0z's merge, because this ticket has the epic, the acceptance criteria and ter-e91p's dependency. Its one extra point: also report the frame's wall time between named passes, so that a with/without table can read the cost that the frame pays (ter-0y0z's 'window', from the end of the reflections to the start of the bloom).

**2026-10-31T23:30:04Z**

Settled choices (builder, 2026-10-31).

1. The time alone of a pass in a frame is the part of its busy time in which no other key of the report's intervals is open. The keys are (command buffer, pass) pairs, so the encoders before the first pass and after the last pass count as other work, and so does the same pass in an overlapping frame. busy_and_alone (tools/gpu-trace.py) computes both times from open_segments, which now takes any key. counter_means passes it the intervals grouped by pass name, as before. Proof that the refactor changes nothing: on ter-0y0z's above-alt export, the old and the new open_segments give the same 30,668 cuts and segments, and the report before the new columns gave byte-identical passes.md and passes.json. That export has no counters, so the segment comparison is the proof for the counter path.
2. passes.md gives the time alone with the same three statistics as the busy time: Alone mean (ms), Alone median (ms) and Alone p95 (ms). passes.json gives each pass an "alone" summary with frames, meanMs, medianMs and p95Ms. The busy columns are unchanged.
3. The window is one optional argument, --window FROM TO, on report. Why: ter-e91p's paired mode needs one statistic per frame between two named passes, and the pure function window(spans, first, second) gives exactly that for one frame's intervals, or None. The paired mode can take the same argument and call the same function on each frame. A repeatable flag or a list of pairs has no caller yet. The window ends at the first interval of TO that starts at or after FROM's last end. On above-alt, that definition and ter-0y0z's window.py (which used a strict "after") agree in all 201 frames. A frame without FROM or TO, or whose TO starts only before FROM ends, has no window, and a line below the table counts those frames.
4. gpu-trace.sh passes no --window. tools/README.md says how to run the report again on a recorded trace's folder with it. Adding the flag to gpu-trace.sh waits for a caller.
5. Tests: tools/test_gpu_trace.py, unittest from the standard library, loads gpu-trace.py by its path. Run it with nix develop --command python3 -I tools/test_gpu_trace.py. Discovery also works: python3 -I -m unittest discover -s tools -p 'test_*.py'.

**2026-10-31T23:30:04Z**

The new table on ter-0y0z's above-alt export of mars-air (builder, 2026-10-31): /tmp/ter-0y0z/traces/above-alt, 201 frames, the haze in every other frame, no counters, so the limiter columns are left out here. Command: python3 -I tools/gpu-trace.py report <dir>/run.trace <dir>/run/gpu-timings.json <out> --window reflections bloom.

The ticket's numbers are medians of the shared time per frame, the busy time minus the time alone. A temporary script computed them from busy_and_alone: G-buffer 0.177, bloom 0.107, haze 0.141, direct light 0.052, SMAA edges 0.015, SMAA weights 0.017, SMAA blending 0.017 ms. They match the description. The medians of the table do not subtract to these values, because each median comes from its own frame. The means subtract exactly: for example, G-buffer 0.478 - 0.296 = 0.182 ms mean shared time.

The Mean, Median and p95 columns are the busy time in ms. The last row is the window from the end of the reflections to the start of the bloom, in ms, over all 201 frames, with and without the haze.

| Pass | Frames | Mean | Median | p95 | Alone mean (ms) | Alone median (ms) | Alone p95 (ms) |
|---|---|---|---|---|---|---|---|
| (before the first pass) | 201 | 0.016 | 0.008 | 0.011 | 0.014 | 0.008 | 0.011 |
| environment faces | 201 | 0.097 | 0.032 | 0.223 | 0.097 | 0.032 | 0.223 |
| environment prefilter | 201 | 0.355 | 0.350 | 0.413 | 0.355 | 0.350 | 0.413 |
| G-buffer | 201 | 0.478 | 0.479 | 0.561 | 0.296 | 0.287 | 0.456 |
| depth pyramid | 201 | 0.199 | 0.197 | 0.231 | 0.176 | 0.169 | 0.231 |
| direct light | 201 | 1.513 | 1.517 | 1.846 | 1.368 | 1.413 | 1.759 |
| occlusion depth | 201 | 0.148 | 0.145 | 0.181 | 0.143 | 0.143 | 0.181 |
| occlusion sectors | 201 | 0.290 | 0.283 | 0.338 | 0.279 | 0.279 | 0.338 |
| occlusion denoise | 201 | 0.157 | 0.156 | 0.188 | 0.154 | 0.152 | 0.188 |
| occlusion upsample | 201 | 0.263 | 0.261 | 0.317 | 0.258 | 0.259 | 0.317 |
| ambient light | 201 | 0.543 | 0.542 | 0.636 | 0.518 | 0.525 | 0.615 |
| scatter rows | 201 | 0.085 | 0.085 | 0.097 | 0.084 | 0.084 | 0.096 |
| scatter columns | 201 | 0.049 | 0.048 | 0.056 | 0.048 | 0.048 | 0.056 |
| radiance pyramid | 201 | 0.795 | 0.784 | 0.918 | 0.788 | 0.783 | 0.918 |
| reflection trace | 201 | 0.148 | 0.149 | 0.173 | 0.147 | 0.149 | 0.173 |
| reflections | 201 | 0.567 | 0.566 | 0.682 | 0.564 | 0.565 | 0.682 |
| haze | 101 | 0.173 | 0.164 | 0.190 | 0.034 | 0.008 | 0.136 |
| exposure | 201 | 0.121 | 0.078 | 0.215 | 0.064 | 0.063 | 0.084 |
| bloom | 201 | 0.599 | 0.571 | 0.842 | 0.441 | 0.433 | 0.540 |
| tone map | 201 | 0.228 | 0.202 | 0.278 | 0.190 | 0.190 | 0.234 |
| SMAA edges | 201 | 0.089 | 0.075 | 0.118 | 0.055 | 0.054 | 0.073 |
| SMAA weights | 201 | 0.106 | 0.094 | 0.148 | 0.071 | 0.071 | 0.087 |
| SMAA blending | 201 | 0.126 | 0.088 | 0.154 | 0.070 | 0.068 | 0.089 |
| capture | 200 | 0.048 | 0.048 | 0.057 | 0.048 | 0.048 | 0.057 |
| (after the last pass) | 201 | 0.007 | 0.007 | 0.008 | 0.003 | 0.003 | 0.003 |
| frame span | 201 | 8.322 | 8.428 | 11.032 | | | |
| reflections end to bloom start | 201 | 0.271 | 0.275 | 0.416 | | | |

**2026-10-31T23:42:47Z**

Review fixes (fixer, 2026-10-31). Each finding of the review and how it is resolved:

1. README told the reader to compare the window of separate runs ("Compare the window of runs with and without a pass to read what the pass costs the frame."). Replaced: compare frames with and without the pass in one trace whose frames alternate (ter-bgpv's method, which ter-e91p automates). Separate runs drift by about 13 % under load (ter-0y0z), so two runs resolve only a larger cost.
2. The window was reachable only through a rerun that drops the load line. Settled choice 4 is replaced: gpu-trace.sh now takes --window FROM TO and forwards it to the report, so the common path gives the window with the load line. The rerun recipe stays for traces recorded before, and writes to a folder of its own (<trace folder>/window), so the first passes.md keeps its load line. The README says that the new passes.md has no load line and that the export stays in the new folder. A short --window (one name) stops the script with "--window takes two pass names".
3. "No other encoder of the process runs" was false for the timestamp encoders and the skipped command buffers. passes.md and the README now say: no other encoder that the table counts (the passes, the encoders before the first and after the last pass, in any frame). The timestamp encoders and the other command buffers do not count.
4. open_segments rebuilt each segment from every key seen so far. It now deletes a key whose count returns to 0 and builds the segment from the open keys. The reviewer's recipe (26 passes per frame), timed: 200 frames 0.45 s before and 0.01 s after, 1,000 frames 10.91 s before and 0.06 s after, with identical results. The report on above-alt takes 3.4 s instead of 4.7 s.
5. The shared time had no summary in the output. passes.json now gives each pass a "shared" summary: the busy time minus the time alone in each frame, with frames, meanMs, medianMs and p95Ms. On above-alt it gives the ticket's medians directly: G-buffer 0.177, bloom 0.107, haze 0.141, direct light 0.052, SMAA edges 0.015, SMAA weights 0.017, SMAA blending 0.017 ms. The README example now quotes the table's means (haze 0.173 - 0.034 = 0.139 ms mean shared) and the shared median from passes.json (0.141 ms). passes.md keeps its twelve columns, because it is read on a phone.
6. The interval stock was named windows beside the new window(). It is now intervals in every function of gpu-trace.py. gpu_intervals's parameter for the exported table, which was named intervals, is now table.
7. Two groupings of one frame's data. summarize builds only the frame mapping {pass: intervals}, and the pure frame_span(frame) and window(frame, first, second) both read it.
8. report() mixed computation with formatting and its wiring had no test. It is split: summarize(intervals, passes, counters, between) computes the passes.json statistics, table(report, has_counters) formats passes.md, and report() only exports, reads and writes. New tests on synthetic frames cover the alone and shared summaries, the row order, the idle passes, the frame span without the encoders before the first pass, the window and its missing count, the column headers, the pass row, the window row and the missing line.
9. The busy columns did not name their quantity. They are now "Busy mean (ms)", "Busy median (ms)" and "Busy p95 (ms)". A line above the table says that the frame span row and the window row give a wall-clock GPU time in the busy columns. No code parses passes.md.

Proof on ter-0y0z's above-alt export: passes.json before and after the fixes are equal once the new "shared" entries are removed, and passes.md differs only in its header lines. window() now has a string annotation for its "int | None" result, so gpu-trace.py --help and the tests also run on the Mac's /usr/bin/python3 3.9.
