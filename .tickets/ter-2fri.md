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
