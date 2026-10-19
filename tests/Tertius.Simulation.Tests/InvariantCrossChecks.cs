using System.Reflection;

namespace Tertius.Simulation.Tests;

/// <summary>
/// What the cross-checks have looked at over a whole run, and the handles they remember between checks. A check that
/// looks at nothing proves nothing, so the tests read the counters and require that the workload reached each set.
/// </summary>
internal sealed class CrossCheckHistory
{
    private const int MaxDeadRemembered = 500;

    public long Checks, MemberEntries, RecipientEntries, AttachedLinks, InboxEvents, TopicDeliveries, SpatialMatches, DeadHandlesProbed, StaleFramesProbed;

    // The live set of the previous check, so that the next check knows which handles died meanwhile.
    internal HashSet<Entity> LastLive { get; } = new();
    // The most recently died handles. None of them may be reachable from any set.
    internal Queue<Entity> Dead { get; } = new();
    internal HashSet<FrameHandle> KnownFrames { get; } = new();

    public override string ToString() =>
        $"checks {Checks}, member entries {MemberEntries}, topic recipients {RecipientEntries}, attachment links {AttachedLinks}, " +
        $"inbox events {InboxEvents} (via a topic {TopicDeliveries}), spatial matches {SpatialMatches}, " +
        $"dead handles probed {DeadHandlesProbed}, stale frames probed {StaleFramesProbed}";

    internal void Remember(IReadOnlyList<Entity> live, IEnumerable<FrameHandle> frames)
    {
        var now = new HashSet<Entity>(live);
        foreach (Entity before in LastLive)
        {
            if (now.Contains(before))
                continue;
            Dead.Enqueue(before);
            if (Dead.Count > MaxDeadRemembered)
                Dead.Dequeue();
        }
        LastLive.Clear();
        LastLive.UnionWith(now);
        KnownFrames.UnionWith(frames);
    }
}

/// <summary>
/// An independent audit of a world, written from the outside of the subsystems: it reads their results and works out by
/// hand what they must be. The subsystems check themselves with <c>AssertInvariants</c>. These checks are a second
/// opinion that does not share any code with them, so a mistake in one of those checks does not hide a mistake in the
/// subsystem. Each returns messages, not exceptions, so that a failing test shows every broken rule at once.
/// </summary>
/// <remarks>
/// The cross-checks need the topic sets and the router, which no public member exposes. They reach them by reflection
/// in <see cref="Inside"/>, in one place. The rules, with the check that tests each:
/// <list type="bullet">
/// <item>Rows: after a sync point, every live entity's front row and back row are equal bit for bit.</item>
/// <item>Membership: every live entity is in the member set of exactly its front frame, or in none if it owns a root frame.</item>
/// <item>Topics: every recipient list is sorted without duplicates and holds only live entities. The recipients of a frame topic include the members. The topic of a dead frame has nobody, even if a new frame reuses its index.</item>
/// <item>Attachments: a parent and its children name each other, share a frame, and the child sits where its chain puts it.</item>
/// <item>Spatial index: a radius query equals a search through every live entity.</item>
/// <item>Inboxes: every delivered event is one the entity accepts and is addressed to, and the inbox sizes add up to the deliveries the router wrote. With the router's own check that the ranges are in slot order, disjoint and inside the deliveries, the ranges then cover the sorted deliveries exactly.</item>
/// <item>Frames: each frame's owner is live and owns it, and the owner's row names the frame's parent.</item>
/// <item>Reachability: no handle that reached any internal set is dead, and none of the handles that died recently is reachable.</item>
/// </list>
/// </remarks>
internal static class InvariantCrossChecks
{
    private const int MaxMessages = 25;

    /// <param name="inboxesCurrent">
    /// True when the state is the one the sync point left, so that the inboxes still match the topic sets they were
    /// routed from. False after a change between ticks, such as a spawn or a destroy: the inboxes then still hold
    /// the old mail.
    /// </param>
    public static List<string> Find(World world, CrossCheckHistory history, bool inboxesCurrent, Random sampler)
    {
        var problems = new List<string>();
        void Report(string message)
        {
            if (problems.Count < MaxMessages)
                problems.Add(message);
        }

        EntityTable entities = world.Entities;
        TopicSets topics = Inside.TopicsOf(world);
        var live = new List<Entity>();
        world.CopyLiveEntities(live);
        var liveSet = new HashSet<Entity>(live);
        var frames = new List<FrameHandle>();
        world.Frames.GetAll(frames);
        var reachable = new HashSet<Entity>();
        history.Checks++;

        CheckRows(world, live, Report);
        CheckMembership(world, topics, live, frames, history, reachable, Report);
        CheckTopics(world, topics, frames, history, reachable, Report);
        CheckAttachments(world, live, history, reachable, Report);
        CheckSpatialIndex(world, live, frames, history, reachable, sampler, Report);
        CheckFrames(world, live, frames, Report);
        if (inboxesCurrent)
            CheckInboxes(world, topics, live, history, Report);

        foreach (Entity intent in entities.IntentEntities.Span)
        {
            reachable.Add(intent);
            if (!liveSet.Contains(intent) || entities.Body(intent) == null)
                Report($"{intent} is in the intent list but is not a live entity with a body.");
        }
        foreach (Entity entity in reachable)
        {
            if (!world.IsAlive(entity))
                Report($"{entity} is reachable from an internal set, and it is dead.");
        }
        foreach (Entity dead in history.Dead)
        {
            history.DeadHandlesProbed++;
            if (reachable.Contains(dead))
                Report($"{dead} died earlier, and a set still reaches it.");
        }

        if (world.Stats.LiveEntities != live.Count)
            Report($"Stats says {world.Stats.LiveEntities} live entities and a scan finds {live.Count}.");
        if (world.Stats.Frames != frames.Count)
            Report($"Stats says {world.Stats.Frames} frames and the frame tree holds {frames.Count}.");

        history.Remember(live, frames);
        return problems;
    }

    // ---- Rows ---------------------------------------------------------------------------------------------------------

    private static void CheckRows(World world, List<Entity> live, Action<string> report)
    {
        foreach (Entity entity in live)
        {
            ref readonly TransformRow front = ref world.Entities.FrontRow(entity);
            ref readonly TransformRow back = ref world.Entities.BackRow(entity);
            bool equal = front.Frame == back.Frame
                && Poses.BitEquals(front.Pose, back.Pose)
                && front.LinearVelocity.Equals(back.LinearVelocity)
                && front.AngularVelocity.Equals(back.AngularVelocity);
            if (!equal)
                report($"{entity}: the front row {front.Frame} {front.Pose} differs from the back row {back.Frame} {back.Pose} after the sync point.");
        }
    }

    // ---- Membership ---------------------------------------------------------------------------------------------------

    private static void CheckMembership(World world, TopicSets topics, List<Entity> live, List<FrameHandle> frames, CrossCheckHistory history,
        HashSet<Entity> reachable, Action<string> report)
    {
        var memberOf = new Dictionary<Entity, List<FrameHandle>>();
        foreach (FrameHandle frame in frames)
        {
            Entity? previous = null;
            foreach (Entity member in topics.MembersOf(frame))
            {
                history.MemberEntries++;
                reachable.Add(member);
                if (previous is { } before && before.CompareTo(member) >= 0)
                    report($"The members of {frame} are not strictly ascending at {member}.");
                previous = member;
                if (!memberOf.TryGetValue(member, out List<FrameHandle>? sets))
                    memberOf[member] = sets = new List<FrameHandle>();
                sets.Add(frame);
            }
        }

        foreach (Entity entity in live)
        {
            FrameHandle frame = world.GetFrame(entity);
            memberOf.TryGetValue(entity, out List<FrameHandle>? sets);
            if (frame.IsNull)
            {
                if (sets is { Count: > 0 })
                    report($"{entity} is in no frame, and is a member of {string.Join(", ", sets)}.");
            }
            else if (sets is not { Count: 1 } || sets[0] != frame)
            {
                report($"{entity} is in {frame}, and is a member of [{(sets == null ? "" : string.Join(", ", sets))}].");
            }
        }
    }

    // ---- Topics -------------------------------------------------------------------------------------------------------

    private static List<Entity> RecipientsOf(TopicSets topics, Topic topic)
    {
        var recipients = new List<Entity>();
        foreach (Entity recipient in topics.RecipientsOf(topic))
            recipients.Add(recipient);
        return recipients;
    }

    private static void CheckTopics(World world, TopicSets topics, List<FrameHandle> frames, CrossCheckHistory history,
        HashSet<Entity> reachable, Action<string> report)
    {
        void CheckList(string name, List<Entity> recipients)
        {
            for (int i = 0; i < recipients.Count; i++)
            {
                history.RecipientEntries++;
                reachable.Add(recipients[i]);
                if (!world.IsAlive(recipients[i]))
                    report($"{name} has the dead entity {recipients[i]} as a recipient.");
                if (i > 0 && recipients[i - 1].CompareTo(recipients[i]) >= 0)
                    report($"The recipients of {name} are not strictly ascending at {recipients[i]}.");
            }
        }

        foreach (FrameHandle frame in frames)
        {
            List<Entity> recipients = RecipientsOf(topics, frame.Topic);
            CheckList($"the topic of {frame}", recipients);
            foreach (Entity member in topics.MembersOf(frame))
            {
                if (recipients.BinarySearch(member) < 0)
                    report($"{member} is a member of {frame} and does not hear its topic.");
            }
        }
        for (int id = 0; id < Topics.Count; id++)
            CheckList($"named topic {Topics.NameOf(new Topic(id))}", RecipientsOf(topics, new Topic(id)));

        // A frame that died has nobody to tell, and so has the topic of a frame that took over its index.
        foreach (FrameHandle old in history.KnownFrames)
        {
            if (world.Frames.IsAlive(old))
                continue;
            history.StaleFramesProbed++;
            if (RecipientsOf(topics, old.Topic).Count > 0)
                report($"The topic of the dead frame {old} still has recipients.");
            if (topics.MembersOf(old).Length > 0)
                report($"The dead frame {old} still has members.");
        }
    }

    // ---- Attachments --------------------------------------------------------------------------------------------------

    private static void CheckAttachments(World world, List<Entity> live, CrossCheckHistory history, HashSet<Entity> reachable, Action<string> report)
    {
        Attachments attachments = world.Attachments;
        var attachedByScan = new List<Entity>();
        foreach (Entity entity in live)
        {
            Entity parent = attachments.ParentOf(entity);
            ReadOnlySpan<Entity> children = attachments.ChildrenOf(entity);
            for (int i = 0; i < children.Length; i++)
            {
                reachable.Add(children[i]);
                if (i > 0 && children[i - 1].CompareTo(children[i]) >= 0)
                    report($"The children of {entity} are not strictly ascending at {children[i]}.");
                if (!world.IsAlive(children[i]) || attachments.ParentOf(children[i]) != entity)
                    report($"{children[i]} is listed as a child of {entity} and does not have it as its parent.");
            }
            if (parent.IsNull)
                continue;

            history.AttachedLinks++;
            attachedByScan.Add(entity);
            reachable.Add(parent);
            if (!world.IsAlive(parent))
            {
                report($"{entity} is attached to the dead entity {parent}.");
                continue;
            }
            if (attachments.ChildrenOf(parent).IndexOf(entity) < 0)
                report($"{entity} is attached to {parent}, which does not list it as a child.");
            if (world.GetFrame(entity) != world.GetFrame(parent))
                report($"{entity} is in {world.GetFrame(entity)} and its parent {parent} is in {world.GetFrame(parent)}.");
            if (world.Entities.Body(entity) != null)
                report($"{entity} is attached and has a body.");

            // Where the chain puts it: the root's pose, then each offset on the way down to the entity.
            var chain = new List<Entity>();
            Entity root = entity;
            while (!attachments.ParentOf(root).IsNull)
            {
                chain.Add(root);
                root = attachments.ParentOf(root);
            }
            DPose expected = world.PoseInOwnFrame(root);
            for (int i = chain.Count - 1; i >= 0; i--)
                expected = DPose.Compose(expected, Poses.Safe(attachments.WritableOffset(chain[i])).ToDPose());
            DPose actual = world.PoseInOwnFrame(entity);
            if (!TestWorld.RoughlyEqual(expected.Position, actual.Position, 1e-6))
                report($"{entity} should be at {expected.Position} by its chain from {root} and is at {actual.Position}.");
        }

        ReadOnlySpan<Entity> attachedSet = attachments.Attached;
        foreach (Entity entity in attachedSet)
            reachable.Add(entity);
        if (!attachedSet.SequenceEqual(attachedByScan.ToArray()))
            report($"The attached set has {attachedSet.Length} entities and a scan of the parent links finds {attachedByScan.Count}.");
    }

    // ---- Spatial index ------------------------------------------------------------------------------------------------

    private static void CheckSpatialIndex(World world, List<Entity> live, List<FrameHandle> frames, CrossCheckHistory history,
        HashSet<Entity> reachable, Random sampler, Action<string> report)
    {
        var found = new List<Entity>();
        foreach (FrameHandle frame in frames)
        {
            Entity[] inFrame = live.Where(entity => world.GetFrame(entity) == frame).ToArray();
            world.Snapshot.QueryRadius(frame, Double3.Zero, 1e15, found);
            foreach (Entity entity in found)
                reachable.Add(entity);
            history.SpatialMatches += found.Count;
            if (!found.SequenceEqual(inFrame))
                report($"A query over all of {frame} finds {found.Count} entities, and {inFrame.Length} are in it.");
        }

        // Sampled radius queries against a search through every entity, by the plain distance formula.
        for (int sample = 0; sample < 4 && frames.Count > 0; sample++)
        {
            FrameHandle frame = frames[sampler.Next(frames.Count)];
            Entity[] inFrame = live.Where(entity => world.GetFrame(entity) == frame).ToArray();
            Double3 center = inFrame.Length > 0 && sampler.Next(2) == 0
                ? world.PoseInOwnFrame(inFrame[sampler.Next(inFrame.Length)]).Position
                : new Double3(sampler.Next(-60, 61), 0, sampler.Next(-60, 61));
            double radius = sampler.Next(0, 80);
            Entity[] expected = inFrame.Where(entity =>
            {
                Double3 position = world.PoseInOwnFrame(entity).Position;
                double dx = position.X - center.X, dy = position.Y - center.Y, dz = position.Z - center.Z;
                return dx * dx + dy * dy + dz * dz <= radius * radius;
            }).ToArray();
            world.Snapshot.QueryRadius(frame, center, radius, found);
            history.SpatialMatches += found.Count;
            if (!found.SequenceEqual(expected))
                report($"A query in {frame} at {center} with radius {radius} finds {found.Count} entities, and {expected.Length} are within it.");
        }
    }

    // ---- Frames -------------------------------------------------------------------------------------------------------

    private static void CheckFrames(World world, List<Entity> live, List<FrameHandle> frames, Action<string> report)
    {
        int owners = 0;
        foreach (Entity entity in live)
        {
            if (!world.GetOwnedFrame(entity).IsNull)
                owners++;
        }
        if (owners != frames.Count)
            report($"{owners} live entities own a frame and the frame tree holds {frames.Count}.");

        foreach (FrameHandle frame in frames)
        {
            Entity owner = frame.Entity;
            if (!world.IsAlive(owner) || world.GetOwnedFrame(owner) != frame)
            {
                report($"The owner {owner} of {frame} is not a live entity that owns it.");
                continue;
            }
            FrameHandle parent = world.Frames.Parent(frame);
            if (!parent.IsNull && !world.Frames.IsAlive(parent))
                report($"{frame} has the dead parent {parent}.");
            if (world.GetFrame(owner) != parent)
                report($"The owner {owner} of {frame} is in {world.GetFrame(owner)} and the frame's parent is {parent}.");
            if (!world.Frames.TryGetFrame(owner, out FrameHandle byOwner) || byOwner != frame)
                report($"The frame tree finds {byOwner} for the owner {owner} of {frame}.");

            int depth = 0;
            for (FrameHandle up = parent; !up.IsNull; up = world.Frames.Parent(up))
            {
                if (++depth > frames.Count)
                {
                    report($"The parents of {frame} loop.");
                    break;
                }
            }
        }
    }

    // ---- Inboxes ------------------------------------------------------------------------------------------------------

    private static void CheckInboxes(World world, TopicSets topics, List<Entity> live, CrossCheckHistory history, Action<string> report)
    {
        Router router = Inside.RouterOf(world);
        var buffer = new EventBuffer();
        long total = 0;
        foreach (Entity entity in live)
        {
            int count = router.InboxCount(entity);
            total += count;
            EventList events = buffer.Begin(entity);
            router.CopyInbox(entity, events);
            if (events.Count != count)
                report($"The inbox of {entity} counts {count} events and copies {events.Count}.");
            for (int i = 0; i < events.Count; i++)
            {
                history.InboxEvents++;
                ref Event e = ref events[i];
                if (!world.Entities.Accepts(entity, e.Type))
                    report($"{entity} got {e}, which none of its components wants.");
                switch (e.Target)
                {
                    case Entity target:
                        if (target != entity)
                            report($"{entity} got {e}, which is addressed to {target}.");
                        break;
                    case Topic topic:
                        history.TopicDeliveries++;
                        if (RecipientsOf(topics, topic).BinarySearch(entity) < 0)
                            report($"{entity} got {e}, and does not hear that topic.");
                        break;
                    case Broadcast:
                        break;
                    default:
                        report($"{entity} got {e}, which has no target.");
                        break;
                }
            }
        }
        if (total != world.Stats.Deliveries)
            report($"The inboxes of the live entities hold {total} events, and the router wrote {world.Stats.Deliveries} deliveries: the inbox ranges do not cover the deliveries exactly.");
    }
}

/// <summary>Reaches the subsystems that <see cref="World"/> keeps private, for the cross-checks and for tests that damage a world on purpose.</summary>
internal static class Inside
{
    public static TopicSets TopicsOf(World world) => Read<TopicSets>(world, "topics");

    public static Router RouterOf(World world) => Read<Router>(world, "router");

    public static RenderSnapshotBuilder RenderBuilderOf(World world) => Read<RenderSnapshotBuilder>(world, "renderBuilder");

    private static T Read<T>(World world, string field)
    {
        FieldInfo info = typeof(World).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new MissingFieldException(nameof(World), field);
        return (T)info.GetValue(world)!;
    }
}
