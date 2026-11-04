---
id: ter-abun
status: in_progress
deps: []
links: []
created: 2026-10-26T21:23:47Z
type: bug
priority: 1
assignee: Alexis Purslane
tags: [tests, flaky, rendering]
---
# Make GpuTimingTests.APassThatRecordsNothing stable under load: it measured 0.127 ms against 0.05 ms once

Found in ter-izsu (2026-10-26): GpuTimingTests.APassThatRecordsNothingReportsTheTimeItTookWhichIsNearlyZero failed once while other agents built and traced on the machine: the empty pass took 0.127 ms against the test's bound of 0.05 ms. A pass's stamp time includes any idle time before it (ARCHITECTURE.md, 'GPU timings'), so a busy GPU can stretch it. Find a bound or a measure (for example the fewest of several frames, as Allocations.Fewest does for the allocation tests) that holds under load and still fails when an empty pass records work.

## Acceptance Criteria

The test passes in 20 runs of the Rendering tests in a row while another process renders, and still fails when the empty pass is made to record a dispatch.


## Notes

**2026-10-27T02:41:53Z**

2026-10-26 (ter-okrp): the flake is probably the late stamp on MoltenVK: the empty pass's time can hold part of the next pass's work (ARCHITECTURE.md, 'GPU timings'). A bound that holds under load may need to read the pass beside it, or the test may hold only on drivers whose stamps do not latch late (VulkanRenderer.StampsLatchLate).

**2026-10-27T05:11:59Z**

Merged (b2bcb027). Two tests now, one per promise of 'GPU timings', each on the fewest of 64 frames: on every driver the empty pass adds under 0.05 ms between the G-buffer's stamp and the lighting's; where stamps latch on time (not MoltenVK) its own time stays under 0.05 ms. 20 runs in a row passed at a load of 13 to 24 with a demo rendering; a dispatch of about 0.1 ms or more fails the first test. When the bounce light merges, the first test's 'lighting' stamp becomes the direct pass's.

**2026-11-03T22:25:33Z**

2026-11-03: failed again in the pre-commit hook on the merge of ter-ohqr into main, now as APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt (GpuTimingTests.cs:302): fewest[1] - fewest[0] = 0.0581 ms against the bound of 0.05 ms, with other agents building (the one-minute load was 3 to 33 in the 20 minutes before). It passed in the full Checked and Release runs on the same code just before.

**2026-11-03T23:58:00Z**

2026-11-03 (ter-gmv8): failed three times in a row in Release on MoltenVK, at a one-minute load of 7 to 14: 0.0832 ms in the full Release run of the ter-gmv8 branch, 0.0715 ms in a run of GpuTimingTests alone on that branch, and 0.0723 ms in a run of GpuTimingTests alone on an export of the base commit 116a0e50a, which has no ter-gmv8 change. The same branch passed the test in its full Checked run a few minutes before. So the failure does not come from ter-gmv8.

**2026-11-04T00:14:37Z**

2026-11-03: findings moved from ter-35w5, which duplicated this ticket (closed as a duplicate by ter-nmbm's continuation).

GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt measures, in Release on the M1 Max, the fewest time over 64 frames from the G-buffer's stamp to the stamp of "accumulate direct", with and without an empty pass, and bounds the difference by 0.05 ms. Since ticket ter-nmbm moved the window's end to the accumulation's stamp (commit 16f48fb82), it fails about one run in ten when other agents load the machine: 3 of 30 runs of 16f48fb82 at a 1-minute load of about 20 (0.051, 0.073 and 0.080 ms), and 5 of about 50 runs of the fix stage's HEAD at loads of 4 to 25, with a median of 0.014 to 0.018 ms on both (2026-11-03). It failed once in a full Release run of the fix stage. ter-abun's acceptance, 20 runs in a row under another rendering process, no longer holds.

Probes of the fix stage, 24 runs each, at loads of 5 to 18: 128 frames in place of 64 left 2 of 24 over the bound. One renderer that skips the empty pass in every other frame (Graph.Skipped), with the fewest of each kind, gave a median of 0.002 ms but a spread of -0.080 to 0.055 ms. The median of the 64 paired frame differences gave a tight median of 0.026 ms but 0.046 to 0.055 ms in the 3 runs at the highest load. So the noise is per frame, not per renderer, and the stamp of the empty pass costs about 0.026 ms on this window.

Options to design: the paired median of one renderer with a bound set from that measure, a window that ends at a fragment pass's stamp, or the frame span as the window.

ter-35w5's acceptance was the same as this ticket's.

**2026-11-04T00:50:00Z**

2026-11-03 (orchestrator, ter-gmv8 hardening): in full runs of Tertius.Rendering.Tests today the test failed about half the time: 0.0581 ms (ter-ohqr merge hook), 0.0832 ms (Release), a failure in a Debug coverage run, a failure in a full Checked run, and 0.0648 ms in the hook, against passes in four other full runs. Run alone with `--filter-method "*APassThatRecordsNothingAddsNearlyNoTime*"` at a one-minute load of about 5, it passed 6 times in 6. The GPU tests share one xunit collection, so no other GPU test runs beside it, but the CPU-only test classes of the same process do. So the next step is to find what in the full run moves the gap: CPU tests beside it, the GPU state that earlier tests leave, or the order of the tests. This test now blocks about every second commit of Rendering code, so its priority is raised to 1.

**2026-11-04T01:00:00Z**

2026-11-03: skipped with `[Fact(Skip = ...)]` at the user's request, until this ticket fixes it. The fix removes the skip.

**2026-11-04T01:46:32Z**

2026-11-03 (ter-3ug8, the GPU calibration spike): APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt failed in 4 of the 9 test runs of the spike, which held full runs of the solution and runs of the rendering tests alone. It measured fewest[1] - fewest[0] = 0.0723 ms (full Checked run, checked-1.log), 0.0590 and 0.0559 ms (rendering tests in Checked, checked-rendering-2.log and checked-rendering-3.log) and 0.0563 ms (full Release run, release-1.log), against the bound of 0.05 ms. The logs are in that branch's captures/3ug8/, which git ignores. Other agents built, tested and traced on the machine at the same time, and the GPU's clock under such runs moves between 648 and 1296 MHz with the duty cycle (docs/research/temporal-renderer/gpu-calibration.md). So the margin of the bound depends on the load and on the clock. This note records data only and proposes no fix.

**2026-11-04T11:54:12Z**

2026-11-04: started as a workflow, run c81f71df-e136-4251-b500-d591a04ecab3 (version 869b0b07), branch ter-abun-stable-empty-pass from 730f94b8e.

**2026-11-04T16:07:42Z**

2026-11-04 (ter-abun, the fix): the test now reads the frame's GPU span, and its skip is gone. It is renamed APassThatRecordsNothingAddsNearlyNoTimeToTheFramesGpuSpan, because its measure changed.

What moved the old gap (evidence in /private/tmp/abun on the development Mac, not committed):
- The old measure was the fewest of 64 frames of the time from the G-buffer's stamp to the direct pass's stamp, in two renderers that drew in turn. On MoltenVK the G-buffer's stamp latches after part of the next passes' work, by a different amount in each frame. So the fewest of 64 frames is an extreme value of that lateness, and the gap of two such values is noise.
- Repeated 200 times in one process (load 22), the gap had a standard deviation of 0.024 to 0.032 ms and was over 0.05 ms 39 times. Repeated 100 times at load 6, it was over 0.05 ms twice (range -0.074 to +0.081 ms).
- The clock was not the cause. gpu-clock (tools/gpu-calibration, branch ter-3ug8) beside the test showed the clock move from 648 to 1296 MHz within the test's 64 frames. The failure rate was about the same in each band: 648-900 MHz 4 of 22, 900-1100 MHz 2 of 15, 1100-1400 MHz 33 of 163.
- The old measure also could not see work. A pass that filled 32 MiB (about 0.17 ms in the span) read -0.04 ms, because the G-buffer's stamp latched after the fill. So the old test could not fail for the case that it was meant to catch.
- The full run added no cause of its own. Two full runs (one beside a demo render) and one Release run passed the old test, at gaps of -0.038, 0.015 and 0.007 ms. The failures on 2026-11-03 fit the same spread.

The new measure (ARCHITECTURE.md, "GPU timings"):
- One renderer with one frame in flight records the empty pass in every second frame (FrameGraph.Skipped). A pair is a frame without the pass and the frame right after it. Its value is the difference of their spans.
- Chunks of 128 pairs. Each chunk gives a distribution-free 99.7 % interval of the median. The first chunk whose interval lies wholly below or above 0.05 ms, and is at most 0.1 ms wide, decides. If none of 16 chunks does, the test skips with the reason.
- The width rule exists because a saturating load (three demo renders at once) stretched over 95 % of the frames. Without the rule, a 32 MiB fill passed once there.
- The bound stays 0.05 ms. The empty pass reads a median of -0.002 to 0.007 ms, with intervals near [-0.02, 0.03] ms.

Red: with the empty pass made to fill 32 MiB, the test failed alone at 0.117 ms (the first version of the measure, a median of 256 pairs). Beside one demo render it failed in 10 of 11 runs, with intervals from [0.07, 0.17] to [0.16, 0.20] ms. The other run, before the width rule, did not fail, and its outcome (skip or pass) was not recorded. With the width rule, beside one demo render it failed 4 times in 4. Beside three demo renders it failed once and skipped twice. A 16 MiB fill also failed alone, at 0.106 ms.

Green, the acceptance: 20 full Checked runs of Tertius.Rendering.Tests in a row, each beside lamp-hall-4 rendered headless in a loop (88 to 102 demo runs during each run). Each run held the GPU lock and released it after the run. The one-minute load was 4.2 to 19.5. All 20 passed the test, none skipped it, and each ran 2,562 tests with no failure.

Also tried and dropped: a permanent test that a 128 MiB fill exceeds the bound. Under a load it read -0.10 ms once, because the frame after a long pass meets the work that the other process could not run during it.

**2026-11-04T18:50:00Z**

2026-11-04 (ter-abun, the review's fix stage). Evidence in /private/tmp/abun2 on the development Mac, not committed.

What changed:
- The frames run in runs of two of each kind, not one by one. A pair is the last frame of a run and the first frame of the next run, so both frames of a pair follow a frame of the same kind. Beside one demo render, over 32 chunks of each order, the chunk medians of a 128 MiB fill went down to −0.01 ms when the frames alternated one by one, and down to 0.10 ms in runs of two. The empty pass read chunk medians of 0.007 ms (one by one) and 0.020 ms (runs of two) there.
- The end rule. A chunk still decides when its interval is at most 0.1 ms wide and lies wholly on one side of 0.05 ms. If none of 16 chunks decides, the narrowest interval now fails the test when it is at most 0.05 ms wide: it then lies across the bound, so the pass adds about the bound. Otherwise the test skips. The pure function `DecidingInterval` holds the rule, and three CPU tests pin it on made-up intervals. One of them failed under the old end rule (no decision, a skip) before the fix.
- Why 0.05 ms and not the review's 0.1 ms for the end rule: with 0.1 ms, the empty pass failed once in 10 runs beside one demo render. In that run no chunk decided, and the four narrowest intervals were 0.06 to 0.09 ms wide and lay across the bound ([0.0063, 0.0698] ms the narrowest). None of 9 intervals of the empty pass that were at most 0.05 ms wide lay across the bound. Of fills of 4 KiB to 4 MiB, 28 of 47 such intervals did.
- `AddedSpans` fails after 4 · 128 + 8 frames when the pairs do not form, with the number of pairs. Checked by a hand mutant (`Frame == last.Frame + 2`): both span tests failed with "Only 0 of 128 pairs of frames formed in 520 frames."
- A permanent test, `APassThatFillsABufferAddsMoreThanNearlyNoTimeToTheFramesGpuSpan`: a pass that fills 128 MiB must give an interval wholly above 0.05 ms. Alone it read [0.39, 0.47] ms. It kills the two hand mutants of the review: every pair value set to 0 (read 0.0) and the sign swapped (read −0.44 ms). The empty-pass test passed both mutants. 128 MiB and not 32 MiB, so that a desktop GPU that writes 1 TB/s still takes about 0.13 ms.

The real fill sizes on MoltenVK: no fill adds about the bound alone. A fill of 4 KiB added 0.05 to 0.1 ms (a barrier and an encoder of its own), 1 to 12 MiB added 0.06 to 0.11 ms. All of them failed the empty-pass test, under both end rules (10 runs of 10 each with 4 KiB). So the review's predicted skip of a pass at the bound did not show with a real pass on this Mac, and the CPU tests pin the rule instead.

Red, with the empty pass made to fill 16 MiB: failed 4 runs of 4 alone and 4 of 4 beside one demo render, at intervals from [0.054, 0.101] to [0.134, 0.189] ms.

Beside one demo render, both span tests passed 10 runs of 10 with no skip. Beside three demo renders, each skipped once in three runs, and neither failed.

Green, the acceptance: 20 full Checked runs of Tertius.Rendering.Tests in a row, with `--no-build` on one build whose test assembly had the SHA-256 9e824185…8bacd before and after the 20 runs. Each ran beside lamp-hall-4 rendered headless in a loop (89 to 100 demo runs during each run). Each run took the GPU lock through the queue protocol of tools/gpu-lock.sh and released it after the run. The one-minute load was 2.8 to 24.3. All 20 passed: 2,566 tests succeeded in each, and the two skips were the FSR timing test and the test for a driver whose stamps latch on time. Neither span test skipped.

Open: under a load that stretches nearly every frame (three demo renders), the span tests skip, and a skip passes the hook. That choice waits for the user (skip, fail, or more chunks under a longer time limit).
