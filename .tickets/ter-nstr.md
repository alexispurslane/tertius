---
id: ter-nstr
status: open
deps: []
links: []
created: 2026-11-12T02:59:35Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# Fewer GPU dispatches: one dispatch for FSR's 13 clears, and the depth and light pyramids in fewer dispatches

Sweep rows 2 and 4 (/private/tmp/tertius-sweep-20261111/report.md, section 7), approved by the user on 2026-11-11. Exact image. Expected gain on the deck: 0.15 to 0.30 ms for the clears and 0.10 to 0.15 ms for the pyramids. Each dispatch behind a barrier is its own Metal encoder under MoltenVK 1.4.2, and each encoder boundary idles the GPU about 17 us (2.32 ms over 139 boundaries on the deck), so fewer dispatches also cut that idle time on the Mac. Images must stay bit-identical; measure the deck before and after.

