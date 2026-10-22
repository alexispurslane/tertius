using System.Buffers;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>A stable handle to a node in a <see cref="PoseTree{TPose}"/>. Generation 0 is the null node.</summary>
public readonly record struct PoseNode(int Id, uint Generation)
{
    public static PoseNode Null => default;
    public bool IsNull => Generation == 0;
}

/// <summary>
/// A packed tree of poses. Nodes live in dense parallel arrays with no gaps. Each parent comes before its
/// children, so <see cref="UpdateWorld"/> computes every world pose in one linear pass. A reparent can break
/// that order. The tree then marks itself dirty and restores the order at the next pass.
/// Node handles are stable. Dense indices change when the tree adds, removes or reorders nodes.
/// Mutate the tree from one thread only. Reads are safe from many threads at once only while nothing mutates the
/// tree and the order is clean: every read calls EnsureOrder, which reorders a dirty tree. Call
/// <see cref="UpdateWorld"/> after mutations to leave it clean.
/// <para>
/// The tree stores each local pose as it is given, and <see cref="Local"/> hands out a reference to it, so the caller writes
/// the pose. The tree does not check the rotation of a <see cref="DPose"/> that the caller writes. <see cref="DPose.Compose"/>
/// and <see cref="DPose.Inverse"/> normalize the rotation that they use, so a rotation that is a little off, such as a float
/// quaternion from Jolt, still gives consistent answers for the poses that the tree computes from it. The world pose of a root
/// is the exception, because it is the local pose as it is.
/// </para>
/// <para>
/// Only <see cref="FrameTree"/> keeps the rule that the local rotation of every pose is unit length, and it checks that rule
/// itself (see <see cref="FrameTree.AssertInvariants()"/>). Another tree relies on <see cref="DPose.Compose"/> and
/// <see cref="DPose.Inverse"/>, and not on the caller. The tree of attachments, for one, stores the pose of a chain root as
/// it is, and gameplay can write a pose with any rotation.
/// </para>
/// </summary>
public sealed class PoseTree<TPose> where TPose : struct, IPose<TPose>
{
    // Dense arrays, indexed by dense position.
    private int[] parent;        // dense index of the parent, or -1 for a root
    private TPose[] local;
    private TPose[] world;
    private int[] idAt;          // stable id of the node at each dense position

    // Stable arrays, indexed by stable id.
    private int[] denseOf;       // dense position of each id, or -1 for a free id
    private uint[] generation;
    private int[] freeIds;
    private int freeCount;
    private int idCount;

    private int count;
    private bool orderDirty;
    private bool worldDirty;

    public PoseTree(int capacity = 16)
    {
        capacity = Math.Max(capacity, 4);
        parent = new int[capacity];
        local = new TPose[capacity];
        world = new TPose[capacity];
        idAt = new int[capacity];
        denseOf = new int[capacity];
        generation = new uint[capacity];
        freeIds = new int[capacity];
    }

    public int Count => count;

    /// <summary>Increments on every structural change: add, remove or reparent.</summary>
    public int Version { get; private set; }

    public bool IsAlive(PoseNode node) =>
        (uint)node.Id < (uint)idCount && node.Generation != 0 && generation[node.Id] == node.Generation && denseOf[node.Id] >= 0;

    public PoseNode Add(PoseNode parentNode, in TPose localPose)
    {
        int parentDense = parentNode.IsNull ? -1 : Dense(parentNode);
        EnsureCapacity(count + 1);

        int id;
        if (freeCount > 0)
        {
            id = freeIds[--freeCount];
        }
        else
        {
            id = idCount++;
            generation[id] = 0;
        }
        generation[id]++;
        if (generation[id] == 0)
            generation[id] = 1;

        int dense = count++;
        parent[dense] = parentDense;
        local[dense] = localPose;
        world[dense] = parentDense < 0 ? localPose : TPose.Compose(world[parentDense], localPose);
        idAt[dense] = id;
        denseOf[id] = dense;
        // Appending never breaks parent-before-child order, because the parent already exists.
        Version++;
        return new PoseNode(id, generation[id]);
    }

    /// <summary>
    /// Removes a node. Its children move to its parent and keep their world poses.
    /// </summary>
    public void Remove(PoseNode node)
    {
        int dense = Dense(node);
        int newParent = parent[dense];
        TPose removedLocal = local[dense];

        for (int i = dense + 1; i < count; i++)
        {
            if (parent[i] == dense)
            {
                local[i] = TPose.Compose(removedLocal, local[i]);
                parent[i] = newParent;
            }
        }

        // Shift everything after the node down by one. Order is preserved, so no reorder is needed.
        int tail = count - dense - 1;
        if (tail > 0)
        {
            Array.Copy(parent, dense + 1, parent, dense, tail);
            Array.Copy(local, dense + 1, local, dense, tail);
            Array.Copy(world, dense + 1, world, dense, tail);
            Array.Copy(idAt, dense + 1, idAt, dense, tail);
        }
        count--;
        for (int i = dense; i < count; i++)
        {
            if (parent[i] > dense)
                parent[i]--;
            denseOf[idAt[i]] = i;
        }

        denseOf[node.Id] = -1;
        freeIds[freeCount++] = node.Id;
        Version++;
        worldDirty = true;
    }

    /// <summary>
    /// Moves a node and its subtree under a new parent. With <paramref name="keepWorldPose"/>, the node's local
    /// pose changes so that its pose relative to everything else stays the same.
    /// </summary>
    public void Reparent(PoseNode node, PoseNode newParentNode, bool keepWorldPose = true)
    {
        int dense = Dense(node);
        int newParent = newParentNode.IsNull ? -1 : Dense(newParentNode);
        if (newParent == parent[dense])
            return;
        if (newParent >= 0 && IsAncestorOrSelf(dense, newParent))
            throw new InvalidOperationException("Cannot reparent a node under itself or its own descendant.");

        if (keepWorldPose)
            local[dense] = newParentNode.IsNull ? RelativeToRoot(dense) : Relative(node, newParentNode);

        parent[dense] = newParent;
        if (newParent > dense)
            orderDirty = true;
        Version++;
        worldDirty = true;
    }

    public PoseNode Parent(PoseNode node)
    {
        int dense = Dense(node);
        int p = parent[dense];
        return p < 0 ? PoseNode.Null : NodeAt(p);
    }

    /// <summary>The local pose, relative to the parent. Writing it makes world poses stale until <see cref="UpdateWorld"/>.</summary>
    public ref TPose Local(PoseNode node)
    {
        worldDirty = true;
        // Dense first: a pending reorder replaces the arrays, and an array read before it would be the old one.
        int dense = Dense(node);
        return ref local[dense];
    }

    public TPose GetLocal(PoseNode node)
    {
        int dense = Dense(node);
        return local[dense];
    }

    /// <summary>The pose relative to the root of the node's tree, as of the last <see cref="UpdateWorld"/>.</summary>
    public TPose World(PoseNode node)
    {
        int dense = Dense(node);
        return world[dense];
    }

    public int DenseIndex(PoseNode node) => Dense(node);

    public PoseNode NodeAt(int dense)
    {
        EnsureOrder();
        int id = idAt[dense];
        return new PoseNode(id, generation[id]);
    }

    /// <summary>Dense parent indices. Valid until the next structural change.</summary>
    public ReadOnlySpan<int> ParentsDense
    {
        get { EnsureOrder(); return parent.AsSpan(0, count); }
    }

    /// <summary>
    /// The stable id of the node at each dense index. Valid until the next structural change. Internal, like
    /// <see cref="Generations"/>: with both, code could build a live <see cref="PoseNode"/> for any index without the
    /// generation check, so only the frame tree's walks in this module read them.
    /// </summary>
    internal ReadOnlySpan<int> IdsDense
    {
        get { EnsureOrder(); return idAt.AsSpan(0, count); }
    }

    /// <summary>The generation of each stable id. Internal, see <see cref="IdsDense"/>.</summary>
    internal ReadOnlySpan<uint> Generations => generation.AsSpan(0, idCount);

    public ReadOnlySpan<TPose> LocalPoses
    {
        get { EnsureOrder(); return local.AsSpan(0, count); }
    }

    /// <summary>
    /// The local poses by dense index, to write, for a pass over many nodes. Writing them makes world poses stale until
    /// <see cref="UpdateWorld"/>, as <see cref="Local"/> does. Valid until the next structural change.
    /// </summary>
    internal Span<TPose> LocalPosesForWrite
    {
        get { EnsureOrder(); worldDirty = true; return local.AsSpan(0, count); }
    }

    /// <summary>World poses by dense index, as of the last <see cref="UpdateWorld"/>.</summary>
    public ReadOnlySpan<TPose> WorldPoses
    {
        get { EnsureOrder(); return world.AsSpan(0, count); }
    }

    /// <summary>Restores parent-before-child order if needed, then recomputes every world pose in one pass.</summary>
    public void UpdateWorld()
    {
        EnsureOrder();
        for (int i = 0; i < count; i++)
        {
            int p = parent[i];
            world[i] = p < 0 ? local[i] : TPose.Compose(world[p], local[i]);
        }
        worldDirty = false;
    }

    public bool WorldIsStale => worldDirty || orderDirty;

    /// <summary>
    /// The pose of <paramref name="of"/> expressed in the space of <paramref name="to"/>. The result composes local
    /// poses along the path through the lowest common ancestor, so precision depends only on that path.
    /// </summary>
    public TPose Relative(PoseNode of, PoseNode to)
    {
        int a = Dense(of), b = Dense(to);
        int lca = LowestCommonAncestor(a, b);
        TPose ofInLca = ChainTo(a, lca);
        TPose toInLca = ChainTo(b, lca);
        return TPose.Compose(toInLca.Inverse(), ofInLca);
    }

    /// <summary>
    /// Writes the pose of every node relative to <paramref name="reference"/> into <paramref name="result"/>, by
    /// dense index. Nodes in other trees are placed through a shared virtual root. Safe to call from several
    /// threads at once while the tree does not change.
    /// </summary>
    public void ComputeRelativeTo(PoseNode reference, Span<TPose> result)
    {
        EnsureOrder();
        ComputeRelativeTo(reference, local.AsSpan(0, count), result);
    }

    /// <summary>
    /// Like <see cref="ComputeRelativeTo(PoseNode, Span{TPose})"/>, but reads the local poses from
    /// <paramref name="localPoses"/> (by dense index) instead of the tree. The tree supplies only the structure.
    /// Use it to evaluate the tree at another time, such as the previous tick.
    /// </summary>
    public void ComputeRelativeTo(PoseNode reference, ReadOnlySpan<TPose> localPoses, Span<TPose> result)
    {
        EnsureOrder();
        if (result.Length < count)
            throw new ArgumentException("Result span is shorter than the node count.", nameof(result));
        if (localPoses.Length < count)
            throw new ArgumentException("Local pose span is shorter than the node count.", nameof(localPoses));

        int r = Dense(reference);
        int[]? rented = count > 256 ? ArrayPool<int>.Shared.Rent(count) : null;
        Span<int> onPath = rented is null ? stackalloc int[count] : rented.AsSpan(0, count);
        try
        {
            onPath.Clear();

            // Walk up from the reference: rel(parent) = rel(child) ∘ inverse(local(child)).
            result[r] = TPose.Identity;
            onPath[r] = 1;
            int x = r;
            while (parent[x] >= 0)
            {
                int p = parent[x];
                result[p] = TPose.Compose(result[x], localPoses[x].Inverse());
                onPath[p] = 1;
                x = p;
            }
            TPose virtualRoot = TPose.Compose(result[x], localPoses[x].Inverse());

            for (int i = 0; i < count; i++)
            {
                if (onPath[i] != 0)
                    continue;
                int p = parent[i];
                result[i] = p < 0 ? TPose.Compose(virtualRoot, localPoses[i]) : TPose.Compose(result[p], localPoses[i]);
            }
        }
        finally
        {
            if (rented != null)
                ArrayPool<int>.Shared.Return(rented);
        }
    }

    /// <summary>Visits the stable handles of every child of <paramref name="node"/>.</summary>
    public void GetChildren(PoseNode node, List<PoseNode> children)
    {
        int dense = Dense(node);
        for (int i = 0; i < count; i++)
            if (parent[i] == dense)
                children.Add(NodeAt(i));
    }

    public bool IsAncestorOf(PoseNode ancestor, PoseNode node) =>
        ancestor != node && IsAncestorOrSelf(Dense(ancestor), Dense(node));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Dense(PoseNode node)
    {
        if (!IsAlive(node))
            ThrowStale(node);
        EnsureOrder();
        return denseOf[node.Id];
    }

    private static void ThrowStale(PoseNode node) => throw new InvalidOperationException($"Stale or null pose node {node}.");

    private bool IsAncestorOrSelf(int ancestor, int dense)
    {
        for (int x = dense; x >= 0; x = parent[x])
            if (x == ancestor)
                return true;
        return false;
    }

    private int LowestCommonAncestor(int a, int b)
    {
        for (int x = a; x >= 0; x = parent[x])
            if (IsAncestorOrSelf(x, b))
                return x;
        return -1;
    }

    /// <summary>The pose of dense node <paramref name="from"/> in the space of its ancestor <paramref name="ancestor"/> (-1 for the virtual root).</summary>
    private TPose ChainTo(int from, int ancestor)
    {
        TPose acc = TPose.Identity;
        for (int x = from; x != ancestor; x = parent[x])
            acc = TPose.Compose(local[x], acc);
        return acc;
    }

    private TPose RelativeToRoot(int dense) => ChainTo(dense, -1);

    private void EnsureOrder()
    {
        if (!orderDirty)
            return;
        orderDirty = false;

        // Stable topological order: roots in their current order, each followed by its subtree depth-first.
        var childStart = new int[count + 1];
        for (int i = 0; i < count; i++)
            if (parent[i] >= 0)
                childStart[parent[i] + 1]++;
        for (int i = 0; i < count; i++)
            childStart[i + 1] += childStart[i];
        var children = new int[count];
        var fill = (int[])childStart.Clone();
        for (int i = 0; i < count; i++)
            if (parent[i] >= 0)
                children[fill[parent[i]]++] = i;

        var order = new int[count];
        var stack = new Stack<int>();
        int n = 0;
        for (int root = 0; root < count; root++)
        {
            if (parent[root] >= 0)
                continue;
            stack.Push(root);
            while (stack.Count > 0)
            {
                int x = stack.Pop();
                order[n++] = x;
                for (int c = childStart[x + 1] - 1; c >= childStart[x]; c--)
                    stack.Push(children[c]);
            }
        }

        var newDense = new int[count];
        for (int i = 0; i < count; i++)
            newDense[order[i]] = i;

        var newParent = new int[parent.Length];
        var newLocal = new TPose[local.Length];
        var newWorld = new TPose[world.Length];
        var newIdAt = new int[idAt.Length];
        for (int i = 0; i < count; i++)
        {
            int old = order[i];
            newParent[i] = parent[old] < 0 ? -1 : newDense[parent[old]];
            newLocal[i] = local[old];
            newWorld[i] = world[old];
            newIdAt[i] = idAt[old];
            denseOf[idAt[old]] = i;
        }
        parent = newParent;
        local = newLocal;
        world = newWorld;
        idAt = newIdAt;
        worldDirty = true;
    }

    private void EnsureCapacity(int needed)
    {
        if (needed > parent.Length)
        {
            int size = Math.Max(needed, parent.Length * 2);
            Array.Resize(ref parent, size);
            Array.Resize(ref local, size);
            Array.Resize(ref world, size);
            Array.Resize(ref idAt, size);
        }
        if (idCount + 1 > denseOf.Length)
        {
            int size = Math.Max(idCount + 1, denseOf.Length * 2);
            Array.Resize(ref denseOf, size);
            Array.Resize(ref generation, size);
            Array.Resize(ref freeIds, size);
        }
    }
}
