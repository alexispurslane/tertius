---
id: ter-abun
status: open
deps: []
links: []
created: 2026-10-26T21:23:47Z
type: bug
priority: 3
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

**2026-11-04T00:14:37Z**

2026-11-03: findings moved from ter-35w5, which duplicated this ticket (closed as a duplicate by ter-nmbm's continuation).

GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt measures, in Release on the M1 Max, the fewest time over 64 frames from the G-buffer's stamp to the stamp of "accumulate direct", with and without an empty pass, and bounds the difference by 0.05 ms. Since ticket ter-nmbm moved the window's end to the accumulation's stamp (commit 16f48fb82), it fails about one run in ten when other agents load the machine: 3 of 30 runs of 16f48fb82 at a 1-minute load of about 20 (0.051, 0.073 and 0.080 ms), and 5 of about 50 runs of the fix stage's HEAD at loads of 4 to 25, with a median of 0.014 to 0.018 ms on both (2026-11-03). It failed once in a full Release run of the fix stage. ter-abun's acceptance, 20 runs in a row under another rendering process, no longer holds.

Probes of the fix stage, 24 runs each, at loads of 5 to 18: 128 frames in place of 64 left 2 of 24 over the bound. One renderer that skips the empty pass in every other frame (Graph.Skipped), with the fewest of each kind, gave a median of 0.002 ms but a spread of -0.080 to 0.055 ms. The median of the 64 paired frame differences gave a tight median of 0.026 ms but 0.046 to 0.055 ms in the 3 runs at the highest load. So the noise is per frame, not per renderer, and the stamp of the empty pass costs about 0.026 ms on this window.

Options to design: the paired median of one renderer with a bound set from that measure, a window that ends at a fragment pass's stamp, or the frame span as the window.

ter-35w5's acceptance was the same as this ticket's.
