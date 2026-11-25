---
id: lin-5bsm
status: closed
deps: []
links: []
created: 2026-11-15T15:59:39Z
type: bug
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# FSR's batch timing test fails when other agents load the GPU: a median of 0.568 against a bound of 0.0383

**2026-11-25 · Retired.** This test or host failure was reported under load or in an isolated run. Keep its log. A repeat on the current build with the GPU reserved warrants a new regression ticket.

Found in t8-69v7, 2026-11-15. In the line's full Release suite (tools/test-all.sh -c Release, worktree at 9494a34f4, a 1-minute load of about 12 to 20 from other agents' GPU runs), Fsr3UpscalerTests.ATimedBatchReportsTheTimeOfOneOfItsUpscalesNotOfTheWholeBatch failed: 'Expected large.Order().ElementAt(large.Count / 2) to be less than 0.038313 ... but found 0.5679296875'. A rerun of the one test on the same build passed a few minutes later. The change under test touched no FSR code. The test runs under the shared GPU test lock, so another process's GPU work can fall inside its timed span.

