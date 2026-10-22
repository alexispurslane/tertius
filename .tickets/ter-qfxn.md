---
id: ter-qfxn
status: open
deps: []
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, analyzers]
---
# Union generator: write the members of a [GeneratedUnion] struct

Build analyzers/Tertius.Unions, the Roslyn incremental generator of docs/plans/union-tooling.md (sections The generator, Storage, Generated members, The empty value). GeneratedUnionAttribute in Tertius.Core with ZeroCase. Reference it as an analyzer from Directory.Build.props. Tests: driver tests of the layout decisions (which fields share a slot, which cases are stored whole, the order of the slots) and behavior tests of generated test unions (equality with NaN, ToString, TryGetValue, sizes). Convert no existing union in this ticket.

## Acceptance Criteria

A [GeneratedUnion] partial struct gets every member of the table in the design doc. Shared slots follow name and type. The build stays clean. Every suite passes.

