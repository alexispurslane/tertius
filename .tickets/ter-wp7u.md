---
id: ter-wp7u
status: closed
deps: []
links: []
created: 2026-11-12T07:03:49Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-8hyd
---
# Fewer time stamps in a window: 0.07 to 0.15 ms

**2026-11-25 · Retired.** This optimization was scoped to an earlier GPU workload. Reopen against a named film segment if its pass dominates a repeatable GPU timing there.

Sweep addendum (/private/tmp/tertius-sweep-20261111/addendum.md) A6 proposal 8. Five stamp encoders per frame, each ending the encoder before it. Careful: the render scale's controller reads these stamps, so keep the ones it needs.

