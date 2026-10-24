---
id: ter-0om4
status: open
deps: []
links: []
created: 2026-10-24T15:37:19Z
type: task
priority: 2
assignee: Alexis Purslane
---
# Unwind the nested ternary chains that the cleanup made

Before 2026-10-24 the cleanup's IDE0046 and IDE0045 turned if-return and if-assign guards into nested conditional expressions, one more per run. Find each conditional expression nested three or more levels deep in src and tests, and rewrite it as a list of guards, or as a switch expression where it chooses among the cases of a closed set. Examples: VulkanDevice's feature check, MotionIntent.RefusalFor, InputEventJsonConverter.Read. The cleanup no longer makes them (docs/plans/code-quality-tooling.md, decision 8).

