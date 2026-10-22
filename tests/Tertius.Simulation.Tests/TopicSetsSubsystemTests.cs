using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// The topic sets on their own: every read sees every change made so far, in slot order; a frame topic reaches the union
/// of the frame's members and its explicit subscribers; and a released slot leaves every set.
/// </summary>
public class TopicSetsSubsystemTests
{
    private static readonly Topic Squad = Topics.Get("tests.topicsets.squad");
    private static readonly Topic Convoy = Topics.Get("tests.topicsets.convoy");

    private static FrameHandle Frame(int index, uint generation) => new(new(63, generation), index, generation);

    private static List<Entity> Recipients(TopicSets topics, Topic topic)
    {
        var recipients = new List<Entity>();
        foreach (Entity recipient in topics.RecipientsOf(topic))
            recipients.Add(recipient);
        return recipients;
    }

    /// <summary>Live entities in fresh slots 0, 1, 2, ... so that a test can say "the entity of slot 3" and mean the fourth one.</summary>
    private static Entity[] SpawnLiveInSlotOrder(EntityTable entities, int count)
    {
        Entity[] spawned = [.. Enumerable.Range(0, count).Select(_ => entities.SpawnLive())];
        spawned.Select(entity => (int)entity.Index).Should()
            .Equal(Enumerable.Range(0, count), "the fresh slots are handed out in order");
        return spawned;
    }

    // ---- Every read sees every change ----------------------------------------------------------------------------------

    [Fact]
    public void FrameMembersIncludeEveryEntityMovedInSoFar()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity a = entities.SpawnLive(), b = entities.SpawnLive();

        topics.MoveMember(b, bay, out FrameHandle from).Should().BeTrue();
        from.IsNull.Should().BeTrue("the entity was in no frame before");
        topics.MembersOf(bay).ToArray().Should().Equal(b);

        topics.MoveMember(a, bay, out _).Should().BeTrue();
        topics.MoveMember(a, bay, out _).Should().BeFalse("it is a member already, so nothing changes");
        topics.MembersOf(bay).ToArray().Should().Equal(a, b);
        topics.AssertInvariants();
    }

    [Fact]
    public void RecipientsOfANamedTopicIncludeEverySubscriptionMadeAndNotEveryOneEnded()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        Entity a = entities.SpawnLive(), b = entities.SpawnLive();

        topics.Subscribe(b, Squad).Should().BeTrue();
        topics.Subscribe(a, Squad).Should().BeTrue();
        Recipients(topics, Squad).Should().Equal(a, b);

        topics.Unsubscribe(a, Squad).Should().BeTrue();
        Recipients(topics, Squad).Should().Equal(b);
        topics.AssertInvariants();
    }

    [Fact]
    public void ASpanReadEarlierKeepsItsContentsWhileLaterChangesQueueAndTheNextReadSeesThem()
    {
        // The span is documented to stay valid until the class is read again, so a caller can loop over it while it
        // sends messages that queue changes. The next read applies those changes.
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity a = entities.SpawnLive(), b = entities.SpawnLive();
        topics.MoveMember(a, bay, out _);
        topics.MoveMember(b, bay, out _);

        ReadOnlySpan<Entity> members = topics.MembersOf(bay);
        topics.MoveMember(a, FrameHandle.Null, out _);

        members.ToArray().Should().Equal(a, b);
        topics.MembersOf(bay).ToArray().Should().Equal(b);
        topics.AssertInvariants();
    }

    // ---- Slot order, whatever the order of the changes ---------------------------------------------------------------

    [Fact]
    public void FrameMembersAreListedInSlotOrderWhateverTheOrderTheyWereMovedIn()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity[] e = SpawnLiveInSlotOrder(entities, 6);

        // Moved in this order, with no read between the moves, so that all six changes are queued together.
        foreach (int slot in new[] { 4, 1, 5, 0, 3, 2 })
            topics.MoveMember(e[slot], bay, out _);

        topics.MembersOf(bay).ToArray().Should().Equal(e);
    }

    [Fact]
    public void SubscribersOfANamedTopicAreListedInSlotOrderWhateverTheOrderTheySubscribedIn()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        Entity[] e = SpawnLiveInSlotOrder(entities, 6);

        foreach (int slot in new[] { 3, 5, 0, 2, 4, 1 })
            topics.Subscribe(e[slot], Squad);

        Recipients(topics, Squad).Should().Equal(e);
    }

    [Fact]
    public void AMemberLeavingAndAnotherJoiningGiveTheSameMembersInEitherOrderOfTheChanges()
    {
        // The two changes are queued in opposite orders in the two halves. Both must give the same sorted result.
        foreach (bool leaverFirst in new[] { true, false })
        {
            EntityTable entities = TableSupport.NewTable();
            var topics = new TopicSets(entities);
            FrameHandle bay = Frame(0, 1);
            topics.AddFrame(bay);
            Entity[] e = SpawnLiveInSlotOrder(entities, 4);
            topics.MoveMember(e[1], bay, out _);
            topics.MoveMember(e[3], bay, out _);
            topics.MembersOf(bay).ToArray().Should().Equal(e[1], e[3]);

            if (leaverFirst)
            {
                topics.MoveMember(e[1], FrameHandle.Null, out _);
                topics.MoveMember(e[0], bay, out _);
            }
            else
            {
                topics.MoveMember(e[0], bay, out _);
                topics.MoveMember(e[1], FrameHandle.Null, out _);
            }

            topics.MembersOf(bay).ToArray().Should().Equal([e[0], e[3]], $"leaver first: {leaverFirst}");
        }
    }

    // ---- The last queued change to an entity wins --------------------------------------------------------------------

    [Theory]
    [InlineData("S", true)]
    [InlineData("SU", false)]
    [InlineData("SUS", true)]
    [InlineData("SUSU", false)]
    public void TheLastOfSeveralSubscriptionChangesMadeBeforeAReadDecidesWhetherTheEntityHearsTheTopic(string changes,
        bool hearsAtTheEnd)
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        Entity entity = entities.SpawnLive();

        foreach (char change in changes)
            if (change == 'S')
                topics.Subscribe(entity, Squad);
            else
                topics.Unsubscribe(entity, Squad);

        Recipients(topics, Squad).Contains(entity).Should().Be(hearsAtTheEnd);
        topics.AssertInvariants();
    }

    [Fact]
    public void AnEntityMovedThroughSeveralFramesBeforeAReadIsOnlyAMemberOfTheLastOne()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1), hold = Frame(1, 1), deck = Frame(2, 1);
        foreach (FrameHandle frame in new[] { bay, hold, deck })
            topics.AddFrame(frame);
        Entity entity = entities.SpawnLive();

        topics.MoveMember(entity, bay, out _);
        topics.MoveMember(entity, hold, out FrameHandle fromHold).Should().BeTrue();
        topics.MoveMember(entity, deck, out FrameHandle fromDeck).Should().BeTrue();

        fromHold.Should().Be(bay, "each move reports the frame the entity was in just before it");
        fromDeck.Should().Be(hold);
        topics.MembersOf(bay).ToArray().Should().BeEmpty();
        topics.MembersOf(hold).ToArray().Should().BeEmpty();
        topics.MembersOf(deck).ToArray().Should().Equal(entity);
        topics.AssertInvariants();
    }

    // ---- A frame topic reaches its members and its explicit subscribers ------------------------------------------------

    // Each case names entities by slot. The expectation is the union by hand: every slot that is in either list, once,
    // from the lowest slot to the highest.
    [Theory]
    [InlineData(new[] { 0, 2, 4 }, new[] { 1, 3, 5 }, new[] { 0, 1, 2, 3, 4, 5 })]
    [InlineData(new[] { 1, 3 }, new[] { 1, 3 }, new[] { 1, 3 })]
    [InlineData(new[] { 0, 1, 2 }, new int[0], new[] { 0, 1, 2 })]
    [InlineData(new int[0], new[] { 5, 2 }, new[] { 2, 5 })]
    [InlineData(new int[0], new int[0], new int[0])]
    [InlineData(new[] { 6, 2, 4 }, new[] { 4, 1, 6, 7 }, new[] { 1, 2, 4, 6, 7 })]
    public void AFrameTopicReachesTheUnionOfItsMembersAndSubscribersOnceEachInSlotOrder(int[] memberSlots,
        int[] subscriberSlots, int[] expectedSlots)
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity[] e = SpawnLiveInSlotOrder(entities, 8);
        foreach (int slot in memberSlots)
            topics.MoveMember(e[slot], bay, out _);
        foreach (int slot in subscriberSlots)
            topics.Subscribe(e[slot], bay.Topic);

        Recipients(topics, bay.Topic).Should().Equal(expectedSlots.Select(slot => e[slot]));
        topics.AssertInvariants();
    }

    [Fact]
    public void ASubscriberOutsideAFrameHearsItsTopicWithoutBecomingAMember()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity inside = entities.SpawnLive(), watcher = entities.SpawnLive();
        topics.MoveMember(inside, bay, out _);
        topics.Subscribe(watcher, bay.Topic);

        Recipients(topics, bay.Topic).Should().Equal(inside, watcher);
        topics.MembersOf(bay).ToArray().Should().Equal(inside);
    }

    [Fact]
    public void LeavingAFrameKeepsTheExplicitSubscriptionToItsTopic()
    {
        // Membership and subscription are separate: moving out ends the first, not the second.
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity entity = entities.SpawnLive();
        topics.MoveMember(entity, bay, out _);
        topics.Subscribe(entity, bay.Topic);

        topics.MoveMember(entity, FrameHandle.Null, out _);

        topics.MembersOf(bay).ToArray().Should().BeEmpty();
        Recipients(topics, bay.Topic).Should().Equal(entity);
    }

    [Fact]
    public void UnsubscribingFromAFrameTopicKeepsTheMembership()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity entity = entities.SpawnLive();
        topics.MoveMember(entity, bay, out _);
        topics.Subscribe(entity, bay.Topic);

        topics.Unsubscribe(entity, bay.Topic).Should().BeTrue();

        Recipients(topics, bay.Topic).Should().Equal([entity], "it still hears the topic as a member");
    }

    [Fact]
    public void ANamedTopicAndAFrameTopicKeepSeparateSubscribers()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity a = entities.SpawnLive(), b = entities.SpawnLive();
        topics.Subscribe(a, Squad);
        topics.Subscribe(b, Convoy);
        topics.Subscribe(b, bay.Topic);

        Recipients(topics, Squad).Should().Equal(a);
        Recipients(topics, Convoy).Should().Equal(b);
        Recipients(topics, bay.Topic).Should().Equal(b);
    }

    [Fact]
    public void NobodyHearsANamedTopicNobodyEverSubscribedTo()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        entities.SpawnLive();

        Recipients(topics, Topics.Get("tests.topicsets.never-used")).Should().BeEmpty();
    }

    // ---- Releasing and removing ------------------------------------------------------------------------------------

    [Fact]
    public void AReleasedEntityLeavesTheMemberSetAndEverySubscriberSet()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(1, 1);
        topics.AddFrame(bay);
        Entity watcher = entities.SpawnLive();
        Entity doomed = entities.SpawnLive();
        topics.MoveMember(doomed, bay, out _);
        topics.Subscribe(doomed, Squad);
        topics.Subscribe(doomed, bay.Topic);
        topics.Subscribe(watcher, Squad);

        entities.Release(doomed);

        topics.MembersOf(bay).ToArray().Should().BeEmpty();
        Recipients(topics, Squad).Should().Equal(watcher);
        Recipients(topics, bay.Topic).Should().BeEmpty();
        topics.AssertInvariants();
    }

    [Fact]
    public void TheEntityThatReusesAReleasedSlotStartsWithNoSubscriptionsOfItsPredecessor()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        Entity watcher = entities.SpawnLive();
        Entity doomed = entities.SpawnLive();
        topics.Subscribe(doomed, Squad);
        topics.Subscribe(watcher, Squad);
        entities.Release(doomed);

        Entity successor = entities.SpawnLive();

        successor.Index.Should().Be(doomed.Index, "the slot is reused");
        Recipients(topics, Squad).Should().Equal(watcher);
        topics.Subscribe(successor, Squad).Should()
            .BeTrue("the predecessor's subscription is gone, so this is a new one");
        Recipients(topics, Squad).Should().Equal(watcher, successor);
        topics.AssertInvariants();
    }

    [Fact]
    public void RemovingAFrameEndsTheSubscriptionsToItsTopicAndEmptiesItsSets()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(2, 1);
        topics.AddFrame(bay);
        Entity inside = entities.SpawnLive(), watcher = entities.SpawnLive();
        topics.MoveMember(inside, bay, out _);
        topics.Subscribe(watcher, bay.Topic);
        topics.Subscribe(watcher, Squad);

        topics.MoveMember(inside, FrameHandle.Null, out _);
        topics.RemoveFrame(bay);

        topics.IsLive(bay).Should().BeFalse();
        Recipients(topics, bay.Topic).Should().BeEmpty();
        topics.Subscribe(watcher, bay.Topic).Should()
            .BeFalse("the topic of a frame that is not alive takes no subscribers");
        Recipients(topics, Squad).Should().Equal([watcher], "its other subscriptions stay");
        topics.AssertInvariants();
    }

    [Fact]
    public void AFrameThatReusesTheIndexOfARemovedFrameStartsEmptyAndTheOldTopicStaysDead()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle old = Frame(2, 1);
        topics.AddFrame(old);
        Entity watcher = entities.SpawnLive(), inside = entities.SpawnLive();
        topics.Subscribe(watcher, old.Topic);
        topics.RemoveFrame(old);

        FrameHandle next = Frame(2, 2);
        topics.AddFrame(next);
        Recipients(topics, next.Topic).Should().BeEmpty("a new frame starts with no members or subscribers");
        topics.MoveMember(inside, next, out _);

        Recipients(topics, old.Topic).Should()
            .BeEmpty("a message to the old frame never reaches the frame that reuses its index");
        topics.Subscribe(watcher, old.Topic).Should().BeFalse();
        Recipients(topics, next.Topic).Should().Equal(inside);
        topics.AssertInvariants();
    }

    // ---- Doing it twice, and undoing what was never done --------------------------------------------------------------

    [Fact]
    public void SubscribingTwiceToATopicListsTheEntityOnce()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        Entity entity = entities.SpawnLive();

        topics.Subscribe(entity, Squad).Should().BeTrue();
        topics.Subscribe(entity, Squad).Should().BeTrue("the entity is subscribed, so the request is met");

        Recipients(topics, Squad).Should().Equal(entity);
        topics.AssertInvariants();
    }

    [Fact]
    public void UnsubscribingFromATopicTheEntityNeverJoinedChangesNothing()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        Entity subscriber = entities.SpawnLive(), bystander = entities.SpawnLive();
        topics.Subscribe(subscriber, Squad);

        topics.Unsubscribe(bystander, Squad).Should().BeFalse();

        Recipients(topics, Squad).Should().Equal(subscriber);
        topics.AssertInvariants();
    }

    [Fact]
    public void AnEntityMovedToNoFrameIsInNoMemberSet()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity entity = entities.SpawnLive();
        topics.MoveMember(entity, bay, out _);

        topics.MoveMember(entity, FrameHandle.Null, out FrameHandle from).Should().BeTrue();

        from.Should().Be(bay);
        topics.MembersOf(bay).ToArray().Should().BeEmpty();
        topics.MoveMember(entity, FrameHandle.Null, out _).Should().BeFalse("it is in no frame already");
    }

    [Fact]
    public void TheMembersOfAFrameThatIsNotAliveAreNobody()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);

        topics.MembersOf(Frame(5, 1)).ToArray().Should().BeEmpty("the frame was never added");
        topics.MembersOf(FrameHandle.Null).ToArray().Should().BeEmpty();
    }

    // ---- Agreement with a simple model, over a long random sequence -------------------------------------------------------

    // The model keeps the same facts in plain dictionaries and sets, and sorts at the moment of every read. The real sets
    // queue their changes and merge them in one pass, so this checks that the merge gives what sorting gives, however the
    // changes were mixed with releases, reused slots, frames that die and frames that take over an index.
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(20261010)]
    [InlineData(424242)]
    public void ALongRandomSequenceOfChangesAndReadsAgreesWithASimpleModelOfTheSets(int seed)
    {
        var random = new Random(seed);
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        Topic[] named = [Squad, Convoy];

        var frames = new FrameHandle[4];
        for (int i = 0; i < frames.Length; i++)
        {
            frames[i] = Frame(i, 1);
            topics.AddFrame(frames[i]);
        }

        var live = new List<Entity>();
        var memberOf = new Dictionary<Entity, FrameHandle>();
        var subscribers = new Dictionary<Topic, HashSet<Entity>>();

        HashSet<Entity> SubscribersOf(Topic topic) => subscribers.TryGetValue(topic, out HashSet<Entity>? set)
            ? set
            : subscribers[topic] = [];

        List<Entity> ModelMembers(FrameHandle frame) =>
            [.. memberOf.Where(pair => pair.Value == frame).Select(pair => pair.Key).Order()];

        void ReadEverythingAndCompare(int step)
        {
            string at = $"seed {seed}, step {step}";
            foreach (Topic topic in named)
                Recipients(topics, topic).Should().Equal([.. SubscribersOf(topic).Order()],
                    $"{at}: subscribers of {Topics.NameOf(topic)}");
            foreach (FrameHandle frame in frames)
            {
                List<Entity> members = ModelMembers(frame);
                topics.MembersOf(frame).ToArray().Should().Equal(members, $"{at}: members of {frame}");
                Recipients(topics, frame.Topic).Should()
                    .Equal([.. members.Union(SubscribersOf(frame.Topic)).Order()],
                        $"{at}: recipients of the topic of {frame}");
            }

            topics.AssertInvariants();
        }

        for (int step = 0; step < 1500; step++)
        {
            int roll = random.Next(100);
            if ((roll < 12 && live.Count < 40) || live.Count == 0)
            {
                live.Add(entities.SpawnLive());
            }
            else if (roll < 20)
            {
                // A release takes the entity out of everything. The slot is free for the next spawn.
                Entity doomed = live[random.Next(live.Count)];
                entities.Release(doomed).Should().BeTrue();
                live.Remove(doomed);
                memberOf.Remove(doomed);
                foreach (HashSet<Entity> set in subscribers.Values)
                    set.Remove(doomed);
            }
            else if (roll < 45)
            {
                Entity entity = live[random.Next(live.Count)];
                int pick = random.Next(frames.Length + 1);
                FrameHandle to = pick == frames.Length ? FrameHandle.Null : frames[pick];
                memberOf.TryGetValue(entity, out FrameHandle before);

                bool changed = topics.MoveMember(entity, to, out FrameHandle from);

                changed.Should().Be(before != to, $"seed {seed}, step {step}");
                from.Should().Be(before, $"seed {seed}, step {step}");
                if (to.IsNull)
                    memberOf.Remove(entity);
                else
                    memberOf[entity] = to;
            }
            else if (roll < 65)
            {
                Entity entity = live[random.Next(live.Count)];
                Topic topic = random.Next(3) == 0
                    ? frames[random.Next(frames.Length)].Topic
                    : named[random.Next(named.Length)];

                topics.Subscribe(entity, topic).Should().BeTrue($"seed {seed}, step {step}: the topic is alive");
                SubscribersOf(topic).Add(entity);
            }
            else if (roll < 77)
            {
                Entity entity = live[random.Next(live.Count)];
                Topic topic = random.Next(3) == 0
                    ? frames[random.Next(frames.Length)].Topic
                    : named[random.Next(named.Length)];
                bool wasSubscribed = SubscribersOf(topic).Contains(entity);

                topics.Unsubscribe(entity, topic).Should().Be(wasSubscribed, $"seed {seed}, step {step}");
                SubscribersOf(topic).Remove(entity);
            }
            else if (roll < 81)
            {
                // A frame dies and another takes over its index. Its members move out first, as the world does.
                int index = random.Next(frames.Length);
                FrameHandle dying = frames[index];
                foreach (Entity member in ModelMembers(dying))
                {
                    topics.MoveMember(member, FrameHandle.Null, out _).Should().BeTrue();
                    memberOf.Remove(member);
                }

                topics.RemoveFrame(dying);
                subscribers.Remove(dying.Topic);
                topics.IsLive(dying).Should().BeFalse();
                Recipients(topics, dying.Topic).Should().BeEmpty($"seed {seed}, step {step}: a dead frame has nobody");
                topics.Subscribe(live[random.Next(live.Count)], dying.Topic).Should()
                    .BeFalse($"seed {seed}, step {step}: a dead frame's topic takes nobody");

                frames[index] = Frame(index, dying.Generation + 1);
                topics.AddFrame(frames[index]);
            }
            else
            {
                ReadEverythingAndCompare(step);
            }
        }

        ReadEverythingAndCompare(1500);
    }
}
