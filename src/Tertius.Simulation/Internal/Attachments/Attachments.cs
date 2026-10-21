using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>
/// The attachment chains: entities that follow a parent entity with a fixed offset. Chains never span frames, so one
/// pose tree holds them all, with one root per chain. The sync point derives every attached entity's back transform
/// from its chain root with <see cref="Derive"/>.
/// </summary>
/// <remarks>
/// Every member that takes an entity checks its handle and throws <see cref="StaleEntityException"/> for a stale one;
/// <see cref="CheckParent"/> instead reports a dead parent as the reason it gives. The members that hand out entities
/// never hand out a dead one.
/// <para>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>An attached entity is live, its parent is alive, and its node sits under its parent's node in the tree. So
/// chains never loop.</item>
/// <item>A parent's child list is sorted by slot, and every child names that parent. A chain root has a node only
/// while it has children, and the tree holds no other nodes.</item>
/// <item><see cref="Attached"/> holds exactly the attached entities.</item>
/// <item>Releasing an entity detaches it. Its attached children must be released in the same batch, which
/// <c>World</c> guarantees by destroying them with it.</item>
/// <item>The root of every link is the chain root that a walk up the parents finds, and it is live.</item>
/// <item>Each live entity among the entity table's front pushers and the pushers of the tick is attached, and its
/// chain root has a body.</item>
/// <item>The scratch of <see cref="CarryPushes"/> is empty.</item>
/// </list>
/// The gameplay workers call only <see cref="ParentOf"/>, <see cref="RootOf"/> and <see cref="WritableOffset"/>, for their
/// own entity.
/// </para>
/// </remarks>
internal sealed class Attachments
{
    /// <summary>One entity's place in the attachment chains.</summary>
    private struct Link
    {
        public Entity Parent;
        /// <summary>
        /// The chain root: the one entity of the chain that is not attached, or null for an entity that is not attached. It
        /// never changes, because an entity is attached only at its spawn and dies with its parent.
        /// </summary>
        public Entity Root;
        /// <summary>
        /// For a chain root while <see cref="CarryPushes"/> runs: one more than the index of its sum in the scratch of the
        /// flow, or 0 when it has none yet. 0 outside the call.
        /// </summary>
        public int SumSlot;
        /// <summary>The offset from the parent. The entity itself may change it during its run.</summary>
        public Pose Offset;
        /// <summary>This entity's node in the tree, if it is attached or has attached children.</summary>
        public PoseNode Node;
        /// <summary>Attached children, sorted by slot.</summary>
        public List<Entity>? Children;
    }

    private readonly EntityTable entities;
    private readonly EntityTable.Column<Link> links;
    private readonly PoseTree<DPose> tree = new(16);
    private readonly EntitySet attached = new();

    public Attachments(EntityTable entities)
    {
        this.entities = entities;
        links = entities.AddColumn<Link>(Detach);
    }

    /// <summary>The attached entities, in slot order. Never a dead entity.</summary>
    public ReadOnlySpan<Entity> Attached => attached.Items;

    /// <summary>
    /// The attachment parent, or null if the entity is not attached. Throws <see cref="StaleEntityException"/> for a
    /// stale handle, so null always means "not attached". Worker-safe.
    /// </summary>
    public Entity ParentOf(Entity entity) => links[entity].Parent;

    /// <summary>
    /// The attached children of an entity, in slot order. Never a dead entity. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public ReadOnlySpan<Entity> ChildrenOf(Entity entity) =>
        links[entity].Children is { } children ? CollectionsMarshal.AsSpan(children) : default;

    /// <summary>
    /// The offset from the parent, for writing by the running entity. Worker-safe for the running entity. Throws
    /// <see cref="InvalidOperationException"/> if the entity is not attached, and <see cref="StaleEntityException"/>
    /// for a stale handle.
    /// </summary>
    public ref Pose WritableOffset(Entity entity)
    {
        ref Link link = ref links[entity];
        if (link.Parent.IsNull)
            throw new InvalidOperationException($"{entity} is not attached to a parent.");
        return ref link.Offset;
    }

    /// <summary>
    /// Why <paramref name="parent"/> cannot take a new child, or null if it can. A parent that is not live cannot, and
    /// that includes a stale or null handle. The owner of a root frame cannot either, because it is in no frame. When the
    /// parent can, <paramref name="childFrame"/> is the frame in which the new child must become live: the frame of the
    /// parent's chain root in the back table. Every link of a chain is in that frame after the next <see cref="Derive"/>,
    /// also when physics has moved the chain root into another frame since the last one.
    /// </summary>
    public string? CheckParent(Entity parent, out FrameHandle childFrame)
    {
        childFrame = FrameHandle.Null;
        if (!entities.IsAlive(parent))
            return $"the attachment parent {parent} is not live.";
        FrameHandle frame = entities.BackRow(ChainRoot(parent)).Frame;
        if (frame.IsNull)
            return $"the attachment parent {parent} is in no frame: it owns a root frame.";
        childFrame = frame;
        return null;
    }

    /// <summary>
    /// Attaches an unattached entity to a parent that <see cref="CheckParent"/> accepted. Its transform is derived at the
    /// next <see cref="Derive"/>. Throws <see cref="StaleEntityException"/> if either handle is stale.
    /// </summary>
    public void Attach(Entity child, Entity parent, Pose offset)
    {
        ref Link link = ref links[child];
        ref Link parentLink = ref links[parent];
        // A child that is the root of its parent's chain would close a loop.
        Contract.Requires(link.Parent.IsNull && ChainRoot(parent) != child);
        if (parentLink.Node.IsNull)
            parentLink.Node = tree.Add(PoseNode.Null, entities.BackRow(parent).HomePose);
        List<Entity> children = parentLink.Children ??= new List<Entity>(2);
        int at = children.BinarySearch(child);
        children.Insert(at < 0 ? ~at : at, child);

        link.Parent = parent;
        link.Root = ChainRoot(parent);
        link.Offset = offset;
        link.Node = tree.Add(parentLink.Node, Poses.Safe(offset).ToDPose());
        attached.Queue(child, true);
    }

    /// <summary>
    /// Derives the back transform of every attached entity from its chain root, parent before child. The chain root's
    /// row comes from the back table, so this runs after physics and the frame owners have written theirs. An attached
    /// entity takes its root's frame, velocity at its lever arm, and spin.
    /// </summary>
    public void Derive()
    {
        ReadOnlySpan<Entity> children = attached.Items;
        if (children.IsEmpty)
            return;
        foreach (Entity child in children)
        {
            ref Link link = ref links[child];
            tree.Local(link.Node) = Poses.Safe(link.Offset).ToDPose();
            ref Link parentLink = ref links[link.Parent];
            if (parentLink.Parent.IsNull)
                tree.Local(parentLink.Node) = entities.BackRow(link.Parent).HomePose;
        }
        tree.UpdateWorld();

        foreach (Entity child in children)
        {
            ref readonly TransformRow rootRow = ref entities.BackRow(ChainRoot(child));
            // The chain is placed in the root's physics home, where positions near the root stay exact.
            DPose inHome = tree.World(links[child].Node);
            Double3 lever = inHome.Position - rootRow.HomePose.Position;
            entities.SetBackTransform(child, new TransformRow
            {
                Frame = rootRow.Frame,
                PhysicsHome = rootRow.PhysicsHome,
                Pose = rootRow.PhysicsHome == rootRow.Frame ? inHome : new DPose(rootRow.Pose.Position + lever, inHome.Rotation),
                HomePosition = inHome.Position,
                LinearVelocity = rootRow.LinearVelocity + Double3.Cross(rootRow.AngularVelocity, lever).ToVector3(),
                AngularVelocity = rootRow.AngularVelocity,
            });
        }
    }

    /// <summary>
    /// The chain root of an attached entity: the one entity of its chain that is not attached. Null for an entity that is
    /// not attached. Throws <see cref="StaleEntityException"/> for a stale handle. Worker-safe.
    /// </summary>
    public Entity RootOf(Entity entity) => links[entity].Root;

    /// <summary>
    /// Carries the push of every part that pushed in the tick to its chain root, the one choke-point method of the flow
    /// (docs/plans/parts.md, "Where the push flows up"). Step 5 of the sync point calls it once, after the swap and after the
    /// destroy batch has grown, before any entity of the batch moves to another frame or dies. For each live part of the
    /// table's front pushers, in their order, it reads the part's push from its front intent and empties that intent. A part
    /// that <paramref name="dying"/> marks (indexed by slot) is skipped: it is not live in the next step, and its chain root
    /// dies with it or survives without its push. Each other part's push moves to the center of mass of its root's body at
    /// the part's place (see <see cref="Push.At"/>), with the lever of the offsets of its chain, and joins the root's sum in
    /// doubles, which starts from the root's own push. Each sum then narrows once into its root's front intent (see
    /// <see cref="EntityTable.AddPush"/>), roots in the order of their first part. The intents of the part and of its root
    /// name the front frame of the chain here, so the sum needs no relation between frames. A part whose intent names
    /// another frame than its root's front row comes only from a bug: it fails a contract in a build with contracts, and
    /// in any other build it is skipped and added to <paramref name="skipped"/> for the World to report.
    /// </summary>
    public void CarryPushes(ReadOnlySpan<bool> dying, List<Entity> skipped)
    {
        ReadOnlySpan<Entity> parts = entities.FrontPushers;
        if (parts.IsEmpty)
            return;
        Contract.Requires(sums.Count == 0);
        ReadOnlySpan<PhysicsIntent> intents = entities.FrontIntents.Span;
        foreach (Entity part in parts)
        {
            if (!entities.IsAlive(part))
                continue;
            ref readonly PhysicsIntent pushed = ref intents[(int)part.Index];
            if (dying[(int)part.Index])
            {
                entities.ClearPush(part);
                continue;
            }
            Entity root = links[part].Root;
            ref readonly TransformRow rootRow = ref entities.FrontRow(root);
            Contract.Requires(pushed.Frame == rootRow.Frame);
            if (pushed.Frame != rootRow.Frame)
            {
                skipped.Add(part);
                entities.ClearPush(part);
                continue;
            }
            Double3 lever = Push.Lever(rootRow.Pose.Rotation, PlaceInBody(part), entities.BodyCenterOfMass(root));
            Push contribution = Push.Of(in pushed).At(lever);
            ref int slot = ref links[root].SumSlot;
            if (slot == 0)
            {
                sums.Add((root, Push.Of(in intents[(int)root.Index])));
                slot = sums.Count;
            }
            ref (Entity Root, Push Sum) sum = ref CollectionsMarshal.AsSpan(sums)[slot - 1];
            sum.Sum = sum.Sum.Plus(in contribution);
            entities.ClearPush(part);
        }
        foreach ((Entity root, Push sum) in sums)
        {
            entities.AddPush(root, in sum);
            links[root].SumSlot = 0;
        }
        sums.Clear();
    }

    // The scratch of CarryPushes: the sum of each root in doubles, in the order of its first part. Each root's link holds
    // the place of its sum here (see Link.SumSlot). Empty outside the call.
    private readonly List<(Entity Root, Push Sum)> sums = new();

    /// <summary>
    /// The place of <paramref name="part"/> in the body of its chain root, in meters in the root body's own axes: the
    /// offsets of the chain from the root down to the part, composed in doubles, with the writes of this tick. Each offset
    /// widens as <see cref="Derive"/> widens it, so the place is where step 7 puts the part in the root's axes.
    /// </summary>
    private Double3 PlaceInBody(Entity part)
    {
        ref readonly Link link = ref links[part];
        // A child of the root is at its offset's position, which widens exactly, with no rotation to compose.
        if (link.Parent == link.Root)
            return link.Offset.Position;
        DPose place = Poses.Safe(link.Offset).ToDPose();
        for (Entity parent = link.Parent; parent != link.Root; parent = links[parent].Parent)
            place = DPose.Compose(Poses.Safe(links[parent].Offset).ToDPose(), place);
        return place.Position;
    }

    /// <summary>
    /// The one entity of the chain of <paramref name="entity"/> that is not attached: the entity itself if it is not
    /// attached. Chains never loop, so the walk ends.
    /// </summary>
    private Entity ChainRoot(Entity entity)
    {
        Entity root = entity;
        while (links[root].Parent is { IsNull: false } parent)
            root = parent;
        return root;
    }

    /// <summary>
    /// Checks every invariant in the class comment. O(capacity + attached). That the link of every slot that is not live
    /// is clear is the entity table's invariant over its columns, so this checks the live entities' links.
    /// </summary>
    internal void AssertInvariants()
    {
        attached.AssertInvariants();
        ReadOnlySpan<Entity> attachedEntities = attached.Items;
        int attachedCount = 0, nodes = 0;
        foreach (Entity entity in entities.LiveEntities)
        {
            ref readonly Link link = ref links[entity];
            if (!link.Node.IsNull)
            {
                Contract.Invariant(tree.IsAlive(link.Node));
                nodes++;
            }
            if (!link.Parent.IsNull)
            {
                attachedCount++;
                Contract.Invariant(entities.IsAlive(link.Parent));
                ref readonly Link parentLink = ref links[link.Parent];
                Contract.Invariant(parentLink.Children != null && parentLink.Children.BinarySearch(entity) >= 0);
                Contract.Invariant(!link.Node.IsNull && tree.Parent(link.Node) == parentLink.Node);
                Contract.Invariant(attachedEntities.BinarySearch(entity) >= 0);
            }
            else
            {
                Contract.Invariant(link.Node.IsNull == (link.Children is not { Count: > 0 }));
            }
            if (link.Children is { } children)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    Contract.Invariant(i == 0 || children[i - 1].CompareTo(children[i]) < 0);
                    Contract.Invariant(entities.IsAlive(children[i]) && links[children[i]].Parent == entity);
                }
            }
        }
        Contract.Invariant(attachedEntities.Length == attachedCount && tree.Count == nodes);

        foreach (Entity entity in attachedEntities)
        {
            Entity root = links[entity].Root;
            Contract.Invariant(root == ChainRoot(entity) && entities.IsAlive(root));
        }
        var pushers = new List<Entity>();
        entities.CollectPushers(pushers);
        foreach (Entity pusher in entities.FrontPushers)
            if (entities.IsAlive(pusher))
                pushers.Add(pusher);
        foreach (Entity pusher in pushers)
            Contract.Invariant(!links[pusher].Parent.IsNull && entities.Body(links[pusher].Root) != null);
        Contract.Invariant(sums.Count == 0);
        foreach (Entity entity in entities.LiveEntities)
            Contract.Invariant(links[entity].SumSlot == 0);
    }

    /// <summary>
    /// Takes a released entity, still live while this runs, out of the chains. Its attached children are released in the
    /// same batch, each detaching itself. The entity table clears the entity's link next.
    /// </summary>
    private void Detach(Entity entity)
    {
        ref Link link = ref links[entity];
        if (!link.Parent.IsNull)
        {
            attached.Queue(entity, false);
            // A parent released earlier in the same batch already let go of its children and left the tree.
            if (entities.IsAlive(link.Parent))
            {
                ref Link parentLink = ref links[link.Parent];
                parentLink.Children?.Remove(entity);
                // A chain root with no children left leaves the tree.
                if (parentLink.Children is { Count: 0 } && parentLink.Parent.IsNull && !parentLink.Node.IsNull)
                {
                    tree.Remove(parentLink.Node);
                    parentLink.Node = PoseNode.Null;
                }
            }
        }
        if (!link.Node.IsNull)
            tree.Remove(link.Node);
    }
}
