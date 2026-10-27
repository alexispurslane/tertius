---
id: ter-a0al
status: closed
deps: [ter-4vzc]
links: []
created: 2026-10-27T02:06:33Z
type: task
priority: 3
assignee: Alexis Purslane
tags: [rendering, refactor]
---
# One rule spreads a compute pass's workgroups over rows

Found in the review of the merge of the prefilter profile into skinning (2026-10-26). DeformPass.Grid (uint) and PrefilterLevels.Grid (int) are two copies of one rule: spread N workgroups over rows of at most ComputeLimits.MaxGroupsPerDimension (65,535, the spec's least maxComputeWorkGroupCount per dimension). Make one ComputeLimits.Grid that both passes and their tests use, with its tests (the row count, the last row's width, the largest count). Do it after ter-4vzc merges into main.

