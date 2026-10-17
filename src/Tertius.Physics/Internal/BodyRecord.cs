using System.Numerics;
using JoltPhysicsSharp;

namespace Tertius.Physics;

/// <summary>What a Jolt body stands for. Stored in the top byte of the body's user data.</summary>
internal enum BodyKind : byte
{
    None = 0,
    /// <summary>The entity's own body: a rigid body, or the hull of a frame in its parent frame.</summary>
    Primary = 1,
    /// <summary>The static interior geometry of a frame, in the frame's own system.</summary>
    Interior = 2,
    /// <summary>A kinematic copy of a body that lives in another frame.</summary>
    Ghost = 3,
    /// <summary>The kinematic inner body of a character controller.</summary>
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

/// <summary>A kinematic copy of a body in a neighboring frame's system.</summary>
internal struct GhostBody
{
    public FrameHandle Frame;
    public FrameWorld World;
    public BodyID Body;
    /// <summary>Set by the ghost pass when the ghost is still needed this step.</summary>
    public bool Wanted;
    /// <summary>
    /// The ghost stands for an anchor in the home of a group of the anchor's own semantic frame (see
    /// <see cref="FrameTree.AnchorGhosts"/>). It has its anchor's own layer, so it meets there only what the anchor meets:
    /// a hull meets only static bodies and other hulls. Every other ghost has the ghost layer of its body.
    /// </summary>
    public bool Anchor;
}

/// <summary>Everything the backend knows about one entity's physics.</summary>
internal sealed class BodyRecord
{
    public Entity Entity;
    public BodyDesc Desc = null!;
    /// <summary>The bounding-sphere radius of the primary shape, about the body origin.</summary>
    public float Radius;
    /// <summary>
    /// The motion of the primary body as Jolt sees it. For a frame hull it follows <see cref="Driver"/>, as far as Jolt can
    /// move the hull that way.
    /// </summary>
    public BodyMotion Motion;
    /// <summary>
    /// For the owner of a frame: the driver of <see cref="OwnedFrame"/> that <see cref="Motion"/> follows. It is the
    /// frame's driver when the record was created or at the last reconcile, whichever came later.
    /// </summary>
    public MotionDriver Driver;
    /// <summary>For the owner of a frame: the <see cref="FrameTree.DriverVersion"/> of <see cref="OwnedFrame"/> that <see cref="Driver"/> follows.</summary>
    public int DriverVersion;
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

    public CharacterVirtual? Character;
    /// <summary>The desired movement velocity of a character, in frame axes. It persists until a new CharacterMove.</summary>
    public Vector3 DesiredVelocity;
    public float PendingJump;
    /// <summary>Velocity change from impulses and forces on a character, applied at its next update.</summary>
    public Vector3 PendingVelocityChange;
    public Vector3 Up = Vector3.UnitY;
    /// <summary>
    /// For a character: true when its last update stood it on walkable ground under gravity. A new character starts
    /// false, and so does a character that a hand-off moved to another frame. A growth of the system keeps it, because
    /// the character stays where it stood.
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
    /// The effective gravity that the last step applied to the body, in the axes of its home: to a dynamic rigid body
    /// while it was awake, and to a character that the ground did not hold (zero when it did).
    /// </summary>
    public Vector3 GravityApplied;
    /// <summary>For a physics-driven frame hull: the frame's proper acceleration in frame axes, from the last step.</summary>
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

    /// <summary>True when the primary body can move: dynamic, kinematic or a character.</summary>
    public bool IsMovable => Motion != BodyMotion.Static;

    public bool HasPrimary => World is not null;
}
