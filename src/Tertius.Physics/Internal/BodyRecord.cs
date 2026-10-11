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
    public ObjectLayer Layer;
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

    /// <summary>The frame that this entity owns, or null.</summary>
    public FrameHandle OwnedFrame;
    public FrameWorld? InteriorWorld;
    public BodyID InteriorBody = BodyID.Invalid;

    public List<GhostBody>? Ghosts;

    // Per-step bookkeeping.
    public Vector3 VelocityBefore;
    public Vector3 GravityApplied;
    /// <summary>
    /// The linear velocity of an awake dynamic body when the gravity pass of step <see cref="GravityStep"/> saw it. The
    /// next pass compares it with the velocity then, to tell whether gravity accelerates the body.
    /// </summary>
    public Vector3 GravityVelocity;
    public long GravityStep = -1;
    /// <summary>For a physics-driven frame hull: the frame's proper acceleration in frame axes, from the last step.</summary>
    public Vector3 ProperAcceleration;
    public long KinematicTargetStep = -1;
    public long VelocitySetStep = -1;
    public long ReportedStep = -1;

    public bool IsHull => !OwnedFrame.IsNull;

    /// <summary>True when the primary body can move: dynamic, kinematic or a character.</summary>
    public bool IsMovable => Motion != BodyMotion.Static;

    public bool HasPrimary => World is not null;
}
