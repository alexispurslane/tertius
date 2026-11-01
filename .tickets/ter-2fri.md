---
id: ter-2fri
status: open
deps: []
links: []
created: 2026-11-01T01:02:23Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-pq5y
tags: [rendering, performance, tools]
---
# gpu-trace: a block bootstrap for the paired mode's standard errors

Found by the review of ter-e91p, 2026-10-31. The paired mode of tools/gpu-trace.py takes a bootstrap of single pair differences. Neighboring pairs share a frame: in an alternating trace, the pairs at frames 1 and 3 both use frame 2. If the noise of each frame is independent, neighboring differences correlate by 1/6, and the real standard error of their mean is about sqrt(4/3), 1.15 times what the independent bootstrap gives. ter-0y0z's control window median, +0.0077 ms with a standard error of 0.0027 ms, lies 2.9 standard errors from 0, which fits an error that is too small. ter-e91p only states this in passes.md and tools/README.md. A moving-block bootstrap over the pairs gives the right error, but it changes the numbers so that they no longer match ter-0y0z's scripts.

## Acceptance Criteria

The paired mode's standard errors come from a moving-block bootstrap over the pairs, with the block length and the seed stated in the output. A test on synthetic frames with independent noise gives an error near the analytic one. tools/README.md drops the warning that the errors are somewhat too small, or gives the remaining limit.

## Notes

**2026-11-01T01:18:40Z**

Settled choices (builder, 2026-10-31).

1. Block length: BLOCK = 5 consecutive pairs, a module constant beside RESAMPLES and SEED, with no command-line option. A scratch simulation (100 pairs, 201 frames, independent Gaussian frame noise, 400 traces of 250 resamples, real error from the spread of the paired median over 20,000 traces) gave these mean ratios of the bootstrap error to the real standard error, for two trace seeds: single pairs 0.928 and 0.934, blocks of 2 0.970 and 0.975, 3 0.978 and 0.976, 4 0.984 and 0.974, 5 0.979 and 0.975, 6 0.979 and 0.970, 8 0.964 and 0.962, 10 0.949 and 0.953. Blocks of 3 to 6 form a plateau at about 0.98. Longer blocks leave too few blocks in 100 pairs and fall off. 5 is the longest length on the plateau, so it keeps the most of a slow drift: correlations up to 4 pairs, 8 frames, apart.
2. The rule: a linear moving-block bootstrap. Each block starts at a position drawn with replacement from 0 to n - BLOCK, the resample joins blocks until it holds n differences, and the values past n go. With fewer than BLOCK differences, one block holds them all, so the error is 0. Not circular: a circular block would join the last pair to the first, and a drift makes those two differ.
3. bootstrap_error keeps its signature (values, resamples, seed) and reads BLOCK itself. The block length is one fixed rule, not a parameter that only one caller passes. The seed tests still vary resamples and seed.
4. Gaps: paired_median passes its differences in the order of the triples, the order of the trace, without the pairs that it leaves out. A block can hold the differences on the two sides of a gap. Those two pairs share no frame, so they do not correlate through a shared frame, and the block keeps them as they are. It adds no correlation that the trace does not have. Splitting the series into runs at each gap would need more code and would not change what a block copies.
5. Output: passes.json gives "blockPairs": 5 beside "resamples" and "seed". The text above the paired table says "a moving-block bootstrap of 2000 resamples, with blocks of 5 consecutive pairs and the seed 1".
6. The warning: passes.md and tools/README.md now say that the blocks keep the correlation of neighboring pairs, and give the limit that remains: a drift that correlates pairs farther apart than a block still makes the errors too small. The README's examples now quote the new errors.

**2026-11-01T01:18:40Z**

Tests (builder, 2026-10-31). tools/test_gpu_trace.py, 73 tests, about 4.4 s, of which BlockBootstrapOnAlternatingFrames takes about 4 s.

New, red first on the old code:
- BlockBootstrapOnAlternatingFrames.test_the_block_bootstrap_error_lies_near_the_real_standard_error_of_the_paired_median: 400 synthetic alternating traces of 100 pairs with independent noise. The real error is the standard deviation of the paired median over 20,000 other independent traces, computed in the test. The mean ratio must lie within 0.045 of 1. Old code: 0.927, a failure for the right reason. New code: 0.979.
- BootstrapError.test_each_resample_joins_blocks_of_consecutive_values: 1 to 6 with BLOCK = 5 gives exactly two medians, 2.5 and 3.5, so the error is 0.5 (derivation in the comment). Old code: 1.006.
- BootstrapError.test_fewer_values_than_a_block_give_no_error. Old code: 0.727.
- PairedFrames.test_the_result_names_the_pass_the_mode_and_the_bootstrap now includes "blockPairs". Old code: KeyError.
- PairedTable.test_the_text_states_the_resamples_the_block_length_and_the_seed. Old code: no BLOCK.
- PairedText.test_the_text_says_that_the_blocks_hold_the_shared_frames_but_not_a_slower_drift. Old code: the old sentence.

New, passing on the old code, which lock in the premise and the decisions:
- BlockBootstrapOnAlternatingFrames.test_the_bootstrap_of_single_differences_is_too_small_in_the_same_setup: the old bootstrap, copied into the test as _independent_bootstrap_error, gives a mean ratio below 0.955 (0.927). So the setup tells the two apart. The bound 0.955 lies about 2 standard deviations of the mean ratio (about 0.012) from both results.
- BootstrapError.test_the_paired_errors_use_the_differences_in_trace_order: replaces test_the_paired_errors_use_the_fixed_seed_and_resample_count. The old test had 5 pairs, and 5 pairs now make a single block with an error of 0, so it would compare 0 with 0. The new one has 12 pairs whose differences grow with the frame, and checks that the error equals that of the differences in trace order and differs from that of the reversed order.
- BootstrapError.test_a_pair_left_out_of_a_quantity_joins_the_pairs_on_its_two_sides_into_one_series: locks in choice 4.

Changed because they pinned the old statistic:
- test_the_error_is_the_spread_of_the_medians_of_the_resamples is gone. It pinned sqrt(14/27) for single draws from 1, 2 and 3. Three values now make one block, so the error is 0. The two block tests above replace it.
- test_the_paired_errors_use_the_fixed_seed_and_resample_count is replaced, as above.
- The two text tests changed with the text.
The PairedTable rows with "0.0000" errors keep their values: one pair gives an error of 0 with either bootstrap.

**2026-11-01T01:18:40Z**

Real data (builder, 2026-10-31). The kept exports of ter-0y0z were still in /tmp/e91p-fix. Command, with the exports linked into new folders under /tmp/ter-2fri-real: python3 -I tools/gpu-trace.py report /tmp/ter-0y0z/traces/<name>/run.trace /tmp/ter-0y0z/traces/<name>/run/gpu-timings.json /tmp/ter-2fri-real/<name> --window reflections bloom --paired haze (--control haze for above-control). No new trace ran. Every paired median is unchanged.

Paired median in ms, then the standard error in ms of ter-e91p (single differences) and of this ticket (blocks of 5):

| Row | above-alt, 100 pairs | day-alt, 100 pairs | above-control, 99 pairs |
|---|---|---|---|
| haze busy | +0.1622, 0.0033, 0.0054 | +0.1856, 0.0017, 0.0032 | -0.0001, 0.0010, 0.0013 |
| exposure busy | +0.1016, 0.0067, 0.0094 | +0.1144, 0.0049, 0.0088 | -0.0005, 0.0014, 0.0015 |
| bloom busy | +0.0068, 0.0086, 0.0102 | -0.0083, 0.0060, 0.0058 | -0.0101, 0.0062, 0.0053 |
| frame span | +0.2193, 0.0306, 0.0371 | +0.1719, 0.0147, 0.0112 | -0.0052, 0.0717, 0.0770 |
| reflections end to bloom start | +0.1634, 0.0049, 0.0057 | +0.1725, 0.0029, 0.0047 | +0.0077, 0.0027, 0.0026 |

Most errors grow, by up to about 1.8 times (day-alt's exposure and haze). A few fall, such as day-alt's frame span and above-alt's environment faces (0.0033 to 0.0012). The errors of the other pass rows change by about 0.001 ms or less.

The control's window: the description expected +0.0077 ms to lie fewer standard errors from 0 than 2.9. It does not. The error falls from 0.0027 to 0.0026 ms, so the median lies 2.9 standard errors from 0, as before. The block bootstrap does not explain that distance. A diagnosis on the same differences: their lag-1 autocorrelation is 0.03, below the 1/6 of independent frame noise. The error with blocks of 1, 5, 10 and 20 pairs is 0.0027, 0.0026, 0.0026 and 0.0023 ms, so no slow drift hides in it. The paired median is +0.0049 ms in the first half of the trace and +0.0085 ms in the second. The median window of the even frames is 0.3264 ms, and that of the odd frames 0.3215 ms. So the even frames' window is about 0.005 ms longer through the whole trace: an offset between the even and the odd frame slots, not an error that is too small. Its mean difference, -0.0021 ms, differs in sign from the median, so the differences are skewed. tools/README.md now says this beside the control's numbers.

**2026-11-01T01:41:44Z**

Review fix (fixer, 2026-10-31). The resolution of each finding.

1. Short series (correctness). Confirmed by a simulation, then fixed. With blocks of 6 pairs and noise only in the frames with the pass, the mean ratio of the block error to the real standard error was 0.54 for 12 pairs, 0.74 for 18, 0.82 for 24, 0.85 for 30 and 0.93 for 60 (/tmp/ter-2fri-scratch/sim4.py). So the reviewer's minimum of 2 blocks is not enough. MIN_ERROR_PAIRS = 5 * BLOCK_PAIRS = 30. paired_median leaves out "standardErrorMs" below it, the table prints an empty error cell, passes.json gives "minErrorPairs", and the text above the table and tools/README.md state the rule. bootstrap_error no longer clamps the block to the series. Its only caller passes at least 30 values, so its docstring states that values holds at least BLOCK_PAIRS values. The rows of 1 pair that ter-e91p printed as 0.0000 now print no error. Real traces have 99 or 100 pairs, so no kept row loses its error.
2. The row of the pass itself (fulfillment). Confirmed. New class BlockBootstrapOnTheRowOfThePass: no noise in the frames without the pass, so neighboring differences correlate by 1/2. Mean ratio 0.961 for blocks of 6 and 0.793 for single differences, as the reviewer predicted (0.96 and 0.79). The block sweep and the lag 1 to 5 autocorrelations of every row of the three kept exports are in /tmp/ter-2fri-scratch/real.out (script real.py). Rows that the paired mode exists to measure:
   - haze busy, the row of the pass: lag-1 autocorrelation 0.51 (above-alt) and 0.55 (day-alt), lags 2 to 5 near 0. The error rises from blocks of 1 to 4 and is flat from 4 to 10 (0.0053 to 0.0059 ms, and 0.0032 to 0.0033 ms). Blocks of 6 sit on that plateau. The growth over ter-e91p's error (1.7 and 1.8 times) is larger than the synthetic 1/0.79, and I did not find why. The plateau shows that no drift beyond the neighbor pair adds to it.
   - exposure busy, above-alt: autocorrelation 0.58, 0.29, 0.36, 0.28, 0.19 at lags 1 to 5. The error keeps growing with the block: 0.0094 (5), 0.0097 (6), 0.0118 (10), 0.0131 ms (20). That is a drift longer than any block that 100 pairs can hold, and the README now gives it as the first remaining limit. day-alt's exposure busy: 0.60, 0.19, 0.10, 0.02, flat from 6 to 20 (0.0092 to 0.0110 ms).
   - environment faces: a cycle. Their busy time follows a cycle of 12 frames in all three traces (frame autocorrelation +0.27 to +0.52 at lag 12, -0.41 at lag 6 in day-alt, /tmp/ter-2fri-scratch/faces.py): about 6 frames near 0.03 ms, then 6 near 0.16 to 0.27 ms. That is 6 pairs, and the error falls with the block length (control: 0.0054 ms for single pairs, 0.0014 for 6, 0.0010 for 20). The control's environment faces median, +0.0040 ms, lies 2.9 of these errors from 0, so here the error is too small. The README and the text above the table now give the cycle as the second limit. ARCHITECTURE.md's step 6 ("each face that the frame draws") fits it.
   The accuracy claim in tools/README.md now gives both kinds of row: 0.98 and 0.93 for the rows of the other passes, 0.96 and 0.79 for the row of the pass.
3. The block length (fulfillment). The new rule: the block length that brings the smaller of the two structures' mean ratios closest to 1. Means over two trace seeds (11 and 23), noise in every frame, then none in the frames without the pass: 1 pair 0.931 and 0.792, 4 0.979 and 0.954, 5 0.977 and 0.960, 6 0.975 and 0.964, 8 0.963 and 0.954, 10 0.951 and 0.945 (/tmp/ter-2fri-scratch/sim3.py). The smaller is always the second, and it is highest at 6, so BLOCK_PAIRS = 6, not 5. 5 and 6 differ by less than the simulation's noise. 6 also keeps more of a slow drift. One span in both places: a block of 6 pairs spans 13 frames.
4. The block test (design). It now builds the values 1 to BLOCK_PAIRS + 1. I checked the reviewer's derivation for even and odd lengths: the medians are B/2 and B/2 + 1 for every B of 4 or more, whichever value the second block adds, so the error stays 0.5.
5. The name (design). BLOCK is now BLOCK_PAIRS, beside the JSON key "blockPairs". The minimum follows the same pattern: MIN_ERROR_PAIRS and "minErrorPairs".
6. The offset between the even and the odd frames (fulfillment). New ticket ter-svcs under ter-pq5y holds it, together with the environment faces' cycle, because both are control medians about 3 standard errors from 0. tools/README.md now gives the evidence that the window's error is not too small (0.0027, 0.0026, 0.0026 and 0.0023 ms for blocks of 1, 5, 10 and 20, and a lag-1 autocorrelation of 0.03), says that the cause of the offset is unknown, and drops the "two facts" sentence. tk create took the prefix t2gtbb from the worktree folder, so I renamed the ticket to ter-svcs.

Assessment gaps. The reviewer's notes: the BootstrapError class docstring now says "moving-block". The reviewer noted that a mutant that sorts the differences survives the trace-order test. It did more than that: a linear series and its reverse give the same error up to rounding, so the old test passed only through float rounding. The test now gives x a busy time that grows by ((7 i) mod 11) * 10 us, and it compares the error with that of the sorted differences. The gap test now has 31 kept pairs in no order of their values, so that it has an error under the minimum.

**2026-11-01T01:41:44Z**

Tests changed by the review fix (fixer, 2026-10-31). tools/test_gpu_trace.py, 78 tests, about 6 to 7 s, of which the two simulation classes take about 3.7 s and 2.8 s.

Red first on the code of 7228b161: PairedFrames.test_the_result_names_the_pass_the_mode_and_the_bootstrap (KeyError "minErrorPairs"); PairedMedian.test_a_quantity_with_fewer_pairs_than_the_minimum_has_a_median_but_no_standard_error and test_a_quantity_with_the_minimum_number_of_pairs_has_a_standard_error, PairedTable.test_the_text_states_the_fewest_pairs_that_give_a_standard_error and test_a_paired_row_gives_the_pairs_and_the_signed_medians_and_errors (no MIN_ERROR_PAIRS); PairedTable.test_a_row_of_fewer_pairs_than_the_minimum_gives_its_medians_without_errors and test_the_paired_window_row_names_its_two_passes (0.0000 where no error belongs); PairedText.test_the_text_says_that_the_blocks_hold_the_shared_frames_but_not_a_slower_drift_or_a_cycle (the old sentence); the BLOCK_PAIRS tests (the old name).
Changed: test_fewer_values_than_a_block_give_no_error is gone, because paired_median no longer calls the bootstrap with fewer than 30 values. The paired-row table test now uses 30 pairs, because a row of 1 pair has no error. The trace-order and gap tests now use 36 and 31 pairs, above the minimum.
New: the classes share the mixin _AlternatingFrames. BlockBootstrapOnAlternatingFrames keeps its setup and bounds (0.979 for blocks of 6, 0.927 for single differences). BlockBootstrapOnTheRowOfThePass has no noise in the frames without the pass: 400 traces from the seed 11, real error from 10,000 traces from the seed 7, block bound 1 +- 0.08 (0.961), single bound below 0.88 (0.793). The old single-difference bootstrap fails the block bound in both classes.

**2026-11-01T01:42:03Z**

Real data with blocks of 6 (fixer, 2026-10-31). The same command as the note above, into /tmp/ter-2fri-fix/<name>, whose export folders link to those of /tmp/ter-2fri-real. No new trace ran, and every paired median is unchanged. Each row whose error moved by more than 0.001 ms from ter-e91p's gives the paired median, then the standard error in ms of ter-e91p (single differences), of blocks of 5 (7228b161) and of blocks of 6 (this fix). All other rows moved by 0.001 ms or less. Window and frame span rows are given for every trace.

| Trace | Row | Median (ms) | ter-e91p | Blocks of 5 | Blocks of 6 |
|---|---|---|---|---|---|
| above-alt | environment faces busy | -0.0035 | 0.0033 | 0.0012 | 0.0011 |
| above-alt | environment faces alone | -0.0035 | 0.0032 | 0.0012 | 0.0011 |
| above-alt | G-buffer alone | +0.0053 | 0.0106 | 0.0116 | 0.0116 |
| above-alt | direct light busy | +0.0004 | 0.0097 | 0.0107 | 0.0108 |
| above-alt | direct light alone | -0.0095 | 0.0137 | 0.0147 | 0.0150 |
| above-alt | haze busy | +0.1622 | 0.0033 | 0.0054 | 0.0055 |
| above-alt | haze alone | +0.0100 | 0.0041 | 0.0074 | 0.0076 |
| above-alt | exposure busy | +0.1016 | 0.0067 | 0.0094 | 0.0097 |
| above-alt | bloom busy | +0.0068 | 0.0086 | 0.0102 | 0.0099 |
| above-alt | frame span | +0.2193 | 0.0306 | 0.0371 | 0.0391 |
| above-alt | window | +0.1634 | 0.0049 | 0.0057 | 0.0058 |
| day-alt | environment faces busy | -0.0065 | 0.0043 | 0.0030 | 0.0025 |
| day-alt | environment faces alone | -0.0085 | 0.0040 | 0.0023 | 0.0020 |
| day-alt | haze busy | +0.1856 | 0.0017 | 0.0032 | 0.0032 |
| day-alt | exposure busy | +0.1144 | 0.0049 | 0.0088 | 0.0092 |
| day-alt | SMAA weights busy | +0.0042 | 0.0044 | 0.0053 | 0.0055 |
| day-alt | frame span | +0.1719 | 0.0147 | 0.0112 | 0.0105 |
| day-alt | window | +0.1725 | 0.0029 | 0.0047 | 0.0048 |
| above-control | environment faces busy | +0.0040 | 0.0049 | 0.0020 | 0.0014 |
| above-control | environment faces alone | +0.0040 | 0.0049 | 0.0020 | 0.0014 |
| above-control | direct light busy | -0.0039 | 0.0135 | 0.0161 | 0.0158 |
| above-control | direct light alone | +0.0158 | 0.0144 | 0.0163 | 0.0162 |
| above-control | frame span | -0.0052 | 0.0717 | 0.0770 | 0.0758 |
| above-control | window | +0.0077 | 0.0027 | 0.0026 | 0.0027 |

The control's window still lies 2.9 standard errors from 0 (ter-svcs). The control's environment faces lie 2.9 from 0 as well, because of their cycle of 12 frames (finding 2). The errors with blocks of 6 lie within 0.0006 ms of those with blocks of 5 in every row above, except the three frame span rows, which move by 0.0007 to 0.0020 ms. The README's examples now quote the errors of blocks of 6: exposure 0.010 ms, window 0.006 ms, frame span 0.011 to 0.076 ms, control window 0.003 ms.
