---
id: ter-35w5
status: closed
deps: []
links: []
created: 2026-11-03T22:42:15Z
type: bug
priority: 3
assignee: Alexis Purslane
tags: [tests, flaky, rendering]
---
# GpuTimingTests' empty-pass sum fails about one run in ten under load since the direct signal's passes


GpuTimingTests.APassThatRecordsNothingAddsNearlyNoTimeBetweenTheEndOfThePassBeforeItAndTheEndOfThePassAfterIt measures, in Release on the M1 Max, the fewest time over 64 frames from the G-buffer's stamp to the stamp of "accumulate direct", with and without an empty pass, and bounds the difference by 0.05 ms. Since ticket ter-nmbm moved the window's end to the accumulation's stamp (commit 16f48fb82), it fails about one run in ten when other agents load the machine: 3 of 30 runs of 16f48fb82 at a 1-minute load of about 20 (0.051, 0.073 and 0.080 ms), and 5 of about 50 runs of the fix stage's HEAD at loads of 4 to 25, with a median of 0.014 to 0.018 ms on both (2026-11-03). It failed once in a full Release run of the fix stage. ter-abun's acceptance, 20 runs in a row under another rendering process, no longer holds.

Probes of the fix stage, 24 runs each, at loads of 5 to 18: 128 frames in place of 64 left 2 of 24 over the bound. One renderer that skips the empty pass in every other frame (Graph.Skipped), with the fewest of each kind, gave a median of 0.002 ms but a spread of -0.080 to 0.055 ms. The median of the 64 paired frame differences gave a tight median of 0.026 ms but 0.046 to 0.055 ms in the 3 runs at the highest load. So the noise is per frame, not per renderer, and the stamp of the empty pass costs about 0.026 ms on this window.

Options to design: the paired median of one renderer with a bound set from that measure, a window that ends at a fragment pass's stamp, or the frame span as the window.

## Acceptance Criteria

The test passes in 20 runs of the Rendering tests in a row while another process renders, and still fails when the empty pass is made to record a dispatch.

## Notes

**2026-11-04T00:14:37Z**

2026-11-03: closed as a duplicate of ter-abun, which main reopened. Its findings, measures and acceptance moved into a note on ter-abun (ter-nmbm's continuation).
