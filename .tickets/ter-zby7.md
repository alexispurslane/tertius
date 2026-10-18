---
id: ter-zby7
status: open
deps: []
links: []
created: 2026-10-18T17:25:57Z
type: chore
priority: 3
assignee: Alexis Purslane
tags: [simulation, refactor]
---
# Make QueryFilter a custom union

User idea (2026-10-18). QueryFilter (src/Tertius.Simulation/Queries/QueryFilter.cs) is a closed set of five kinds (In, Near around the asker, Near another entity, Near a point, Where) that today shares one record struct where every kind carries every field. Following the project rule for closed sets (a custom [Union] struct with HasValue and TryGetValue methods, like Tertius.Target), give each kind its own small record struct, pack them into one [Union] QueryFilter, and replace the 14 checks of QueryFilterKind in Simulation with pattern-matching switches. No change of behavior: the size stays about 64 bytes (the reference field cannot overlap the value fields), queries still allocate nothing, and StackFilters keeps its inline array of 8. Scope: the files in Queries/ and their tests.

