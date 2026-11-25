---
id: ter-jj53
status: closed
deps: [ter-2zga]
links: []
created: 2026-11-12T07:03:50Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# Compute passes next to each other in the frame's order: 0.05 to 0.10 ms

**2026-11-25 · Retired.** This optimization was scoped to an earlier GPU workload. Reopen against a named film segment if its pass dominates a repeatable GPU timing there.

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A6 proposal 9. Three or four compute encoders start after a render pass; reordering independent compute passes next to each other lets them share an encoder with the patched MoltenVK.

