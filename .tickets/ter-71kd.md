---
id: ter-71kd
status: open
deps: []
links: []
created: 2026-10-24T17:07:50Z
type: task
priority: 2
assignee: Alexis Purslane
---
# BodyRecord: its primary state as a union

BodyRecord holds nullable World and Character fields whose presence the physics backend guards with contracts and [MemberNotNullWhen] (ter-2ku2 added them to remove the ! operator). Make the primary state a union of its cases, so that the type says which fields exist and the contracts go away. Found by the style analyzer work.


## Design sketch (2026-10-24)

Three groups of BodyRecord fields allow illegal states: the primary body (World, MemberIndex, Body, Character, Frame), the role (character fields, hull fields, aim fields) and the step marks (a step number or -1 beside data valid only for that step).

```csharp
// Sketch
public readonly record struct RigidPrimary(FrameHandle Frame, FrameWorld World, int MemberIndex, BodyID Body, BodyMotion Motion);
public readonly record struct WalkingPrimary(FrameHandle Frame, FrameWorld World, int MemberIndex, CharacterVirtual Character);
[GeneratedUnion(typeof(RigidPrimary), typeof(WalkingPrimary))]
internal readonly partial struct Primary; // the empty value: no primary body

internal union Role(PlainBody, CharacterState, HullState); // fixed at the spawn
internal sealed class CharacterState { public Vector3 DesiredVelocity, PendingVelocityChange, Up; public float PendingJump; public bool Standing, Held; public long? InstantVelocityStep; }
internal sealed class HullState { public FrameHandle OwnedFrame; public Interior Interior; public Double3 StepAcceleration; ... public long? HandOffStep; }

internal readonly record struct Impulse(long Step, Double3 Linear, Double3 Angular);
internal readonly record struct Teleport(long Step, DPose Pose);
internal readonly record struct Aim(long Step, MotionSample Record, FrameHandle Home);

internal sealed class BodyRecord
{
    public Entity Entity;
    public required BodyDesc Desc, Material;
    public Primary Primary;
    public required Role Role;
    public Impulse? LastImpulse;   // replaces ImpulseStep, ImpulseVelocityChange, ImpulseAngularVelocityChange
    public Teleport? LastTeleport; // replaces TeleportStep, TeleportPose
    ...
}
```

HasPrimary, HasInterior, IsCharacter and IsHull become pattern matches, and their contracts and [MemberNotNullWhen] go away.

Forks to design twice when the ticket starts:
- Whether Motion moves into the primary case. A root frame owner with no hull has no primary body but a static interior motion today.
- Whether the role is a union of classes, or nullable state objects beside a role enum.
- Whether the generator supports reference fields in shared slots (FrameWorld, CharacterVirtual), or the primary cases are classes in a built-in union.
