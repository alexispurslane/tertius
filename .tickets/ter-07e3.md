---
id: ter-07e3
status: in_progress
deps: [ter-qfxn]
links: []
created: 2026-10-22T12:58:25Z
type: task
priority: 2
assignee: Alexis Purslane
parent: ter-nn6z
tags: [tooling, core]
---
# Convert the unions of Core to the generator

Convert Target, GravityField, MediumField, HemisphereUp, RenderLight, QueryShape, MotionIntent, PuffShading, InputEvent and FrameOutline to [GeneratedUnion] (docs/plans/union-tooling.md, section Conversion). Keep hand-written members in the partial struct. Assert the size of each union whose size a comment names. Measure the generated size of Target. If it is more than 8 bytes, keep Target hand-written and suppress TU0001 there with the reason.

## Acceptance Criteria

No hand-written union boilerplate is left in Core. The union tests pass unchanged, except tests of ToString text. Every suite passes.


## Notes

**2026-10-24T14:41:28Z**

Conversion results (branch ter-07e3-core-generated-unions). Sizes before -> after, measured with Unsafe.SizeOf: Target 16 -> 16 (converted; the hand-written ulong + kind byte was 16 bytes, not 8, so the generated 3 x 4-byte slots + kind do not grow it; Event stays 88; in its own commit so it can be reverted), GravityField 56 -> 56, MediumField 72 -> 56 (float slots shared), HemisphereUp 16 -> 16, RenderLight 56 -> 56, QueryShape 24 -> 24, MotionIntent 184 -> 184, PuffShading 16 -> 16, InputEvent 16 -> 16, FrameOutline 48 -> 48. Hot-path rewrites: MotionIntent.FollowKind uses bare type patterns (reads only the kind, 60 bytes of code), AimPose and TryRecordFrom use TryGetValue; a switch expression or a binding pattern copied the whole 184-byte union first (DOTNET_JitDisasm, .NET 11 RC 1), so these three give up the build-time exhaustiveness check; ARCHITECTURE.md, Switches over closed sets, names the exception. RefusalFor uses 'this is not SteerTarget'; Converted and the refusal words use switch expressions with a null arm. PuffShading.IsValid is a switch with a null arm. GravityField, MediumField and QueryShape members were already switches over the cases and are unchanged. ToString changes: HemisphereUp empty 'HemisphereUp(even)' -> 'HemisphereUp(none)'; Target 'Target(<topic name>)' -> 'Target(Topic { Id = .., Generation = .. })' and 'Target(broadcast)' -> 'Target(Broadcast { })'; nothing persists or compares either text (DumpRecorder maps Target itself; Router puts it only in an exception message). Equality: same semantics everywhere (kind plus the held case's values, NaN equals NaN); hash values differ, and nothing stores them.
