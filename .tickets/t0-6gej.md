---
id: t0-6gej
status: open
deps: []
links: []
created: 2026-11-14T15:07:19Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t0-qwx8
---
# The light-age record counts a change only over an area (sub-pixel strips, step 1)

docs/plans/sub-pixel-strips.md, sections 5.2 and 17, step 1. Extract insideChangedArea (the opening of FSR's light-change mask) and use it in lightAgeOf, with its three tests and ARCHITECTURE.md. Branch t0-qwx8-area-changes; merges into main before Design X, because it removes the young records (false light evidence) in a still: 0.136 % of the cabin's pixels with jitter. Scheduled as the next build after the Design X builder's current step (t0-e8rh), not in parallel. Before the merge, merge main into a scratch copy of t7-3e22-retained-deviation to see the conflict in lightAgeOf and screen_trace.glsl.

