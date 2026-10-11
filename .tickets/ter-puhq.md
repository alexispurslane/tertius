---
id: ter-puhq
status: open
deps: [ter-7e55]
links: []
created: 2026-10-11T14:07:11Z
type: task
priority: 3
assignee: Alexis Purslane
parent: ter-tanj
tags: [design, performance]
---
# Make the entity liveness check a Debug-only contract where only a bug can pass a stale handle

The user's idea (2026-10-11). Today every member that takes an Entity checks its generation in every build, through EntityTable.LiveSlot, which cost 5.6 to 15.8 percent on WorldTickBenchmarks. Split the checks by who can hold a stale handle. Inside the engine, and on the running entity's own Self members, the entity is live by construction, so the check can be Contract.Requires. Game code can hold a handle to an entity that died since, so the public API keeps its always-on check, its Try methods and its false returns. First measure which checks the benchmark cost comes from. This reverses part of a CLAUDE.md decision, so the user decides before the change.

## Acceptance Criteria

The benchmark cost of the checks is measured per call site before and after. CLAUDE.md records the new rule.

