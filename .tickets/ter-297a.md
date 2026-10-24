---
id: ter-297a
status: in_progress
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, physics]
---
# Convert the unions of Physics to the generator

Convert CharacterStartVelocity and VelocityShadow to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. 

## Acceptance Criteria

No hand-written union boilerplate is left in Physics. The union tests pass unchanged, except tests of ToString text. Every suite passes.


## Notes

**2026-10-24T14:24:42Z**

Converted CharacterStartVelocity and VelocityShadow to [GeneratedUnion]. Sizes (Unsafe.SizeOf): CharacterStartVelocity 40 -> 56 bytes (HeldStart/InstantStart now keep their Vector3 values in two Vector3 slots beside the ChordStart Double3 slot, instead of widening into one Double3; nothing relied on the widening; it is a local built once per walking character per step, so the growth is accepted). VelocityShadow 80 -> 72 bytes (ShadowTracking stored as its four fields, kind byte in the padding). VelocityShadow keeps an empty value: default means no shadow (record.Shadow = default at every end of a shadow, ShadowOf returns default, Step checks HasValue), so no ZeroCase. Moved is now a switch over the cases with a null arm; its first arm must build the union explicitly, because with a bare ShadowTracking arm the switch's natural type is ShadowTracking and the null arm's default became a zero tracking with HasValue true. New VelocityShadowTests pin both arms. No persisted ToString output found. ARCHITECTURE.md now calls both generated unions.
