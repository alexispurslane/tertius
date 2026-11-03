---
id: ter-k63u
status: open
deps: []
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, fsr]
---
# FSR 3.1: the pinned source, the build and the host

Build ticket 1 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, section 5 and decisions 12, 30 and 31. Acceptance: section 14, item 1, except the lavapipe run: the user decided on 2026-10-25 to skip Linux runs for now (ter-kzj3), so this ticket runs its GPU test under the validation layer on MoltenVK, and ticket 7 runs it on lavapipe. Nothing in the frame changes: FSR runs only in its tests.

