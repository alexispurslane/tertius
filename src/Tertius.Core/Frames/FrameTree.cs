using System.Numerics;
using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>
/// The tree of reference frames. Poses live in a packed <see cref="PoseTree{TPose}"/> of <see cref="DPose"/>.
/// Per-frame data lives in parallel arrays, indexed by the frame's stable index.
/// Mutate the tree only at the sync point, then call <see cref="Update"/>. During a tick, the tree is read-only
/// and safe to read from any thread.
/// <para>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>The rotation of every frame's local pose is a unit quaternion: the square of its length is within
/// <see cref="DQuaternion.UnitTolerance"/> of 1. A frame whose rotation is a little long or short moves everything in its
/// parent by that much of the distance to the parent's origin, as seen from the frame. The tree checks it in a build with
/// contracts, when a caller creates a frame or sets a local pose, and the World keeps it: it normalizes a rotation that a
/// caller gives it, and physics normalizes the rotation that leaves Jolt. The poses that the tree composes itself, when it
/// moves or destroys a frame, are unit length.</item>
/// <item>A numeric frame (see <see cref="CreateNumeric"/>) has no owner, a parent, no bubble, the identity rotation, no
/// spin, the Static driver and no gravity. A resting or nesting one has no velocity and no step acceleration. The chain of
/// tree parents of a numeric frame ends at a semantic frame: a falling or resting frame lies in a semantic frame or a
/// nesting frame, an orbit frame lies in a semantic frame, and a nesting frame lies in a semantic frame, on a whole
/// multiple of <see cref="NumericFrameOptions.NestGrid"/>, and holds only numeric frames. An orbit frame, and only an
/// orbit frame, has an orbiter: a frame with a kinematic hull that hangs in it, at its origin. At the end of
/// <see cref="UpdateNumericFrames"/>, every falling or resting frame is the home of a body or holds a frame, and every
/// nesting frame holds a frame and is the home of no body; between updates, the count of bodies is the one of the last
/// update. <see cref="NumericCount"/> is the number of numeric frames. A semantic frame has an owner and no step
/// acceleration, and a semantic frame whose tree parent is a numeric frame has the Physics driver, or it is the orbiter of
/// that frame: a frame with a static hull or without a hull moves relative to its semantic parent, and a frame with a
/// kinematic hull hangs in its semantic parent or in its orbit frame, which follows the frame's motion samples. Such a frame also owns a
/// hull that was a body of the last update: only the update moves a frame into a numeric frame, with its hull, and a frame
/// without a hull goes to the semantic frame when the frame that holds it dies (see <see cref="Heir"/>). Every ghost of
/// <see cref="AnchorGhosts"/> names the hull of the orbiter of a live orbit frame and a live frame of that orbiter's
/// semantic parent other than the orbit frame. This holds between updates too: a frame that leaves the tree takes the
/// ghosts that name it (see <see cref="RemoveNode"/>), and an orbit frame that loses its orbiter takes the ghosts of its
/// hull (see <see cref="LeaveOrbit"/>). Every leave clock (see <see cref="NumericFrameRules.LeaveClock"/>)
/// is at least zero and names a body of the last update that is not an anchor, needs a frame by itself and stays in its
/// semantic frame's own system, so no clock outlives its body's place in the input. Every thrust clock (see
/// <see cref="NumericFrameRules.ThrustClock"/>) names a body of the last update that is not static, with a thrust time above
/// zero and a quiet time from zero to below <see cref="NumericFrameOptions.ThrustWindow"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class FrameTree
{
    private readonly PoseTree<DPose> poses = new(64);
    private readonly Dictionary<Entity, FrameHandle> byOwner = new();

    private Entity[] owner = new Entity[64];
    private FrameBounds[] bounds = new FrameBounds[64];
    private MotionDriver[] driver = new MotionDriver[64];
    private GravityField[] gravity = new GravityField[64];
    private Double3[] linearVelocity = new Double3[64];
    private Vector3[] angularVelocity = new Vector3[64];
    private Vector3[] properAcceleration = new Vector3[64];
    // Numeric frames: the mode, and the acceleration that a falling frame keeps for the current step, relative to its
    // parent. A semantic frame has the mode None and no step acceleration.
    private NumericMode[] numeric = new NumericMode[64];
    private Double3[] stepAcceleration = new Double3[64];
    // How long the members of each numeric frame have been apart, in seconds, and the number of bodies whose physics home
    // it was at the end of the last UpdateNumericFrames.
    private double[] apartTime = new double[64];
    private int[] numericBodies = new int[64];
    // The number of calls to SetDriver for each frame, and for every frame together.
    private int[] driverVersion = new int[64];
    private int driverChanges;

    public FrameTree()
    {
        Samples = new MotionSampleTable(this, owner.Length);
    }

    /// <summary>
    /// The motion samples that the owners of frames write, by frame index (see <see cref="MotionSampleTable"/>). The tree
    /// sizes it with its own rows, so that every frame has a row in each buffer from its creation on.
    /// </summary>
    internal MotionSampleTable Samples { get; }

    /// <summary>
    /// The motion sample that <paramref name="frame"/> takes in the physics step that runs beside the running tick: the
    /// sample that its owner wrote one tick before with <c>Self.MoveOnRails</c>, relative to the frame's parent (see
    /// <see cref="Parent"/>), in its axes. Between two ticks, it is the sample of the next step. False when the frame has no
    /// such sample, and for a stale or null frame.
    /// </summary>
    /// <remarks>
    /// A rails component reads it to learn where its frame stands when the step of the component's own sample starts: at
    /// this sample's place, or, when the frame has none, where it stands now (see <see cref="LocalPose"/>). After a swap of
    /// components, the last sample of the old component is this one.
    /// </remarks>
    public bool TryGetAppliedSample(FrameHandle frame, out MotionSample sample) => Samples.Applied.TryGet(frame, out sample);

    /// <summary>How many frames the tree holds, numeric frames included.</summary>
    internal int Count => poses.Count;

    /// <summary>How many of the frames are numeric frames, which the engine makes and gameplay never sees.</summary>
    internal int NumericCount { get; private set; }

    /// <summary>
    /// Changes on every structural change (add, remove or reparent) and on every call to <see cref="SetDriver"/>. Physics
    /// compares it with the version that it last followed.
    /// </summary>
    internal int Version => poses.Version + driverChanges;

    public FrameHandle Create(
        Entity frameOwner,
        FrameHandle parent,
        in DPose localPose,
        FrameBounds frameBounds,
        MotionDriver motion,
        GravityField field = default)
    {
        if (frameOwner.IsNull)
            throw new ArgumentException("A frame needs an owner entity.", nameof(frameOwner));
        if (byOwner.ContainsKey(frameOwner))
            throw new InvalidOperationException($"{frameOwner} already owns a frame.");
        Contract.Requires(localPose.Rotation.IsUnit);

        FrameHandle handle = AddFrame(frameOwner, parent, localPose, frameBounds, motion, field, NumericMode.None);
        byOwner.Add(frameOwner, handle);
        return handle;
    }

    private FrameHandle AddFrame(Entity frameOwner, FrameHandle parent, in DPose localPose, FrameBounds frameBounds, MotionDriver motion,
        GravityField field, NumericMode mode)
    {
        PoseNode node = poses.Add(parent.IsNull ? PoseNode.Null : Node(parent), localPose);
        Grow(node.Id + 1);
        owner[node.Id] = frameOwner;
        bounds[node.Id] = frameBounds;
        driver[node.Id] = motion;
        gravity[node.Id] = field;
        linearVelocity[node.Id] = Double3.Zero;
        angularVelocity[node.Id] = Vector3.Zero;
        properAcceleration[node.Id] = Vector3.Zero;
        numeric[node.Id] = mode;
        stepAcceleration[node.Id] = Double3.Zero;
        apartTime[node.Id] = 0;
        numericBodies[node.Id] = 0;
        orbiter[node.Id] = FrameHandle.Null;
        driverVersion[node.Id] = 0;
        return new FrameHandle(frameOwner, node.Id, node.Generation);
    }

    /// <summary>Destroys a frame. Its child frames move to its parent and keep their poses and velocities.</summary>
    public void Destroy(FrameHandle frame) => Destroy(frame, null);

    /// <summary>
    /// Destroys a frame. Its semantic child frames move to its parent and keep their poses and velocities (see
    /// <see cref="Heir"/>). Its numeric child frames go with it, because they lie in it: the frames that they hold move to
    /// the parent in the same way, and each numeric frame that goes is added to <paramref name="dissolved"/>. The caller
    /// has moved the bodies of those numeric frames out.
    /// </summary>
    internal void Destroy(FrameHandle frame, List<FrameHandle>? dissolved)
    {
        PoseNode node = Node(frame);
        FrameHandle parent = TreeParent(frame);
        var children = new List<FrameHandle>();
        GetTreeChildren(frame, children);
        // Reparent converts each child's velocity into the new parent's axes. A child of a root frame becomes a root. A
        // numeric child that moved would keep the rotation and the spin of the frame that died, which no numeric frame has.
        foreach (FrameHandle child in children)
        {
            if (IsNumeric(child))
                Dissolve(child, parent, dissolved);
            else
                Reparent(child, Heir(parent));
        }
        RemoveNode(frame, node);
    }

    /// <summary>
    /// The frame that takes each semantic frame that a dying frame holds, when <paramref name="parent"/> is the tree parent of
    /// the dying frame: the semantic frame of that parent, whatever the driver of the frame that moves. That parent is
    /// numeric for the frame of a Kepler hull in its orbit frame, which falls on when its orbiter dies, and for the frame of
    /// a ship in a numeric frame. The World moves the bodies of the dying frame into the own system of the nearest semantic
    /// frame that survives, the hull of each frame that moves among them, so each such frame hangs where its hull lives. The
    /// next numeric update moves the hull, with its frame, into a numeric frame when its group needs one. So only the update
    /// puts a frame in a numeric frame, and only for a hull in its input. A frame without a hull, such as a room with no
    /// body, is in no update, and nothing would move it out of a numeric frame (see the invariants of the class). Before the
    /// review of ter-4psc, a frame with the Physics driver went to the numeric parent, and such a room then kept the ghosts
    /// of its bodies there.
    /// </summary>
    private FrameHandle Heir(FrameHandle parent) => parent.IsNull ? parent : SemanticOf(parent);

    /// <summary>
    /// Destroys the numeric frame <paramref name="frame"/> and the numeric frames in it, and moves the semantic frames in
    /// them to <paramref name="target"/>, or to its semantic frame (see <see cref="Heir"/>).
    /// </summary>
    private void Dissolve(FrameHandle frame, FrameHandle target, List<FrameHandle>? dissolved)
    {
        var children = new List<FrameHandle>();
        GetTreeChildren(frame, children);
        foreach (FrameHandle child in children)
        {
            if (IsNumeric(child))
                Dissolve(child, target, dissolved);
            else
                Reparent(child, Heir(target));
        }
        dissolved?.Add(frame);
        RemoveNode(frame, Node(frame));
    }

    /// <summary>
    /// Removes <paramref name="frame"/>, whose node is <paramref name="node"/>, from the tree. The ghosts of anchors that a
    /// group whose home it is keeps go with it (see the invariants of the class). An orbit frame whose orbiter goes falls on.
    /// </summary>
    private void RemoveNode(FrameHandle frame, PoseNode node)
    {
        if (InOrbitFrame(frame))
            LeaveOrbit(TreeParent(frame));
        for (int i = anchorGhosts.Count - 1; i >= 0; i--)
            if (anchorGhosts[i].Frame == frame)
                anchorGhosts.RemoveAt(i);
        if (!owner[node.Id].IsNull)
            byOwner.Remove(owner[node.Id]);
        owner[node.Id] = Entity.Null;
        if (numeric[node.Id] != NumericMode.None)
            NumericCount--;
        numeric[node.Id] = NumericMode.None;
        orbiter[node.Id] = FrameHandle.Null;
        poses.Remove(node);
    }

    /// <summary>
    /// Moves a frame under a new parent. Its pose and velocity relative to everything else stay the same. A frame that
    /// leaves its orbit frame leaves it falling (see <see cref="NumericMode.Orbiting"/>). Throws
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

        if (InOrbitFrame(frame))
            LeaveOrbit(oldParent);

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
    /// child replaced by its own semantic children, in the order of their indices, so that numeric frames that come and go
    /// do not change it. Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    public void GetChildren(FrameHandle frame, List<FrameHandle> children)
    {
        int first = children.Count;
        AddSemanticChildren(frame, children);
        children.Sort(first, children.Count - first, ByIndex);
    }

    private static readonly Comparer<FrameHandle> ByIndex = Comparer<FrameHandle>.Create((a, b) => a.Index.CompareTo(b.Index));

    private void AddSemanticChildren(FrameHandle frame, List<FrameHandle> children)
    {
        var tree = new List<FrameHandle>();
        GetTreeChildren(frame, tree);
        foreach (FrameHandle child in tree)
        {
            if (IsNumeric(child))
                AddSemanticChildren(child, children);
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

    /// <summary>
    /// The pose of the frame in its parent (see <see cref="Parent"/>). The numeric frames between them do not rotate, so
    /// this adds their offsets to the pose in the tree parent.
    /// </summary>
    public DPose LocalPose(FrameHandle frame)
    {
        DPose local = TreeLocalPose(frame);
        FrameHandle treeParent = TreeParent(frame);
        return treeParent.IsNull || !IsNumeric(treeParent) ? local
            : new DPose(InSemantic(treeParent, SemanticOf(treeParent), local.Position), local.Rotation);
    }

    /// <summary>The pose of the frame in its tree parent, as it is stored.</summary>
    internal DPose TreeLocalPose(FrameHandle frame) => poses.GetLocal(Node(frame));

    /// <summary>
    /// Sets the pose of the frame in its parent (see <see cref="Parent"/>). The rotation must be unit length (see
    /// <see cref="DQuaternion.IsUnit"/>): a caller that cannot promise it normalizes the rotation first (see
    /// <see cref="DPose.WithUnitRotation"/>).
    /// </summary>
    public void SetLocalPose(FrameHandle frame, in DPose pose)
    {
        FrameHandle treeParent = TreeParent(frame);
        SetTreeLocalPose(frame, treeParent.IsNull || !IsNumeric(treeParent) ? pose
            : new DPose(pose.Position - HomeOffset(treeParent, SemanticOf(treeParent)), pose.Rotation));
    }

    /// <summary>Sets the pose of the frame in its tree parent, as it is stored. The rotation must be unit length.</summary>
    internal void SetTreeLocalPose(FrameHandle frame, in DPose pose)
    {
        Contract.Requires(pose.Rotation.IsUnit);
        poses.Local(Node(frame)) = pose;
    }

    /// <summary>The bubble of the frame. It never changes after the frame is created.</summary>
    public FrameBounds Bounds(FrameHandle frame) => bounds[Node(frame).Id];

    /// <summary>What moves the frame. <see cref="SetDriver"/> changes it.</summary>
    public MotionDriver Driver(FrameHandle frame) => driver[Node(frame).Id];

    /// <summary>
    /// Changes on every call to <see cref="SetDriver"/> for the frame, also a call that gives the frame the driver that it
    /// has, because each can change how the frame's hull moves. Physics compares it with the version that the frame's hull
    /// last followed.
    /// </summary>
    public int DriverVersion(FrameHandle frame) => driverVersion[Node(frame).Id];

    /// <summary>
    /// Gives a frame a motion driver, for example to park a ship. A frame that gets the static driver stops where it is:
    /// both of its velocities become zero, and it keeps its proper acceleration, so the cargo of a ship that is parked on
    /// the ground keeps its weight. A frame that gets another driver keeps its pose and velocities until that driver moves
    /// it. Every call changes <see cref="Version"/> and <see cref="DriverVersion"/>, also a call that gives the frame the
    /// driver that it has. So physics follows the call at the start of its next step, the same way that it follows a
    /// reparent: it puts the frame's hull at the frame's pose and velocity in the tree. A frame that hangs in its orbit
    /// frame and gets a driver whose hull is not kinematic leaves that frame falling, because no sample moves it any more: a frame
    /// with the Physics driver may hang in a falling frame, and the numeric update moves any other one, with its hull, into
    /// its semantic parent. Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>
    /// Only the simulation calls it, through <c>World.SetFrameDriver</c>, which refuses a driver that the frame cannot
    /// follow and keeps the owner of the frame in step.
    /// </remarks>
    internal void SetDriver(FrameHandle frame, MotionDriver motion)
    {
        Contract.Requires(Enum.IsDefined(motion));
        int id = Node(frame).Id;
        if (BodyDesc.FrameHullMotion(motion) != BodyMotion.Kinematic && InOrbitFrame(frame))
            LeaveOrbit(TreeParent(frame));
        driver[id] = motion;
        driverVersion[id]++;
        driverChanges++;
        if (motion == MotionDriver.Static)
        {
            linearVelocity[id] = Double3.Zero;
            angularVelocity[id] = Vector3.Zero;
        }
    }

    /// <summary>The gravity field of the frame. <see cref="SetGravity"/> changes it.</summary>
    public GravityField Gravity(FrameHandle frame) => gravity[Node(frame).Id];

    /// <summary>
    /// Gives a frame a new gravity field. Physics wakes the frame's bodies at the start of its next step. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>
    /// Only the simulation calls it, through <c>World.SetFrameGravity</c>, which refuses a field that is not valid.
    /// </remarks>
    internal void SetGravity(FrameHandle frame, in GravityField value) => gravity[Node(frame).Id] = value;

    /// <summary>
    /// Checks the invariants of the tree (see the class summary): the rotation of every frame's local pose is unit length,
    /// and the numeric frames keep the rules of the class summary.
    /// O(frames). It runs only on demand, so it works in every build.
    /// </summary>
    internal void AssertInvariants()
    {
        ReadOnlySpan<DPose> locals = poses.LocalPoses;
        var holdsFrame = new bool[owner.Length];
        for (int i = 0; i < poses.Count; i++)
            if (poses.Parent(poses.NodeAt(i)) is { IsNull: false } parentNode)
                holdsFrame[parentNode.Id] = true;
        int numericFrames = 0;
        // The frames whose hulls were bodies of the last update, which the first semantic frame in a numeric frame collects.
        HashSet<FrameHandle>? hulls = null;
        for (int i = 0; i < poses.Count; i++)
        {
            FrameHandle frame = AtDense(i);
            Contract.Invariant(locals[i].Rotation.IsUnit);
            int id = poses.NodeAt(i).Id;
            FrameHandle parent = TreeParent(frame);
            NumericMode parentMode = parent.IsNull ? NumericMode.None : numeric[parent.Index];
            if (numeric[id] == NumericMode.None)
            {
                Contract.Invariant(!owner[id].IsNull && stepAcceleration[id] == Double3.Zero);
                Contract.Invariant(orbiter[id].IsNull);
                // A nesting frame holds only numeric frames.
                Contract.Invariant(parentMode != NumericMode.Nesting);
                // A frame with a static hull or without a hull moves relative to its semantic parent, and a frame with a kinematic
                // hull hangs in its semantic parent or at the origin of its orbit frame.
                if (parentMode == NumericMode.Orbiting && orbiter[parent.Index] == frame)
                    Contract.Invariant(BodyDesc.FrameHullMotion(Driver(frame)) == BodyMotion.Kinematic && locals[i].Position == Double3.Zero);
                else if (parentMode != NumericMode.None)
                    Contract.Invariant(Driver(frame) == MotionDriver.Physics);
                // Only a frame with a hull hangs in a numeric frame.
                if (parentMode != NumericMode.None)
                {
                    if (hulls is null)
                    {
                        hulls = new HashSet<FrameHandle>();
                        foreach (NumericBody body in sortedBodies)
                            if (!body.OwnedFrame.IsNull)
                                hulls.Add(body.OwnedFrame);
                    }
                    Contract.Invariant(hulls.Contains(frame));
                }
                continue;
            }
            numericFrames++;
            Contract.Invariant(owner[id].IsNull && !parent.IsNull && bounds[id] == default);
            Contract.Invariant(locals[i].Rotation == DQuaternion.Identity && angularVelocity[id] == Vector3.Zero);
            Contract.Invariant(driver[id] == MotionDriver.Static && !gravity[id].HasValue);
            if (numeric[id] is NumericMode.Resting or NumericMode.Nesting)
                Contract.Invariant(linearVelocity[id] == Double3.Zero && stepAcceleration[id] == Double3.Zero);
            // Only an orbit frame has an orbiter, a live frame that hangs in it.
            if (numeric[id] == NumericMode.Orbiting)
                Contract.Invariant(IsAlive(orbiter[id]) && TreeParent(orbiter[id]) == frame);
            else
                Contract.Invariant(orbiter[id].IsNull);
            // The chain of tree parents ends at a semantic frame: a falling or resting frame lies in one or in a nesting
            // frame, an orbit frame lies in one, and a nesting frame lies in one, on an exact grid point.
            if (numeric[id] == NumericMode.Nesting)
                Contract.Invariant(parentMode == NumericMode.None && NumericFrameRules.OnGrid(locals[i].Position, NumericOptions.NestGrid));
            else if (numeric[id] == NumericMode.Orbiting)
                Contract.Invariant(parentMode == NumericMode.None);
            else
                Contract.Invariant(parentMode is NumericMode.None or NumericMode.Nesting);
            // The last update left no falling or resting frame without a body or a frame, and no nesting frame without a
            // frame or with a body.
            if (numeric[id] == NumericMode.Nesting)
                Contract.Invariant(numericBodies[id] == 0 && holdsFrame[id]);
            else
                Contract.Invariant(numericBodies[id] > 0 || holdsFrame[id]);
        }
        Contract.Invariant(NumericCount == numericFrames);
        foreach (NumericGhost ghost in anchorGhosts)
        {
            Contract.Invariant(TryGetFrame(ghost.Anchor, out FrameHandle hullFrame) && InOrbitFrame(hullFrame) && IsAlive(ghost.Frame));
            Contract.Invariant(ghost.Frame != TreeParent(hullFrame) && SemanticOf(ghost.Frame) == Parent(hullFrame));
        }
        if (thrustClocks.Count > 0)
        {
            // Only a body that is not static has a thrust clock. A frame owner has two bodies, its interior, which is static,
            // and its hull.
            var moving = new HashSet<Entity>();
            foreach (NumericBody body in sortedBodies)
                if (!body.Static)
                    moving.Add(body.Entity);
            foreach ((Entity entity, (double thrustTime, double quietTime)) in thrustClocks)
            {
                Contract.Invariant(moving.Contains(entity));
                Contract.Invariant(thrustTime > 0 && quietTime >= 0 && quietTime < NumericOptions.ThrustWindow);
            }
        }
        if (leaveClocks.Count == 0)
            return;
        // Only a body that is not an anchor has a clock. A frame owner has two bodies, its interior, an anchor, and its hull.
        var lastBodies = new Dictionary<Entity, NumericBody>();
        foreach (NumericBody body in sortedBodies)
            if (!body.Anchor)
                lastBodies[body.Entity] = body;
        foreach ((Entity entity, double apart) in leaveClocks)
        {
            bool known = lastBodies.TryGetValue(entity, out NumericBody body);
            Contract.Invariant(apart >= 0 && known);
            Contract.Invariant(!body.Anchor && body.Home == body.Semantic && NumericFrameRules.BodyMode(body, NumericOptions) != NumericMode.None);
        }
    }

    /// <summary>
    /// Linear velocity of the frame origin relative to the parent (see <see cref="Parent"/>), in parent axes. The numeric
    /// frames between them do not rotate, so this adds their velocities to the velocity in the tree parent.
    /// </summary>
    public Double3 LinearVelocity(FrameHandle frame)
    {
        Double3 velocity = linearVelocity[Node(frame).Id];
        FrameHandle treeParent = TreeParent(frame);
        return treeParent.IsNull || !IsNumeric(treeParent) ? velocity : velocity + HomeVelocity(treeParent, SemanticOf(treeParent));
    }

    /// <summary>Linear velocity of the frame origin relative to its tree parent, in its axes, as it is stored.</summary>
    internal ref Double3 TreeLinearVelocity(FrameHandle frame) => ref linearVelocity[Node(frame).Id];

    /// <summary>
    /// Angular velocity of the frame relative to the parent, in parent axes. A numeric frame between them has the axes of
    /// its parent and does not turn, so this is also the angular velocity relative to the tree parent.
    /// </summary>
    public ref Vector3 AngularVelocity(FrameHandle frame) => ref angularVelocity[Node(frame).Id];

    /// <summary>What an accelerometer at the frame origin reads, in frame axes. Zero for a frame in free fall.</summary>
    public ref Vector3 ProperAcceleration(FrameHandle frame) => ref properAcceleration[Node(frame).Id];

    /// <summary>The pose of <paramref name="of"/> in the space of <paramref name="to"/>, through their common ancestor.</summary>
    public DPose Relative(FrameHandle of, FrameHandle to) => poses.Relative(Node(of), Node(to));

    /// <summary>Writes every frame's pose relative to <paramref name="reference"/> into <paramref name="result"/>, by dense index.</summary>
    internal void ComputeRelativeTo(FrameHandle reference, Span<DPose> result) => poses.ComputeRelativeTo(Node(reference), result);

    /// <summary>
    /// Like <see cref="ComputeRelativeTo(FrameHandle, Span{DPose})"/>, but with the local poses taken from
    /// <paramref name="localPoses"/> by dense index, such as a copy of last tick's poses.
    /// </summary>
    internal void ComputeRelativeTo(FrameHandle reference, ReadOnlySpan<DPose> localPoses, Span<DPose> result) =>
        poses.ComputeRelativeTo(Node(reference), localPoses, result);

    /// <summary>The local poses of all frames, by dense index. Copy it to keep a tick's poses.</summary>
    internal ReadOnlySpan<DPose> LocalPosesDense => poses.LocalPoses;

    internal int DenseIndex(FrameHandle frame) => poses.DenseIndex(Node(frame));

    internal FrameHandle AtDense(int dense) => Handle(poses.NodeAt(dense));

    /// <summary>Restores order and recomputes root-relative poses. Call it at the end of every sync point.</summary>
    public void Update() => poses.UpdateWorld();

    /// <summary>
    /// Below this effective gravity, in m/s², physics never stands a character, so the character is weightless: it
    /// keeps its momentum and its last up, and only forces, the weak gravity and what it touches change its velocity. The
    /// physics backend then makes it a rigid body that cannot turn, until the gravity at it reaches twice this value, or
    /// this value while it touches something. At or above it, physics stands a character only on walkable ground. A
    /// character in the air is weightless there too, because it falls freely. Physics reports which characters it stood,
    /// so the components that steer characters agree with it.
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
    /// between them. It equals <see cref="AccelerationRelative"/> of the two frames, without its turns, because a numeric
    /// frame never turns in its parent.
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
            offset += TreeLocalPose(f).Position + (linearVelocity[id] + stepAcceleration[id] * dt) * dt;
        }
        return offset;
    }

    /// <summary>
    /// Like <see cref="HomeVelocity"/>, but at the end of a step of <paramref name="dt"/> seconds that starts now (see
    /// <see cref="AdvanceNumericFrames"/>).
    /// </summary>
    internal Double3 HomeVelocityAfter(FrameHandle home, FrameHandle semantic, double dt) =>
        HomeVelocity(home, semantic) + HomeAcceleration(home, semantic) * dt;

    /// <summary>
    /// How much the instant velocity of the origin of <paramref name="home"/> exceeds its frame velocity, relative to
    /// <paramref name="semantic"/>, the home itself or an ancestor of it, in a step of <paramref name="dt"/> seconds: half a
    /// step of the home's acceleration in the current step (see <see cref="HomeAcceleration"/>). A numeric frame steps as
    /// Jolt steps a body (see <see cref="AdvanceNumericFrames"/>), so its frame velocity is the chord of a step, and its
    /// instant velocity at the end of the step is half a step of its acceleration further on. Zero for a semantic frame.
    /// The tree keeps the velocity of a frame under a numeric home relative to the home's frame velocity, so that a read in
    /// the semantic frame gives the frame's instant velocity. Physics keeps a velocity relative to the home's instant
    /// velocity. So a velocity converts from physics into the tree by the addition of this one term: a frame at rest in its
    /// home keeps it (see <see cref="HoldStill"/>), and a step record that physics releases adds it.
    /// </summary>
    internal Double3 HomeInstantVelocityOverChord(FrameHandle home, FrameHandle semantic, double dt) =>
        HomeAcceleration(home, semantic) * (0.5 * dt);

    /// <summary>
    /// The position of the origin of <paramref name="of"/> in <paramref name="to"/>, two frames of one semantic frame (the
    /// semantic frame itself or numeric frames below it), at the end of a step of <paramref name="dt"/> seconds that starts
    /// now. It sums through their lowest common ancestor, so no large number enters between two frames near each other.
    /// </summary>
    internal Double3 OffsetAfter(FrameHandle of, FrameHandle to, double dt)
    {
        Contract.Requires(SemanticOf(of) == SemanticOf(to));
        FrameHandle common = of;
        while (common != to && !IsAncestorOf(common, to))
            common = TreeParent(common);
        return HomeOffsetAfter(of, common, dt) - HomeOffsetAfter(to, common, dt);
    }

    /// <summary>Converts a point from frame coordinates to parent coordinates (see <see cref="Parent"/>).</summary>
    public Double3 PointToParent(FrameHandle frame, Double3 point) => LocalPose(frame).TransformPoint(point);

    /// <summary>Converts a point from parent coordinates (see <see cref="Parent"/>) to frame coordinates.</summary>
    public Double3 PointFromParent(FrameHandle frame, Double3 point) => LocalPose(frame).InverseTransformPoint(point);

    /// <summary>Velocity of a point in the parent (see <see cref="Parent"/>): R·v + v_frame + ω × (R·p).</summary>
    public Double3 VelocityToParent(FrameHandle frame, Double3 positionInFrame, Double3 velocityInFrame) =>
        VelocityToParent(LocalPose(frame), LinearVelocity(frame), AngularVelocity(frame), positionInFrame, velocityInFrame);

    /// <summary>Velocity of a point in the frame, from the parent (see <see cref="Parent"/>): R⁻¹(v − v_frame − ω × (p − origin)).</summary>
    public Double3 VelocityFromParent(FrameHandle frame, Double3 positionInParent, Double3 velocityInParent) =>
        VelocityFromParent(LocalPose(frame), LinearVelocity(frame), AngularVelocity(frame), positionInParent, velocityInParent);

    private static Double3 VelocityToParent(in DPose pose, Double3 velocity, Double3 angular, Double3 positionInFrame, Double3 velocityInFrame) =>
        pose.TransformDirection(velocityInFrame) + velocity + Double3.Cross(angular, pose.TransformDirection(positionInFrame));

    private static Double3 VelocityFromParent(in DPose pose, Double3 velocity, Double3 angular, Double3 positionInParent, Double3 velocityInParent) =>
        pose.InverseTransformDirection(velocityInParent - velocity - Double3.Cross(angular, positionInParent - pose.Position));

    /// <summary>
    /// Adds every live semantic frame, parents before children: the root frames in the order of their indices, each
    /// followed by its children in the order of <see cref="GetChildren"/>. Numeric frames are an engine detail, so it skips
    /// them, and their coming and going does not change the order.
    /// </summary>
    public void GetAll(List<FrameHandle> frames)
    {
        var roots = new List<FrameHandle>();
        for (int i = 0; i < poses.Count; i++)
            if (poses.Parent(poses.NodeAt(i)).IsNull)
                roots.Add(AtDense(i));
        roots.Sort(ByIndex);
        foreach (FrameHandle root in roots)
            AddWithDescendants(root, frames);
    }

    private void AddWithDescendants(FrameHandle frame, List<FrameHandle> frames)
    {
        frames.Add(frame);
        var children = new List<FrameHandle>();
        GetChildren(frame, children);
        foreach (FrameHandle child in children)
            AddWithDescendants(child, frames);
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
            pose = DPose.Compose(TreeLocalPose(f), pose);
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
            int id = Node(x).Id;
            DPose pose = TreeLocalPose(x);
            vel = VelocityToParent(pose, linearVelocity[id], angularVelocity[id], p, vel);
            p = pose.TransformPoint(p);
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
            int id = Node(child).Id;
            DPose pose = TreeLocalPose(child);
            vel = VelocityFromParent(pose, linearVelocity[id], angularVelocity[id], p, vel);
            p = pose.InverseTransformPoint(p);
        }
        return vel;
    }

    /// <summary>
    /// The velocity of the origin of a body's physics home <paramref name="home"/> relative to its semantic frame
    /// <paramref name="semantic"/>, which is the home itself or an ancestor of it. The frames between them are numeric
    /// frames, which never rotate, so this is the sum of their frame velocities, in doubles. It is zero when the home is the
    /// semantic frame.
    /// </summary>
    internal Double3 HomeVelocity(FrameHandle home, FrameHandle semantic)
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
    internal Double3 HomeOffset(FrameHandle home, FrameHandle semantic) => InSemantic(home, semantic, Double3.Zero);

    /// <summary>
    /// <paramref name="position"/>, in the coordinates of <paramref name="home"/>, in the coordinates of
    /// <paramref name="semantic"/>, the home itself or an ancestor of it. The sum starts from the position and adds the
    /// place of each numeric frame on the way up, the nearest first. Below a nesting frame those are small numbers, so only
    /// the last addition, of the nest's place, rounds at the scale of the distance: a light-year out, the result is the
    /// exact position rounded once to the 2 m step of a double. Adding the position to <see cref="HomeOffset"/> instead
    /// rounds twice, and can miss by a whole step.
    /// </summary>
    internal Double3 InSemantic(FrameHandle home, FrameHandle semantic, Double3 position)
    {
        Contract.Requires(home == semantic || IsAncestorOf(semantic, home));
        for (FrameHandle f = home; f != semantic; f = TreeParent(f))
            position += TreeLocalPose(f).Position;
        return position;
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
            omega = TreeLocalPose(x).TransformDirection(omega) + angularVelocity[Node(x).Id];
            x = parent;
        }
        var path = new Stack<FrameHandle>();
        for (FrameHandle y = to; !y.IsNull && y != x; y = TreeParent(y))
            path.Push(y);
        while (path.Count > 0)
        {
            FrameHandle child = path.Pop();
            omega = TreeLocalPose(child).InverseTransformDirection(omega - angularVelocity[Node(child).Id]);
        }
        return omega;
    }

    /// <summary>
    /// The linear acceleration of the origin of frame <paramref name="of"/> relative to frame <paramref name="to"/> in the
    /// current step, in <paramref name="to"/> axes. An acceleration in <paramref name="of"/> converts into
    /// <paramref name="to"/> as the acceleration turned into <paramref name="to"/> axes plus this, as a velocity converts with
    /// the motion of one frame relative to the other (see <see cref="ConvertVelocity"/>). It adds the acceleration of each
    /// frame on the path through the common ancestor relative to its tree parent, level by level. The tree keeps that
    /// acceleration for a numeric frame, its step acceleration (see <see cref="BeginNumericStep"/>), so between a physics home
    /// and its semantic frame this equals <see cref="HomeAcceleration"/>. Two terms are not in it yet. A semantic frame adds
    /// none, because the tree keeps no acceleration of a semantic frame in its parent: only the frame's step record holds it.
    /// And a turning frame on the path adds no rotation terms (Euler, centrifugal and Coriolis), which spin gravity brings
    /// (docs/plans/spin-gravity.md). A null frame is the virtual root above every root frame.
    /// </summary>
    internal Double3 AccelerationRelative(FrameHandle of, FrameHandle to)
    {
        // Sum the accelerations up to the common ancestor in its axes, then subtract the path down to 'to'.
        FrameHandle x = of;
        Double3 acceleration = Double3.Zero;
        while (!x.IsNull && x != to && !IsAncestorOrNull(x, to))
        {
            FrameHandle parent = TreeParent(x);
            acceleration = TreeLocalPose(x).TransformDirection(acceleration) + stepAcceleration[Node(x).Id];
            x = parent;
        }
        var path = new Stack<FrameHandle>();
        for (FrameHandle y = to; !y.IsNull && y != x; y = TreeParent(y))
            path.Push(y);
        while (path.Count > 0)
        {
            FrameHandle child = path.Pop();
            acceleration = TreeLocalPose(child).InverseTransformDirection(acceleration - stepAcceleration[Node(child).Id]);
        }
        return acceleration;
    }

    private void Grow(int needed)
    {
        if (needed <= owner.Length)
            return;
        int size = Math.Max(needed, owner.Length * 2);
        Array.Resize(ref owner, size);
        Array.Resize(ref bounds, size);
        Array.Resize(ref driver, size);
        Array.Resize(ref gravity, size);
        Array.Resize(ref linearVelocity, size);
        Array.Resize(ref angularVelocity, size);
        Array.Resize(ref properAcceleration, size);
        Array.Resize(ref numeric, size);
        Array.Resize(ref stepAcceleration, size);
        Array.Resize(ref apartTime, size);
        Array.Resize(ref numericBodies, size);
        Array.Resize(ref orbiter, size);
        Array.Resize(ref driverVersion, size);
        Samples.Grow(size);
    }
}
