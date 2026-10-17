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

/// <summary>
/// Object layers, which the physics backend calls roles. See the collision table in ARCHITECTURE.md. Collision groups
/// refine the table: see <see cref="BodyDesc.CollisionGroups"/> and <see cref="BodyDesc.CollisionMask"/>.
/// </summary>
public enum BodyLayer : byte
{
    Static,
    Moving,
    FrameHull,
    Ghost,
}

/// <summary>The kind of a <see cref="ShapeDesc"/>: one for each of the six shape records.</summary>
public enum ShapeKind : byte
{
    Box,
    Sphere,
    Capsule,
    ConvexHull,
    Mesh,
    Compound,
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

    /// <summary>Which of the six shape records this is.</summary>
    public ShapeKind Kind => this switch
    {
        BoxShapeDesc => ShapeKind.Box,
        SphereShapeDesc => ShapeKind.Sphere,
        CapsuleShapeDesc => ShapeKind.Capsule,
        ConvexHullShapeDesc => ShapeKind.ConvexHull,
        MeshShapeDesc => ShapeKind.Mesh,
        CompoundShapeDesc => ShapeKind.Compound,
        _ => throw new NotSupportedException($"The shape type {GetType().Name} is not one of the six shape records."),
    };

    /// <summary>
    /// The radius, in m, of the smallest sphere about the body's origin that holds the shape, rounded up to the next float.
    /// It is exact for every shape, also for a compound, whose children it places at their poses. A mesh counts only the
    /// vertices that a triangle uses. The cost grows with the number of points, vertices and children, so a caller that
    /// asks often keeps the answer. Throws <see cref="ArgumentException"/> for a convex hull, mesh or compound without its
    /// arrays, and for a mesh index outside its vertices.
    /// </summary>
    public float BoundingRadius()
    {
        double radius = BoundingRadius(this, DPose.Identity);
        // Round up, so that the float radius still bounds the shape.
        float result = (float)radius;
        return result < radius ? MathF.BitIncrement(result) : result;
    }

    private static double BoundingRadius(ShapeDesc shape, in DPose pose)
    {
        switch (shape)
        {
            case BoxShapeDesc box:
            {
                double max = 0;
                Vector3 h = box.HalfExtents;
                for (int corner = 0; corner < 8; corner++)
                {
                    var p = new Double3((corner & 1) != 0 ? h.X : -h.X, (corner & 2) != 0 ? h.Y : -h.Y, (corner & 4) != 0 ? h.Z : -h.Z);
                    max = Math.Max(max, pose.TransformPoint(p).Length);
                }
                return max;
            }
            case SphereShapeDesc sphere:
                return pose.Position.Length + sphere.Radius;
            case CapsuleShapeDesc capsule:
            {
                double top = pose.TransformPoint(new Double3(0, capsule.HalfHeight, 0)).Length;
                double bottom = pose.TransformPoint(new Double3(0, -capsule.HalfHeight, 0)).Length;
                return Math.Max(top, bottom) + capsule.Radius;
            }
            case ConvexHullShapeDesc hull:
            {
                if (hull.Points is null)
                    throw new ArgumentException("A convex hull needs points.");
                double max = 0;
                foreach (Vector3 p in hull.Points)
                    max = Math.Max(max, pose.TransformPoint(p).Length);
                return max;
            }
            case MeshShapeDesc mesh:
            {
                if (mesh.Vertices is null || mesh.Indices is null)
                    throw new ArgumentException("A mesh needs vertices and indices.");
                // Only vertices that a triangle uses are part of the collision shape.
                double max = 0;
                foreach (int index in mesh.Indices)
                {
                    if ((uint)index >= (uint)mesh.Vertices.Length)
                        throw new ArgumentException($"Mesh index {index} is outside the {mesh.Vertices.Length} vertices.");
                    max = Math.Max(max, pose.TransformPoint(mesh.Vertices[index]).Length);
                }
                return max;
            }
            case CompoundShapeDesc compound:
            {
                if (compound.Children is null)
                    throw new ArgumentException("A compound shape needs children.");
                double max = 0;
                foreach (CompoundChild child in compound.Children)
                {
                    var local = new DPose(child.Pose.Position, DQuaternion.FromQuaternion(Quaternion.Normalize(child.Pose.Rotation)));
                    max = Math.Max(max, BoundingRadius(child.Shape, DPose.Compose(pose, local)));
                }
                return max;
            }
            default:
                throw new NotSupportedException($"The shape type {shape.GetType().Name} is not one of the six shape records.");
        }
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
/// <param name="Restitution">
/// How much of its approach speed a contact gives back as a rebound. A contact takes the larger restitution of its two
/// bodies, and does not bounce when its bounce would rise less than 4 cm against the pull toward the surface: about a
/// rebound of 0.89 m/s at 1 g, more under a stronger pull, and any rebound without one. The default is 0, as in Jolt, so
/// a body bounces only when its content names a restitution.
/// </param>
public sealed record BodyDesc(
    ShapeDesc Shape,
    BodyMotion Motion = BodyMotion.Dynamic,
    BodyLayer Layer = BodyLayer.Moving,
    float Mass = 1f,
    float Friction = 0.5f,
    float Restitution = 0f,
    float LinearDamping = 0.05f,
    float AngularDamping = 0.05f,
    ShapeDesc? InteriorShape = null)
{
    /// <summary>The collision groups that the body is in. Every body starts in <see cref="CollisionGroup.Solid"/>.</summary>
    public CollisionGroups CollisionGroups { get; init; } = CollisionGroups.Of(CollisionGroup.Solid);

    /// <summary>
    /// The groups that the body collides with. Two bodies collide only when each body's mask matches the other body's
    /// groups. Jolt tests only an any-of set, so a body's mask may hold only one: spawn refuses an all-of or a none-of
    /// part. Every body starts with <c>InAny(Solid)</c>.
    /// </summary>
    public CollisionMask CollisionMask { get; init; } = CollisionMask.Empty.InAny(CollisionGroup.Solid);

    /// <summary>
    /// True for a sensor body, which detects touches but does not push: Jolt's sensor. Bodies pass through it. A touch
    /// of a sensor starts when the two bodies overlap and ends when they stop overlapping, with a
    /// <see cref="ContactStarted"/> whose impulse is zero and a <see cref="ContactEnded"/>. A sensor detects dynamic and
    /// kinematic bodies and characters when the collision groups and masks of both match. Sleep and wake end no touch. A
    /// ghost's touch is a touch of its entity, and an entity's body and its ghosts make one touch. A character or the owner
    /// of a frame cannot be a sensor.
    /// </summary>
    public bool IsSensor { get; init; }

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
        if (CollisionMask.BodyProblem() is { } mask)
            return $"has a collision mask that {mask}";
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
            if (IsSensor)
                return "owns a frame, so it cannot be a sensor";
            return InteriorShape?.Problem() is { } interior ? $"has an interior shape that {interior}" : null;
        }
        if (Motion == BodyMotion.Character && IsSensor)
            return "is a character, so it cannot be a sensor";
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
    /// The motion that the hull of a frame with <paramref name="driver"/> has: dynamic for the physics driver, kinematic for
    /// the Kepler and Spin drivers, which move it on rails, and static for the static driver. Only a frame with a parent
    /// has a hull. The description's own <see cref="Motion"/> does not count for a hull.
    /// </summary>
    public static BodyMotion FrameHullMotion(MotionDriver driver) => driver switch
    {
        MotionDriver.Physics => BodyMotion.Dynamic,
        MotionDriver.Kepler or MotionDriver.Spin => BodyMotion.Kinematic,
        _ => BodyMotion.Static,
    };

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
    SetCollision = 1 << 6,
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
    /// <summary>The new linear velocity relative to the entity's frame, in doubles. Physics converts it to the body's home when it applies it.</summary>
    public Double3 Velocity;
    public Vector3 AngularVelocity;
    /// <summary>The target pose for a kinematic body, in frame coordinates. Used with <see cref="IntentFlags.KinematicTarget"/>.</summary>
    public DPose Target;
    /// <summary>The new pose for a teleport, in frame coordinates. Used with <see cref="IntentFlags.Teleport"/>.</summary>
    public DPose TeleportPose;
    /// <summary>The desired movement velocity of a character.</summary>
    public Vector3 CharacterVelocity;
    public float JumpSpeed;
    /// <summary>The body's new collision groups. Used with <see cref="IntentFlags.SetCollision"/>.</summary>
    public CollisionGroups CollisionGroups;
    /// <summary>The body's new collision mask, which holds only an any-of set. Used with <see cref="IntentFlags.SetCollision"/>.</summary>
    public CollisionMask CollisionMask;
    public IntentFlags Flags;

    public readonly bool IsEmpty =>
        Flags == IntentFlags.None && Force == Vector3.Zero && Torque == Vector3.Zero &&
        Impulse == Vector3.Zero && AngularImpulse == Vector3.Zero;

    public void Clear() => this = default;
}

/// <summary>
/// A body that physics must create at the sync point. <see cref="Desc"/> holds the collision groups and mask that the body
/// has now: a body created again after a move holds those of its last SetCollision intent, not those of its spawn.
/// </summary>
public readonly record struct BodyCreate(
    Entity Entity,
    FrameHandle Frame,
    BodyDesc Desc,
    DPose Pose,
    Double3 LinearVelocity,
    Vector3 AngularVelocity);

/// <summary>
/// A body that moves, with its whole state, from its physics home into <see cref="To"/>: a numeric frame below the same
/// semantic frame, or that semantic frame itself. The simulation has changed the tree first: the frame of a frame owner
/// is under <see cref="To"/> already, because a hull lives in the system of its frame's tree parent.
/// </summary>
internal readonly record struct BodyMove(Entity Entity, FrameHandle To);

/// <summary>
/// A numeric frame that moved by <see cref="Offset"/> in its parent and gained <see cref="Velocity"/> there, both in the
/// parent's axes, which are the frame's own. The simulation has changed the tree already. Every body in the frame's system
/// keeps its place and velocity in the parent, so it loses the offset from its position and the velocity from its own: a
/// sleeping body that loses no velocity stays asleep, and a static body loses only the offset.
/// </summary>
internal readonly record struct FrameShift(FrameHandle Frame, Double3 Offset, Double3 Velocity);

/// <summary>
/// The structural changes of one sync point. Physics applies them in this order: <see cref="BodiesDestroyed"/>,
/// <see cref="FramesDestroyed"/>, <see cref="FramesShifted"/>, <see cref="BodiesMoved"/>, <see cref="GhostPlacesWithdrawn"/>,
/// <see cref="BodiesCreated"/>. <see cref="FramesCreated"/> is informational: a
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
    /// Bodies that move between the systems of one semantic frame and its numeric frames, as groups: every pair of moved
    /// bodies that touch and go to the same frame keeps touching, so its contact is not reported again. Sleeping bodies
    /// stay asleep. Numeric frames are an engine detail, so only the engine fills it.
    /// </summary>
    internal readonly List<BodyMove> BodiesMoved = new();
    /// <summary>
    /// Numeric frames that moved or changed velocity in their parents, for a drain or a change of mode. Physics applies
    /// them before <see cref="BodiesMoved"/>, while the bodies that a move takes out of a shifted frame are still in it.
    /// Only the engine fills it.
    /// </summary>
    internal readonly List<FrameShift> FramesShifted = new();

    /// <summary>
    /// Numeric frames that the numeric update of the last sync point named as a place for a ghost and this one does not:
    /// the falling frame of an anchor (see <see cref="FrameTree.AnchorGhosts"/>) or a frame that keeps the ghosts of the
    /// bodies that stick out of a bubble (see <see cref="FrameTree.StickOutGhosts"/>). Physics drops the ghosts there that
    /// the tree no longer allows, at once, so that it keeps a ghost only where the tree allows one. Only the engine fills
    /// it.
    /// </summary>
    internal readonly List<FrameHandle> GhostPlacesWithdrawn = new();

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
        BodiesMoved.Clear();
        FramesShifted.Clear();
        GhostPlacesWithdrawn.Clear();
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
/// A contact that started during the step between two different entities, once for each pair of entities. Point and
/// normal are in <see cref="Frame"/>, the frame whose physics system found the touch, and the normal points from
/// <see cref="A"/> toward <see cref="B"/>. That system holds a body of each entity: both real bodies, or the real body
/// of one and the ghost of the other's body, which stands for its real body. When the systems of several frames start
/// the contact in the same step, <see cref="Frame"/> is the one with the lowest index.
/// </summary>
public readonly record struct ContactStarted(Entity A, Entity B, FrameHandle Frame, Double3 Point, Vector3 Normal, float Impulse);

/// <summary>
/// A contact that a <see cref="ContactStarted"/> of an earlier step, or of the same step, reported and that ended during
/// the step: no system holds it any more. The two bodies stopped touching, or one of them was destroyed or left for a
/// system where nothing carries the contact on: no body of the other entity there, real or ghost, that can touch it.
/// <see cref="A"/> comes before <see cref="B"/> in entity order. An entity whose body was destroyed at the last sync
/// point may no longer exist.
/// </summary>
public readonly record struct ContactEnded(Entity A, Entity B);

/// <summary>
/// The new state of a physics-driven frame, relative to its parent after this step. If the step also handed the
/// frame off, the parent is the hand-off's <see cref="HandOff.To"/>, so the sync point applies hand-offs first.
/// </summary>
public readonly record struct FrameState(FrameHandle Frame, DPose LocalPose, Double3 LinearVelocity, Vector3 AngularVelocity, Vector3 ProperAcceleration);

/// <summary>
/// Results of one step. The backend fills it on the physics thread. The sync point reads and clears it. Every entity
/// that the output names is live and has a body, except in an ended contact, and every frame that it names is alive and
/// not null. An ended contact names two different entities, and one of them may be gone, because destroying a body ends
/// its contacts. A hand-off leaves
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

    /// <summary>The contacts that ended in the step, after the contacts that started, in entity order.</summary>
    public readonly List<ContactEnded> ContactsEnded = new();

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
        ContactsEnded.Clear();
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

    /// <summary>
    /// Casts a ray in the physics system of <paramref name="frame"/> alone, in that frame's coordinates: from
    /// <paramref name="origin"/> + <paramref name="direction"/> · <paramref name="start"/> to <paramref name="origin"/> +
    /// <paramref name="direction"/> · <paramref name="end"/>, where <paramref name="direction"/> is a unit vector. It adds
    /// to <paramref name="hits"/> the hits whose entity <paramref name="filter"/> accepts, with their distances from
    /// <paramref name="origin"/>: for <see cref="RayCastKind.Nearest"/> the nearest one, and for
    /// <see cref="RayCastKind.All"/> every one, by distance. Any body of an entity can be hit: its own body, a frame's
    /// hull and interior, a character's inner body and a ghost. The frame may be a numeric frame, and a frame without a
    /// system has nothing to hit. It runs at the sync point, never during a step. A ray ignores the back faces of a mesh,
    /// so a ray that starts inside a mesh passes out of it, and it treats a convex shape as solid, so a ray that starts
    /// inside one hits it at once.
    /// </summary>
    void CastRay(FrameHandle frame, Double3 origin, Double3 direction, double start, double end, IPhysicsQueryFilter filter,
        RayCastKind kind, List<RayCastHit> hits);

    /// <summary>
    /// Moves <paramref name="shape"/>, turned by the rotation of <paramref name="start"/>, in the physics system of
    /// <paramref name="frame"/> alone, in that frame's coordinates: from <paramref name="start"/>'s position +
    /// <paramref name="direction"/> · <paramref name="startDistance"/> to that position + <paramref name="direction"/> ·
    /// <paramref name="endDistance"/>, where <paramref name="direction"/> is a unit vector. It adds to
    /// <paramref name="hits"/> the first hit whose entity <paramref name="filter"/> accepts, if any, with its distance from
    /// <paramref name="start"/>. Any body of an entity can be hit, as for <see cref="CastRay"/>. A shape that overlaps a
    /// convex body where its part of the cast starts hits it there. It ignores the back faces of a mesh, so a shape that
    /// starts inside a mesh passes out of it. It runs at the sync point, never during a step.
    /// </summary>
    void CastShape(FrameHandle frame, in QueryShape shape, in DPose start, Double3 direction, double startDistance, double endDistance,
        IPhysicsQueryFilter filter, List<ShapeCastHit> hits);

    /// <summary>
    /// Adds to <paramref name="entities"/> the entity of each body in the physics system of <paramref name="frame"/> alone
    /// that overlaps <paramref name="shape"/> at <paramref name="pose"/>, in that frame's coordinates, and that
    /// <paramref name="filter"/> accepts, in no fixed order. An entity whose bodies or parts overlap the shape more than
    /// once can be added more than once. A convex body is solid, and a mesh counts only where the shape meets the front of
    /// a triangle. It runs at the sync point, never during a step.
    /// </summary>
    void Overlap(FrameHandle frame, in QueryShape shape, in DPose pose, IPhysicsQueryFilter filter, List<Entity> entities);

    /// <summary>The radius of the bounding sphere of a shape. Used for hand-off and rendering.</summary>
    float BoundingRadius(ShapeDesc shape);

    /// <summary>
    /// Why this backend cannot build <paramref name="shape"/>, or null when it can: what <see cref="ShapeDesc.Problem"/>
    /// finds, and what only the backend can tell, for example that the points of a convex hull coincide. The message
    /// completes a sentence that names the shape. Thread-safe, so that a spawn during a tick can ask.
    /// </summary>
    string? ShapeProblem(ShapeDesc shape);
}
