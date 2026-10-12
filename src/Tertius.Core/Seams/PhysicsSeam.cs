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

/// <summary>
/// A collision shape. Shape descriptions are immutable and shared. Backends cache native shapes by reference. The set of
/// shapes is closed: only the six records below derive from it.
/// </summary>
public abstract record ShapeDesc
{
    private protected ShapeDesc()
    {
    }

    /// <summary>
    /// Why no physics backend can build this shape, or null when its parameters are in range. The message completes a
    /// sentence that names the shape. A backend can refuse a shape for more reasons, for example a convex hull whose points
    /// coincide: see <see cref="IPhysicsBackend.ShapeProblem"/>.
    /// </summary>
    public string? Problem()
    {
        switch (this)
        {
            case BoxShapeDesc { HalfExtents: var half }:
                return IsFinite(half) && half.X >= 0 && half.Y >= 0 && half.Z >= 0
                    ? null
                    : $"has the half extents {half}, which must be finite and not negative";
            case SphereShapeDesc { Radius: var radius }:
                return float.IsFinite(radius) && radius > 0 ? null : $"has the radius {radius}, which must be finite and positive";
            case CapsuleShapeDesc capsule:
                if (!(float.IsFinite(capsule.Radius) && capsule.Radius > 0))
                    return $"has the radius {capsule.Radius}, which must be finite and positive";
                return float.IsFinite(capsule.HalfHeight) && capsule.HalfHeight >= 0
                    ? null
                    : $"has the half height {capsule.HalfHeight}, which must be finite and not negative";
            case ConvexHullShapeDesc hull:
                if (hull.Points is not { Length: >= 4 } points)
                    return "has fewer than 4 points";
                return AllFinite(points) ? null : "has a point that is not finite";
            case MeshShapeDesc mesh:
                if (mesh.Vertices is not { } vertices || mesh.Indices is not { } indices)
                    return "has no vertex list or no index list";
                if (indices.Length == 0 || indices.Length % 3 != 0)
                    return $"has {indices.Length} indices, which must be a positive multiple of 3";
                foreach (int index in indices)
                    if ((uint)index >= (uint)vertices.Length)
                        return $"has the index {index}, which is outside its {vertices.Length} vertices";
                return AllFinite(vertices) ? null : "has a vertex that is not finite";
            case CompoundShapeDesc compound:
                if (compound.Children is not { Length: > 0 } children)
                    return "has no child shape";
                for (int i = 0; i < children.Length; i++)
                {
                    CompoundChild child = children[i];
                    if (child.Shape is null)
                        return $"has no shape for child {i}";
                    Quaternion rotation = child.Pose.Rotation;
                    if (!IsFinite(child.Pose.Position) || !float.IsFinite(rotation.X + rotation.Y + rotation.Z + rotation.W) || rotation.LengthSquared() == 0)
                        return $"has child {i} at the pose {child.Pose}, whose position and rotation must be finite and whose rotation must not be zero";
                    if (child.Shape.Problem() is { } problem)
                        return $"has child {i}, which {problem}";
                }
                return null;
            default:
                return $"is a {GetType().Name}, which physics does not know";
        }
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);

    private static bool AllFinite(Vector3[] points)
    {
        foreach (Vector3 point in points)
            if (!IsFinite(point))
                return false;
        return true;
    }

    /// <summary>True when the shape is a mesh or a compound that contains one. Only static bodies can use those.</summary>
    public bool ContainsMesh()
    {
        switch (this)
        {
            case MeshShapeDesc:
                return true;
            case CompoundShapeDesc { Children: { } children }:
                foreach (CompoundChild child in children)
                    if (child.Shape?.ContainsMesh() == true)
                        return true;
                return false;
            default:
                return false;
        }
    }
}

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
    ShapeDesc? InteriorShape = null)
{
    /// <summary>
    /// Why no physics backend can create a body from this description, or null when one can. The message completes a
    /// sentence that names the body. <paramref name="ownsFrame"/> is true for the owner of a frame. Its body is the frame's
    /// static interior, and when the frame has a parent, also a hull in the parent frame. The hull's motion comes from the
    /// frame's driver (see <see cref="FrameHullProblem"/>), so the description's motion and mass do not matter here. Any
    /// other body has the motion of the description. A backend can also refuse a shape that only it can tell is broken
    /// (see <see cref="IPhysicsBackend.ShapeProblem"/>).
    /// </summary>
    public string? Problem(bool ownsFrame)
    {
        if (Shape is null)
            return "has no shape";
        if (!Enum.IsDefined(Motion))
            return $"has the motion {Motion}, which BodyMotion does not define";
        if (!Enum.IsDefined(Layer))
            return $"has the layer {Layer}, which BodyLayer does not define";
        if (Shape.Problem() is { } shape)
            return $"has a shape that {shape}";
        // Jolt divides by the mass, so a subnormal mass gives an infinite inverse mass. A body that does not need a mass can have 0.
        if (!(Mass == 0 || (float.IsNormal(Mass) && Mass > 0)))
            return $"has the mass {Mass}, which must be 0 or a finite positive number of at least 1.2E-38";
        if ((NotFinite("friction", Friction) ?? NotFinite("restitution", Restitution) ?? NotFinite("linear damping", LinearDamping) ??
             NotFinite("angular damping", AngularDamping)) is { } material)
            return material;
        if (ownsFrame)
        {
            if (Motion == BodyMotion.Character)
                return "owns a frame, so it cannot be a character";
            return InteriorShape?.Problem() is { } interior ? $"has an interior shape that {interior}" : null;
        }
        if (Motion == BodyMotion.Character && Shape is not CapsuleShapeDesc)
            return $"is a character, so its shape must be a CapsuleShapeDesc, not a {Shape.GetType().Name}";
        if (Motion != BodyMotion.Static && Shape.ContainsMesh())
            return $"is {Motion.ToString().ToLowerInvariant()}, and its shape is or holds a mesh, which only a static body or a frame interior can have";
        if (Motion == BodyMotion.Dynamic && !(Mass > 0))
            return $"is dynamic, so its mass must be positive, not {Mass}";
        return null;
    }

    private static string? NotFinite(string name, float value) => float.IsFinite(value) ? null : $"has the {name} {value}, which must be finite";

    /// <summary>
    /// Why the hull of a frame with this description cannot move the way that <paramref name="driver"/> needs, or null
    /// when it can. The physics driver needs a dynamic hull, which has mass and is not a mesh. The Kepler driver needs a
    /// kinematic hull, which is not a mesh. Any hull can be static. The message completes a sentence that names the hull.
    /// </summary>
    public string? FrameHullProblem(MotionDriver driver) => driver switch
    {
        MotionDriver.Physics or MotionDriver.Kepler or MotionDriver.Spin when Shape.ContainsMesh() =>
            $"is a mesh, which never moves, so it cannot follow the {driver} driver",
        MotionDriver.Physics when !(Mass > 0) => $"has a mass of {Mass}, so it cannot be dynamic for the Physics driver",
        _ => null,
    };
}

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

    /// <summary>
    /// Filled by <see cref="IPhysicsBackend.ApplyChanges"/>: one message for each state that only a bug produces, which the
    /// backend found and skipped while it applied the batch, for example a change that it could not apply. A Debug build
    /// breaks a contract instead. The simulation records each message in its errors.
    /// </summary>
    public readonly List<string> Errors = new();

    public void Clear()
    {
        FramesCreated.Clear();
        BodiesDestroyed.Clear();
        FramesDestroyed.Clear();
        BodiesCreated.Clear();
        Errors.Clear();
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

/// <summary>
/// The state of one body after a step, relative to <see cref="Frame"/>: the frame whose physics system holds the body.
/// </summary>
public readonly record struct BodyState(Entity Entity, FrameHandle Frame, DPose Pose, Vector3 LinearVelocity, Vector3 AngularVelocity);

/// <summary>
/// The state of one character after a step. <see cref="Standing"/> is true when physics stood the character on walkable
/// ground under gravity in the step: the effective gravity was at least <see cref="FrameTree.ZeroGravityThreshold"/>,
/// and the character did not move away from the ground. It is false for a character in the air, on steep ground or
/// weightless, and for a character that a hand-off moved to another frame in the step, which has not stood there yet.
/// </summary>
public readonly record struct CharacterState(Entity Entity, bool Standing);

/// <summary>
/// Physics moved an entity's body to another frame during the step. <see cref="From"/> is the frame that the entity was in
/// when the step began (for the owner of a frame, the frame's parent), and <see cref="To"/> is a different frame. Pose and
/// velocities are in the new frame.
/// </summary>
public readonly record struct HandOff(Entity Entity, FrameHandle From, FrameHandle To, DPose Pose, Vector3 LinearVelocity, Vector3 AngularVelocity);

/// <summary>
/// A contact that started during the step between two different entities. Point and normal are in <see cref="Frame"/>,
/// the frame whose physics system holds both bodies, and the normal points from <see cref="A"/> toward <see cref="B"/>.
/// </summary>
public readonly record struct ContactStarted(Entity A, Entity B, FrameHandle Frame, Double3 Point, Vector3 Normal, float Impulse);

/// <summary>
/// The new state of a physics-driven frame, relative to its parent after this step. If the step also handed the
/// frame off, the parent is the hand-off's <see cref="HandOff.To"/>, so the sync point applies hand-offs first.
/// </summary>
public readonly record struct FrameState(FrameHandle Frame, DPose LocalPose, Double3 LinearVelocity, Vector3 AngularVelocity, Vector3 ProperAcceleration);

/// <summary>
/// Results of one step. The backend fills it on the physics thread. The sync point reads and clears it. Every entity
/// that the output names is live and has a body, and every frame that it names is alive and not null. A hand-off leaves
/// the frame that its entity is in, each entity hands off at most once, and the owner of a frame hands off only when
/// the frame has the physics driver. A frame state names a frame with the physics driver and a parent, whose owner has
/// a body. A contact names two different entities. A character state names a live character, and each character has
/// at most one state in a step. <see cref="PhysicsOutputCheck"/> holds the full rules. The sync point treats any other
/// output as a bug in the backend: see ARCHITECTURE.md, "Threads and the tick", step 2.
/// </summary>
public sealed class PhysicsStepOutput
{
    /// <summary>Bodies that moved: awake dynamic, kinematic and character bodies.</summary>
    public readonly List<BodyState> Bodies = new();
    public readonly List<HandOff> HandOffs = new();
    public readonly List<ContactStarted> Contacts = new();
    public readonly List<FrameState> Frames = new();

    /// <summary>
    /// The state of every character, once each, after the hand-offs of the step. So a character that a hand-off moved
    /// reports from its new frame.
    /// </summary>
    public readonly List<CharacterState> Characters = new();

    /// <summary>
    /// One message for each state that only a bug produces, which the backend found and skipped during the step, for
    /// example a frame whose driver its hull cannot follow. A Debug build breaks a contract instead. The sync point records
    /// each message in the simulation's errors.
    /// </summary>
    public readonly List<string> Errors = new();

    public void Clear()
    {
        Bodies.Clear();
        HandOffs.Clear();
        Contacts.Clear();
        Frames.Clear();
        Characters.Clear();
        Errors.Clear();
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

    /// <summary>
    /// Why this backend cannot build <paramref name="shape"/>, or null when it can: what <see cref="ShapeDesc.Problem"/>
    /// finds, and what only the backend can tell, for example that the points of a convex hull coincide. The message
    /// completes a sentence that names the shape. Thread-safe, so that a spawn during a tick can ask.
    /// </summary>
    string? ShapeProblem(ShapeDesc shape);
}
