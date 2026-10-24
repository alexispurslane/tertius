using System.Numerics;
using System.Runtime.CompilerServices;
using Tertius.Frames.Rails;

namespace Tertius;

/// <summary>
/// The tree of reference frames. Poses live in a packed <see cref="PoseTree{TPose}"/> of <see cref="DPose"/>.
/// Per-frame data lives in parallel arrays, indexed by the frame's stable index.
/// Mutate the tree only at the sync point, then call <see cref="Update"/>. During a tick, the tree is read-only
/// and safe to read from any thread.
/// <para>
/// Invariants, checked by <see cref="AssertInvariants()"/>:
/// <list type="bullet">
/// <item>The rotation of every frame's local pose is a unit quaternion: the square of its length is within
/// <see cref="DQuaternion.UnitTolerance"/> of 1. A frame whose rotation is a little long or short moves everything in its
/// parent by that much of the distance to the parent's origin, as seen from the frame. The tree checks it in a build with
/// contracts, when a caller creates a frame or sets a local pose, and the World keeps it: it normalizes a rotation that a
/// caller gives it, and physics normalizes the rotation that leaves Jolt. The poses that the tree composes itself, when it
/// moves or destroys a frame, are unit length.</item>
/// <item>A numeric frame (see <see cref="CreateNumeric"/>) has no owner, a parent, no bubble, the identity rotation, no
/// spin, no gravity, no medium and no hull motion, and it keeps no turns of its own, no angular acceleration and no place in the list
/// of frames without a last turn: it reads its semantic frame's turns (see <see cref="Turns"/>). A resting or nesting one
/// has no velocity, no step acceleration and no step plan (see <see cref="HomeInstantVelocityOverChord"/>). The chain of
/// tree parents of a numeric frame ends at a semantic frame: a falling or resting frame lies in a semantic frame or a
/// nesting frame, a following frame lies in a semantic frame, and a nesting frame lies in a semantic frame, on a whole
/// multiple of <see cref="NumericFrameOptions.NestGrid"/>, and holds only numeric frames. A following frame, and only a
/// following frame, has an anchor (see <see cref="Anchors"/>): the owner of a frame with a kinematic hull that hangs in
/// it, at its origin, or a plain kinematic body of the last update whose home it is. At the end of
/// <see cref="UpdateNumericFrames"/>, every falling or resting frame is the home of a body or holds a frame, and every
/// nesting frame holds a frame and is the home of no body; between updates, the count of bodies is the one of the last
/// update. <see cref="NumericCount"/> is the number of numeric frames. A semantic frame has an owner, no step
/// acceleration and no step plan, and a semantic frame whose tree parent is a numeric frame
/// has a dynamic hull, or it hangs at the origin of that frame, whose anchor its owner is, and its hull is kinematic: a
/// frame with a static hull or without a hull moves relative to its semantic parent, and a frame with a kinematic hull
/// hangs in its semantic parent or in its following frame, which follows its owner's motion intents. The motion of a hull here is its motion in the step that the
/// numeric frames are planned for, which a change of motion can make differ from the stored motion of the last finished
/// step (see <see cref="HullMotionInStep(FrameHandle, ReadOnlySpan{PhysicsIntent})"/> and <see cref="PlanHullMotion"/>). Such a frame also owns a
/// hull that was a body of the last update: only the update moves a frame into a numeric frame, with its hull, and a frame
/// without a hull goes to the semantic frame when the frame that holds it dies (see <see cref="Heir"/>). Every ghost of
/// <see cref="AnchorGhosts"/> names the anchor of a live following frame and a live frame of that frame's semantic frame
/// other than the following frame. This holds between updates too: a frame that leaves the tree takes the ghosts that
/// name it (see <see cref="RemoveNode"/>), and a following frame that loses its anchor takes the ghosts of that anchor (see
/// <see cref="LeaveFollowingFrame"/>). Every leave clock (see <see cref="NumericFrameRules.LeaveClock"/>)
/// is at least zero and names a body of the last update that is not an anchor, needs a frame by itself and stays in its
/// semantic frame's own system, so no clock outlives its body's place in the input. Every thrust clock (see
/// <see cref="NumericFrameRules.ThrustClock"/>) names a body of the last update that is not static, with a thrust time above
/// zero and a quiet time from zero to below <see cref="NumericFrameOptions.ThrustWindow"/>.</item>
/// </list>
/// </para>
/// </summary>
public sealed partial class FrameTree
{
    /// <summary>
    /// Below this effective gravity, in m/s², physics never stands a character, so the character is weightless: it
    /// keeps its momentum and its last up, and only forces, the weak gravity and what it touches change its velocity. The
    /// physics backend then makes it a rigid body that cannot turn, until the gravity at it reaches twice this value, or
    /// this value while it touches something. At or above it, physics stands a character only on walkable ground. A
    /// character in the air is weightless there too, because it falls freely. Physics reports which characters it stood,
    /// so the components that steer characters agree with it.
    /// </summary>
    public const float ZeroGravityThreshold = 0.05f;

    private static readonly Comparer<FrameHandle> ByIndex =
        Comparer<FrameHandle>.Create((a, b) => a.Index.CompareTo(b.Index));

    private readonly PoseTree<DPose> poses = new(64);
    private readonly Dictionary<Entity, FrameHandle> byOwner = [];

    private Entity[] owner = new Entity[64];

    private FrameBounds[] bounds = new FrameBounds[64];

    // The stored motion of each frame's hull, the motion of the last finished step, or null for a frame without a hull
    // (see HullMotion).
    private BodyMotion?[] hullMotion = new BodyMotion?[64];

    private GravityField[] gravity = new GravityField[64];

    // The medium of each frame, beside its gravity field (see Medium). A numeric frame has none.
    private MediumField[] medium = new MediumField[64];
    private Double3[] linearVelocity = new Double3[64];
    private Vector3[] angularVelocity = new Vector3[64];

    private Vector3[] properAcceleration = new Vector3[64];

    // Each frame's proper acceleration as the last finished step found it, the value that physics used for the frame's
    // bodies during that step: TakeStepRecords copies the rows here before it writes the new values, and a new frame has
    // zero. It is valid from step 2 to the next step 2 (see FieldTheStepFound).
    private Vector3[] properAccelerationTheStepFound = new Vector3[64];

    // What made the record that each frame took at the last sync point (see Followed).
    private FollowKind[] followKind = new FollowKind[64];

    // Numeric frames: the mode, and the acceleration that a falling frame keeps for the current step, relative to its
    // parent. A semantic frame has the mode None and no step acceleration.
    private NumericMode[] numeric = new NumericMode[64];

    private Double3[] stepAcceleration = new Double3[64];

    // For a following frame, the step record of its anchor for the current step in the anchor's semantic frame: the record
    // of its motion intent, or without one the record of a kinematic body that holds still where the step starts
    // (PhysicsIntent.StepRecordOrStill). It is the step plan, which lands the following frame and which the sync point
    // reads (see BeginStep). Null for every other frame.
    private MotionSample?[] stepPlan = new MotionSample?[64];

    // How long the members of each numeric frame have been apart, in seconds, and the number of bodies whose physics home
    // it was at the end of the last UpdateNumericFrames.
    private double[] apartTime = new double[64];

    private int[] numericBodies = new int[64];

    // The number of changes of the state that an owner view reads (see OwnerViewStamp). Only the writers of the section
    // "Writes that an owner view reads" increase it.

    /// <summary>How many frames the tree holds, numeric frames included.</summary>
    internal int Count => poses.Count;

    /// <summary>How many of the frames are numeric frames, which the engine makes and gameplay never sees.</summary>
    internal int NumericCount { get; private set; }

    /// <summary>
    /// Changes on every structural change (add, remove or reparent). Physics compares it with the version that it last
    /// followed.
    /// </summary>
    internal int Version => poses.Version;

    /// <summary>
    /// Changes on every change of the state that an owner view reads (see <see cref="FrameOwnerView"/>): a write of the local
    /// pose, the linear velocity or the angular velocity of any frame, and every add, remove or move of a frame. Every such
    /// change passes through the writers of the section "Writes that an owner view reads", which increase it. An owner view
    /// also reads whether each frame is numeric: a numeric frame has no owner view, and the walk up from a frame's parent
    /// passes every numeric frame to find the semantic frame and adds its position. Only an add or a remove changes whether
    /// a frame is numeric, because a numeric frame only changes from one numeric mode to another (the contracts of
    /// <see cref="SetNumericMode"/> and <see cref="LeaveFollowingFrame"/> require it), so the mode needs no increase of its own.
    /// Reads leave it as it is, and so do the writes of what no owner view reads: the proper acceleration, the gravity
    /// field, the stored motion of a hull (see <see cref="SetHullMotion"/>), and the mode of a frame that stays numeric,
    /// such as a following frame that a hull leaves falling (see <see cref="PlanHullMotion"/>). The World compares it with its
    /// value at the World's last pass over every owner view, to skip a pass that would change no row.
    /// </summary>
    internal long OwnerViewStamp { get; private set; }

    /// <summary>The local poses of all frames, by dense index. Copy it to keep a tick's poses.</summary>
    internal ReadOnlySpan<DPose> LocalPosesDense => poses.LocalPoses;

    /// <summary>
    /// How <paramref name="frame"/> followed its owner at the last sync point, which the frame's row stores beside the record
    /// of the step that ended (see "The follow kind" in docs/plans/rails-components.md): <see cref="FollowKind.Rails"/> when
    /// it took a full sample that its owner wrote, <see cref="FollowKind.Kinematic"/> when it took an end pose,
    /// <see cref="FollowKind.Physics"/> when it took the move that physics made of its dynamic hull, and
    /// <see cref="FollowKind.Static"/> otherwise: a teleport, a write of its owner's transform, or no record. The motion of
    /// its hull in the step that ended counts, not a change of motion that the next step applies (see
    /// <see cref="FollowKindInStep"/>). A new frame has followed nothing. Throws <see cref="InvalidOperationException"/> for
    /// a stale frame.
    /// </summary>
    public FollowKind Followed(FrameHandle frame) => followKind[Node(frame).Id];

    public FrameHandle Create(
        Entity frameOwner,
        FrameHandle parent,
        in DPose localPose,
        FrameBounds frameBounds,
        GravityField field = default,
        MediumField medium = default)
    {
        if (frameOwner.IsNull)
            throw new ArgumentException("A frame needs an owner entity.", nameof(frameOwner));
        if (byOwner.ContainsKey(frameOwner))
            throw new InvalidOperationException($"{frameOwner} already owns a frame.");
        Contract.Requires(localPose.Rotation.IsUnit);

        FrameHandle handle = AddFrame(frameOwner: frameOwner, parent: parent, localPose: localPose, frameBounds: frameBounds, field: field, mode: NumericMode.None, frameMedium: medium);
        byOwner.Add(frameOwner, handle);
        return handle;
    }

    private FrameHandle AddFrame(Entity frameOwner, FrameHandle parent, in DPose localPose, FrameBounds frameBounds,
        GravityField field, NumericMode mode, MediumField frameMedium = default)
    {
        PoseNode node = AddNode(parent.IsNull ? PoseNode.Null : Node(parent), localPose);
        Grow(node.Id + 1);
        // The new frame's row: the add above changed the stamp for all of it.
        owner[node.Id] = frameOwner;
        bounds[node.Id] = frameBounds;
        hullMotion[node.Id] = null;
        gravity[node.Id] = field;
        medium[node.Id] = frameMedium;
        linearVelocity[node.Id] = Double3.Zero;
        angularVelocity[node.Id] = Vector3.Zero;
        properAcceleration[node.Id] = Vector3.Zero;
        properAccelerationTheStepFound[node.Id] = Vector3.Zero;
        followKind[node.Id] = FollowKind.Static;
        numeric[node.Id] = mode;
        stepAcceleration[node.Id] = Double3.Zero;
        stepPlan[node.Id] = null;
        apartTime[node.Id] = 0;
        numericBodies[node.Id] = 0;
        anchor[node.Id] = Entity.Null;
        // A new frame has had no step, so it has no turn and no last turn until its first BeginStep. A numeric frame keeps
        // no turns of its own (see Turns).
        turns[node.Id] = NoTurns;
        angularAcceleration[node.Id] = Double3.Zero;
        withoutLastTurn[node.Id] = mode == NumericMode.None;
        return new(frameOwner, node.Id, node.Generation);
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
            if (IsNumeric(child))
                Dissolve(child, parent, dissolved);
            else
                Reparent(child, Heir(parent));
        RemoveNode(frame, node);
    }

    /// <summary>
    /// The frame that takes each semantic frame that a dying frame holds, when <paramref name="parent"/> is the tree parent of
    /// the dying frame: the semantic frame of that parent, whatever the hull of the frame that moves (see
    /// <see cref="HullMotion"/>). That parent is numeric for the frame of a kinematic hull in its following frame, which falls
    /// on when its anchor dies, and for the frame of a ship in a numeric frame. The World moves the bodies of the dying
    /// frame into the own system of the nearest semantic frame that survives, the hull of each frame that moves among them,
    /// so each such frame hangs where its hull lives. The next numeric update moves the hull, with its frame, into a numeric
    /// frame when its group needs one. So only the update puts a frame in a numeric frame, and only for a hull in its input.
    /// A frame without a hull, such as a room with no body, is in no update, and nothing would move it out of a numeric
    /// frame (see the invariants of the class). Before the review of ter-4psc, the heir depended on how the moving frame
    /// moved, so a room without a body could go to the numeric parent, and it then kept the ghosts of its bodies there.
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
            if (IsNumeric(child))
                Dissolve(child, target, dissolved);
            else
                Reparent(child, Heir(target));
        dissolved?.Add(frame);
        RemoveNode(frame, Node(frame));
    }

    /// <summary>
    /// Removes <paramref name="frame"/>, whose node is <paramref name="node"/>, from the tree. The ghosts of anchors that a
    /// group whose home it is keeps go with it (see the invariants of the class). A following frame whose hull's frame goes
    /// falls on, and a following frame that goes with its anchor in it, a plain body, takes the ghosts of that anchor (see
    /// <see cref="LeaveFollowingFrame"/>).
    /// </summary>
    private void RemoveNode(FrameHandle frame, PoseNode node)
    {
        if (InFollowingFrame(frame))
            LeaveFollowingFrame(TreeParent(frame));
        if (numeric[node.Id] == NumericMode.Following && !anchor[node.Id].IsNull)
            LeaveFollowingFrame(frame);
        for (int i = anchorGhosts.Count - 1; i >= 0; i--)
            if (anchorGhosts[i].Frame == frame)
                anchorGhosts.RemoveAt(i);
        if (!owner[node.Id].IsNull)
            byOwner.Remove(owner[node.Id]);
        owner[node.Id] = Entity.Null;
        if (numeric[node.Id] != NumericMode.None)
            NumericCount--;
        numeric[node.Id] = NumericMode.None;
        anchor[node.Id] = Entity.Null;
        withoutLastTurn[node.Id] = false;
        RemovePoseNode(node);
    }

    /// <summary>
    /// Moves a frame under a new parent. Its pose and velocity relative to everything else stay the same. A frame that
    /// leaves its following frame leaves it falling (see <see cref="NumericMode.Following"/>). Throws
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
            throw new InvalidOperationException(
                $"Cannot move frame {frame} under itself or its own descendant {newParent}.");

        if (InFollowingFrame(frame))
            LeaveFollowingFrame(oldParent);

        // Carry the frame origin's velocity, the frame's spin and its angular acceleration from the old parent into the new
        // parent. A null parent is the virtual root above every root frame. The angular acceleration converts by the rule of
        // a relative motion (see FrameMotion), with the motion of the old parent in the new one.
        DPose oldPose = poses.GetLocal(node);
        Double3 w = angularVelocity[node.Id];
        DQuaternion oldToNew = RootPose(newParent).Rotation.Conjugate() * RootPose(oldParent).Rotation;
        Double3 relativeSpin = AngularVelocityRelative(oldParent, newParent);
        var oldParentInNew = new FrameMotion(Pose: new(Double3.Zero, oldToNew), Velocity: Double3.Zero, AngularVelocity: relativeSpin, Acceleration: Double3.Zero,
            AngularAcceleration: AngularAccelerationRelative(oldParent, newParent));
        angularAcceleration[node.Id] = oldParentInNew.AngularAccelerationToParent(angularAcceleration[node.Id], w);
        LinearVelocityForWrite(node.Id) =
            ConvertVelocity(from: oldParent, to: newParent, position: oldPose.Position, v: linearVelocity[node.Id]);
        AngularVelocityForWrite(node.Id) = (oldToNew.Rotate(w) + relativeSpin).ToVector3();
        MoveNode(node, newParent.IsNull ? PoseNode.Null : Node(newParent), keepWorldPose: true);
    }

    public bool IsAlive(FrameHandle frame) => !frame.IsNull && poses.IsAlive(new(frame.Index, frame.Generation)) &&
                                              owner[frame.Index] == frame.Entity;

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

    public bool IsAncestorOf(FrameHandle ancestor, FrameHandle frame) =>
        poses.IsAncestorOf(Node(ancestor), Node(frame));

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

    private void AddSemanticChildren(FrameHandle frame, List<FrameHandle> children)
    {
        var tree = new List<FrameHandle>();
        GetTreeChildren(frame, tree);
        foreach (FrameHandle child in tree)
            if (IsNumeric(child))
                AddSemanticChildren(child, children);
            else
                children.Add(child);
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
        return treeParent.IsNull || !IsNumeric(treeParent)
            ? local
            : new(InSemantic(treeParent, SemanticOf(treeParent), local.Position), local.Rotation);
    }

    /// <summary>The pose of the frame in its tree parent, as it is stored.</summary>
    internal DPose TreeLocalPose(FrameHandle frame) => poses.GetLocal(Node(frame));

    /// <summary>
    /// Hands the owner view of every frame that is not numeric (see <see cref="FrameOwnerView"/>) to
    /// <paramref name="visitor"/>, in dense order: one pass over the tree's own storage, with no handle check. The visitor
    /// must not change what an owner view reads, which a contract checks with <see cref="OwnerViewStamp"/> after each visit.
    /// </summary>
    internal void VisitOwnerViews<TVisitor>(ref TVisitor visitor) where TVisitor : struct, IOwnerViewVisitor
    {
        ReadOnlySpan<int> parents = poses.ParentsDense;
        ReadOnlySpan<DPose> locals = poses.LocalPoses;
        ReadOnlySpan<int> ids = poses.IdsDense;
        ReadOnlySpan<uint> generations = poses.Generations;
        long stamp = OwnerViewStamp;
        for (int dense = 0; dense < ids.Length; dense++)
        {
            if (numeric[ids[dense]] != NumericMode.None)
                continue;
            visitor.Visit(OwnerViewAt(dense: dense, parents: parents, locals: locals, ids: ids, generations: generations));
            Contract.Assert(OwnerViewStamp == stamp, "An owner view visitor changed the frame tree during the walk.");
        }
    }

    /// <summary>
    /// The owner view of <paramref name="frame"/>, a frame that is not numeric (see <see cref="FrameOwnerView"/>). Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    internal FrameOwnerView OwnerView(FrameHandle frame)
    {
        PoseNode node = Node(frame);
        Contract.Requires(numeric[node.Id] == NumericMode.None);
        return OwnerViewAt(dense: poses.DenseIndex(node), parents: poses.ParentsDense, locals: poses.LocalPoses, ids: poses.IdsDense,
            generations: poses.Generations);
    }

    /// <summary>
    /// The owner view of the frame at dense index <paramref name="dense"/>, from the tree's dense arrays. The position in
    /// the semantic frame is the same sum, in the same order, as <see cref="InSemantic"/>: the frame's local position, then
    /// the place of each numeric frame on the way up, the nearest first, so that it rounds once at the scale of the
    /// distance.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private FrameOwnerView OwnerViewAt(int dense, ReadOnlySpan<int> parents, ReadOnlySpan<DPose> locals,
        ReadOnlySpan<int> ids,
        ReadOnlySpan<uint> generations)
    {
        int id = ids[dense];
        ref readonly DPose local = ref locals[dense];
        int p = parents[dense];
        FrameHandle home = FrameHandle.Null, semantic = FrameHandle.Null;
        Double3 position = local.Position;
        if (p >= 0)
        {
            home = HandleOf(ids[p], generations);
            int s = p;
            while (numeric[ids[s]] != NumericMode.None)
            {
                position += locals[s].Position;
                s = parents[s];
            }

            semantic = s == p ? home : HandleOf(ids[s], generations);
        }

        return new(Frame: HandleOf(id, generations), Home: home, Semantic: semantic, LocalPose: local, PositionInSemantic: position, LinearVelocity: linearVelocity[id], AngularVelocity: angularVelocity[id]);
    }

    /// <summary>The handle of the live frame with stable index <paramref name="id"/>, with its generation from
    /// <paramref name="generations"/>.</summary>
    private FrameHandle HandleOf(int id, ReadOnlySpan<uint> generations) => new(owner[id], id, generations[id]);

    /// <summary>
    /// Sets the pose of the frame in its parent (see <see cref="Parent"/>). The rotation must be unit length (see
    /// <see cref="DQuaternion.IsUnit"/>): a caller that cannot promise it normalizes the rotation first (see
    /// <see cref="DPose.WithUnitRotation"/>).
    /// </summary>
    public void SetLocalPose(FrameHandle frame, in DPose pose)
    {
        FrameHandle treeParent = TreeParent(frame);
        SetTreeLocalPose(frame, treeParent.IsNull || !IsNumeric(treeParent)
            ? pose
            : new(pose.Position - HomeOffset(treeParent, SemanticOf(treeParent)), pose.Rotation));
    }

    /// <summary>Sets the pose of the frame in its tree parent, as it is stored. The rotation must be unit length.</summary>
    internal void SetTreeLocalPose(FrameHandle frame, in DPose pose)
    {
        Contract.Requires(pose.Rotation.IsUnit);
        LocalForWrite(Node(frame)) = pose;
    }

    /// <summary>The bubble of the frame. It never changes after the frame is created.</summary>
    public FrameBounds Bounds(FrameHandle frame) => bounds[Node(frame).Id];

    /// <summary>
    /// The stored motion of the frame's hull, the body of the frame's owner in the system of the frame's parent: the motion
    /// that the hull had in the last finished step. Null for a frame without a hull: a root frame, whose owner's body is
    /// only the frame's static interior, a frame whose owner has no body, and a numeric frame. A frame becomes a root when
    /// its root parent dies, and it then has no hull. The World sets the motion at spawn from the body's description (see
    /// <see cref="BodyDesc.Motion"/>), and at step 4 of each sync point from the changes of motion in the intents that the
    /// step that ended applied (<c>Self.Body.SetMotion</c>, written one tick before), where the collision rows of the bodies
    /// change too. So like every state that a tick reads, it describes the last finished step. The motion of a step that
    /// has not finished is derived from the pending change of motion, and never stored: see
    /// <see cref="HullMotionInStep(FrameHandle, ReadOnlySpan{PhysicsIntent})"/> and <c>World.NextStepHullMotion</c>. The tree changes only at sync points, so
    /// the stored motion is stable during a tick, and any worker may read it. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    public BodyMotion? HullMotion(FrameHandle frame)
    {
        PoseNode node = Node(frame);
        return poses.Parent(node).IsNull ? null : hullMotion[node.Id];
    }

    /// <summary>
    /// The motion that the frame's hull has in the step that applies <paramref name="stepIntents"/>, which is the step after
    /// the last finished step. That is the change of motion in the intent of the frame's owner there
    /// (<see cref="IntentFlags.SetMotion"/>), or else the stored motion of the last finished step (see
    /// <see cref="HullMotion"/>). Null for a frame without a hull, whatever the intents hold. Which step that is depends on
    /// the intents that the caller passes. With the intents that the World calls front, it is step k during tick k, which
    /// runs beside step k, and at the sync point after tick k until step 4: the running step, whose output that sync point
    /// reads. Physics asks it so at the start of each step, with the intents of its context, and so do the check of the
    /// step's output (see <see cref="PhysicsOutputCheck"/>) and the follow kind of the record that each frame takes of the
    /// step (see <see cref="FollowKindInStep"/>). Step 4 stores the changes of step k and swaps the intents, so after it the
    /// front intents are those of step k + 1, the next step: the World asks it for that step there (see
    /// <c>World.NextStepHullMotion</c>), and so does the check of the tree's invariants at the end of the sync point (see
    /// <see cref="AssertInvariants()"/>). Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <param name="frame">The frame whose hull it asks about.</param>
    /// <param name="stepIntents">
    /// The intents that the step applies, by entity slot (<see cref="Entity.Index"/>), as
    /// <see cref="PhysicsStepContext.Intents"/> holds them. A slot past their end holds no intent.
    /// </param>
    public BodyMotion? HullMotionInStep(FrameHandle frame, ReadOnlySpan<PhysicsIntent> stepIntents) =>
        HullMotionInStep(HullMotion(frame), frame.Entity.Index, stepIntents);

    /// <summary>
    /// The rule of <see cref="HullMotionInStep(FrameHandle, ReadOnlySpan{PhysicsIntent})"/> for a frame whose hull has the
    /// stored motion <paramref name="stored"/>, or no hull, and whose owner has the slot <paramref name="slot"/>.
    /// </summary>
    private static BodyMotion? HullMotionInStep(BodyMotion? stored, uint slot, ReadOnlySpan<PhysicsIntent> stepIntents)
    {
        if (stored is null || slot >= (uint)stepIntents.Length)
            return stored;
        ref readonly PhysicsIntent intent = ref stepIntents[(int)slot];
        return (intent.Flags & IntentFlags.SetMotion) != 0 ? intent.Motion : stored;
    }

    /// <summary>
    /// Stores <paramref name="motion"/>, or none, as the motion of the hull of <paramref name="frame"/> in the last finished
    /// step (see <see cref="HullMotion"/>). It changes nothing else: the numeric frames and the motion intents follow the
    /// motion of the step that they are for, which a change gets before it runs (see <see cref="PlanHullMotion"/>). It changes no
    /// velocity of the frame, and not <see cref="Version"/>. Throws <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>
    /// Only the simulation calls it: at a spawn, and at step 4 of a sync point for a change of motion that the step that
    /// ended applied. It checks nothing, because the tree does not know the owner's body. The World checks that the body can
    /// take the motion (see <see cref="BodyDesc.Problem"/>) before the motion gets here: when it checks the spawn, and when
    /// the owner writes the change (<c>World.WriteHullMotion</c>). <c>World.AssertInvariants</c> checks it again.
    /// </remarks>
    internal void SetHullMotion(FrameHandle frame, BodyMotion? motion) => hullMotion[Node(frame).Id] = motion;

    /// <summary>
    /// Keeps <paramref name="frame"/> coherent with <paramref name="nextStepMotion"/>, the motion that its hull has in the
    /// next step, which its owner wrote in the running tick. The tree stores that motion only when the step has finished
    /// (see <see cref="SetHullMotion"/>). Only a kinematic hull hangs at the origin of a following frame. So a hull that is not
    /// kinematic in the next step leaves its following frame falling: a frame with a dynamic hull may hang in a falling frame,
    /// and the numeric update moves any other one, with its hull, into its semantic parent. The World drops the motion intent
    /// that the hull's motion in that step refuses (see <c>World.ApplyMotionChanges</c>). It changes no velocity of the frame, and not
    /// <see cref="Version"/>. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>
    /// Only the simulation calls it, at step 4 of a sync point before the intents swap, for each owner that wrote a change
    /// of motion in the tick, whatever the motion of the running step: a following frame of a hull that is not kinematic in the
    /// next step is wrong in any case.
    /// </remarks>
    internal void PlanHullMotion(FrameHandle frame, BodyMotion nextStepMotion)
    {
        Contract.Requires(HullMotion(frame) is not null);
        if (nextStepMotion == BodyMotion.Kinematic)
            return;
        if (InFollowingFrame(frame))
            LeaveFollowingFrame(TreeParent(frame));
    }

    /// <summary>
    /// The medium of the frame, the gas that fills it, at rest in its axes (see <see cref="MediumField"/>).
    /// <see cref="SetMedium"/> changes it. Like the gravity field, it changes only between ticks, so the value stored when a
    /// step starts is the value of that step, and the sync point of tick k reads the value of step k. A frame keeps it when
    /// it hands off or is reparented. A numeric frame has none. Only puffs read it: physics reads no medium. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    public MediumField Medium(FrameHandle frame) => medium[Node(frame).Id];

    /// <summary>
    /// Gives a frame a new medium (see <see cref="Medium"/>). The frame's puffs feel it from the next step on. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </summary>
    /// <remarks>
    /// Only the simulation calls it, through <c>World.SetFrameMedium</c>, which refuses a medium that is not valid.
    /// </remarks>
    internal void SetMedium(FrameHandle frame, in MediumField value) => medium[Node(frame).Id] = value;

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
    /// Checks the invariants of the tree for a step whose intents hold no change of motion (see
    /// <see cref="AssertInvariants(ReadOnlySpan{PhysicsIntent})"/>).
    /// </summary>
    internal void AssertInvariants() => AssertInvariants([]);

    /// <summary>
    /// Checks the invariants of the tree (see the class summary): the rotation of every frame's local pose is unit length,
    /// and the numeric frames keep the rules of the class summary. The numeric frames are planned for the step that applies
    /// <paramref name="stepIntents"/>, so the rules about the motion of a hull read the motion of that step (see
    /// <see cref="HullMotionInStep(FrameHandle, ReadOnlySpan{PhysicsIntent})"/>). At the end of a sync point, the World passes the intents that the next step
    /// applies. O(frames). It runs only on demand, so it works in every build.
    /// </summary>
    internal void AssertInvariants(ReadOnlySpan<PhysicsIntent> stepIntents)
    {
        ReadOnlySpan<DPose> locals = poses.LocalPoses;
        bool[] holdsFrame = new bool[owner.Length];
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
                Contract.Invariant(!owner[id].IsNull && stepAcceleration[id] == Double3.Zero && stepPlan[id] is null);
                Contract.Invariant(anchor[id].IsNull);
                // A nesting frame holds only numeric frames.
                Contract.Invariant(parentMode != NumericMode.Nesting);
                // A frame with a static hull or without a hull moves relative to its semantic parent, and a frame with a kinematic
                // hull hangs in its semantic parent or at the origin of its following frame, in the step that the numeric frames
                // are for. So a frame in any other numeric frame has a dynamic hull in that step.
                if (parentMode == NumericMode.Following && anchor[parent.Index] == frame.Entity)
                    Contract.Invariant(HullMotionInStep(frame, stepIntents) == BodyMotion.Kinematic &&
                                       locals[i].Position == Double3.Zero);
                else if (parentMode != NumericMode.None)
                    Contract.Invariant(HullMotionInStep(frame, stepIntents) == BodyMotion.Dynamic);
                // Only a frame with a hull hangs in a numeric frame.
                if (parentMode != NumericMode.None)
                {
                    if (hulls is null)
                    {
                        hulls = [];
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
            Contract.Invariant(!gravity[id].HasValue && !medium[id].HasValue && !hullMotion[id].HasValue);
            // A numeric frame never turns: it reads its semantic frame's turns, and keeps none of its own.
            Contract.Invariant(turns[id] == NoTurns && angularAcceleration[id] == Double3.Zero && !withoutLastTurn[id]);
            if (numeric[id] is NumericMode.Resting or NumericMode.Nesting)
                Contract.Invariant(linearVelocity[id] == Double3.Zero && stepAcceleration[id] == Double3.Zero &&
                                   stepPlan[id] is null);
            // Only a following frame has an anchor: a hull whose frame hangs in it, or a plain body of the last update whose
            // home it is.
            if (numeric[id] == NumericMode.Following)
            {
                Contract.Invariant(!anchor[id].IsNull);
                if (byOwner.TryGetValue(anchor[id], out FrameHandle anchorFrame))
                    Contract.Invariant(TreeParent(anchorFrame) == frame);
                else
                    Contract.Invariant(IsPlainAnchorOfTheLastUpdate(anchor[id], frame));
            }
            else
            {
                Contract.Invariant(anchor[id].IsNull);
            }

            // The chain of tree parents ends at a semantic frame: a falling or resting frame lies in one or in a nesting
            // frame, a following frame lies in one, and a nesting frame lies in one, on an exact grid point.
            if (numeric[id] == NumericMode.Nesting)
                Contract.Invariant(parentMode == NumericMode.None &&
                                   NumericFrameRules.OnGrid(locals[i].Position, NumericOptions.NestGrid));
            else if (numeric[id] == NumericMode.Following)
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
            FrameHandle following = FollowingFrameOf(ghost.Anchor);
            Contract.Invariant(!following.IsNull && IsAlive(ghost.Frame));
            Contract.Invariant(ghost.Frame != following && SemanticOf(ghost.Frame) == TreeParent(following));
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
            if (!NumericFrameRules.IsAnchor(body, NumericOptions))
                lastBodies[body.Entity] = body;
        foreach ((Entity entity, double apart) in leaveClocks)
        {
            bool known = lastBodies.TryGetValue(entity, out NumericBody body);
            Contract.Invariant(apart >= 0 && known);
            Contract.Invariant(!NumericFrameRules.IsAnchor(body, NumericOptions) && body.Home == body.Semantic &&
                               NumericFrameRules.BodyMode(body, NumericOptions) != NumericMode.None);
        }
    }

    /// <summary>
    /// True when <paramref name="entity"/>, which owns no frame, is a kinematic body of the last numeric update whose home is
    /// <paramref name="following"/>: a plain anchor at its place in the input, which lives in that frame's system. For the
    /// invariants only.
    /// </summary>
    private bool IsPlainAnchorOfTheLastUpdate(Entity entity, FrameHandle following)
    {
        foreach (NumericBody body in sortedBodies)
            if (body.Entity == entity && body.Home == following && body.Kinematic && body.OwnedFrame.IsNull)
                return true;
        return false;
    }

    /// <summary>The live following frame that <paramref name="entity"/> anchors, or null. O(frames), for the invariants only.</summary>
    private FrameHandle FollowingFrameOf(Entity entity)
    {
        for (int i = 0; i < poses.Count; i++)
        {
            int id = poses.NodeAt(i).Id;
            if (numeric[id] == NumericMode.Following && anchor[id] == entity)
                return AtDense(i);
        }

        return FrameHandle.Null;
    }

    /// <summary>
    /// Linear velocity of the frame origin relative to the parent (see <see cref="Parent"/>), in parent axes. The numeric
    /// frames between them do not rotate, so this adds their velocities to the velocity in the tree parent.
    /// </summary>
    public Double3 LinearVelocity(FrameHandle frame)
    {
        Double3 velocity = linearVelocity[Node(frame).Id];
        FrameHandle treeParent = TreeParent(frame);
        return treeParent.IsNull || !IsNumeric(treeParent)
            ? velocity
            : velocity + HomeVelocity(treeParent, SemanticOf(treeParent));
    }

    /// <summary>Linear velocity of the frame origin relative to its tree parent, in its axes, as it is stored.</summary>
    internal Double3 TreeLinearVelocity(FrameHandle frame) => linearVelocity[Node(frame).Id];

    /// <summary>Sets the linear velocity of the frame origin relative to its tree parent, in its axes (see
    /// <see cref="TreeLinearVelocity"/>).</summary>
    internal void SetTreeLinearVelocity(FrameHandle frame, Double3 velocity) =>
        LinearVelocityForWrite(Node(frame).Id) = velocity;

    /// <summary>
    /// Angular velocity of the frame relative to the parent, in parent axes. A numeric frame between them has the axes of
    /// its parent and does not turn, so this is also the angular velocity relative to the tree parent.
    /// </summary>
    public Vector3 AngularVelocity(FrameHandle frame) => angularVelocity[Node(frame).Id];

    /// <summary>Sets the angular velocity of the frame relative to its parent, in parent axes (see
    /// <see cref="AngularVelocity"/>).</summary>
    internal void SetAngularVelocity(FrameHandle frame, Vector3 velocity) =>
        AngularVelocityForWrite(Node(frame).Id) = velocity;

    /// <summary>
    /// What an accelerometer at the frame origin reads over the last finished step, in m/s², in frame axes: the frame's
    /// acceleration minus the gravity that acts at its place. Zero for a frame in free fall, and the gravity at its place,
    /// reversed, for a frame that holds still, so a parked cabin's contents have weight. Step 2 of every sync point derives
    /// it from the frame's step record (see <see cref="TakeStepRecords"/>), and takes a dynamic hull's from physics, which
    /// measures it by the same rule. No gameplay writes it. The rule reads the parent's value as the step found it, the
    /// value stored before step 2, so a change moves one level down a chain of frames in each step (see
    /// <see cref="FieldTheStepFound"/>). A new frame reads zero during its first step, and the rule's value from
    /// the first step 2 after its spawn on (docs/plans/rails-components.md, "The proper acceleration of a new frame").
    /// </summary>
    public Vector3 ProperAcceleration(FrameHandle frame) => properAcceleration[Node(frame).Id];

    /// <summary>
    /// The gravity that a body at rest at <paramref name="position"/> in <paramref name="frame"/> feels over the last
    /// finished step, in m/s², in frame axes: the frame's field there (see <see cref="Gravity"/>), less its proper
    /// acceleration (see <see cref="ProperAcceleration"/>), plus the centrifugal term −ω × (ω × r) and the Euler term
    /// −α × r of the frame's inertial turn at r = <paramref name="position"/> (docs/plans/spin-gravity.md, "V5, the effective
    /// gravity"). ω and α are the frame's angular velocity and angular acceleration relative to the virtual root above the
    /// roots (see <see cref="AngularVelocityRelative"/> and <see cref="AngularAccelerationRelative"/>), turned into the
    /// frame's axes. It takes no velocity, so it has no Coriolis term. Game code reads it to ask whether a place has weight,
    /// and which way is down.
    /// <para>
    /// It reads only stored values, which step 2 of each sync point writes for the step that ended, so it describes the last
    /// finished step in every tick, as <see cref="ProperAcceleration"/> does. The internal effective gravity that physics
    /// reads describes the step in flight during a tick. In a frame that does not turn, with ω and α zero, it is the field
    /// less the proper acceleration, to the bit. It walks up the frame's chain three times and allocates nothing. Throws
    /// <see cref="InvalidOperationException"/> for a stale frame.
    /// </para>
    /// </summary>
    public Double3 FeltGravity(FrameHandle frame, Double3 position)
    {
        Double3 linear = gravity[Node(frame).Id].AtPrecise(position) - ProperAcceleration(frame);
        Double3 omega = AngularVelocityRelative(frame, FrameHandle.Null),
            alpha = AngularAccelerationRelative(frame, FrameHandle.Null);
        if (omega == Double3.Zero && alpha == Double3.Zero)
            return linear;
        DPose inRoot = RootPose(frame);
        omega = inRoot.InverseTransformDirection(omega);
        alpha = inRoot.InverseTransformDirection(alpha);
        return linear - Double3.Cross(omega, Double3.Cross(omega, position)) - Double3.Cross(alpha, position);
    }

    /// <summary>The pose of <paramref name="of"/> in the space of <paramref name="to"/>, through their common ancestor.</summary>
    public DPose Relative(FrameHandle of, FrameHandle to) => poses.Relative(Node(of), Node(to));

    /// <summary>Writes every frame's pose relative to <paramref name="reference"/> into <paramref name="result"/>, by dense index.</summary>
    internal void ComputeRelativeTo(FrameHandle reference, Span<DPose> result) =>
        poses.ComputeRelativeTo(Node(reference), result);

    /// <summary>
    /// Like <see cref="ComputeRelativeTo(FrameHandle, Span{DPose})"/>, but with the local poses taken from
    /// <paramref name="localPoses"/> by dense index, such as a copy of last tick's poses.
    /// </summary>
    internal void ComputeRelativeTo(FrameHandle reference, ReadOnlySpan<DPose> localPoses, Span<DPose> result) =>
        poses.ComputeRelativeTo(Node(reference), localPoses, result);

    internal int DenseIndex(FrameHandle frame) => poses.DenseIndex(Node(frame));

    internal FrameHandle AtDense(int dense) => Handle(poses.NodeAt(dense));

    /// <summary>Restores order and recomputes root-relative poses. Call it at the end of every sync point.</summary>
    public void Update() => poses.UpdateWorld();

    /// <summary>
    /// The linear gravity on a body at <paramref name="position"/> in the frame <paramref name="frame"/>, which holds the
    /// body in its physics system, in frame axes: the field of the frame's semantic frame at the body's true position, minus
    /// the acceleration of <paramref name="frame"/> relative to the semantic frame (docs/plans/spin-gravity.md, "Terms"). For
    /// a semantic frame, that is its own field minus its proper acceleration. In a falling numeric frame it is the tidal
    /// gravity, plus the difference of the rotation terms that the frame's origin takes (see <see cref="BeginStep"/>). It
    /// leaves out the rotation terms of a turning frame, which <see cref="EffectiveGravity"/> adds. Physics reads it for the
    /// field part of a body's update and of the chord rule. It stays in doubles, so that the reader that needs a float narrows
    /// only the difference, and a tidal gravity of a fraction of a mm/s² keeps its digits. Like the effective gravity, it
    /// reads the home's acceleration in the step in flight, so it is internal.
    /// </summary>
    internal Double3 LinearGravity(FrameHandle frame, Double3 position)
    {
        int id = Node(frame).Id;
        if (numeric[id] == NumericMode.None)
            return FieldOf(id, position);
        FrameHandle semantic = SemanticOf(frame);
        return FieldOf(semantic.Index, position + HomeOffset(frame, semantic)) - HomeAcceleration(frame, semantic);
    }

    /// <summary>
    /// The effective gravity on a body at <paramref name="position"/> in the frame <paramref name="frame"/>, in frame axes,
    /// which are the axes of its semantic frame (docs/plans/spin-gravity.md, "V5, the effective gravity"): the linear gravity
    /// (see <see cref="LinearGravity"/>) plus the centrifugal and the Euler term of the semantic frame's turns at the body's
    /// semantic place (see <see cref="WithRotationTerms"/>). It takes no velocity, so it has no Coriolis term. It is the one
    /// continuous value for the readers that need one: the form, the standing and the weight of a character, the sleep
    /// threshold, and the acceleration that a sleeping hull rests against. The sum of its double terms narrows once, in the
    /// reader that needs a float. In a frame that does not turn it is the linear gravity, to the bit, and so it is before the
    /// frame's first <see cref="BeginStep"/>.
    /// <para>
    /// It reads the turns, which describe the step in flight from <see cref="BeginStep"/> on and the last finished step from
    /// step 2 on, so only physics and the frame tree read it, in the step whose turn they hold. Game code reads
    /// <see cref="FeltGravity"/>, which describes the last finished step.
    /// </para>
    /// </summary>
    internal Double3 EffectiveGravity(FrameHandle frame, Double3 position) =>
        AddRotationTerms(frame, position, LinearGravity(frame, position));

    /// <summary>
    /// The effective gravity on a body at <paramref name="position"/> in the frame <paramref name="frame"/> (see
    /// <see cref="EffectiveGravity"/>), from <paramref name="linear"/>, the linear gravity there that the caller read already
    /// (see <see cref="LinearGravity"/>): <paramref name="linear"/> plus the centrifugal and the Euler term of the semantic
    /// frame's turns at the body's semantic place, so that a reader of both evaluates the field once. It equals
    /// <see cref="EffectiveGravity"/> to the bit. In a frame that does not turn it is <paramref name="linear"/> itself, and it
    /// reads no place.
    /// </summary>
    internal Double3 AddRotationTerms(FrameHandle frame, Double3 position, Double3 linear)
    {
        int id = Node(frame).Id;
        if (numeric[id] == NumericMode.None)
            return WithRotationTerms(linear, id, position);
        FrameHandle semantic = SemanticOf(frame);
        return turns[semantic.Index].Turns
            ? WithRotationTerms(linear, semantic.Index, position + HomeOffset(frame, semantic))
            : linear;
    }

    /// <summary>
    /// The push of <paramref name="frame"/> in the step in flight, in m/s², in frame axes: what an accelerometer at its origin
    /// reads, apart from the frame's own field. The members of the frame feel it, reversed, in their effective gravity (see
    /// <see cref="EffectiveGravity"/>). For a semantic frame it is the stored proper acceleration, which the step in flight
    /// subtracts. For a numeric frame it is the acceleration of its origin relative to its semantic frame in the step in
    /// flight less that of a free body at its origin (see <see cref="FreeAccelerationAtOrigin"/>), which takes in the
    /// semantic frame's own proper acceleration, and in a turning semantic frame its rotation terms at the origin: zero for a
    /// falling frame, the field reversed for a resting frame, and for a following frame the acceleration that lands it on
    /// its anchor's step plan less the free body's. Physics wakes the members of a frame when its push moves, by the same
    /// rule as a change of the frame's turn (docs/plans/spin-gravity.md, "The wake"). Valid from <see cref="BeginStep"/> to
    /// the end of the step.
    /// </summary>
    internal Vector3 PushInStep(FrameHandle frame)
    {
        int id = Node(frame).Id;
        if (numeric[id] == NumericMode.None)
            return properAcceleration[id];
        FrameHandle semantic = SemanticOf(frame);
        return (HomeAcceleration(frame, semantic) - FreeAccelerationAtOrigin(frame, semantic, turnsStep)).ToVector3();
    }

    /// <summary>
    /// The gravity of the semantic frame of <paramref name="frame"/> at a body's true position, for a body at
    /// <paramref name="position"/> in <paramref name="frame"/>, in frame axes: the semantic frame's field minus its proper
    /// acceleration, plus its centrifugal and Euler term there (see <see cref="WithRotationTerms"/>). It equals
    /// <see cref="EffectiveGravity"/> in a semantic frame. In a numeric frame it is the gravity that the semantic frame
    /// shows, which says where up is. It reads the turns as the effective gravity does.
    /// </summary>
    internal Double3 SemanticGravity(FrameHandle frame, Double3 position)
    {
        FrameHandle semantic = SemanticOf(frame);
        Double3 place = position + HomeOffset(frame, semantic);
        return WithRotationTerms(FieldOf(semantic.Index, place), semantic.Index, place);
    }

    /// <summary>The field of the semantic frame with index <paramref name="id"/> minus its proper acceleration, in doubles.</summary>
    private Double3 FieldOf(int id, Double3 position) => gravity[id].AtPrecise(position) - properAcceleration[id];

    /// <summary>
    /// Like <see cref="FieldOf"/>, but with the frame's proper acceleration as the last finished step found it: the gravity at
    /// <paramref name="position"/> that the rule of <see cref="ProperAccelerationFrom"/> subtracts for a child at step 2,
    /// whether the pass gave the frame its new value already or not. After step 2 a frame's row holds the value of the
    /// step that ended, so a read of a child's acceleration from its stored proper acceleration, the rule backward, takes
    /// this one too (see <see cref="CreateFollowingFrame"/>). It is valid from step 2 to the next step 2: the pass of
    /// <see cref="TakeStepRecords"/> copies every row's value before it writes the new ones.
    /// </summary>
    private Double3 FieldTheStepFound(int id, Double3 position) =>
        gravity[id].AtPrecise(position) - properAccelerationTheStepFound[id];

    /// <summary>
    /// The acceleration of the origin of <paramref name="home"/> relative to <paramref name="semantic"/>, which is the home
    /// itself or an ancestor of it, during the current step: the sum of the step accelerations of the numeric frames
    /// between them, with no rotation terms, because a numeric frame never turns in its parent.
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
    /// How far the origin of <paramref name="home"/> moves relative to <paramref name="semantic"/>, the home itself or an
    /// ancestor of it, over a step of <paramref name="dt"/> seconds that starts now: the sum of the move of each numeric frame
    /// between them, as <see cref="AdvanceNumericFrames"/> moves it. It is <see cref="HomeOffsetAfter"/> less
    /// <see cref="HomeOffset"/>, without the rounding of their sums of places. A body that holds still in its home ends the
    /// step this far from where it stands now, in the axes of the semantic frame, because a numeric frame never turns. Zero
    /// when the home is the semantic frame.
    /// </summary>
    internal Double3 HomeMoveOver(FrameHandle home, FrameHandle semantic, double dt)
    {
        Contract.Requires(home == semantic || IsAncestorOf(semantic, home));
        Double3 move = Double3.Zero;
        for (FrameHandle f = home; f != semantic; f = TreeParent(f))
        {
            int id = Node(f).Id;
            move += (linearVelocity[id] + stepAcceleration[id] * dt) * dt;
        }

        return move;
    }

    /// <summary>
    /// Like <see cref="HomeVelocity"/>, but at the end of a step of <paramref name="dt"/> seconds that starts now (see
    /// <see cref="AdvanceNumericFrames"/>).
    /// </summary>
    internal Double3 HomeVelocityAfter(FrameHandle home, FrameHandle semantic, double dt) =>
        HomeVelocity(home, semantic) + HomeAcceleration(home, semantic) * dt;

    /// <summary>
    /// How much the instant velocity of the origin of <paramref name="home"/> at the end of the current step, a step of
    /// <paramref name="dt"/> seconds, exceeds its frame velocity, relative to <paramref name="semantic"/>, the home itself or
    /// an ancestor of it: the sum of that excess for each numeric frame between them. A numeric frame steps as Jolt steps a
    /// body (see <see cref="AdvanceNumericFrames"/>), so its frame velocity is the chord of the step. A falling frame keeps
    /// one acceleration over the step, so its instant velocity at the end of the step is half a step of that acceleration
    /// past the chord. A following frame moves as its anchor's step plan does, so the excess is the plan's instant velocity
    /// less its chord, which takes in the change of the orbit's pull over the step, and is zero for the plan of an anchor
    /// that holds still (see <see cref="BeginStep"/>). Zero for a semantic frame.
    /// <para>
    /// The tree keeps the velocity of a frame under a numeric home relative to the home's frame velocity, so that a read in
    /// the semantic frame gives the frame's instant velocity. Physics keeps a velocity relative to the home's instant
    /// velocity. So a velocity converts from physics into the tree by the addition of this one term, and back by its
    /// subtraction: a frame at rest in its home keeps it (see <see cref="TakeStepRecords"/>), every step record that physics
    /// releases adds it. A velocity that a body gets at the start of a step converts by the excess where the step starts
    /// instead (see <see cref="HomeInstantVelocityOverChordAtStepStart"/>). A body that takes its home's linear gravity
    /// moves as Jolt moves it in the semantic frame's own system, so physics measures its velocity at the end of a step
    /// against the home's frame velocity after the step plus half a step of the home's step acceleration, and adds the
    /// difference of that excess and this one before it hands the body off or releases its record.
    /// </para>
    /// </summary>
    internal Double3 HomeInstantVelocityOverChord(FrameHandle home, FrameHandle semantic, double dt)
    {
        Contract.Requires(home == semantic || IsAncestorOf(semantic, home));
        Double3 excess = Double3.Zero;
        for (FrameHandle f = home; f != semantic; f = TreeParent(f))
        {
            int id = Node(f).Id;
            excess += stepPlan[id] is { } plan ? plan.Velocity - plan.ChordVelocity : stepAcceleration[id] * (0.5 * dt);
        }

        return excess;
    }

    /// <summary>
    /// Like <see cref="HomeInstantVelocityOverChord"/>, but where the current step starts: how much the instant velocity of
    /// the origin of <paramref name="home"/> there exceeds its frame velocity, the chord of the step before, relative to
    /// <paramref name="semantic"/>, the home itself or an ancestor of it. Each numeric frame between them steps as Jolt steps
    /// a body, with one step acceleration (see <see cref="AdvanceNumericFrames"/>), so the excess is half a step of that
    /// acceleration, for a falling frame and a following frame alike. It equals the excess at the end of the step for a
    /// falling frame, and for a following frame on a smooth plan to the plan's jerk. A following frame whose plan changes its
    /// velocity at the start of the step, such as an end pose that speeds up, or the plan of an anchor that stops (ticket
    /// ter-zsku), lands with the acceleration that turns its velocity into the plan's chord, and only this excess converts a
    /// velocity that a body gets at the start of that step into the motion that Jolt gives it in the semantic frame's own
    /// system: the step acceleration then cancels out of the body's motion in the semantic frame (docs/plans/numeric-frames.md,
    /// "Velocity"). Physics converts every velocity that a spawn, gameplay or a hand-off gives a body for a step with it.
    /// Zero for a semantic frame. Valid from <see cref="BeginStep"/> to the end of the step.
    /// </summary>
    internal Double3 HomeInstantVelocityOverChordAtStepStart(FrameHandle home, FrameHandle semantic, double dt)
    {
        Contract.Requires(home == semantic || IsAncestorOf(semantic, home));
        Double3 excess = Double3.Zero;
        for (FrameHandle f = home; f != semantic; f = TreeParent(f))
            excess += stepAcceleration[Node(f).Id] * (0.5 * dt);
        return excess;
    }

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
        VelocityToParent(pose: LocalPose(frame), velocity: LinearVelocity(frame), angular: AngularVelocity(frame), positionInFrame: positionInFrame,
            velocityInFrame: velocityInFrame);

    /// <summary>Velocity of a point in the frame, from the parent (see <see cref="Parent"/>): R⁻¹(v − v_frame − ω × (p − origin)).</summary>
    public Double3 VelocityFromParent(FrameHandle frame, Double3 positionInParent, Double3 velocityInParent) =>
        VelocityFromParent(pose: LocalPose(frame), velocity: LinearVelocity(frame), angular: AngularVelocity(frame), positionInParent: positionInParent,
            velocityInParent: velocityInParent);

    private static Double3 VelocityToParent(in DPose pose, Double3 velocity, Double3 angular, Double3 positionInFrame,
        Double3 velocityInFrame) =>
        pose.TransformDirection(velocityInFrame) + velocity +
        Double3.Cross(angular, pose.TransformDirection(positionInFrame));

    private static Double3 VelocityFromParent(in DPose pose, Double3 velocity, Double3 angular,
        Double3 positionInParent, Double3 velocityInParent) =>
        pose.InverseTransformDirection(velocityInParent - velocity -
                                       Double3.Cross(angular, positionInParent - pose.Position));

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
        return !poses.IsAlive(node) || owner[frame.Index] != frame.Entity
            ? throw new InvalidOperationException($"Stale or null frame {frame}.")
            : node;
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
            vel = VelocityToParent(pose: pose, velocity: linearVelocity[id], angular: angularVelocity[id], positionInFrame: p, velocityInFrame: vel);
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
            vel = VelocityFromParent(pose: pose, velocity: linearVelocity[id], angular: angularVelocity[id], positionInParent: p, velocityInParent: vel);
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

        if (x == to)
            return omega;
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

    // ---- Writes that an owner view reads ------------------------------------------------------------------------
    // Every change of a frame's local pose, linear velocity or angular velocity, and every add, remove or move of a frame,
    // passes through these seven members, which increase ownerViewStamp (see OwnerViewStamp). AddFrame fills the new frame's
    // row after its add, so that row needs no write of its own. An owner view also reads whether each frame is numeric,
    // which only an add or a remove changes: a write that makes a frame numeric, or stops it being numeric, any other way
    // must increase ownerViewStamp too.

    private PoseNode AddNode(PoseNode parent, in DPose localPose)
    {
        OwnerViewStamp++;
        return poses.Add(parent, localPose);
    }

    private void RemovePoseNode(PoseNode node)
    {
        OwnerViewStamp++;
        poses.Remove(node);
    }

    private void MoveNode(PoseNode node, PoseNode newParent, bool keepWorldPose)
    {
        OwnerViewStamp++;
        poses.Reparent(node, newParent, keepWorldPose);
    }

    /// <summary>The local pose of <paramref name="node"/>, to write.</summary>
    private ref DPose LocalForWrite(PoseNode node)
    {
        OwnerViewStamp++;
        return ref poses.Local(node);
    }

    /// <summary>
    /// The local poses of all frames by dense index, to write, for a pass that writes many frames (see
    /// <see cref="TakeStepRecords"/>). One increase covers every write of the pass, because no reader compares the stamp
    /// during it. Valid until the next structural change.
    /// </summary>
    private Span<DPose> LocalPosesForWrite()
    {
        OwnerViewStamp++;
        return poses.LocalPosesForWrite;
    }

    /// <summary>The linear velocity of the frame with stable index <paramref name="id"/>, to write.</summary>
    private ref Double3 LinearVelocityForWrite(int id)
    {
        OwnerViewStamp++;
        return ref linearVelocity[id];
    }

    /// <summary>The angular velocity of the frame with stable index <paramref name="id"/>, to write.</summary>
    private ref Vector3 AngularVelocityForWrite(int id)
    {
        OwnerViewStamp++;
        return ref angularVelocity[id];
    }

    private void Grow(int needed)
    {
        if (needed <= owner.Length)
            return;
        int size = Math.Max(needed, owner.Length * 2);
        Array.Resize(ref owner, size);
        Array.Resize(ref bounds, size);
        Array.Resize(ref hullMotion, size);
        Array.Resize(ref gravity, size);
        Array.Resize(ref medium, size);
        Array.Resize(ref linearVelocity, size);
        Array.Resize(ref angularVelocity, size);
        Array.Resize(ref properAcceleration, size);
        Array.Resize(ref properAccelerationTheStepFound, size);
        Array.Resize(ref followKind, size);
        Array.Resize(ref numeric, size);
        Array.Resize(ref stepAcceleration, size);
        Array.Resize(ref stepPlan, size);
        Array.Resize(ref apartTime, size);
        Array.Resize(ref numericBodies, size);
        Array.Resize(ref anchor, size);
        Array.Resize(ref anchorSeen, size);
        GrowTurns(size);
    }
}

/// <summary>
/// The state of <see cref="Frame"/> that the transform row of its owner mirrors (see
/// <see cref="FrameTree.VisitOwnerViews"/>):
/// its tree parent <see cref="Home"/>, which holds its hull and is the owner's physics home; the semantic frame of that
/// parent, or null for a root frame; its pose in the tree parent; the position of its origin in that semantic frame,
/// summed as <see cref="FrameTree.InSemantic"/> sums it; and its velocities relative to the tree parent.
/// </summary>
internal readonly record struct FrameOwnerView(
    FrameHandle Frame,
    FrameHandle Home,
    FrameHandle Semantic,
    DPose LocalPose,
    Double3 PositionInSemantic,
    Double3 LinearVelocity,
    Vector3 AngularVelocity);

/// <summary>Takes the owner view of each frame in <see cref="FrameTree.VisitOwnerViews"/>.</summary>
internal interface IOwnerViewVisitor
{
    void Visit(in FrameOwnerView view);
}
