using System.Numerics;

namespace Tertius.Simulation.Tests;

/// <summary>An entity table with live entities, for the subsystems that keep per-entity state in its columns.</summary>
internal static class TableSupport
{
    public static EntityTable NewTable()
    {
        EngineEvents.Register();
        return new EntityTable(chunkSize: 4, initialCapacity: 8);
    }

    public static Entity SpawnLive(this EntityTable table, FrameHandle frame = default, double x = 0)
    {
        Entity entity = table.Reserve();
        table.Activate(entity, new TransformRow { Frame = frame, Pose = TestWorld.At(x) }, new RenderData(), [], [], null, FrameHandle.Null);
        return entity;
    }
}

/// <summary>The topic sets on their own: every read sees every change made so far, and a released slot leaves every set.</summary>
public class TopicSetsTests
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

/// <summary>Attachments and the spatial index on their own: releasing an entity takes it out of the chains and the grids.</summary>
public class SlotReleaseTests
{
    private static Pose Offset(float x, float y) => new(new Vector3(x, y, 0), Quaternion.Identity);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReleasingAChainDetachesEveryLinkAndTheRootLeavesTheTree(bool parentFirst)
    {
        EntityTable entities = TableSupport.NewTable();
        var attachments = new Attachments(entities);
        Entity root = entities.SpawnLive(x: 10);
        Entity child = entities.SpawnLive();
        Entity grandchild = entities.SpawnLive();
        attachments.Attach(child, root, Offset(1, 0));
        attachments.Attach(grandchild, child, Offset(0, 1));
        attachments.Derive();
        Assert.Equal([child, grandchild], attachments.Attached.ToArray());
        Assert.Equal(new Double3(11, 1, 0), entities.BackRow(grandchild).Pose.Position);
        attachments.AssertInvariants();

        // The world releases an attached entity together with its children, in either order.
        Entity[] order = parentFirst ? [child, grandchild] : [grandchild, child];
        foreach (Entity entity in order)
            entities.Release(entity);

        Assert.Empty(attachments.Attached.ToArray());
        Assert.True(attachments.ChildrenOf(root).IsEmpty);
        // The released links are clear: the table checks every column entry of a slot that is not live.
        entities.AssertInvariants();
        attachments.AssertInvariants();

        // The root can take a new child, which starts a new chain.
        Entity next = entities.SpawnLive();
        attachments.Attach(next, root, Offset(2, 0));
        attachments.Derive();
        Assert.Equal(new Double3(12, 0, 0), entities.BackRow(next).Pose.Position);
        attachments.AssertInvariants();
    }

    [Fact]
    public void ReleasingASlotTakesItOutOfItsCell()
    {
        EntityTable entities = TableSupport.NewTable();
        var frames = new FrameTree();
        FrameHandle root = frames.Create(new Entity(60, 1), FrameHandle.Null, DPose.Identity, FrameBounds.Sphere(1000), MotionDriver.Static);
        var spatial = new SpatialIndex(entities, frames);
        Entity near = entities.SpawnLive(root, x: 1);
        Entity far = entities.SpawnLive(root, x: 500);
        Entity alsoNear = entities.SpawnLive(root, x: 2);
        spatial.Update();
        spatial.AssertInvariants();

        var found = new List<Entity>();
        spatial.QueryRadius(root, Double3.Zero, 10, found);
        Assert.Equal([near, alsoNear], found);

        entities.Release(near);
        spatial.AssertInvariants();
        spatial.QueryRadius(root, Double3.Zero, 10, found);
        Assert.Equal([alsoNear], found);

        // A new entity in the released slot is placed by the next update, and only then.
        Entity successor = entities.SpawnLive(root, x: 3);
        Assert.Equal(near.Index, successor.Index);
        spatial.QueryRadius(root, Double3.Zero, 10, found);
        Assert.Equal([alsoNear], found);
        spatial.Update();
        spatial.QueryRadius(root, Double3.Zero, 1000, found);
        Assert.Equal([successor, far, alsoNear], found);
        spatial.AssertInvariants();
    }
}
