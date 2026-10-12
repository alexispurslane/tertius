using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>What the world's public members do with a stale handle.</summary>
public class StaleHandleWorldTests
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
        successor.Index.Should().Be(stale.Index);
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

        ((Action)(() => snapshot.PoseInOwnFrame(stale))).Should().Throw<StaleEntityException>();
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
            world.PoseInOwnFrame(stale);
        }
        catch (InvalidOperationException ex)
        {
            caught = ex;
        }

        caught.Should().BeOfType<StaleEntityException>().Which.Message.Should().Contain(stale.ToString());
    }

    // ---- Every kind of handle, every member of the world -----------------------------------------------------------
    // A handle names no live entity in five ways. The world has to answer all five the same way for each member: throw
    // StaleEntityException where it reads or writes the entity's data, and answer false where the goal already holds.

    private static readonly string[] NamesNoLiveEntity = ["null", "never issued", "from the future", "destroyed", "destroyed and reused"];

    private static readonly Topic AnyTopic = Topics.Get("tests.stale.world.members");

    /// <summary>A world with one live entity, and a handle of the named kind.</summary>
    private static World WithHandle(string kind, out Entity handle)
    {
        World world = TestWorld.Create(out FrameHandle root);
        Entity live = world.SpawnWith(root, new Recorder());
        handle = kind switch
        {
            "null" => Entity.Null,
            "never issued" => new Entity(100_000, 1),
            "from the future" => new Entity(live.Index, live.Generation + 5),
            "destroyed" => DestroyedEntity(world, root),
            "destroyed and reused" => DestroyedAndReusedEntity(world, root),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
        return world;
    }

    private static Entity DestroyedEntity(World world, FrameHandle root)
    {
        Entity entity = world.SpawnWith(root, new Recorder());
        world.Destroy(entity);
        return entity;
    }

    private static Entity DestroyedAndReusedEntity(World world, FrameHandle root)
    {
        Entity entity = DestroyedEntity(world, root);
        world.SpawnWith(root, new Recorder()).Index.Should().Be(entity.Index);
        return entity;
    }

    private static readonly Dictionary<string, Action<World, Entity>> Throwing = new()
    {
        // The first live frame is the root, so the frame is live and only the entity can make a read in a frame throw.
        [nameof(World.PoseIn)] = (world, entity) => _ = world.PoseIn(entity, world.Frames.AtDense(0)),
        [nameof(World.GetFrame)] = (world, entity) => _ = world.GetFrame(entity),
        [nameof(World.VelocityIn)] = (world, entity) => _ = world.VelocityIn(entity, world.Frames.AtDense(0)),
        [nameof(World.AngularVelocityIn)] = (world, entity) => _ = world.AngularVelocityIn(entity, world.Frames.AtDense(0)),
        [nameof(World.GetRender)] = (world, entity) => _ = world.GetRender(entity),
        [nameof(World.GetOwnedFrame)] = (world, entity) => _ = world.GetOwnedFrame(entity),
        [nameof(World.GetComponents)] = (world, entity) => _ = world.GetComponents(entity),
        [nameof(World.GetComponent)] = (world, entity) => _ = world.GetComponent<Recorder>(entity),
        [nameof(World.AddComponent)] = (world, entity) => world.AddComponent(entity, new Recorder()),
        [nameof(World.Subscribe)] = (world, entity) => world.Subscribe(entity, AnyTopic),
        [nameof(Snapshot.FrameOf)] = (world, entity) => _ = world.Snapshot.FrameOf(entity),
        [nameof(Snapshot.RenderOf)] = (world, entity) => _ = world.Snapshot.RenderOf(entity),
        [nameof(Snapshot.HasBody)] = (world, entity) => _ = world.Snapshot.HasBody(entity),
    };

    private static readonly Dictionary<string, Func<World, Entity, bool>> AnsweringFalse = new()
    {
        [nameof(World.IsAlive)] = (world, entity) => world.IsAlive(entity),
        [nameof(World.Destroy)] = (world, entity) => world.Destroy(entity),
        [nameof(World.RemoveComponent)] = (world, entity) => world.RemoveComponent(entity, new Recorder()),
        [nameof(World.Unsubscribe)] = (world, entity) => world.Unsubscribe(entity, AnyTopic),
        ["Snapshot.IsAlive"] = (world, entity) => world.Snapshot.IsAlive(entity),
        // The first live frame is the root, so the frame is live and only the entity can make the answer false.
        [nameof(Snapshot.TryGetPoseIn)] = (world, entity) => world.Snapshot.TryGetPoseIn(entity, world.Frames.AtDense(0), out _),
        [nameof(Snapshot.TryGetVelocityIn)] = (world, entity) => world.Snapshot.TryGetVelocityIn(entity, world.Frames.AtDense(0), out _),
        [nameof(Snapshot.TryGetAngularVelocityIn)] = (world, entity) => world.Snapshot.TryGetAngularVelocityIn(entity, world.Frames.AtDense(0), out _),
    };

    private static TheoryData<string, string> Cross(IEnumerable<string> members)
    {
        var data = new TheoryData<string, string>();
        foreach (string member in members)
            foreach (string kind in NamesNoLiveEntity)
                data.Add(member, kind);
        return data;
    }

    public static TheoryData<string, string> ThrowingCases() => Cross(Throwing.Keys);

    public static TheoryData<string, string> AnsweringFalseCases() => Cross(AnsweringFalse.Keys);

    [Theory]
    [MemberData(nameof(ThrowingCases))]
    public void AMemberThatReadsOrWritesAnEntityRefusesAHandleThatNamesNoLiveEntity(string member, string kind)
    {
        using World world = WithHandle(kind, out Entity handle);

        Action call = () => Throwing[member](world, handle);

        call.Should().Throw<StaleEntityException>().Which.Entity.Should().Be(handle);
        world.AssertInvariants();
    }

    [Theory]
    [MemberData(nameof(AnsweringFalseCases))]
    public void AMemberWhoseGoalAlreadyHoldsAnswersFalseForAHandleThatNamesNoLiveEntity(string member, string kind)
    {
        using World world = WithHandle(kind, out Entity handle);

        bool answer = AnsweringFalse[member](world, handle);

        answer.Should().BeFalse();
        world.AssertInvariants();
    }

    [Theory]
    [InlineData("null")]
    [InlineData("never issued")]
    [InlineData("from the future")]
    [InlineData("destroyed")]
    [InlineData("destroyed and reused")]
    public void DestroyingAHandleThatNamesNoLiveEntityDestroysNothing(string kind)
    {
        using World world = WithHandle(kind, out Entity handle);
        int liveBefore = world.Stats.LiveEntities;

        world.Destroy(handle);

        world.Stats.LiveEntities.Should().Be(liveBefore);
    }

    // ---- Queries --------------------------------------------------------------------------------------------------

    [Fact]
    public void TheListOfLiveEntitiesHasTheEntityThatTookASlotAndNotTheOneThatLeftIt()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out _);
        var live = new List<Entity>();

        world.CopyLiveEntities(live);

        live.Should().Contain(successor).And.NotContain(stale);
    }

    [Fact]
    public void TheListOfLiveEntitiesIsInSlotOrderAndAddsToTheListItIsGiven()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity first = world.SpawnWith(root), second = world.SpawnWith(root), third = world.SpawnWith(root);
        world.Destroy(second);
        Entity fourth = world.SpawnWith(root);
        var live = new List<Entity> { Entity.Null };

        world.CopyLiveEntities(live);

        // Slot 0 is the root frame's owner. The fourth entity took the slot that the second one freed.
        live.Skip(1).Should().Equal(root.Entity, first, fourth, third);
        live[0].Should().Be(Entity.Null);
    }

    // ---- Messages -------------------------------------------------------------------------------------------------

    [Fact]
    public void AMessagePostedToADestroyedEntitysHandleDoesNotReachTheEntityThatTookItsSlot()
    {
        using World world = WithReusedSlot(out Entity stale, out _, out Recorder successorsRecorder);

        world.Post(Event.Create(stale, Entity.Null, new Ping { Value = 1 }));
        world.Run(3);

        successorsRecorder.Seen.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AMessageSentToADestroyedEntitysHandleDoesNotReachTheEntityThatTookItsSlot()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out Recorder successorsRecorder);
        world.SpawnWith(world.GetFrame(successor), SendOnce.Of(stale, new Ping { Value = 1 }));

        world.Run(3);

        successorsRecorder.Seen.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AMessageToTheLiveEntityStillArrivesWhileTheStaleHandleIsIgnored()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out Recorder successorsRecorder);
        world.SpawnWith(world.GetFrame(successor), SendOnce.Of(stale, new Ping { Value = 1 }), SendOnce.Of(successor, new Ping { Value = 2 }));

        world.Run(3);

        successorsRecorder.PingValues().Should().Equal(2);
    }

    [Fact]
    public void AMessageThatWasSentToAnEntityThatDiesInTheSameTickIsNotDeliveredToTheEntityThatTakesItsSlot()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity doomed = world.SpawnWith(root, Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy()));
        world.SpawnWith(root, SendOnce.Of(doomed, new Ping { Value = 1 }));
        world.Tick();
        world.IsAlive(doomed).Should().BeFalse();
        var heir = new Recorder();
        Entity successor = world.SpawnWith(root, heir);
        successor.Index.Should().Be(doomed.Index);

        world.Run(3);

        heir.Seen.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    [Fact]
    public void AMessageAlreadyWaitingForAnEntityThatIsDestroyedIsNotDeliveredToTheEntityThatTakesItsSlot()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity doomed = world.SpawnWith(root, new Recorder());
        world.SpawnWith(root, SendOnce.Of(doomed, new Ping { Value = 1 }));
        world.Tick();
        // The Ping has been routed to the doomed entity's inbox, and its next tick has not run yet.
        world.Destroy(doomed);
        var heir = new Recorder();
        Entity successor = world.SpawnWith(root, heir);
        successor.Index.Should().Be(doomed.Index);

        world.Run(3);

        heir.Seen.Should().BeEmpty();
        world.Errors.Should().BeEmpty();
    }

    // ---- Inside a tick --------------------------------------------------------------------------------------------

    [Fact]
    public void AComponentThatKeptAHandleToADeadEntityFindsItDeadAndNotReplacedByTheEntityInItsSlot()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out _);
        bool staleIsAlive = true, successorIsAlive = false, stalePoseFound = true;
        world.SpawnWith(world.GetFrame(successor), Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            Snapshot snapshot = self.Snapshot;
            staleIsAlive = snapshot.IsAlive(stale);
            successorIsAlive = snapshot.IsAlive(successor);
            stalePoseFound = snapshot.TryGetPoseIn(stale, self.Frame, out _);
        }));

        world.Tick();

        (staleIsAlive, successorIsAlive, stalePoseFound).Should().Be((false, true, false));
    }

    [Fact]
    public void AComponentThatReadsADeadEntitysPoseGetsAnErrorThatTheSchedulerReports()
    {
        using World world = WithReusedSlot(out Entity stale, out Entity successor, out _);
        world.SpawnWith(world.GetFrame(successor), Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Snapshot.PoseInOwnFrame(stale)));

        world.Tick();

        world.Errors.Should().ContainSingle().Which.Should().Contain(nameof(StaleEntityException)).And.Contain(stale.ToString());
    }
}
