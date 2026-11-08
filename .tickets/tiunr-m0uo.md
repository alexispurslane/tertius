---
id: tiunr-m0uo
status: in_progress
deps: [tiunr-k2wn, tiunr-oi26]
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

**2026-11-07T01:32:21Z**

Parent verified353 sealed entries, both host trace hashes/loaded H13 engine identities and final182/182 focused greens. Intended candidate engine dbc04d91 was NOT loaded by either native capture; executable symlink resolved into H13. red7=3pass/4assertionfail; firstcandidate182=181pass/1fail; final182/182. Counts Rendering1/tool0/tests3,captures2(192draws),metrics1;validAcaptures0. This is failed capture provenance, not algorithmic null. RELEASE confirmed, no retry/allocation/reservation. Follow-up tiunr-oi26 records local-entry/actual-engine preflight. H13 unchanged. Receipt C/parent-inspections/A-h13-sample-distribution-v1/inspection.json.

**2026-11-07T02:15:03Z**

Valid A measurement after capture repair: smooth noise-11.5618%,persistent mean error-0.77246%; rough noise-2.83078%,persistent mean error+11.11939%. Rough fixed outer-band missing/reference5.55257%->11.15734%,141valid/19invalid rows covering13.95262% of reference. All four A previews genuinely differ from H13; rough clumps remain. Units/RMS definitions and full-crop versus band denominators retained. Source/engine/probe exact, no algorithm retuning. H13 unchanged; no promotion/merge/closure. Evidence C/A-h13-capture-repair-v2; verified parent receipt C/parent-inspections/A-h13-capture-repair-v2.
