---
id: ter-qfxn
status: closed
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


## Notes

**2026-10-22T13:19:01Z**

Done 2026-10-22. Slots shared by type (the user's choice of 2026-10-22, design doc decision 2). 35 tests: 22 behavior tests on generated test unions, 13 driver tests (7 diagnostics TU1001-TU1007, containers, name clash, caching, a valid union). Hand mutations, each failing its test or the build of the generated code: share no slot (build: unused slots), no padding order, trust a hand-written property, ignore extra state, each of the 7 diagnostic checks off, zero case not kind 0, Equals ignores the kind, TryGetValue unguarded, hash ignores values, HasValue always true, ToString without (none), Equals(object) ignores values, model arrays never equal, record struct and interface container keywords, no fix of a short-name clash.
