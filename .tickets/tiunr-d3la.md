---
id: tiunr-d3la
status: in_progress
deps: [tiunr-pzhn]
links: []
created: 2026-11-07T01:26:41Z
type: bug
priority: 1
assignee: Alexis Purslane
parent: tiunr-lmmx
---
# Fix disabled temporal-statistics image format bindings in Design B candidate

Parent inspected stopped Design B evidence. Candidate GLSL declares RGBA32F storage at signal11/12 and composite6, but disabled paths bind RGBA16F histories/DirectSpecular. All64 failed candidate cases include Vulkan FormatMismatch-ImageView; default-off validation is broken. Keep current H13 baseline untouched. Fix only in B worktree after a fresh explicit bounded continuation; consumed counts remain Rendering3/tool1/tests2, captures0,metrics0. Reproduce preserved validation failure, keep format validation on, then prove off-path validity and actual statistics-to-probe consumption before captures. No automatic retry or allocation.


## Notes

**2026-11-07T01:48:42Z**

User explicitly authorized a new isolated B binding/probe repair and new renders. Fresh ceilings: Rendering3/tool3/test4 compilations, five focused executable test runs, up to four non-rendering engine-admission commands, two original-fixture capture attempts and one saved-data report. Preserve estimator/default-off behavior and prove statistic consumption; old failures/counts remain cumulative. Brief: C/parent-inspections/ab-repair-launch-20261106-v2/AB-REPAIR-BRIEF.md. New evidence: C/B-h13-binding-repair-v2. Parent owns inspection/integration/closure.

**2026-11-07T01:50:45Z**

Launched Codex gpt-6.1-sol/high in T3 worktree t3-e3cd3d20, branch t3/repair-h13-stat-bindings; HEAD verified37e047c88e6bcc086174854912bd57107f948315. Task h13-B-binding-repair-v2-20261106, runordinal1; running at initial read. Full task/thread/link receipt: C/parent-inspections/ab-repair-launch-20261106-v2/launches.json. Completion delivery; no polling/reservation.

**2026-11-07T02:21:43Z**

Parent verified575 artifact identities and208 links; finalXML109/148pass,39fail,0errors/skips. Remaining37 read-after-write validation cases and2 invalid/destroyed resize image-view cases; format mismatch absent from final failures. Actual embedded-probe variance-input case passes but native admission/captures/metrics remain0. Fresh Rendering3/tool2/test3 compilation attempts,4 executable runs,2 correction rounds; cumulative Rendering6/tool3/test5. RELEASE retained; no retry or reservation. Follow-up tiunr-pzhn records remaining GPU-resource faults without new execution authority. Parent receipt C/parent-inspections/B-h13-binding-repair-v2/inspection.json; no merge/closure.

### November 6, 2026: valid B runtime and captures

The tiunr-pzhn continuation resolves the remaining test-owned image ordering and resize faults. Parent inspection confirms 148/148 focused passes, actual embedded-probe statistic consumption and two captures without Vulkan validation messages through disposal. C/B-h13-resource-repair-v3 preserves the final report and cumulative failures. The engine matches V2 exactly. All seven embedded tool shader payloads also match V2. Parent receipt: C/parent-inspections/B-h13-resource-repair-v3/inspection.json. The repair is complete, but integration and ticket closure remain pending. H13 remains selected because rough persistent reference error rises 1.8445% for a 3.0684% temporal-noise reduction.
