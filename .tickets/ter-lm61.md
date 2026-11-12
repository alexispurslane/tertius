---
id: ter-lm61
status: open
deps: [ter-2zga]
links: []
created: 2026-11-12T07:03:49Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-8hyd
---
# Bloom as one chain of compute dispatches: 0.13 to 0.20 ms with the patched MoltenVK

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A6 proposal 4. Bloom runs 11 render passes (one per level down and up). As a compute chain it joins one compute encoder with the patched MoltenVK; on stock it gains nothing, so it depends on the patched MoltenVK ticket.

