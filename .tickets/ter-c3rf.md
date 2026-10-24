---
id: ter-c3rf
status: closed
deps: [ter-07e3, ter-297a, ter-mh11, ter-asoh, ter-kx3k, ter-m0db]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, analyzers]
---
# Union analyzer: rules TU0001 to TU0005

Add the rules of docs/plans/union-tooling.md, section The analyzer, to analyzers/Tertius.Unions, as errors. Tests with the Roslyn analyzer test driver: one case that reports and one that does not, per rule.

## Acceptance Criteria

Each rule reports its case. The build of the whole solution is clean.


## Notes

**2026-10-24T15:06:54Z**

Built the analyzer UnionAnalyzer in analyzers/Tertius.Unions (with ClosedSetRules and SealedHierarchies), rules at error severity. TU0001: a struct with [Union] and no [GeneratedUnion]. TU0002: a built-in union with a struct case (the cases are the parameters of the constructors that the compiler declares). TU0003 dropped: the generator's TU1002 already reports a class case of a [GeneratedUnion] with the same fix, so it is the one report; union-tooling.md updated. TU0004: an abstract class or record of the assembly, not closed, with at least one subtype and only sealed ones, reported at compilation end. TU0005: an instance enum field or property named Kind beside another instance field (not its backing field). Hits in today's code, all suppressed with a Justification: TU0004 RenderPass (Rendering), CheckedInRun (Host.Tests), DriftScene (Physics.Tests), Flag (Simulation.Tests), each an open set reached through virtual members or by type, never a switch; TU0005 PhysicsOutputFault (Core) and RayQuestion (Simulation), where every field means something for every kind. TU0001 and TU0002 find nothing. The Unions tests run Roslyn 5.9.0 in process (VersionOverride), because 4.14.0 does not parse a built-in union; the analyzer itself stays on 4.14.0.

**2026-10-24T15:39:58Z**

2026-10-24: TU0004 now skips a base that declares an abstract or virtual member (not counting a record's compiler-written members or an override). The user chose this: closed records are for algebraic data types. RenderPass and DriftScene lost their suppressions; CheckedInRun and Flag keep theirs. Hand mutations: drop IsVirtual, drop IsAbstract, drop the IsImplicitlyDeclared test, count IsOverride; each failed a test.
