---
id: t0-y4qv
status: in_progress
deps: []
links: []
created: 2026-11-12T19:00:12Z
type: task
priority: 2
assignee: Alexis Purslane
parent: t7-3e22
---
# Compare the fast history with the own-sample mean in the block test, not with the filtered slow history

Design: docs/plans/bounce-light-confidence.md, section 8 (R, after take T3's filter test), section 12 (images), section 17. Branch t7-3e22-retained-deviation. The filter's write-back into S made D = F - S nonzero in a still: 2.1e-2 false windows per block-draw with the filter on, 1.9e-4 with it off (workbench job filter-ab, 2026-11-12).

