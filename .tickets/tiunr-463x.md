---
id: tiunr-463x
status: in_progress
deps: [tiunr-k2wn]
links: []
created: 2026-11-07T00:56:38Z
type: task
priority: 1
assignee: Alexis Purslane
parent: tiunr-lmmx
---
# Design B: guide existing reflection reconstruction with temporal variance

Starting from the exact integrated H13 commit, add per-pixel temporal first/second moments of the accepted raw sample stream to the existing temporal owner. Use measured temporal fluctuation to guide reconstruction; do not add a parallel radiance accumulator or rays. H07 spatial residuals are not this statistic. Return actual frames, separate means and noise/error observables. No automatic promotion.


## Notes

**2026-11-07T01:04:05Z**

Ready for isolated T3 worktree launch from the same H13 integration commit as A. Design adds measured temporal-fluctuation statistics to the existing temporal owner, not another radiance accumulator. Fresh execution bounds/setup in external AB-BRIEF.md. Ray generation stays fixed. No workflows or extra agents.
