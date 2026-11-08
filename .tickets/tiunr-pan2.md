---
id: tiunr-pan2
status: in_progress
deps: []
links: []
created: 2026-11-06T13:50:20Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-9b7v
---
# Isolate neighbor cap with selected history unchanged

User-approved narrow experiment: keep median prototype's selected single diffuse history; disable only incoming neighbor cap. Two new smooth/rough native captures, same64+32draw budget and original crop. Compare old OFF/ON and reference using frozen metric definitions; include temporal noise and actual fixed single-frame previews. Preserve all originals; no default policy change, light integration or fitted gain.

## Design

docs/plans/unity-guided-indirect-light-research.md

## Capture checkpoint, 2026-11-06

Implemented research-only cap override with unchanged original OFF/all-ON
defaults. All 60 engine SPIR-V resources match the original bytes. Baseline
selection checks: 2 genuine assertion reds, 4 passes. Final focused checks:
58/58 passed. Three Rendering compilations, two tool attempts and two
tests-only compilations. Preserve the TS0003 tool failure and the successful
tool build's later directory-hash setup failure. The exact successful frozen
tool was reused for tests. No extra retention/tool rebuild followed.

Both new uncapped cases completed: original smooth/rough materials,
64 warm-up + 32 measured draws each, native 1280x720 zero jitter, original
80x160 crop, geometry audit and surrounding-copy checks. All previous
captures/references/failures and source/control seals remain unchanged.
New raw/emitter/plate observations remain separate. New compressed diagnostic
files have original-byte hashes and verified gzip round trips.

Post-capture wrapper exited 93 on cumulative retention: 138228 KiB versus
131072 KiB. Disk guard passed at 34662376 KiB. Stop preserved all data.
No offline comparison, noise metric export or draw95 preview export ran.
Cross-case compatibility remains to be checked by that report. Do not call
this a completed comparison or infer its result from source inspection.

All owned heavy commands ended and were RELEASED at 09:00:47 EST. No future
reservation or acceptance. Frozen tool c2b50c4a8fd546a2b133110677d3f3a298ac880e4ddb9bad3298d8da74e9a308;
experiment Rendering 7cce5a269847c7537ec47f54f57ed7ea52569b627e4c3ae4e38671493148b261.
Evidence: /private/tmp/tertius-unity-noise-research-20261105/live-reference-comparison/cap-isolation-20261106-0850/README.md.

## Offline result delivery, 2026-11-06

User approved160MiB cumulative retention and offline-only completion. One
frozen-tool report and one bounded presentation/verification command exited0.
No new build/test/render/integration. All43actual input hashes, all old seals,
and exact frozen tool/runtime verify before/after. All12800receiver fields1..15
and classification bytes match across modes; outgoing field0 stays measured.
Old OFF/ON metric/noise objects reproduce exactly. All7compressed table
plaintext byte/hash receipts verify.

Rough cap OFF increases temporal noise RMS27.1160% while reducing persistent
mean error RMS25.7434% relative to cap ON. Outer missing light73.0911%→8.4888%
of band reference; central90%-light width16.7806→25.4679px against24.4711px
reference. Full total75.5720%→105.4089%reference. Both missing and extra light
remain. Smooth cap OFF instead increases noise12.9121% and mean error8.2260%.
Uncapped versus original OFF full-crop L1 is0.001333%rough/0.000252%smooth,
normalized by OFF light. The selected history change alone therefore has
little effect on this SPECULAR fixture; do not generalize to diffuse output.

Delivered actual draw95 crops, separate32-frame means, identical fixed display
mapping, noise/accuracy chart and raw JSON/CSV profiles/coverage/status data.
32frames are correlated; noCI/convergence/quality acceptance. Live source
history feedback differs from the one-bounce reference. Original128MiBguard
failure remains preserved, not reapproved as a compliant old phase.

All owned heavy work ended09:35:11EST and was explicitly RELEASED. No later
reservation. Ticket remains open for inspection/integration, not acceptance.
Evidence: /private/tmp/tertius-unity-noise-research-20261105/live-reference-comparison/cap-isolation-report-20261106-0928/README.md.
