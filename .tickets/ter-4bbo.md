---
id: ter-4bbo
status: open
deps: []
links: []
created: 2026-10-24T20:22:35Z
type: chore
priority: 3
assignee: Alexis Purslane
---
# Split EntityTable.AssertInvariants into named steps

The pre-commit report of 2026-10-24 (ter-hxdc) found EntityTable.AssertInvariants at 186 lines and a cyclomatic complexity of 66. Split it into named steps, for example the free list and the lists of intents, the chunk books, and the checks of each live slot, so that each step reads alone. The checks and their cost, O(slots in use), stay as they are.

