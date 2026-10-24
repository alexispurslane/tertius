---
id: ter-mh11
status: in_progress
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, simulation]
---
# Convert the unions of Simulation to the generator

Convert QueryFilter, PhysicsQuestion, LightData and SpatialIndex.Placement to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. QueryFilter's constructors check their arguments: keep the checks in its factories.

## Acceptance Criteria

No hand-written union boilerplate is left in Simulation. The union tests pass unchanged, except tests of ToString text. Every suite passes.


## Notes

**2026-10-24T14:27:38Z**

Converted QueryFilter, PhysicsQuestion, LightData and SpatialIndex.Placement to [GeneratedUnion] (SpatialIndex is now partial). Sizes (Unsafe.SizeOf, before -> after): QueryFilter 72 -> 72; PhysicsQuestion 128 -> 144 (DPose is now stored whole in its own slot, so the ray origin no longer shares the pose position; PhysicsRequest 400 -> 416; no comment names its size); LightData 60 -> 60; Placement 32 -> 32. Rewrites: no hand-written member read a private field or the kind byte; TryCenter, Passes/PassesHeldCase and the sweep properties already matched cases by pattern, so they are unchanged. QueryFilter checks moved from the constructors into the factories In, Near (three) and Where, with the same messages and parameter names; EntityQuery and FoundEntities build every filter through them. The generated constructors and implicit conversions check nothing, so the two tests of constructor and conversion checks became one test of the factories, plus a boundary test of the factories. ToString: QueryFilter, PhysicsQuestion and LightData keep their text; Placement's empty value reads Placement(none) instead of Placement(not in a grid), which nothing persists or compares. Equality: same semantics (delegate Equals equals delegate ==; double Equals treats 0 and -0 as equal, as == did; NaN is refused by the factories). Generator fix: a reference-typed slot that some case leaves unset failed CS8618; such slots are now declared nullable and read back with !, with a test in UnionDiagnosticsTests whose probe compilation now has nullable on and warnings as errors.
