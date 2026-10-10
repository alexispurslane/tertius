using System.Numerics;

namespace Tertius;

/// <summary>
/// The tree of reference frames. Poses live in a packed <see cref="PoseTree{TPose}"/> of <see cref="DPose"/>.
/// Per-frame data lives in parallel arrays, indexed by the frame's stable index.
/// Mutate the tree only at the sync point, then call <see cref="Update"/>. During a tick, the tree is read-only
/// and safe to read from any thread.
/// </summary>
public sealed class FrameTree
{
    private readonly PoseTree<DPose> poses = new(64);
    private readonly Dictionary<Entity, FrameHandle> byOwner = new();

    private Entity[] owner = new Entity[64];
    private FrameBounds[] bounds = new FrameBounds[64];
    private MotionDriver[] driver = new MotionDriver[64];
    private KeplerOrbit[] orbit = new KeplerOrbit[64];
    private GravityField[] gravity = new GravityField[64];
    private Double3[] linearVelocity = new Double3[64];
    private Vector3[] angularVelocity = new Vector3[64];
    private Vector3[] properAcceleration = new Vector3[64];

    public int Count => poses.Count;

    /// <summary>Increments on every structural change.</summary>
    public int Version => poses.Version;

    public FrameHandle Create(
        Entity frameOwner,
        FrameHandle parent,
        in DPose localPose,
        FrameBounds frameBounds,
        MotionDriver motion,
        GravityField field = default,
        in KeplerOrbit kepler = default)
    {
        if (frameOwner.IsNull)
            throw new ArgumentException("A frame needs an owner entity.", nameof(frameOwner));
        if (byOwner.ContainsKey(frameOwner))
            throw new InvalidOperationException($"{frameOwner} already owns a frame.");

        PoseNode node = poses.Add(parent.IsNull ? PoseNode.Null : Node(parent), localPose);
        Grow(node.Id + 1);
        owner[node.Id] = frameOwner;
        bounds[node.Id] = frameBounds;
        driver[node.Id] = motion;
        orbit[node.Id] = kepler;
        gravity[node.Id] = field;
        linearVelocity[node.Id] = Double3.Zero;
        angularVelocity[node.Id] = Vector3.Zero;
        properAcceleration[node.Id] = Vector3.Zero;
        var handle = new FrameHandle(frameOwner, node.Id, node.Generation);
        byOwner.Add(frameOwner, handle);
        return handle;
    }

    /// <summary>Destroys a frame. Its child frames move to its parent and keep their poses and velocities.</summary>
    public void Destroy(FrameHandle frame)
    {
        PoseNode node = Node(frame);
        FrameHandle parent = Parent(frame);
        var children = new List<FrameHandle>();
        GetChildren(frame, children);
        // Reparent converts each child's velocity into the new parent's axes. A child of a root frame becomes a root.
        foreach (FrameHandle child in children)
            Reparent(child, parent);
        byOwner.Remove(owner[node.Id]);
        owner[node.Id] = Entity.Null;
        poses.Remove(node);
    }

    /// <summary>
    /// Moves a frame under a new parent. Its pose and velocity relative to everything else stay the same.
    /// </summary>
    public void Reparent(FrameHandle frame, FrameHandle newParent)
    {
        PoseNode node = Node(frame);
        FrameHandle oldParent = Parent(frame);
        if (oldParent == newParent)
            return;

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

    public FrameHandle Parent(FrameHandle frame)
    {
        PoseNode p = poses.Parent(Node(frame));
        return p.IsNull ? FrameHandle.Null : Handle(p);
    }

    public bool IsAncestorOf(FrameHandle ancestor, FrameHandle frame) => poses.IsAncestorOf(Node(ancestor), Node(frame));

    public void GetChildren(FrameHandle frame, List<FrameHandle> children)
    {
        var nodes = new List<PoseNode>();
        poses.GetChildren(Node(frame), nodes);
        foreach (PoseNode n in nodes)
            children.Add(Handle(n));
    }

    public DPose LocalPose(FrameHandle frame) => poses.GetLocal(Node(frame));

    public void SetLocalPose(FrameHandle frame, in DPose pose) => poses.Local(Node(frame)) = pose;

    public ref FrameBounds Bounds(FrameHandle frame) => ref bounds[Node(frame).Id];
    public ref MotionDriver Driver(FrameHandle frame) => ref driver[Node(frame).Id];
    public ref KeplerOrbit Orbit(FrameHandle frame) => ref orbit[Node(frame).Id];
    public ref GravityField Gravity(FrameHandle frame) => ref gravity[Node(frame).Id];

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

    /// <summary>The effective gravity on a body at <paramref name="position"/> inside the frame, in frame axes.</summary>
    public Vector3 EffectiveGravity(FrameHandle frame, Double3 position)
    {
        int id = Node(frame).Id;
        return gravity[id].At(position) - properAcceleration[id];
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

    /// <summary>Visits every live frame in dense order.</summary>
    public void GetAll(List<FrameHandle> frames)
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
        for (FrameHandle f = frame; !f.IsNull; f = Parent(f))
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
            x = Parent(x);
        }
        if (x == to)
            return vel;
        // x is now an ancestor of 'to' (or the virtual root). Descend along the path to 'to'.
        var path = new Stack<FrameHandle>();
        for (FrameHandle y = to; !y.IsNull && y != x; y = Parent(y))
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
            FrameHandle parent = Parent(x);
            omega = LocalPose(x).TransformDirection(omega) + angularVelocity[Node(x).Id];
            x = parent;
        }
        var path = new Stack<FrameHandle>();
        for (FrameHandle y = to; !y.IsNull && y != x; y = Parent(y))
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
        Array.Resize(ref gravity, size);
        Array.Resize(ref linearVelocity, size);
        Array.Resize(ref angularVelocity, size);
        Array.Resize(ref properAcceleration, size);
    }
}
