using System.Numerics;

namespace Tertius;

/// <summary>
/// The tree of reference frames. Poses live in a packed <see cref="PoseTree{TPose}"/> of <see cref="DPose"/>.
/// Per-frame data lives in parallel arrays, indexed by the frame's stable index.
/// Mutate the tree only at the sync point, then call <see cref="Update"/>. During a tick, the tree is read-only
/// and safe to read from any thread.
/// <para>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>Every frame with the <see cref="MotionDriver.Kepler"/> driver has a parent whose gravity is a point field with a
/// G·M above 0 (see <see cref="GravityField.CanBeOrbited"/>). An orbit has no G·M of its own. A Kepler frame takes the G·M
/// of its parent's point gravity (see <see cref="KeplerMu"/>), the same G·M that pulls the loose bodies in that parent. The
/// tree does not keep this invariant: its setters take what they are given. The World does, because it refuses every call
/// that would break it.</item>
/// <item>The rotation of every frame's local pose is a unit quaternion: the square of its length is within
/// <see cref="DQuaternion.UnitTolerance"/> of 1. A frame whose rotation is a little long or short moves everything in its
/// parent by that much of the distance to the parent's origin, as seen from the frame. The tree checks it in a build with
/// contracts, when a caller creates a frame or sets a local pose, and the World keeps it: it normalizes a rotation that a
/// caller gives it, and physics normalizes the rotation that leaves Jolt. The poses that the tree composes itself, when it
/// moves or destroys a frame, are unit length.</item>
/// <item>A numeric frame (see <see cref="CreateNumeric"/>) has no owner and a parent, the identity rotation and no spin. A
/// resting one has no velocity and no step acceleration. A semantic frame has an owner and no step acceleration, and a
/// semantic frame with the Kepler or Spin driver is not the child of a numeric frame, because it moves relative to its
/// parent.</item>
/// </list>
/// </para>
/// </summary>
public sealed class FrameTree
{
    private readonly PoseTree<DPose> poses = new(64);
    private readonly Dictionary<Entity, FrameHandle> byOwner = new();

    private Entity[] owner = new Entity[64];
    private FrameBounds[] bounds = new FrameBounds[64];
    private MotionDriver[] driver = new MotionDriver[64];
    private KeplerOrbit[] orbit = new KeplerOrbit[64];
    private FrameSpin[] spin = new FrameSpin[64];
    private GravityField[] gravity = new GravityField[64];
    private Double3[] linearVelocity = new Double3[64];
    private Vector3[] angularVelocity = new Vector3[64];
    private Vector3[] properAcceleration = new Vector3[64];
    // Numeric frames: the mode, and the acceleration that a falling frame keeps for the current step, relative to its
    // parent. A semantic frame has the mode None and no step acceleration.
    private NumericMode[] numeric = new NumericMode[64];
    private Double3[] stepAcceleration = new Double3[64];
    // The number of calls to SetDriver, SetOrbit and SetSpin for each frame, and for every frame together.
    private int[] driverVersion = new int[64];
    private int driverChanges;

    public int Count => poses.Count;

    /// <summary>
    /// Changes on every structural change (add, remove or reparent) and on every call to <see cref="SetDriver"/>,
    /// <see cref="SetOrbit"/> and <see cref="SetSpin"/>. Physics compares it with the version that it last followed.
    /// </summary>
    public int Version => poses.Version + driverChanges;

    public FrameHandle Create(
        Entity frameOwner,
        FrameHandle parent,
        in DPose localPose,
        FrameBounds frameBounds,
        MotionDriver motion,
        GravityField field = default,
        in KeplerOrbit kepler = default,
        in FrameSpin spinning = default)
    {
        if (frameOwner.IsNull)
            throw new ArgumentException("A frame needs an owner entity.", nameof(frameOwner));
        if (byOwner.ContainsKey(frameOwner))
            throw new InvalidOperationException($"{frameOwner} already owns a frame.");
        Contract.Requires(localPose.Rotation.IsUnit);

        FrameHandle handle = AddFrame(frameOwner, parent, localPose, frameBounds, motion, field, kepler, spinning, NumericMode.None);
        byOwner.Add(frameOwner, handle);
        return handle;
    }

    /// <summary>
    /// Creates a numeric frame under <paramref name="parent"/>, at <paramref name="position"/> in the parent and with the
    /// axes of the parent, moving at <paramref name="velocity"/> relative to it. A numeric frame has no owner, no bubble,
    /// no gravity of its own and no rotation. A resting frame does not move in its parent, so its velocity must be zero.
    /// The public members that walk the tree (<see cref="Parent"/>, <see cref="GetChildren"/> and <see cref="GetAll"/>)
    /// never show it. Throws <see cref="InvalidOperationException"/> for a stale parent.
    /// </summary>
    internal FrameHandle CreateNumeric(FrameHandle parent, Double3 position, Double3 velocity, NumericMode mode)
    {
        Contract.Requires(mode != NumericMode.None && Enum.IsDefined(mode));
        Contract.Requires(mode == NumericMode.Falling || velocity == Double3.Zero);
        Node(parent);
        FrameHandle handle = AddFrame(Entity.Null, parent, new DPose(position, DQuaternion.Identity), default, MotionDriver.Static,
            GravityField.None, default, default, mode);
        linearVelocity[handle.Index] = velocity;
        return handle;
    }

    private FrameHandle AddFrame(Entity frameOwner, FrameHandle parent, in DPose localPose, FrameBounds frameBounds, MotionDriver motion,
        GravityField field, in KeplerOrbit kepler, in FrameSpin spinning, NumericMode mode)
    {
        PoseNode node = poses.Add(parent.IsNull ? PoseNode.Null : Node(parent), localPose);
        Grow(node.Id + 1);
        owner[node.Id] = frameOwner;
        bounds[node.Id] = frameBounds;
        driver[node.Id] = motion;
        orbit[node.Id] = kepler;
        spin[node.Id] = spinning;
        gravity[node.Id] = field;
        linearVelocity[node.Id] = Double3.Zero;
        angularVelocity[node.Id] = Vector3.Zero;
        properAcceleration[node.Id] = Vector3.Zero;
        numeric[node.Id] = mode;
        stepAcceleration[node.Id] = Double3.Zero;
        driverVersion[node.Id] = 0;
        return new FrameHandle(frameOwner, node.Id, node.Generation);
    }

    /// <summary>Destroys a frame. Its child frames move to its parent and keep their poses and velocities.</summary>
    public void Destroy(FrameHandle frame)
    {
        PoseNode node = Node(frame);
        FrameHandle parent = TreeParent(frame);
        var children = new List<FrameHandle>();
        GetTreeChildren(frame, children);
        // Reparent converts each child's velocity into the new parent's axes. A child of a root frame becomes a root.
        foreach (FrameHandle child in children)
            Reparent(child, parent);
        if (!owner[node.Id].IsNull)
            byOwner.Remove(owner[node.Id]);
        owner[node.Id] = Entity.Null;
        numeric[node.Id] = NumericMode.None;
        poses.Remove(node);
    }

    /// <summary>
    /// Moves a frame under a new parent. Its pose and velocity relative to everything else stay the same. Throws
    /// <see cref="InvalidOperationException"/>, and changes nothing, if the new parent is the frame itself or one of
    /// its descendants.
    /// </summary>
    public void Reparent(FrameHandle frame, FrameHandle newParent)
    {
        PoseNode node = Node(frame);
        FrameHandle oldParent = TreeParent(frame);
        if (oldParent == newParent)
            return;
        // Refuse a cycle before the velocities below are converted: a refused move must leave the frame as it was.
        if (!newParent.IsNull && (newParent == frame || IsAncestorOf(frame, newParent)))
            throw new InvalidOperationException($"Cannot move frame {frame} under itself or its own descendant {newParent}.");

        // Carry the frame origin's velocity and the frame's spin from the old parent into the new parent.
        // A null parent is the virtual root above every root frame.
        DPose oldPose = poses.GetLocal(node);
        Double3 w = angularVelocity[node.Id];
        DQuaternion oldToNew = RootPose(newParent).Rotation.Conjugate() * RootPose(oldParent).Rotation;
        linearVelocity[node.Id] = ConvertVelocity(oldParent, newParent, oldPose.Position, linearVelocity[node.Id]);
        angularVelocity[node.Id] = (oldToNew.Rotate(w) + AngularVelocityRelative(oldParent, newParent)).ToVector3();
        poses.Reparent(node, newParent.IsNull ? PoseNode.Null : Node(newParent), keepWorldPose: true);
    }

    public bool IsAlive(FrameHandle frame) => !frame.IsNull && poses.IsAlive(new PoseNode(frame.Index, frame.Generation)) && owner[frame.Index] == frame.Entity;

    public bool TryGetFrame(Entity frameOwner, out FrameHandle frame) => byOwner.TryGetValue(frameOwner, out frame);

    /// <summary>
    /// The semantic parent of a frame: the nearest ancestor that is not a numeric frame, or null for a root frame. Numeric
    /// frames are an engine detail, so the public tree skips them. Throws <see cref="InvalidOperationException"/> for a
    /// stale frame.
    /// </summary>
    public FrameHandle Parent(FrameHandle frame)
    {
        FrameHandle parent = TreeParent(frame);
        return parent.IsNull ? parent : SemanticOf(parent);
    }

    /// <summary>The parent of a frame in the tree as it is stored, numeric frames included, or null for a root frame.</summary>
    internal FrameHandle TreeParent(FrameHandle frame)
    {
        PoseNode p = poses.Parent(Node(frame));
        return p.IsNull ? FrameHandle.Null : Handle(p);
    }

    /// <summary>
    /// The frame itself when it is semantic, or else its nearest ancestor that is not numeric. A numeric frame always has
    /// one. Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    internal FrameHandle SemanticOf(FrameHandle frame)
    {
        FrameHandle f = frame;
        while (numeric[Node(f).Id] != NumericMode.None)
            f = TreeParent(f);
        return f;
    }

    /// <summary>True for a numeric frame. Throws <see cref="InvalidOperationException"/> for a stale frame.</summary>
    internal bool IsNumeric(FrameHandle frame) => numeric[Node(frame).Id] != NumericMode.None;

    /// <summary>How a frame moves as a numeric frame: <see cref="NumericMode.None"/> for a semantic frame.</summary>
    internal NumericMode Numeric(FrameHandle frame) => numeric[Node(frame).Id];

    public bool IsAncestorOf(FrameHandle ancestor, FrameHandle frame) => poses.IsAncestorOf(Node(ancestor), Node(frame));

    /// <summary>
    /// Adds the semantic children of a frame to <paramref name="children"/>: its children in the tree, with each numeric
    /// child replaced by its own semantic children. Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    public void GetChildren(FrameHandle frame, List<FrameHandle> children)
    {
        var tree = new List<FrameHandle>();
        GetTreeChildren(frame, tree);
        foreach (FrameHandle child in tree)
        {
            if (IsNumeric(child))
                GetChildren(child, children);
            else
                children.Add(child);
        }
    }

    /// <summary>Adds the children of a frame in the tree as it is stored, numeric frames included.</summary>
    internal void GetTreeChildren(FrameHandle frame, List<FrameHandle> children)
    {
        var nodes = new List<PoseNode>();
        poses.GetChildren(Node(frame), nodes);
        foreach (PoseNode n in nodes)
            children.Add(Handle(n));
    }

    public DPose LocalPose(FrameHandle frame) => poses.GetLocal(Node(frame));

    /// <summary>
    /// Sets the pose of the frame in its parent. The rotation must be unit length (see <see cref="DQuaternion.IsUnit"/>): a
    /// caller that cannot promise it normalizes the rotation first (see <see cref="DPose.WithUnitRotation"/>).
    /// </summary>
    public void SetLocalPose(FrameHandle frame, in DPose pose)
    {
        Contract.Requires(pose.Rotation.IsUnit);
        poses.Local(Node(frame)) = pose;
    }

    /// <summary>The bubble of the frame. It never changes after the frame is created.</summary>
    public FrameBounds Bounds(FrameHandle frame) => bounds[Node(frame).Id];

    /// <summary>What moves the frame. <see cref="SetDriver"/> changes it.</summary>
    public MotionDriver Driver(FrameHandle frame) => driver[Node(frame).Id];

    /// <summary>
    /// Changes on every call to <see cref="SetDriver"/>, <see cref="SetOrbit"/> or <see cref="SetSpin"/> for the frame, also
    /// a call that gives the frame the driver, the orbit or the spin that it has. Each of them can change how the frame moves. Physics compares it with
    /// the version that the frame's hull last followed.
    /// </summary>
    public int DriverVersion(FrameHandle frame) => driverVersion[Node(frame).Id];

    /// <summary>
    /// Gives a frame a motion driver, for example to park a ship. A frame that gets the static driver stops where it is:
    /// both of its velocities become zero, and it keeps its proper acceleration, so the cargo of a ship that is parked on
    /// the ground keeps its weight. A frame that gets another driver keeps its pose and velocities until that driver moves
    /// it. Every call changes <see cref="Version"/> and <see cref="DriverVersion"/>, also a call that gives the frame the
    /// driver that it has. So physics follows the call at the start of its next step, the same way that it follows a
    /// reparent: it puts the frame's hull at the frame's pose and velocity in the tree. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>
    /// Only the simulation calls it, through <c>World.SetFrameDriver</c>, which refuses a driver that the frame cannot
    /// follow and keeps the owner of the frame in step.
    /// </remarks>
    internal void SetDriver(FrameHandle frame, MotionDriver motion)
    {
        Contract.Requires(Enum.IsDefined(motion));
        int id = Node(frame).Id;
        driver[id] = motion;
        driverVersion[id]++;
        driverChanges++;
        if (motion == MotionDriver.Static)
        {
            linearVelocity[id] = Double3.Zero;
            angularVelocity[id] = Vector3.Zero;
        }
    }

    /// <summary>The orbit of the frame around its parent, which the Kepler driver follows. <see cref="SetOrbit"/> changes it.</summary>
    public KeplerOrbit Orbit(FrameHandle frame) => orbit[Node(frame).Id];

    /// <summary>
    /// Gives a frame a new orbit. Like <see cref="SetDriver"/>, every call changes <see cref="Version"/> and
    /// <see cref="DriverVersion"/>, so physics puts the frame's hull at the frame's pose and velocity in the tree at the
    /// start of its next step. The frame keeps its pose and velocities: the caller moves a Kepler frame onto the new orbit.
    /// Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>Only the simulation calls it, through <c>World.SetFrameOrbit</c>, which refuses an orbit that is not valid.</remarks>
    internal void SetOrbit(FrameHandle frame, in KeplerOrbit value)
    {
        int id = Node(frame).Id;
        orbit[id] = value;
        driverVersion[id]++;
        driverChanges++;
    }

    /// <summary>How the frame turns in place, which the Spin driver follows. <see cref="SetSpin"/> changes it.</summary>
    public FrameSpin Spin(FrameHandle frame) => spin[Node(frame).Id];

    /// <summary>
    /// Gives a frame a new spin. Like <see cref="SetOrbit"/>, every call changes <see cref="Version"/> and
    /// <see cref="DriverVersion"/>, so physics puts the frame's hull at the frame's pose and velocity in the tree at the
    /// start of its next step. The frame keeps its pose and velocities: the caller turns a Spin frame to the new spin.
    /// Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>Only the simulation calls it, through <c>World.SetFrameSpin</c>, which refuses a spin that is not valid.</remarks>
    internal void SetSpin(FrameHandle frame, in FrameSpin value)
    {
        int id = Node(frame).Id;
        spin[id] = value;
        driverVersion[id]++;
        driverChanges++;
    }

    /// <summary>The gravity field of the frame. <see cref="SetGravity"/> changes it.</summary>
    public GravityField Gravity(FrameHandle frame) => gravity[Node(frame).Id];

    /// <summary>
    /// Gives a frame a new gravity field. Physics wakes the frame's bodies at the start of its next step. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>
    /// Only the simulation calls it, through <c>World.SetFrameGravity</c>, which refuses a field that is not valid, and a
    /// field that would leave a Kepler child of the frame without the G·M that it orbits.
    /// </remarks>
    internal void SetGravity(FrameHandle frame, in GravityField value) => gravity[Node(frame).Id] = value;

    /// <summary>
    /// The G·M that the Kepler motion of <paramref name="frame"/> uses, in m³/s²: the G·M of the point gravity of its parent
    /// frame. The orbit has none of its own (see <see cref="KeplerOrbit"/>), so a Kepler frame and the loose bodies in its
    /// parent fall on the same G·M. Zero for a frame without a parent, or whose parent has no point gravity with a G·M above
    /// 0, which leaves <see cref="KeplerOrbit.StateAt"/> at zero. A frame with the Kepler driver is never in that state
    /// (see <see cref="AssertInvariants"/>). Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    public double KeplerMu(FrameHandle frame)
    {
        FrameHandle parent = Parent(frame);
        return !parent.IsNull && Gravity(parent) is PointGravity { CanBeOrbited: true } point ? point.Mu : 0;
    }

    /// <summary>
    /// Checks the invariants of the tree (see the class summary): every frame with the Kepler driver has a G·M to orbit, and
    /// the rotation of every frame's local pose is unit length. O(frames). It runs only on demand, so it works in every build.
    /// </summary>
    internal void AssertInvariants()
    {
        ReadOnlySpan<DPose> locals = poses.LocalPoses;
        for (int i = 0; i < poses.Count; i++)
        {
            FrameHandle frame = AtDense(i);
            if (Driver(frame) == MotionDriver.Kepler)
                Contract.Invariant(KeplerMu(frame) > 0);
            Contract.Invariant(locals[i].Rotation.IsUnit);
            int id = poses.NodeAt(i).Id;
            if (numeric[id] == NumericMode.None)
            {
                Contract.Invariant(!owner[id].IsNull && stepAcceleration[id] == Double3.Zero);
                // A frame that moves by its orbit or its spin moves relative to its parent, which must not fall or rest.
                if (Driver(frame) is MotionDriver.Kepler or MotionDriver.Spin)
                    Contract.Invariant(TreeParent(frame).IsNull || !IsNumeric(TreeParent(frame)));
                continue;
            }
            Contract.Invariant(owner[id].IsNull && !TreeParent(frame).IsNull);
            Contract.Invariant(locals[i].Rotation == DQuaternion.Identity && angularVelocity[id] == Vector3.Zero);
            if (numeric[id] == NumericMode.Resting)
                Contract.Invariant(linearVelocity[id] == Double3.Zero && stepAcceleration[id] == Double3.Zero);
        }
    }

    /// <summary>Linear velocity of the frame origin relative to the parent, in parent axes.</summary>
    public ref Double3 LinearVelocity(FrameHandle frame) => ref linearVelocity[Node(frame).Id];

    /// <summary>Angular velocity of the frame relative to the parent, in parent axes.</summary>
    public ref Vector3 AngularVelocity(FrameHandle frame) => ref angularVelocity[Node(frame).Id];

    /// <summary>What an accelerometer at the frame origin reads, in frame axes. Zero for a frame in free fall.</summary>
    public ref Vector3 ProperAcceleration(FrameHandle frame) => ref properAcceleration[Node(frame).Id];

    /// <summary>The pose of <paramref name="of"/> in the space of <paramref name="to"/>, through their common ancestor.</summary>
    public DPose Relative(FrameHandle of, FrameHandle to) => poses.Relative(Node(of), Node(to));

    /// <summary>Writes every frame's pose relative to <paramref name="reference"/> into <paramref name="result"/>, by dense index.</summary>
    public void ComputeRelativeTo(FrameHandle reference, Span<DPose> result) => poses.ComputeRelativeTo(Node(reference), result);

    /// <summary>
    /// Like <see cref="ComputeRelativeTo(FrameHandle, Span{DPose})"/>, but with the local poses taken from
    /// <paramref name="localPoses"/> by dense index, such as a copy of last tick's poses.
    /// </summary>
    public void ComputeRelativeTo(FrameHandle reference, ReadOnlySpan<DPose> localPoses, Span<DPose> result) =>
        poses.ComputeRelativeTo(Node(reference), localPoses, result);

    /// <summary>The local poses of all frames, by dense index. Copy it to keep a tick's poses.</summary>
    public ReadOnlySpan<DPose> LocalPosesDense => poses.LocalPoses;

    public int DenseIndex(FrameHandle frame) => poses.DenseIndex(Node(frame));

    public FrameHandle AtDense(int dense) => Handle(poses.NodeAt(dense));

    /// <summary>Restores order and recomputes root-relative poses. Call it at the end of every sync point.</summary>
    public void Update() => poses.UpdateWorld();

    /// <summary>
    /// Below this effective gravity, in m/s², physics never stands a character, so the character is weightless: it
    /// keeps its momentum and its last up, and only forces and the weak gravity change its velocity. At or above it,
    /// physics stands a character only on walkable ground. A character in the air is weightless there too, because it
    /// falls freely. Physics reports which characters it stood, so the components that steer characters agree with it.
    /// </summary>
    public const float ZeroGravityThreshold = 0.05f;

    /// <summary>
    /// The effective gravity on a body at <paramref name="position"/> in the frame <paramref name="frame"/>, which holds the
    /// body in its physics system, in frame axes: the field of the frame's semantic frame at the body's true position, minus
    /// the acceleration of <paramref name="frame"/> relative to the semantic frame. For a semantic frame, that is its own
    /// field minus its proper acceleration. In a falling numeric frame it is the tidal gravity. Both terms are in doubles,
    /// and only their difference narrows to a float, so a tidal gravity of a fraction of a mm/s² keeps its digits.
    /// </summary>
    public Vector3 EffectiveGravity(FrameHandle frame, Double3 position)
    {
        int id = Node(frame).Id;
        if (numeric[id] == NumericMode.None)
            return FieldOf(id, position).ToVector3();
        FrameHandle semantic = SemanticOf(frame);
        return (FieldOf(semantic.Index, position + HomeOffset(frame, semantic)) - HomeAcceleration(frame, semantic)).ToVector3();
    }

    /// <summary>
    /// The gravity of the semantic frame of <paramref name="frame"/> at a body's true position, for a body at
    /// <paramref name="position"/> in <paramref name="frame"/>, in frame axes. It equals <see cref="EffectiveGravity"/> in a
    /// semantic frame. In a numeric frame it is the gravity that the semantic frame shows, which says where up is.
    /// </summary>
    internal Vector3 SemanticGravity(FrameHandle frame, Double3 position)
    {
        FrameHandle semantic = SemanticOf(frame);
        return FieldOf(semantic.Index, position + HomeOffset(frame, semantic)).ToVector3();
    }

    /// <summary>The field of the semantic frame with index <paramref name="id"/> minus its proper acceleration, in doubles.</summary>
    private Double3 FieldOf(int id, Double3 position) => gravity[id].AtPrecise(position) - (Double3)properAcceleration[id];

    /// <summary>
    /// The acceleration of the origin of <paramref name="home"/> relative to <paramref name="semantic"/>, which is the home
    /// itself or an ancestor of it, during the current step: the sum of the step accelerations of the numeric frames
    /// between them.
    /// </summary>
    internal Double3 HomeAcceleration(FrameHandle home, FrameHandle semantic)
    {
        Contract.Requires(home == semantic || IsAncestorOf(semantic, home));
        Double3 acceleration = Double3.Zero;
        for (FrameHandle f = home; f != semantic; f = TreeParent(f))
            acceleration += stepAcceleration[Node(f).Id];
        return acceleration;
    }

    /// <summary>
    /// Like <see cref="HomeOffset"/>, but where the numeric frames between the two frames will be at the end of a step of
    /// <paramref name="dt"/> seconds that starts now (see <see cref="AdvanceNumericFrames"/>). Physics converts a target
    /// that a body must reach by the end of the step with it.
    /// </summary>
    internal Double3 HomeOffsetAfter(FrameHandle home, FrameHandle semantic, double dt)
    {
        Contract.Requires(home == semantic || IsAncestorOf(semantic, home));
        Double3 offset = Double3.Zero;
        for (FrameHandle f = home; f != semantic; f = TreeParent(f))
        {
            int id = Node(f).Id;
            offset += LocalPose(f).Position + (linearVelocity[id] + stepAcceleration[id] * dt) * dt;
        }
        return offset;
    }

    /// <summary>
    /// Fixes the acceleration that each falling numeric frame keeps for the step that starts now: the field of its semantic
    /// frame at its origin, relative to its parent. That is the field minus the acceleration of the numeric frames above
    /// it, which the dense order, parents first, has fixed already. A resting frame has none. Call it after
    /// <see cref="Update"/> and before the physics step, which reads it through <see cref="EffectiveGravity"/>.
    /// </summary>
    internal void BeginNumericStep()
    {
        for (int i = 0; i < poses.Count; i++)
        {
            int id = poses.NodeAt(i).Id;
            if (numeric[id] != NumericMode.Falling)
                continue;
            FrameHandle frame = AtDense(i);
            FrameHandle semantic = SemanticOf(frame);
            FrameHandle parent = TreeParent(frame);
            stepAcceleration[id] = FieldOf(semantic.Index, HomeOffset(frame, semantic)) - HomeAcceleration(parent, semantic);
        }
    }

    /// <summary>
    /// Moves each falling numeric frame over a step of <paramref name="dt"/> seconds, in doubles, with the scheme that Jolt
    /// uses for bodies: the velocity gains the step acceleration times the step, then the position gains the new velocity
    /// times the step. A resting frame does not move.
    /// </summary>
    internal void AdvanceNumericFrames(double dt)
    {
        for (int i = 0; i < poses.Count; i++)
        {
            PoseNode node = poses.NodeAt(i);
            int id = node.Id;
            if (numeric[id] != NumericMode.Falling)
                continue;
            linearVelocity[id] += stepAcceleration[id] * dt;
            ref DPose local = ref poses.Local(node);
            local = new DPose(local.Position + linearVelocity[id] * dt, local.Rotation);
        }
    }

    /// <summary>Converts a point from frame coordinates to parent coordinates.</summary>
    public Double3 PointToParent(FrameHandle frame, Double3 point) => LocalPose(frame).TransformPoint(point);

    /// <summary>Converts a point from parent coordinates to frame coordinates.</summary>
    public Double3 PointFromParent(FrameHandle frame, Double3 point) => LocalPose(frame).InverseTransformPoint(point);

    /// <summary>Velocity of a point in the parent: R·v + v_frame + ω × (R·p).</summary>
    public Double3 VelocityToParent(FrameHandle frame, Double3 positionInFrame, Double3 velocityInFrame)
    {
        int id = Node(frame).Id;
        DPose pose = poses.GetLocal(Node(frame));
        Double3 offset = pose.TransformDirection(positionInFrame);
        return pose.TransformDirection(velocityInFrame) + linearVelocity[id] + Double3.Cross(angularVelocity[id], offset);
    }

    /// <summary>Velocity of a point in the frame: R⁻¹(v − v_frame − ω × (p − origin)).</summary>
    public Double3 VelocityFromParent(FrameHandle frame, Double3 positionInParent, Double3 velocityInParent)
    {
        int id = Node(frame).Id;
        DPose pose = poses.GetLocal(Node(frame));
        Double3 offset = positionInParent - pose.Position;
        return pose.InverseTransformDirection(velocityInParent - linearVelocity[id] - Double3.Cross(angularVelocity[id], offset));
    }

    /// <summary>Adds every live semantic frame, in dense order. Numeric frames are an engine detail, so it skips them.</summary>
    public void GetAll(List<FrameHandle> frames)
    {
        for (int i = 0; i < poses.Count; i++)
            if (numeric[poses.NodeAt(i).Id] == NumericMode.None)
                frames.Add(AtDense(i));
    }

    /// <summary>Adds every live frame, numeric frames included, in dense order.</summary>
    internal void GetAllInTree(List<FrameHandle> frames)
    {
        for (int i = 0; i < poses.Count; i++)
            frames.Add(AtDense(i));
    }

    private PoseNode Node(FrameHandle frame)
    {
        var node = new PoseNode(frame.Index, frame.Generation);
        if (!poses.IsAlive(node) || owner[frame.Index] != frame.Entity)
            throw new InvalidOperationException($"Stale or null frame {frame}.");
        return node;
    }

    private FrameHandle Handle(PoseNode node) => new(owner[node.Id], node.Id, node.Generation);

    /// <summary>True when <paramref name="x"/> is an ancestor of <paramref name="frame"/>. Nothing is an ancestor of the virtual root.</summary>
    private bool IsAncestorOrNull(FrameHandle x, FrameHandle frame) => !frame.IsNull && IsAncestorOf(x, frame);

    /// <summary>The pose of a frame relative to the virtual root, composed along its path. Identity for the virtual root.</summary>
    private DPose RootPose(FrameHandle frame)
    {
        DPose pose = DPose.Identity;
        for (FrameHandle f = frame; !f.IsNull; f = TreeParent(f))
            pose = DPose.Compose(LocalPose(f), pose);
        return pose;
    }

    /// <summary>
    /// The velocity, in <paramref name="to"/> axes, of a point at <paramref name="position"/> that moves with velocity
    /// <paramref name="v"/> in frame <paramref name="from"/>. Converts level by level through the common ancestor.
    /// A null frame is the virtual root above every root frame.
    /// </summary>
    public Double3 ConvertVelocity(FrameHandle from, FrameHandle to, Double3 position, Double3 v)
    {
        // Walk up from 'from' to the root, converting at each level, then down to 'to' using the inverse rule.
        FrameHandle x = from;
        Double3 p = position, vel = v;
        while (!x.IsNull && x != to && !IsAncestorOrNull(x, to))
        {
            vel = VelocityToParent(x, p, vel);
            p = PointToParent(x, p);
            x = TreeParent(x);
        }
        if (x == to)
            return vel;
        // x is now an ancestor of 'to' (or the virtual root). Descend along the path to 'to'.
        var path = new Stack<FrameHandle>();
        for (FrameHandle y = to; !y.IsNull && y != x; y = TreeParent(y))
            path.Push(y);
        while (path.Count > 0)
        {
            FrameHandle child = path.Pop();
            vel = VelocityFromParent(child, p, vel);
            p = PointFromParent(child, p);
        }
        return vel;
    }

    /// <summary>
    /// The velocity of the origin of a body's physics home <paramref name="home"/> relative to its semantic frame
    /// <paramref name="semantic"/>, which is the home itself or an ancestor of it. The frames between them are numeric
    /// frames, which never rotate, so this is the sum of their frame velocities, in doubles. It is zero when the home is the
    /// semantic frame.
    /// </summary>
    public Double3 HomeVelocity(FrameHandle home, FrameHandle semantic)
    {
        Contract.Requires(home == semantic || IsAncestorOf(semantic, home));
        Double3 velocity = Double3.Zero;
        for (FrameHandle f = home; f != semantic; f = TreeParent(f))
            velocity += linearVelocity[Node(f).Id];
        return velocity;
    }

    /// <summary>
    /// The position of the origin of <paramref name="home"/> in the coordinates of <paramref name="semantic"/>, the home
    /// itself or an ancestor of it. Like <see cref="HomeVelocity"/>, it is a sum along the numeric frames between them.
    /// </summary>
    public Double3 HomeOffset(FrameHandle home, FrameHandle semantic)
    {
        Contract.Requires(home == semantic || IsAncestorOf(semantic, home));
        Double3 offset = Double3.Zero;
        for (FrameHandle f = home; f != semantic; f = TreeParent(f))
            offset += LocalPose(f).Position;
        return offset;
    }

    /// <summary>
    /// The angular velocity of frame <paramref name="of"/> relative to frame <paramref name="to"/>, in
    /// <paramref name="to"/> axes. A null frame is the virtual root above every root frame.
    /// </summary>
    public Double3 AngularVelocityRelative(FrameHandle of, FrameHandle to)
    {
        // Sum angular velocities up to the common ancestor in its axes, then subtract the path down to 'to'.
        FrameHandle x = of;
        Double3 omega = Double3.Zero;
        while (!x.IsNull && x != to && !IsAncestorOrNull(x, to))
        {
            FrameHandle parent = TreeParent(x);
            omega = LocalPose(x).TransformDirection(omega) + angularVelocity[Node(x).Id];
            x = parent;
        }
        var path = new Stack<FrameHandle>();
        for (FrameHandle y = to; !y.IsNull && y != x; y = TreeParent(y))
            path.Push(y);
        while (path.Count > 0)
        {
            FrameHandle child = path.Pop();
            omega = LocalPose(child).InverseTransformDirection(omega - angularVelocity[Node(child).Id]);
        }
        return omega;
    }

    private void Grow(int needed)
    {
        if (needed <= owner.Length)
            return;
        int size = Math.Max(needed, owner.Length * 2);
        Array.Resize(ref owner, size);
        Array.Resize(ref bounds, size);
        Array.Resize(ref driver, size);
        Array.Resize(ref orbit, size);
        Array.Resize(ref spin, size);
        Array.Resize(ref gravity, size);
        Array.Resize(ref linearVelocity, size);
        Array.Resize(ref angularVelocity, size);
        Array.Resize(ref properAcceleration, size);
        Array.Resize(ref numeric, size);
        Array.Resize(ref stepAcceleration, size);
        Array.Resize(ref driverVersion, size);
    }
}
