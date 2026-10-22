using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>What the router does with a stale handle.</summary>
public class StaleHandleRouterTests
{
    private static (EntityTable Table, Router Router, Entity Stale, Entity Successor) Routed()
    {
        EntityTable table = TableSupport.NewTable();
        var topics = new TopicSets(table);
        var router = new Router(table, topics);
        Entity stale = table.ReuseSlot(out Entity successor, components: new Recorder());
        router.Post(Event.Create(successor, Entity.Null, new Ping { Value = 1 }));
        router.Route(tick: 0);
        return (table, router, stale, successor);
    }

    [Fact]
    public void AStaleHandleHasNoInboxToCount()
    {
        (_, Router router, Entity stale, Entity successor) = Routed();

        Action count = () => router.InboxCount(stale);

        count.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        router.InboxCount(successor).Should().Be(1);
    }

    [Fact]
    public void AStaleHandleCannotCopyTheInboxOfTheEntityThatReusedItsSlot()
    {
        (_, Router router, Entity stale, _) = Routed();

        Action copy = () =>
        {
            var buffer = new EventBuffer();
            router.CopyInbox(stale, buffer.Begin(stale));
        };

        copy.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
    }
}

/// <summary>What the topic sets do with a stale handle.</summary>
public class StaleHandleTopicSetsTests
{
    private static readonly Topic Squad = Topics.Get("tests.stale.squad");

    private static List<Entity> Recipients(TopicSets topics, Topic topic)
    {
        var recipients = new List<Entity>();
        foreach (Entity entity in topics.RecipientsOf(topic))
            recipients.Add(entity);
        return recipients;
    }

    [Fact]
    public void TopicRecipientsNeverIncludeADeadEntity()
    {
        EntityTable table = TableSupport.NewTable();
        var topics = new TopicSets(table);
        Entity doomed = table.SpawnLive(), listener = table.SpawnLive();
        topics.Subscribe(doomed, Squad);
        topics.Subscribe(listener, Squad);

        table.Release(doomed);
        Entity successor = table.SpawnLive();

        successor.Index.Should().Be(doomed.Index);
        Recipients(topics, Squad).Should().Equal(listener);
    }

    [Fact]
    public void FrameMembersAreTheHandlesOfTheEntitiesInTheFrameNow()
    {
        EntityTable table = TableSupport.NewTable();
        var topics = new TopicSets(table);
        var bay = new FrameHandle(new(7, 1), 0, 1);
        topics.AddFrame(bay);
        Entity stale = table.ReuseSlot(out Entity successor);
        topics.MoveMember(successor, bay, out _);

        topics.MembersOf(bay).ToArray().Should().Equal(successor);
        Recipients(topics, bay.Topic).Should().Equal(successor);
        stale.Should().NotBe(successor);
    }

    [Fact]
    public void AStaleHandleCannotSubscribe()
    {
        EntityTable table = TableSupport.NewTable();
        var topics = new TopicSets(table);
        Entity stale = table.ReuseSlot(out _);

        Action subscribe = () => topics.Subscribe(stale, Squad);

        subscribe.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        Recipients(topics, Squad).Should().BeEmpty();
    }

    [Fact]
    public void UnsubscribingAStaleHandleReturnsFalseAndKeepsTheSuccessorsSubscription()
    {
        EntityTable table = TableSupport.NewTable();
        var topics = new TopicSets(table);
        Entity stale = table.ReuseSlot(out Entity successor);
        topics.Subscribe(successor, Squad);

        topics.Unsubscribe(stale, Squad).Should().BeFalse();

        Recipients(topics, Squad).Should().Equal(successor);
        topics.AssertInvariants();
    }

    [Fact]
    public void UnsubscribingReturnsTrueOnlyWhenItEndsASubscription()
    {
        EntityTable table = TableSupport.NewTable();
        var topics = new TopicSets(table);
        Entity entity = table.SpawnLive();
        topics.Subscribe(entity, Squad);

        topics.Unsubscribe(entity, Squad).Should().BeTrue();
        topics.Unsubscribe(entity, Squad).Should().BeFalse();
    }

    [Fact]
    public void AStaleHandleCannotMoveIntoAFrame()
    {
        EntityTable table = TableSupport.NewTable();
        var topics = new TopicSets(table);
        var bay = new FrameHandle(new(7, 1), 0, 1);
        topics.AddFrame(bay);
        Entity stale = table.ReuseSlot(out _);

        Action move = () => topics.MoveMember(stale, bay, out _);

        move.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        topics.MembersOf(bay).IsEmpty.Should().BeTrue();
    }
}

/// <summary>What the attachment chains do with a stale handle.</summary>
public class StaleHandleAttachmentsTests
{
    private static readonly Pose Offset = new(new(1, 0, 0), Quaternion.Identity);

    [Fact]
    public void AStaleHandleCannotBeAttached()
    {
        EntityTable table = TableSupport.NewTable();
        var attachments = new Attachments(table);
        Entity parent = table.SpawnLive();
        Entity stale = table.ReuseSlot(out Entity successor);

        Action attach = () => attachments.Attach(stale, parent, Offset);

        attach.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        attachments.ParentOf(successor).IsNull.Should().BeTrue();
    }

    [Fact]
    public void NothingCanBeAttachedToAStaleParent()
    {
        EntityTable table = TableSupport.NewTable();
        var attachments = new Attachments(table);
        Entity stale = table.ReuseSlot(out Entity successor);
        Entity child = table.SpawnLive();

        Action attach = () => attachments.Attach(child, stale, Offset);

        attach.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        attachments.ChildrenOf(successor).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void AStaleHandleHasNoAttachmentParentToRead()
    {
        EntityTable table = TableSupport.NewTable();
        var attachments = new Attachments(table);
        Entity parent = table.SpawnLive();
        Entity stale = table.ReuseSlot(out Entity successor);
        attachments.Attach(successor, parent, Offset);

        Action read = () => attachments.ParentOf(stale);

        read.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        attachments.ParentOf(successor).Should().Be(parent);
    }

    [Fact]
    public void AStaleHandleCannotWriteTheAttachOffsetOfTheEntityThatReusedItsSlot()
    {
        EntityTable table = TableSupport.NewTable();
        var attachments = new Attachments(table);
        Entity parent = table.SpawnLive();
        Entity stale = table.ReuseSlot(out Entity successor);
        attachments.Attach(successor, parent, Offset);

        Action write = () => attachments.WritableOffset(stale) = Pose.Identity;

        write.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
    }

    [Fact]
    public void AttachedChildrenAreTheHandlesOfLiveEntities()
    {
        EntityTable table = TableSupport.NewTable();
        var attachments = new Attachments(table);
        Entity parent = table.SpawnLive();
        Entity first = table.SpawnLive(), second = table.SpawnLive();
        attachments.Attach(second, parent, Offset);
        attachments.Attach(first, parent, Offset);
        table.Release(first);

        attachments.ChildrenOf(parent).ToArray().Should().Equal(second);
        attachments.Attached.ToArray().Should().Equal(second);
        Action read = () => _ = attachments.ChildrenOf(first).Length;
        read.Should().Throw<StaleEntityException>();
    }
}
