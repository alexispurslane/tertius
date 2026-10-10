using System.Runtime.InteropServices;

namespace Tertius.Simulation;

/// <summary>
/// Who hears each topic. A named topic has a set of explicit subscribers. A frame has a member set, which holds every
/// entity that the world moved into the frame, and a set of explicit subscribers to its topic, who may be elsewhere. A
/// message to a frame topic reaches both sets.
/// </summary>
/// <remarks>
/// The sets hold entity handles, and every member that takes an entity checks its handle: an entity that is not live
/// cannot join a set, and a member set or recipient list never holds a dead entity.
/// <para>
/// Invariants, checked by <see cref="AssertInvariants"/>:
/// <list type="bullet">
/// <item>A live entity is in the member set of exactly the frame it last moved to with <see cref="MoveMember"/>, and
/// in no other member set.</item>
/// <item>A live entity is in the subscriber set of exactly the topics in its subscription list, each once.</item>
/// <item>Only live entities are in a set, and only a live frame (the frame at its index, with its generation) has
/// members or subscribers. Releasing an entity takes it out of every set; removing a frame empties its sets.</item>
/// <item>Changes apply before the next read, and readers see every set in slot order, so expansion does not depend
/// on the order in which the changes were made.</item>
/// </list>
/// Only the sync point, or the ticking thread outside a tick, calls this class. The gameplay workers never do.
/// </para>
/// </remarks>
internal sealed class TopicSets
{
    /// <summary>One entity's place in the sets.</summary>
    private struct EntityTopics
    {
        /// <summary>The frame whose member set holds the entity, or null.</summary>
        public FrameHandle Member;
        /// <summary>The topics the entity subscribed to explicitly.</summary>
        public List<Topic>? Subscriptions;
    }

    private readonly EntityTable entities;
    private readonly EntityTable.Column<EntityTopics> byEntity;

    // By frame index. frameAt holds the live frame at each index, so that a frame topic, which carries an index and a
    // generation, can be checked.
    private EntitySet?[] frameMembers = new EntitySet?[16];
    private EntitySet?[] frameSubscribers = new EntitySet?[16];
    private FrameHandle[] frameAt = new FrameHandle[16];
    private readonly List<EntitySet?> named = new();
    // Subscription lists of released entities, for the next entity that subscribes: churn then allocates nothing.
    private readonly Stack<List<Topic>> spareSubscriptionLists = new();

    public TopicSets(EntityTable entities)
    {
        this.entities = entities;
        byEntity = entities.AddColumn<EntityTopics>(Forget);
    }

    /// <summary>Makes a new frame's topic resolvable. Its index must be free.</summary>
    public void AddFrame(FrameHandle frame)
    {
        int index = frame.Index;
        if (index >= frameAt.Length)
        {
            int size = Math.Max(index + 1, frameAt.Length * 2);
            Array.Resize(ref frameAt, size);
            Array.Resize(ref frameMembers, size);
            Array.Resize(ref frameSubscribers, size);
        }
        Contract.Requires(frameAt[index].IsNull);
        frameAt[index] = frame;
    }

    /// <summary>
    /// Forgets a dying frame: its explicit subscribers lose the subscription, and its member set empties. Members that
    /// survive the frame must have moved out before this.
    /// </summary>
    public void RemoveFrame(FrameHandle frame)
    {
        Contract.Requires(IsLive(frame));
        int index = frame.Index;
        if (frameSubscribers[index] is { } subscribers)
        {
            Topic topic = frame.Topic;
            foreach (Entity subscriber in subscribers.Items)
                byEntity[subscriber].Subscriptions?.Remove(topic);
            subscribers.Clear();
        }
        frameMembers[index]?.Clear();
        frameAt[index] = FrameHandle.Null;
    }

    /// <summary>True while <paramref name="frame"/> is the live frame at its index.</summary>
    public bool IsLive(FrameHandle frame) =>
        !frame.IsNull && (uint)frame.Index < (uint)frameAt.Length && frameAt[frame.Index] == frame;

    /// <summary>
    /// The members of a frame, in slot order. Never a dead entity. Empty for a frame that is not live. The span stays
    /// valid while changes are made, until this class is read again.
    /// </summary>
    public ReadOnlySpan<Entity> MembersOf(FrameHandle frame) =>
        IsLive(frame) && frameMembers[frame.Index] is { } members ? members.Items : default;

    /// <summary>
    /// Moves an entity to the member set of <paramref name="to"/>, or out of every member set for a null frame.
    /// Returns false, and changes nothing, when the entity is already a member of <paramref name="to"/>. Throws
    /// <see cref="StaleEntityException"/> for a stale handle.
    /// </summary>
    public bool MoveMember(Entity entity, FrameHandle to, out FrameHandle from)
    {
        ref EntityTopics entry = ref byEntity[entity];
        from = entry.Member;
        if (from == to)
            return false;
        if (IsLive(from))
            frameMembers[from.Index]?.Queue(entity, false);
        if (!to.IsNull)
            (frameMembers[to.Index] ??= new EntitySet()).Queue(entity, true);
        entry.Member = to;
        return true;
    }

    /// <summary>
    /// Subscribes an entity to a topic. Subscribing twice changes nothing. Returns false for the topic of a frame that
    /// is not alive, or of an older frame whose index a new frame now uses. Throws <see cref="StaleEntityException"/>
    /// for a stale handle.
    /// </summary>
    public bool Subscribe(Entity entity, Topic topic)
    {
        ref EntityTopics entry = ref byEntity[entity];
        if (entry.Subscriptions?.Contains(topic) == true)
            return true;
        EntitySet? set = SetOf(topic, create: true);
        if (set == null)
            return false;
        (entry.Subscriptions ??= spareSubscriptionLists.Count > 0 ? spareSubscriptionLists.Pop() : new List<Topic>(2)).Add(topic);
        set.Queue(entity, true);
        return true;
    }

    /// <summary>
    /// Unsubscribes an entity from a topic. The frame membership is separate and does not change. Returns false, and
    /// does nothing, if the entity was not subscribed, which includes a stale handle: a dead entity hears nothing.
    /// </summary>
    public bool Unsubscribe(Entity entity, Topic topic)
    {
        if (!entities.IsAlive(entity) || byEntity[entity].Subscriptions?.Remove(topic) != true)
            return false;
        SetOf(topic, create: false)?.Queue(entity, false);
        return true;
    }

    /// <summary>
    /// Every recipient of a topic, in slot order, without duplicates: the subscribers of a named topic, or the members
    /// and explicit subscribers of a live frame. Never a dead entity. Nobody for an unknown named topic, a dead frame, or
    /// an older frame whose index a new frame reuses.
    /// </summary>
    public Recipients RecipientsOf(Topic topic)
    {
        if (!topic.IsFrameTopic)
            return topic.Id < named.Count && named[topic.Id] is { } set ? new Recipients(set.Items, default) : default;
        if (!IsLive(topic))
            return default;
        int index = topic.FrameIndex;
        ReadOnlySpan<Entity> members = frameMembers[index] is { } m ? m.Items : default;
        ReadOnlySpan<Entity> subscribers = frameSubscribers[index] is { } s ? s.Items : default;
        return new Recipients(members, subscribers);
    }

    /// <summary>Checks every invariant in the class comment. O(capacity + set sizes).</summary>
    internal void AssertInvariants()
    {
        int capacity = entities.Capacity;
        var memberSets = new int[capacity];
        var subscriberSets = new int[capacity];
        for (int index = 0; index < frameAt.Length; index++)
        {
            FrameHandle frame = frameAt[index];
            Contract.Invariant(frame.IsNull || frame.Index == index);
            if (frameMembers[index] is { } members)
            {
                members.AssertInvariants();
                Contract.Invariant(!frame.IsNull || members.Items.IsEmpty);
                foreach (Entity member in members.Items)
                {
                    Contract.Invariant(entities.IsAlive(member) && byEntity[member].Member == frame);
                    memberSets[member.Index]++;
                }
            }
            if (frameSubscribers[index] is { } subscribers)
            {
                subscribers.AssertInvariants();
                Contract.Invariant(!frame.IsNull || subscribers.Items.IsEmpty);
                foreach (Entity subscriber in subscribers.Items)
                {
                    Contract.Invariant(entities.IsAlive(subscriber) && byEntity[subscriber].Subscriptions?.Contains(frame.Topic) == true);
                    subscriberSets[subscriber.Index]++;
                }
            }
        }
        for (int id = 0; id < named.Count; id++)
        {
            if (named[id] is not { } subscribers)
                continue;
            subscribers.AssertInvariants();
            foreach (Entity subscriber in subscribers.Items)
            {
                Contract.Invariant(entities.IsAlive(subscriber) && byEntity[subscriber].Subscriptions?.Contains(new Topic(id)) == true);
                subscriberSets[subscriber.Index]++;
            }
        }

        foreach (Entity entity in entities.LiveEntities)
        {
            ref readonly EntityTopics entry = ref byEntity[entity];
            uint slot = entity.Index;
            Contract.Invariant(entry.Member.IsNull ? memberSets[slot] == 0 : IsLive(entry.Member) && memberSets[slot] == 1);
            List<Topic>? subscriptions = entry.Subscriptions;
            int subscribed = subscriptions?.Count ?? 0;
            Contract.Invariant(subscriberSets[slot] == subscribed);
            for (int i = 0; i < subscribed; i++)
                Contract.Invariant(subscriptions!.IndexOf(subscriptions[i]) == i);
        }
    }

    /// <summary>
    /// Takes a released entity, still live while this runs, out of its member set and every subscriber set. The entity
    /// table clears its entry next.
    /// </summary>
    private void Forget(Entity entity)
    {
        ref EntityTopics entry = ref byEntity[entity];
        if (!entry.Member.IsNull)
            frameMembers[entry.Member.Index]?.Queue(entity, false);
        if (entry.Subscriptions is { } subscriptions)
        {
            foreach (Topic topic in subscriptions)
                SetOf(topic, create: false)?.Queue(entity, false);
            subscriptions.Clear();
            spareSubscriptionLists.Push(subscriptions);
        }
    }

    /// <summary>True if a frame topic names a live frame: the frame at its index is alive and has its generation.</summary>
    private bool IsLive(Topic frameTopic)
    {
        int index = frameTopic.FrameIndex;
        return (uint)index < (uint)frameAt.Length && !frameAt[index].IsNull && frameAt[index].Generation == frameTopic.Generation;
    }

    /// <summary>The explicit subscriber set of a topic. Null for the topic of a frame that is not alive.</summary>
    private EntitySet? SetOf(Topic topic, bool create)
    {
        if (topic.IsFrameTopic)
        {
            if (!IsLive(topic))
                return null;
            int index = topic.FrameIndex;
            return create ? frameSubscribers[index] ??= new EntitySet() : frameSubscribers[index];
        }
        int id = topic.Id;
        if (!create)
            return id < named.Count ? named[id] : null;
        if (id >= named.Count)
            CollectionsMarshal.SetCount(named, id + 1);
        return named[id] ??= new EntitySet();
    }

    /// <summary>
    /// The merge of two sorted entity sets, in slot order, with an entity in both yielded once. Use it with
    /// <c>foreach</c>; it allocates nothing. It reads the sets in place, so it must not outlive the next read of
    /// <see cref="TopicSets"/>.
    /// </summary>
    public ref struct Recipients
    {
        private readonly ReadOnlySpan<Entity> a;
        private readonly ReadOnlySpan<Entity> b;
        private int i;
        private int j;
        private Entity current;

        internal Recipients(ReadOnlySpan<Entity> a, ReadOnlySpan<Entity> b)
        {
            this.a = a;
            this.b = b;
        }

        public readonly Recipients GetEnumerator() => this;

        public readonly Entity Current => current;

        public bool MoveNext()
        {
            if (i < a.Length)
            {
                if (j < b.Length)
                {
                    Entity x = a[i], y = b[j];
                    int order = x.CompareTo(y);
                    if (order <= 0)
                    {
                        current = x;
                        i++;
                        if (order == 0)
                            j++;
                    }
                    else
                    {
                        current = y;
                        j++;
                    }
                    return true;
                }
                current = a[i++];
                return true;
            }
            if (j < b.Length)
            {
                current = b[j++];
                return true;
            }
            return false;
        }
    }
}
