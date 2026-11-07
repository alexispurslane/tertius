---
id: tiunr-m0uo
status: in_progress
deps: [tiunr-k2wn]
links: []
created: 2026-11-07T00:56:38Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-lmmx
---
# Design A: distribute reflection ray samples across pixels and frames

Starting from the exact integrated H13 commit, change only reflection sample distribution at the same configured ray budget. Study spatiotemporal blue-noise applicability. Keep H13 reconstruction, material response, history policy and footprint fixed. Return actual renderer frames, separate means, noise/error observables and provenance. No automatic promotion.


## Notes

**2026-11-07T01:04:05Z**

Ready for isolated T3 worktree launch from the H13 integration commit immediately following this note. Design is sample distribution only at fixed configured ray budget; keep H13 reconstruction/history/reuse fixed. Fresh explicit execution bounds and setup identities in external AB-BRIEF.md under C/parent-inspections/h13-integration-20261106. No workflows or extra agents.
