---
id: ter-k63u
status: in_progress
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


## Notes

**2026-11-03T14:56:37Z**

2026-11-03: launched as workflow run ab327160-9e42-468f-9b3e-3478fde59677, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-k63u-fsr31-host from 70fe35650. The lavapipe run moved to ter-cgif, because the user decided on 2026-10-25 to skip Linux runs for now.

**2026-11-03T17:53:57Z**

2026-11-03: review and fix done (run ab327160, ready for integration at b373581d4). Merged main into the branch (e4c2b8b4b). Hardening runs as delegated task ter-k63u-hardening-1.
