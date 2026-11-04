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

**2026-11-04T01:46:32Z**

2026-11-03 (ter-3ug8, the GPU calibration spike): APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt failed in 4 of the 9 test runs of the spike, which held full runs of the solution and runs of the rendering tests alone. It measured fewest[1] - fewest[0] = 0.0723 ms (full Checked run, checked-1.log), 0.0590 and 0.0559 ms (rendering tests in Checked, checked-rendering-2.log and checked-rendering-3.log) and 0.0563 ms (full Release run, release-1.log), against the bound of 0.05 ms. The logs are in that branch's captures/3ug8/, which git ignores. Other agents built, tested and traced on the machine at the same time, and the GPU's clock under such runs moves between 648 and 1296 MHz with the duty cycle (docs/research/temporal-renderer/gpu-calibration.md). So the margin of the bound depends on the load and on the clock. This note records data only and proposes no fix.
