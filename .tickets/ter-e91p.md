---
id: ter-e91p
status: open
deps: [ter-9hie]
links: []
created: 2026-10-31T21:34:03Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: a paired mode for frames that alternate with and without a pass

Found by ter-0y0z, 2026-10-31. The paired-trace analysis of ter-bgpv was rebuilt as temporary scripts in ter-0y0z (/tmp/ter-0y0z/tools/paired.py, split.py and window.py). Each rebuild can choose a different statistic, and ter-0y0z's first doc compared the window with a haze column that held an interval outside the window. This is the second real use, so the analysis goes into the tool. The renderer's probe that drops a pass on every other frame stays out of the commits.

## Acceptance Criteria

tools/gpu-trace.py has a paired mode. It marks each frame by whether a named pass has GPU work, pairs each frame without it with the mean of its two neighbors with it, and reports per pass the paired medians of the busy time and of the time alone, with bootstrap standard errors of the median. It reports a window statistic between two named passes, and a control mode that pairs odd frames with their even neighbors. tools/README.md gives the method.


## Notes

**2026-11-01T00:49:55Z**

Settled choices (builder, 2026-10-31).

1. Interface: report takes --paired PASS or --control PASS, never both (an argparse group that excludes the other). The control takes the pass's name too, so it pairs only frames that all run the pass. A frame without GPU work of the pass pairs with none in the control, and the output names it as left out. So the control cannot hold a dropped pass by mistake. gpu-trace.sh forwards both options to the report, as it does --window, so the common path keeps the load line (ter-9hie's review finding 2).
2. One model for the two modes. Each frame gets a mark: True for the "with" side, False for the "without" side, None for a frame that no pair can use. pairs(marks) pairs each False frame whose two neighbors are True. The two modes differ only in the marks. With --paired, the mark is whether the pass has GPU work. With --control, it is True for an even frame, False for an odd frame, and None where the pass has no GPU work.
3. Pure functions beside summarize: frames_by_buffer (the grouping that summarize already had, now shared), bootstrap_error, pairs, paired_median and paired. paired_table formats the second table of passes.md. report() only validates the names, reads, calls and writes.
4. The frames are ordered by their first GPU start and counted from 0. Each pair gives the mean of the two neighbors minus the frame, so a positive value is time that the pass adds (ter-0y0z's sign). The paired median is the median of these differences, never a difference of medians. A test locks this with data in which the two differ.
5. A pass with no GPU work in a frame takes 0 ms there. So the row of the dropped pass gives its own busy time and time alone.
6. Rows: every pass with GPU work in any frame, in the order of summarize. Then the frame span, and with --window the window. A window pair needs a window in all three frames. The other pairs are named on a line of their own.
7. Bootstrap: RESAMPLES = 2000 and SEED = 1, module constants, stated in the text above the table and in passes.json ("resamples", "seed"). bootstrap_error uses its own random.Random(seed), not the global generator. It draws as random.seed(1) and random.choices did in ter-0y0z's scripts, so the errors agree exactly.
8. The table prints signed medians and their errors with four decimals, because the control's noise is about 0.001 ms. The column headers name the quantity and the unit: "Busy paired median (ms)", "Busy standard error (ms)", "Alone paired median (ms)", "Alone standard error (ms)".
9. No C# changed, so the dotnet build and test runs do not apply. The required commands: nix develop --command python3 -I tools/test_gpu_trace.py (56 tests, OK), gpu-trace.py --help and report --help work. The tests also pass on the Mac's /usr/bin/python3 3.9.

**2026-11-01T00:49:55Z**

Reproduction on ter-0y0z's kept exports (builder, 2026-10-31). No new trace ran. The reports used /tmp/ter-0y0z/traces/{above-alt,day-alt,above-control}, with their export tables linked into new folders under /tmp/e91p. ter-0y0z's paired.py and window.py ran on the same exports, with their gpu-trace.py path pointed at this branch's file.

Command: python3 -I tools/gpu-trace.py report <dir>/run.trace <dir>/run/gpu-timings.json <out> --window reflections bloom --paired haze (--control haze for above-control).

Paired median in ms, with its standard error in ms in brackets. ter-0y0z's paired.py and window.py give the same values to the four printed decimals in every row below.

| Row | above-alt, 100 pairs | day-alt, 100 pairs | above-control, 99 pairs |
|---|---|---|---|
| haze | +0.1622 (0.0033) | +0.1856 (0.0017) | -0.0001 (0.0010) |
| exposure | +0.1016 (0.0067) | +0.1144 (0.0049) | -0.0005 (0.0014) |
| bloom | +0.0068 (0.0086) | -0.0083 (0.0060) | -0.0101 (0.0062) |
| frame span | +0.2193 (0.0306) | +0.1719 (0.0147) | -0.0052 (0.0717) |
| reflections end to bloom start | +0.1634 (0.0049) | +0.1725 (0.0029) | +0.0077 (0.0027) |

The alternating traces leave out no frame. The control leaves out frame 199, the last odd frame, which has no right neighbor. ter-0y0z's notes give the same numbers, rounded: above exposure +0.102 (0.007), window +0.163 (0.005), frame span +0.219 (0.031). Day exposure +0.114 (0.005), window +0.173 (0.003). Control window +0.008 (0.003).

The report without --paired on above-alt gives a passes.json and a passes.md that are byte-identical to those of the base 74292a18, so the shared frames_by_buffer changes nothing.

ter-qsye's traces (/private/tmp/qsye-traces) have no kept exports, and no analysis script lies beside them. A comparison would need new xctrace exports while ter-qsye still runs, and it would have no script output to compare with. So the reproduction used ter-0y0z's exports only.

**2026-11-01T01:02:44Z**

Review fixes (fixer, 2026-10-31). Each finding of the review and its resolution:

1. Pair shape (design, medium). pairs now returns (before, frame, after) triples. paired_median skips a triple in which a frame has no value (None), and returns the middle frame of each skipped triple as its own "leftOut". So the rule for missing values lives in the pairing method, and the window's own filter is gone. The pass rows keep the 0 ms rule, because they pass 0, not None. Tests: PairedMedian (red first: TypeError on the old shape, then a list of ints where triples were expected).
2. Two computations of the per-frame quantities (design, low). A new pure function per_frame computes, once, the frame order, the (busy, alone) of each row in each frame in ns, the frame spans and the windows. summarize and paired both reduce it, so one rule decides the rows: a row for each pass with an interval in some frame. paired previously kept only a pass with busy > 0. Test: OneRowRule (red first: a pass whose intervals all have zero length had a row in summarize only). The report without --paired on above-alt stays byte-identical to the base 74292a18 (passes.md and passes.json). The frame set of summarize is now that of per_frame: the command buffers in which a pass of the frame graph has GPU work. Encoders before the first pass or after the last pass in a command buffer with no pass work no longer count. No real trace has such a buffer, as the byte-identical output shows.
3. README window decomposition (doc, low). The stale text was "the haze pass's work inside the window, 0.145 ms, plus 0.016 ms". I removed it, because the tool reports neither number and medians do not add.
4. Left-out wording (doc, low). The stale text was "A line below the table names the frames that no pair holds ... Two kinds of frames have no pair". The README now says that the line names each frame without the pass that makes no pair, that a frame with the pass is never named, and what the control's line names. pairs' and paired's docstrings say the same. The control's line has its own text: "Left out, an odd frame without two even neighbors that run PASS, or a frame without GPU work of PASS".
5. No left-out line without a pair (fulfillment, low). paired_table now writes the left-out line in both cases. A line with more than LISTED = 10 frames gives the count, the first 10 frames, and "(passes.json lists all)". Tests: PairedText (red first).
6. Standard errors too small (correctness, low). The text above the table and the README now say that neighboring pairs share a frame, so the standard errors are somewhat too small (about 1.15 times under independent frame noise), and to read them beside the control's medians. The moving-block bootstrap is ticket ter-2fri, because it changes the numbers so that they no longer match ter-0y0z. Test: PairedText (red first).
7. Shell guard (correctness, low). gpu-trace.sh stops at a second --paired or --control with "Give one --paired or one --control, not both and not twice." and exit 2, before any recording. I ran it with --paired haze --control haze: exit 2 with that message.

Review notes also addressed:
- The parity: paired returns "parity" ("odd", "even" or "odd and even") of the paired frames, and the paired text says "The frames without PASS are the odd frames." Test: PairedText.
- report() names each unknown pass with its option, one line each ("--window y: y is not a pass of the run."), then the run's passes. Test: ReportOptions, run as a process.
- paired's between has a type annotation.
- Gaps on the wiring: ReportOptions tests the argparse group that excludes --paired with --control (it passed at once, because the group existed: a lock-in test) and the option checks of report(). The reproduction below covers report()'s wiring of --paired and --control end to end.

Reproduction after the fixes, on ter-0y0z's kept exports (above-alt, day-alt, above-control, copied to /tmp/e91p-fix): every paired number in passes.json equals the earlier run of 1f348d14 in /tmp/e91p, apart from the new keys "leftOut" on each paired_median and "parity". above-alt and day-alt: 100 pairs, nothing left out, parity odd. above-control: 99 pairs, frame 199 left out.
