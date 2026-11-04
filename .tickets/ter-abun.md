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
