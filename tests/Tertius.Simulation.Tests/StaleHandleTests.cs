using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Entity tables in which a handle has gone stale. The dangerous case is a reused slot: the stale handle's index names
/// a live entity, just not the one the handle was made for. Released slots come back first in, first out, so a table
/// with no other free slot hands the released one to the next spawn.
/// </summary>
internal static class StaleTables
{
    public static Entity SpawnLiveWith(this EntityTable table, BodyDesc? body, params IComponent[] components)
    {
        Entity entity = table.Reserve();
        table.Activate(entity, new TransformRow { Pose = TestWorld.At(1) }, new RenderData(), components,
            [.. components.Select(c => c.Wants)], body, FrameHandle.Null);
        return entity;
    }

    /// <summary>
    /// Releases a live entity and spawns <paramref name="successor"/>, which takes the released slot. Returns the
    /// released entity's handle, which is now stale.
    /// </summary>
    public static Entity ReuseSlot(this EntityTable table, out Entity successor, BodyDesc? body = null, params IComponent[] components)
    {
        Entity stale = table.SpawnLiveWith(body);
        table.Release(stale);
        successor = table.SpawnLiveWith(body, components);
        Assert.Equal(stale.Index, successor.Index);
        return stale;
    }
}

/// <summary>What the entity table does with a stale handle, member by member.</summary>
public class EntityTableStaleHandleTests
{
    private static TransformRow RowAt(double x) => new() { Pose = TestWorld.At(x) };

    /// <summary>One read of an entity's data through the table.</summary>
    private static Action Read(EntityTable table, string member, Entity entity) => member switch
    {
        nameof(EntityTable.FrontRow) => () => _ = table.FrontRow(entity).Pose,
        nameof(EntityTable.BackRow) => () => _ = table.BackRow(entity).Pose,
        nameof(EntityTable.FrontRender) => () => _ = table.FrontRender(entity).Scale,
        nameof(EntityTable.Components) => () => _ = table.Components(entity).Length,
        nameof(EntityTable.ComponentWants) => () => _ = table.ComponentWants(entity).Length,
        nameof(EntityTable.Body) => () => _ = table.Body(entity),
        nameof(EntityTable.OwnedFrame) => () => _ = table.OwnedFrame(entity),
        nameof(EntityTable.Accepts) => () => _ = table.Accepts(entity, EventType<Destroy>.Id),
        nameof(EntityTable.PendingIntent) => () => _ = table.PendingIntent(entity),
        nameof(EntityTable.GameplayWroteTransform) => () => _ = table.GameplayWroteTransform(entity),
        nameof(EntityTable.TakeFirstTick) => () => _ = table.TakeFirstTick(entity),
        _ => throw new ArgumentOutOfRangeException(nameof(member), member, null),
    };

    [Theory]
    [InlineData(nameof(EntityTable.FrontRow))]
    [InlineData(nameof(EntityTable.BackRow))]
    [InlineData(nameof(EntityTable.FrontRender))]
    [InlineData(nameof(EntityTable.Components))]
    [InlineData(nameof(EntityTable.ComponentWants))]
    [InlineData(nameof(EntityTable.Body))]
    [InlineData(nameof(EntityTable.OwnedFrame))]
    [InlineData(nameof(EntityTable.Accepts))]
    [InlineData(nameof(EntityTable.PendingIntent))]
    [InlineData(nameof(EntityTable.GameplayWroteTransform))]
    [InlineData(nameof(EntityTable.TakeFirstTick))]
    public void AHandleToAReusedSlotCannotReadTheNewEntitysData(string member)
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out Entity successor);

        Read(table, member, stale).Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        Read(table, member, successor).Should().NotThrow();
    }

    [Theory]
    [InlineData(nameof(EntityTable.WritableTransform))]
    [InlineData(nameof(EntityTable.WritableRender))]
    [InlineData(nameof(EntityTable.WritableIntent))]
    public void AHandleToAReusedSlotCannotWriteTheNewEntitysRowsDuringATick(string member)
    {
        EntityTable table = TableSupport.NewTable();
        // An intent needs a body; a gameplay-written transform needs none.
        BodyDesc? body = member == nameof(EntityTable.WritableIntent) ? TestWorld.Ball : null;
        Entity stale = table.ReuseSlot(out Entity successor, body);
        table.BeginTick(spawnPoolSize: 0);

        Action write = member switch
        {
            nameof(EntityTable.WritableTransform) => () => table.WritableTransform(stale) = TestWorld.At(9),
            nameof(EntityTable.WritableRender) => () => table.WritableRender(stale).Scale = new Vector3(9),
            _ => () => table.WritableIntent(stale).Force = new Vector3(9, 0, 0),
        };
        write.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);

        table.EndGameplay();
        table.GameplayWroteTransform(successor).Should().BeFalse();
        table.PendingIntent(successor).Should().Be(default(PhysicsIntent));
    }

    [Fact]
    public void AStaleHandleCannotWriteAReleasedEntitysTransform()
    {
        EntityTable table = TableSupport.NewTable();
        Entity released = table.SpawnLive();
        table.Release(released);

        Action write = () => table.SetBackTransform(released, RowAt(5));

        write.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(released);
    }

    [Fact]
    public void AStaleHandleCannotGiveTheEntityThatReusedItsSlotAComponent()
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out Entity successor);

        Action add = () => table.AddComponent(stale, new Recorder());

        add.Should().Throw<StaleEntityException>();
        table.Components(successor).Length.Should().Be(0);
    }

    [Fact]
    public void RemovingAComponentThroughAStaleHandleReturnsFalseAndLeavesTheSuccessorsComponent()
    {
        EntityTable table = TableSupport.NewTable();
        var kept = new Recorder();
        Entity stale = table.ReuseSlot(out Entity successor, components: kept);

        table.RemoveComponent(stale, kept).Should().BeFalse();

        table.Components(successor).ToArray().Should().Equal(kept);
    }

    [Fact]
    public void ReleasingAnEntityTwiceReturnsFalseTheSecondTime()
    {
        EntityTable table = TableSupport.NewTable();
        Entity entity = table.SpawnLive();

        table.Release(entity).Should().BeTrue();
        table.Release(entity).Should().BeFalse();

        table.LiveCount.Should().Be(0);
        table.AssertInvariants();
    }

    [Fact]
    public void ReleasingAStaleHandleLeavesTheEntityThatReusedItsSlotLive()
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out Entity successor);

        table.Release(stale).Should().BeFalse();

        table.IsAlive(successor).Should().BeTrue();
        table.LiveCount.Should().Be(1);
        table.AssertInvariants();
    }

    [Fact]
    public void AReservationIsReleasedOnceThroughItsOwnHandle()
    {
        EntityTable table = TableSupport.NewTable();
        Entity reservation = table.Reserve();

        table.Release(reservation).Should().BeTrue();
        table.Release(reservation).Should().BeFalse();

        table.AssertInvariants();
    }

    [Fact]
    public void AnEndedReservationCannotBeActivated()
    {
        EntityTable table = TableSupport.NewTable();
        Entity reservation = table.Reserve();
        table.Release(reservation);
        Entity next = table.Reserve();

        Action activate = () => table.Activate(reservation, default, new RenderData(), [], [], null, FrameHandle.Null);

        activate.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(reservation);
        table.IsAlive(next).Should().BeFalse();
    }

    [Fact]
    public void AStaleSpawnerCannotReserveASlotFromTheSuccessorsPool()
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out _);
        table.BeginTick(spawnPoolSize: 2);

        Action spawn = () => table.ReserveForSpawn(stale);

        spawn.Should().Throw<StaleEntityException>();
        table.EndGameplay().Should().Be(0);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("never issued")]
    [InlineData("reserved, not live yet")]
    public void HandlesThatNeverNamedALiveEntityAreStale(string kind)
    {
        EntityTable table = TableSupport.NewTable();
        table.SpawnLive();
        Entity handle = kind switch
        {
            "null" => Entity.Null,
            "never issued" => new Entity(100_000, 1),
            _ => table.Reserve(),
        };

        table.IsAlive(handle).Should().BeFalse();
        Action read = () => _ = table.FrontRow(handle).Pose;
        read.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(handle);
    }

    [Fact]
    public void TheStaleHandleErrorNamesTheHandle()
    {
        EntityTable table = TableSupport.NewTable();
        Entity stale = table.ReuseSlot(out _);

        Action read = () => _ = table.FrontRow(stale).Pose;

        read.Should().Throw<StaleEntityException>().WithMessage($"*{stale}*");
    }

    [Fact]
    public void TheLiveEntitiesOfAChunkAreTheCurrentHandlesOfItsLiveSlots()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLive(), b = table.SpawnLive(), c = table.SpawnLive();
        table.Release(b);
        Entity reserved = table.Reserve();
        reserved.Index.Should().Be(b.Index);

        Live(table.LiveEntitiesOfChunk(0)).Should().Equal(a, c);
        Live(table.LiveEntities).Should().Equal(a, c);

        table.Activate(reserved, default, new RenderData(), [], [], null, FrameHandle.Null);
        Live(table.LiveEntitiesOfChunk(0)).Should().Equal(a, reserved, c);
        Live(table.LiveEntitiesIn(1, 3)).Should().Equal(reserved, c);
    }

    [Fact]
    public void TheMovedEntitiesNeverIncludeADeadEntity()
    {
        EntityTable table = TableSupport.NewTable();
        Entity a = table.SpawnLive(), b = table.SpawnLive();
        table.TakeMoved();
        table.SetBackTransform(a, RowAt(2));
        table.SetBackTransform(b, RowAt(3));
        table.CopyDirtyRows();
        table.Release(a);
        Entity successor = table.SpawnLive();

        table.TakeMoved().ToArray().Should().Equal(b, successor);
        table.AssertInvariants();
    }

    [Fact]
    public void ReleaseHooksSeeTheEntityWhileItIsStillLive()
    {
        EntityTable table = TableSupport.NewTable();
        var seen = new List<(Entity Entity, bool Alive, int Value)>();
        EntityTable.Column<int> numbers = null!;
        numbers = table.AddColumn<int>(entity => seen.Add((entity, table.IsAlive(entity), numbers[entity])));
        Entity entity = table.SpawnLive();
        numbers[entity] = 7;

        table.Release(entity);

        seen.Should().Equal((entity, true, 7));
    }

    [Fact]
    public void AColumnRefusesAHandleToAReusedSlot()
    {
        EntityTable table = TableSupport.NewTable();
        EntityTable.Column<int> numbers = table.AddColumn<int>();
        Entity stale = table.ReuseSlot(out Entity successor);

        Action read = () => _ = numbers[stale];

        read.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        numbers[successor].Should().Be(0);
    }

    private static List<Entity> Live(EntityTable.LiveEntityEnumerator entities)
    {
        var list = new List<Entity>();
        foreach (Entity entity in entities)
            list.Add(entity);
        return list;
    }
}

/// <summary>What the router does with a stale handle.</summary>
public class RouterStaleHandleTests
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
public class TopicSetsStaleHandleTests
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
        var bay = new FrameHandle(new Entity(7, 1), 0, 1);
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
        var bay = new FrameHandle(new Entity(7, 1), 0, 1);
        topics.AddFrame(bay);
        Entity stale = table.ReuseSlot(out _);

        Action move = () => topics.MoveMember(stale, bay, out _);

        move.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        topics.MembersOf(bay).IsEmpty.Should().BeTrue();
    }
}

/// <summary>What the attachment chains do with a stale handle.</summary>
public class AttachmentsStaleHandleTests
{
    private static readonly Pose Offset = new(new Vector3(1, 0, 0), Quaternion.Identity);

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

/// <summary>What the world's public members do with a stale handle.</summary>
public class WorldStaleHandleTests
{
    private static readonly Topic Squad = Topics.Get("tests.stale.world");

    /// <summary>A world in which <paramref name="stale"/> was destroyed and <paramref name="successor"/> took its slot.</summary>
    private static World WithReusedSlot(out Entity stale, out Entity successor, out Recorder successorsRecorder)
    {
        World world = TestWorld.Create(out FrameHandle root);
        stale = world.SpawnWith(root, new Recorder());
        world.Destroy(stale);
        successorsRecorder = new Recorder();
        successor = world.Spawn(new SpawnDesc { Frame = root, Components = [successorsRecorder], Subscriptions = [Squad] });
        Assert.Equal(stale.Index, successor.Index);
        return world;
    }

    [Fact]
    public void DestroyingADeadEntityReturnsFalseAndLeavesTheEntityThatReusedItsSlotAlive()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out _);

        world.Destroy(stale).Should().BeFalse();

        world.IsAlive(successor).Should().BeTrue();
        world.AssertInvariants();
    }

    [Fact]
    public void DestroyingAnEntityTwiceReturnsFalseTheSecondTime()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity entity = world.SpawnWith(root);

        world.Destroy(entity).Should().BeTrue();
        world.Destroy(entity).Should().BeFalse();
    }

    [Fact]
    public void RemovingAComponentThroughAStaleHandleReturnsFalseAndLeavesTheSuccessorsComponent()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out Recorder recorder);

        world.RemoveComponent(stale, recorder).Should().BeFalse();

        world.GetComponents(successor).Should().Equal(recorder);
    }

    [Fact]
    public void UnsubscribingAStaleHandleReturnsFalseAndKeepsTheSuccessorsSubscription()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out Recorder recorder);

        world.Unsubscribe(stale, Squad).Should().BeFalse();

        world.Post(Event.Create(Squad, Entity.Null, new Ping { Value = 4 }));
        world.Run(2);
        recorder.PingValues().Should().Equal(4);
        world.Unsubscribe(successor, Squad).Should().BeTrue();
    }

    [Fact]
    public void AHandleToAReusedSlotCannotReadTheNewEntitysComponents()
    {
        using World world = WithReusedSlot(out Entity stale, out _, out _);

        Action all = () => world.GetComponents(stale);
        Action one = () => world.GetComponent<Recorder>(stale);

        all.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
        one.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(stale);
    }

    [Fact]
    public void AStaleHandleCannotGiveTheEntityThatReusedItsSlotAComponent()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out Recorder recorder);

        Action add = () => world.AddComponent(stale, new Recorder());

        add.Should().Throw<StaleEntityException>();
        world.GetComponents(successor).Should().Equal(recorder);
    }

    [Fact]
    public void TheSnapshotCannotReadTheEntityThatReusedASlotThroughAStaleHandle()
    {
        using World world = WithReusedSlot(out Entity stale, out _, out _);
        Snapshot snapshot = world.Snapshot;

        ((Action)(() => snapshot.PoseOf(stale))).Should().Throw<StaleEntityException>();
        ((Action)(() => snapshot.RenderOf(stale))).Should().Throw<StaleEntityException>();
        ((Action)(() => snapshot.HasBody(stale))).Should().Throw<StaleEntityException>();
    }

    [Fact]
    public void StaleHandleErrorsCanStillBeCaughtAsInvalidOperationExceptions()
    {
        using World world = WithReusedSlot(out Entity stale, out _, out _);
        InvalidOperationException? caught = null;

        try
        {
            world.GetPose(stale);
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        caught.Should().BeOfType<StaleEntityException>().Which.Message.Should().Contain(stale.ToString());
    }
}
