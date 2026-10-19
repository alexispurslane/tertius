using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>What a Jolt body stands for. Stored in the top byte of the body's user data.</summary>
internal enum BodyKind : byte
{
    None = 0,
    /// <summary>The entity's own body: a rigid body, the rigid form of a character, or the hull of a frame in its parent frame.</summary>
    Primary = 1,
    /// <summary>
    /// The interior geometry of a frame, in the frame's own system: static while the hull is static, and kinematic and at
    /// rest while it moves.
    /// </summary>
    Interior = 2,
    /// <summary>A copy of a body that lives in another frame (see <see cref="GhostBody"/>).</summary>
    Ghost = 3,
    /// <summary>The kinematic inner body of a character in its walking form.</summary>
    CharacterInner = 4,
}

/// <summary>Packs the kind and the entity into a Jolt body's 64-bit user data.</summary>
internal static class BodyUserData
{
    private const uint GenerationMask = 0xFF_FFFF;

    public static ulong Encode(BodyKind kind, Entity entity) =>
        ((ulong)kind << 56) | ((ulong)(entity.Generation & GenerationMask) << 32) | entity.Index;

    public static BodyKind Kind(ulong data) => (BodyKind)(data >> 56);

    public static uint Index(ulong data) => (uint)data;

    /// <summary>True when the user data names this entity: same slot, same low 24 bits of generation.</summary>
    public static bool Names(ulong data, Entity entity) =>
        (uint)data == entity.Index && (uint)((data >> 32) & GenerationMask) == (entity.Generation & GenerationMask);
}

/// <summary>A copy of a body in a neighboring frame's system.</summary>
internal struct GhostBody
{
    public FrameHandle Frame;
    public FrameWorld World;
    public BodyID Body;
    /// <summary>Set by the ghost pass when the ghost is still needed this step.</summary>
    public bool Wanted;
    /// <summary>
    /// The role of the ghost's object layer, which <c>JoltPhysicsBackend.GhostLayer</c> turns into the layer with the
    /// record's collision groups and mask. It is set when the ghost is made and never changes: a ghost that needs another
    /// role is made again. A child ghost of a static body has the body's own role, and a child ghost of a hull the static
    /// role of the owner's interior, so both meet hulls as their real bodies do (fork F1 of docs/plans/complete-ghosts.md).
    /// A child ghost of a body that can move and every stick-out ghost have the <see cref="BodyLayer.Ghost"/> role, which
    /// meets no hull, because the system of a stick-out ghost holds the hull of the frame that its real body sticks out of.
    /// The ghost of an anchor in the home of a group of the anchor's own semantic frame (see
    /// <see cref="FrameTree.AnchorGhosts"/>) has the <see cref="BodyLayer.FrameHull"/> role of its hull, so it meets there
    /// only what the hull meets: static bodies and other hulls.
    /// </summary>
    public BodyLayer Role;
}

/// <summary>Everything the backend knows about one entity's physics.</summary>
internal sealed class BodyRecord
{
    public Entity Entity;
    public BodyDesc Desc = null!;
    /// <summary>
    /// The description of the Jolt rigid body that stands for the entity: <see cref="Desc"/>, or for a character the
    /// description of its rigid form (see <c>JoltPhysicsBackend.RigidForm</c>). The primary rigid body takes its shape, mass,
    /// material and damping from it, and so do the conversions between instant velocities and chords.
    /// </summary>
    public BodyDesc Material = null!;
    /// <summary>The bounding-sphere radius of the primary shape, about the body origin.</summary>
    public float Radius;
    /// <summary>
    /// The motion of the primary body as Jolt sees it. For a frame hull, physics gives the record the motion of each step at
    /// the start of that step (see <see cref="FrameTree.HullMotionInStep"/>), and a record that the simulation creates
    /// between two steps, at a spawn or a rehome, starts with the motion of the next step, which it runs first. The owner of
    /// a root frame, which has no hull, gets the static motion of its interior. For a character it is its form: <see cref="BodyMotion.Character"/> for the walking form, a
    /// Jolt CharacterVirtual in <see cref="Character"/>, and <see cref="BodyMotion.Dynamic"/> for the rigid form, a dynamic
    /// Jolt body in <see cref="Body"/> whose rotation is locked (see <c>JoltPhysicsBackend.NextCharacterMotion</c>).
    /// </summary>
    public BodyMotion Motion;
    /// <summary>
    /// The entity's collision groups and mask now: those of <see cref="Desc"/>, until a collision intent changes them.
    /// Every Jolt body of the record carries them, except the hull of a frame (see <see cref="Layers"/>).
    /// </summary>
    public CollisionGroups CollisionGroups;
    /// <inheritdoc cref="CollisionGroups"/>
    public CollisionMask CollisionMask;
    /// <summary>True when the primary shape is or contains a mesh. Only static bodies can have one.</summary>
    public bool HasMesh;

    /// <summary>The frame whose system holds the primary body or the character. Null if there is none.</summary>
    public FrameHandle Frame;
    public FrameWorld? World;
    public int MemberIndex = -1;
    public BodyID Body = BodyID.Invalid;

    /// <summary>The Jolt character of a character in its walking form, or null.</summary>
    public CharacterVirtual? Character;
    /// <summary>
    /// The desired movement velocity of a character, in frame axes. It persists until a new CharacterMove, in both forms.
    /// </summary>
    public Vector3 DesiredVelocity;
    /// <summary>The speed of the jump that the walking form makes in its next update, if it stands. Zero between steps.</summary>
    public float PendingJump;
    /// <summary>
    /// Velocity change from impulses and forces on a character in its walking form, applied at its next update. Zero
    /// between steps. The rigid form takes impulses and forces as Jolt bodies do.
    /// </summary>
    public Vector3 PendingVelocityChange;
    /// <summary>
    /// The up of a character, in frame axes. The walking form turns it against the gravity when it touches something. The
    /// rigid form keeps it, so that the character walks again with the up that it had.
    /// </summary>
    public Vector3 Up = Vector3.UnitY;
    /// <summary>
    /// For a character: true when its last update stood it on walkable ground under gravity. A new character starts
    /// false, and so does a character that a hand-off moved to another frame. A growth of the system keeps it, because
    /// the character stays where it stood. A character in its rigid form never stands.
    /// </summary>
    public bool Standing;

    /// <summary>The frame that this entity owns, or null.</summary>
    public FrameHandle OwnedFrame;
    public FrameWorld? InteriorWorld;
    public BodyID InteriorBody = BodyID.Invalid;

    /// <summary>
    /// The ghosts of this record's body in other systems, at most one in each. Each one is also among the ghosts of its
    /// system (see <see cref="FrameWorld.AddGhost"/>), which a shift of the system's frame moves.
    /// </summary>
    public List<GhostBody>? Ghosts;

    // Per-step bookkeeping.
    public Vector3 VelocityBefore;
    /// <summary>
    /// For a dynamic hull: Jolt's velocities after the intents of the step, which can set them or add an impulse, and
    /// before the update, for its frame's acceleration and angular acceleration over the step.
    /// </summary>
    public Vector3 VelocityAfterIntents, AngularVelocityAfterIntents;
    /// <summary>
    /// For a dynamic hull: its acceleration and angular acceleration in the last update, in the axes of its home: the
    /// gravity that the step applied plus its proper acceleration, less the change of velocity that the intents made
    /// before the update, and the change of Jolt's angular velocity in the update, each divided by the step. A hand-off
    /// in the step gives the linear one the acceleration that made Jolt's chord in the new home, in the new home's axes
    /// (see JoltPhysicsBackend.HandedOffVelocities), and turns the angular one into the new home's axes, as it turns the
    /// angular velocity (see <see cref="FrameMotion.AngularAccelerationToParent"/>).
    /// </summary>
    public Double3 StepAcceleration;
    public Vector3 AngularAcceleration;
    /// <summary>The step whose intent teleported the body, or -1.</summary>
    public long TeleportStep = -1;
    /// <summary>The pose in its home, with a unit rotation, at which the teleport of <see cref="TeleportStep"/> put the body.</summary>
    public DPose TeleportPose;
    /// <summary>
    /// The effective gravity that the last step applied to the body, in the axes of its home: to a dynamic rigid body
    /// while it was awake, and to a character that the ground did not hold (zero when it did).
    /// </summary>
    public Vector3 GravityApplied;
    /// <summary>For a dynamic frame hull: the frame's proper acceleration in frame axes, from the last step.</summary>
    public Vector3 ProperAcceleration;
    public long KinematicTargetStep = -1;
    public long VelocitySetStep = -1;
    /// <summary>
    /// For a character: the step whose update starts from an instant velocity that the game gave it, by a spawn, a
    /// teleport or SetVelocity. That update moves it by the chord of the instant velocity. A hand-off gives a character
    /// its chord right away, as it does a rigid body.
    /// </summary>
    public long InstantVelocityStep = -1;
    public long ReportedStep = -1;

    public bool IsHull => !OwnedFrame.IsNull;

    /// <summary>True for a character, in either of its forms.</summary>
    public bool IsCharacter => Desc.Motion == BodyMotion.Character;

    /// <summary>True when the primary body can move: dynamic, kinematic or a character.</summary>
    public bool IsMovable => Motion != BodyMotion.Static;

    /// <summary>
    /// True when the body's ghosts reach the content of a frame, its bubble grown by its excess, and not only its bubble
    /// (fork F2 of docs/plans/complete-ghosts.md): a static body, a kinematic body or a hull. A stick-out ghost of a member
    /// of the frame is kinematic and has the ghost role, so it meets none of them but a sensor, and only a ghost of the
    /// body in the frame brings the two together. A dynamic plain body or a character reaches only the bubble: a member
    /// that sticks out meets it through the member's own stick-out ghost, and a ghost of the body in the frame would make
    /// a mirror pair of two kinematic ghosts, in which each real body meets the other as an immovable body.
    /// </summary>
    public bool GhostsReachContent => IsHull || Motion is BodyMotion.Static or BodyMotion.Kinematic;

    public bool HasPrimary => World is not null;
}
