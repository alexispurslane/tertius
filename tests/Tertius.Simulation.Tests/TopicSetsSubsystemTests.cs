namespace Tertius.Simulation.Tests;

/// <summary>The topic sets on their own: every read sees every change made so far, and a released slot leaves every set.</summary>
public class TopicSetsSubsystemTests
{
    private static readonly Topic Squad = Topics.Get("tests.topicsets.squad");

    private static FrameHandle Frame(int index, uint generation) => new(new Entity(63, generation), index, generation);

    private static List<Entity> Recipients(TopicSets topics, Topic topic)
    {
        var recipients = new List<Entity>();
        foreach (Entity recipient in topics.RecipientsOf(topic))
            recipients.Add(recipient);
        return recipients;
    }

    [Fact]
    public void EveryReadSeesEveryChangeMadeSoFar()
    {
        EntityTable entities = TableSupport.NewTable();
        var topics = new TopicSets(entities);
        FrameHandle bay = Frame(0, 1);
        topics.AddFrame(bay);
        Entity a = entities.SpawnLive(), b = entities.SpawnLive();

        Assert.True(topics.MoveMember(b, bay, out FrameHandle from));
        Assert.True(from.IsNull);
        Assert.Equal([b], topics.MembersOf(bay).ToArray());
        Assert.True(topics.MoveMember(a, bay, out _));
        Assert.False(topics.MoveMember(a, bay, out _));
        Assert.Equal([a, b], topics.MembersOf(bay).ToArray());

        Assert.True(topics.Subscribe(b, Squad));
        Assert.True(topics.Subscribe(a, Squad));
        Assert.Equal([a, b], Recipients(topics, Squad));
        topics.Unsubscribe(a, Squad);
        Assert.Equal([b], Recipients(topics, Squad));

        // A span read earlier stays as it was while changes queue; the next read sees them.
        ReadOnlySpan<Entity> members = topics.MembersOf(bay);
        topics.MoveMember(a, FrameHandle.Null, out _);
        Assert.Equal([a, b], members.ToArray());
        Assert.Equal([b], topics.MembersOf(bay).ToArray());
        topics.AssertInvariants();
    }

    [Fact]
    public void AReleasedSlotLeavesEverySetAndItsSuccessorStartsInNone()
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
        Assert.Empty(topics.MembersOf(bay).ToArray());
        Assert.Equal([watcher], Recipients(topics, Squad));
        Assert.Empty(Recipients(topics, bay.Topic));
        topics.AssertInvariants();

        // The predecessor's subscriptions are gone, so subscribing again is a real change.
        Entity successor = entities.SpawnLive();
        Assert.Equal(doomed.Index, successor.Index);
        Assert.Equal([watcher], Recipients(topics, Squad));
        Assert.True(topics.Subscribe(successor, Squad));
        Assert.Equal([watcher, successor], Recipients(topics, Squad));
        topics.AssertInvariants();
    }

    [Fact]
    public void RemovingAFrameEndsItsSubscriptionsAndEmptiesItsSets()
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
        Assert.False(topics.IsLive(bay));
        Assert.Empty(Recipients(topics, bay.Topic));
        Assert.False(topics.Subscribe(watcher, bay.Topic));
        Assert.Equal([watcher], Recipients(topics, Squad));
        topics.AssertInvariants();

        // The index is free for the next frame, which starts with no members or subscribers.
        FrameHandle next = Frame(2, 2);
        topics.AddFrame(next);
        Assert.Empty(Recipients(topics, next.Topic));
        topics.AssertInvariants();
    }
}
