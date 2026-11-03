---
id: ter-ohqr
status: in_progress
deps: []
links: []
created: 2026-11-03T14:53:48Z
type: feature
priority: 2
assignee: Alexis Purslane
parent: ter-89xq
tags: [rendering, temporal]
---
# Motion vectors, the frame history and the surface test

Build ticket 2 of epic ter-89xq. Design: docs/plans/temporal-renderer.md, section 3, section 8 ("The surface test") and decisions 14 and 19 to 24. Acceptance: section 14, item 2. Nothing on screen changes. Its result is the measured time of the G-buffer and the surface test.


## Notes

**2026-11-03T14:56:37Z**

2026-11-03: launched as workflow run 6a1e1603-9008-429e-af16-654b92848535, version 869b0b07-3413-4b06-8707-a88a91bf6e91, branch ter-ohqr-motion-vectors from 70fe35650.

**2026-11-03T17:53:57Z**

2026-11-03: review and fix done (run 6a1e1603, ready for integration at f44ffc4f2). Merged main into the branch (2d0ea65e8). Hardening runs as delegated task ter-ohqr-hardening-1. It first fixes the far-depth limit, settled by the orchestrator under the rule for small choices inside a chosen design: the surface test reads the last view depth from the last frame's R32F device depth, kept by parity, so one store holds the depth and surfaces beyond 65,504 m keep their history.
