---
id: ter-yaqk
status: open
deps: [ter-18hc]
links: []
created: 2026-11-12T02:59:35Z
type: task
priority: 1
assignee: Alexis Purslane
parent: ter-8hyd
---
# One byte per pixel holds the 3 x 3 neighbors' same-surface answers, for both accumulations

Sweep row 1 (/private/tmp/tertius-sweep-20261111/report.md, section 7), approved by the user on 2026-11-11. Exact image. Expected gain on the deck: 0.45 ms. It changes accumulate_spatial.comp, which the clamp-on-evidence fix (ter-18hc, branch ter-18hc-clamp-on-evidence) also changes, so it starts after that merge.

