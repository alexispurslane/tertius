using System.Numerics;

namespace Tertius;

public enum BodyMotion : byte
{
    Static,
    Kinematic,
    Dynamic,
    /// <summary>A capsule character controller. Its shape must be a <see cref="CapsuleShapeDesc"/>.</summary>
    Character,
}

/// <summary>Object layers. See the collision table in ARCHITECTURE.md.</summary>
public enum BodyLayer : byte
{
    Static,
    Moving,
    FrameHull,
    Ghost,
}

/// <summary>A collision shape. Shape descriptions are immutable and shared. Backends cache native shapes by reference.</summary>
public abstract record ShapeDesc;

public sealed record BoxShapeDesc(Vector3 HalfExtents) : ShapeDesc;

public sealed record SphereShapeDesc(float Radius) : ShapeDesc;

/// <summary>A capsule along +Y. The total height is 2 · (HalfHeight + Radius).</summary>
public sealed record CapsuleShapeDesc(float HalfHeight, float Radius) : ShapeDesc;

public sealed record ConvexHullShapeDesc(Vector3[] Points) : ShapeDesc;

/// <summary>A triangle mesh. Only static bodies can use it.</summary>
public sealed record MeshShapeDesc(Vector3[] Vertices, int[] Indices) : ShapeDesc;

public sealed record CompoundShapeDesc(CompoundChild[] Children) : ShapeDesc;

public readonly record struct CompoundChild(ShapeDesc Shape, Pose Pose);

/// <summary>
/// The physics description of an entity. For an entity that owns a frame, <see cref="Shape"/> is the hull body in
/// the parent frame, and <see cref="InteriorShape"/> (or <see cref="Shape"/> if null) is static geometry in the
/// frame's own system.
/// </summary>
public sealed record BodyDesc(
    ShapeDesc Shape,
    BodyMotion Motion = BodyMotion.Dynamic,
    BodyLayer Layer = BodyLayer.Moving,
    float Mass = 1f,
    float Friction = 0.5f,
    float Restitution = 0.1f,
    float LinearDamping = 0.05f,
    float AngularDamping = 0.05f,
    ShapeDesc? InteriorShape = null);

[Flags]
public enum IntentFlags : ushort
{
    None = 0,
    SetVelocity = 1 << 0,
    SetAngularVelocity = 1 << 1,
    KinematicTarget = 1 << 2,
    Teleport = 1 << 3,
    CharacterMove = 1 << 4,
    Jump = 1 << 5,
}

/// <summary>
/// What gameplay asks physics to do with one body during the next step. Vectors are in the entity's frame axes.
/// Forces, torques and impulses add up during a tick. The other fields keep the last value written.
/// </summary>
public struct PhysicsIntent
{
    public Vector3 Force;
    public Vector3 Torque;
    public Vector3 Impulse;
    public Vector3 AngularImpulse;
    public Vector3 Velocity;
    public Vector3 AngularVelocity;
    /// <summary>The target pose for a kinematic body, in frame coordinates. Used with <see cref="IntentFlags.KinematicTarget"/>.</summary>
    public DPose Target;
    /// <summary>The new pose for a teleport, in frame coordinates. Used with <see cref="IntentFlags.Teleport"/>.</summary>
    public DPose TeleportPose;
    /// <summary>The desired movement velocity of a character.</summary>
    public Vector3 CharacterVelocity;
    public float JumpSpeed;
    public IntentFlags Flags;

    public readonly bool IsEmpty =>
        Flags == IntentFlags.None && Force == Vector3.Zero && Torque == Vector3.Zero &&
        Impulse == Vector3.Zero && AngularImpulse == Vector3.Zero;

    public void Clear() => this = default;
}

/// <summary>A body that physics must create at the sync point.</summary>
public readonly record struct BodyCreate(
    Entity Entity,
    FrameHandle Frame,
    BodyDesc Desc,
    DPose Pose,
    Vector3 LinearVelocity,
    Vector3 AngularVelocity);

/// <summary>
/// The structural changes of one sync point. Physics applies them in this order: <see cref="BodiesDestroyed"/>,
/// <see cref="FramesDestroyed"/>, <see cref="BodiesCreated"/>. <see cref="FramesCreated"/> is informational: a
/// frame's physics system can be created lazily with its first body. A body that moves between frames outside a
/// hand-off appears in <see cref="BodiesDestroyed"/> and then in <see cref="BodiesCreated"/> in the same batch.
/// </summary>
public sealed class PhysicsChanges
{
    /// <summary>Bodies of entities that died or that move to another frame. Physics removes them and any ghosts.</summary>
    public readonly List<Entity> BodiesDestroyed = new();
    /// <summary>Frames destroyed at this sync point, after their bodies were destroyed or moved.</summary>
    public readonly List<FrameHandle> FramesDestroyed = new();
    /// <summary>Bodies to create, with poses and velocities in their frames.</summary>
    public readonly List<BodyCreate> BodiesCreated = new();
    /// <summary>Frames created at this sync point.</summary>
    public readonly List<FrameHandle> FramesCreated = new();

    public void Clear()
    {
        FramesCreated.Clear();
        BodiesDestroyed.Clear();
        FramesDestroyed.Clear();
        BodiesCreated.Clear();
    }
}

/// <summary>Read-only inputs to one physics step. Everything here is fixed for the duration of the step.</summary>
public readonly struct PhysicsStepContext
{
    public PhysicsStepContext(long tick, float deltaTime, double time, FrameTree frames, ReadOnlyMemory<PhysicsIntent> intents, ReadOnlyMemory<Entity> intentEntities)
    {
        Tick = tick;
        DeltaTime = deltaTime;
        Time = time;
        Frames = frames;
        Intents = intents;
        IntentEntities = intentEntities;
    }

    public long Tick { get; }
    public float DeltaTime { get; }
    /// <summary>Simulation time at the start of the step, in seconds.</summary>
    public double Time { get; }
    /// <summary>The frame tree. Read-only during the step.</summary>
    public FrameTree Frames { get; }
    /// <summary>Intents by entity slot index. Only the slots in <see cref="IntentEntities"/> are meaningful.</summary>
    public ReadOnlyMemory<PhysicsIntent> Intents { get; }
    /// <summary>The entities that wrote an intent during the previous tick.</summary>
    public ReadOnlyMemory<Entity> IntentEntities { get; }
}

/// <summary>The state of one body after a step, in its frame.</summary>
public readonly record struct BodyState(Entity Entity, FrameHandle Frame, DPose Pose, Vector3 LinearVelocity, Vector3 AngularVelocity);

/// <summary>Physics moved an entity's body to another frame during the step. Pose and velocities are in the new frame.</summary>
public readonly record struct HandOff(Entity Entity, FrameHandle From, FrameHandle To, DPose Pose, Vector3 LinearVelocity, Vector3 AngularVelocity);

/// <summary>A contact that started during the step. Point and normal are in the frame of <see cref="A"/>.</summary>
public readonly record struct ContactStarted(Entity A, Entity B, FrameHandle Frame, Double3 Point, Vector3 Normal, float Impulse);

/// <summary>
/// The new state of a physics-driven frame, relative to its parent after this step. If the step also handed the
/// frame off, the parent is the hand-off's <see cref="HandOff.To"/>, so the sync point applies hand-offs first.
/// </summary>
public readonly record struct FrameState(FrameHandle Frame, DPose LocalPose, Double3 LinearVelocity, Vector3 AngularVelocity, Vector3 ProperAcceleration);

/// <summary>Results of one step. The backend fills it on the physics thread. The sync point reads and clears it.</summary>
public sealed class PhysicsStepOutput
{
    /// <summary>Bodies that moved: awake dynamic, kinematic and character bodies.</summary>
    public readonly List<BodyState> Bodies = new();
    public readonly List<HandOff> HandOffs = new();
    public readonly List<ContactStarted> Contacts = new();
    public readonly List<FrameState> Frames = new();

    public void Clear()
    {
        Bodies.Clear();
        HandOffs.Clear();
        Contacts.Clear();
        Frames.Clear();
    }
}

/// <summary>
/// The physics engine as the simulation sees it. <see cref="ApplyChanges"/> runs at the sync point.
/// <see cref="Step"/> runs on the physics thread while gameplay runs.
/// </summary>
public interface IPhysicsBackend : IDisposable
{
    void ApplyChanges(PhysicsChanges changes, FrameTree frames);

    void Step(in PhysicsStepContext context, PhysicsStepOutput output);

    /// <summary>The radius of the bounding sphere of a shape. Used for hand-off and rendering.</summary>
    float BoundingRadius(ShapeDesc shape);
}
