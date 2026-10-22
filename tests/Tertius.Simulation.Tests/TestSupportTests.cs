using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

// Tests of the shared test support itself. Every other test file builds on these helpers, so a helper that does not do
// what its comment says would send the author of a test looking for a bug in the engine that is not there.
// They also show how each helper is used.

/// <summary>The components in the Components folder: what each one does to the entity it is on.</summary>
public class SharedComponentTests
{
    private static Event Message<T>(Entity target, T payload) where T : unmanaged, IEventPayload =>
        Event.Create(target, Entity.Null, payload);

    [Fact]
    public void ARecorderKeepsTheTickTheEntityAndTheEventTypesOfEachRun()
    {
        using var t = new TestWorld();
        var recorder = new Recorder(EventMask.Of<Tick>().And<Spawned>().And<Ping>());
        Entity entity = t.SpawnWith(recorder);
        t.World.Post(Message(entity, new Ping { Value = 1 }));

        t.Run(2);

        recorder.Runs.Should().Be(2);
        recorder.History.Select(run => (run.Tick, run.Entity)).Should().Equal((0L, entity), (1L, entity));
        // The scheduler puts Tick first, then Spawned on the first tick, then the messages.
        recorder.History[0].EventTypes.Should().Equal(typeof(Tick), typeof(Spawned));
        recorder.History[1].EventTypes.Should().Equal(typeof(Tick), typeof(Ping));
    }

    [Fact]
    public void ARecorderListsPingsWithTheTickOnWhichTheyWereHandled()
    {
        using var t = new TestWorld();
        var recorder = new Recorder();
        Entity entity = t.SpawnWith(recorder);
        t.World.Post(Message(entity, new Ping { Value = 5 }));
        t.World.Post(Message(entity, new Ping { Value = 6 }));

        t.Run(3);

        recorder.PingValues().Should().Equal(5, 6);
        recorder.Pings().Should().Equal((1L, 5), (1L, 6));
    }

    [Fact]
    public void SendOnceSendsOnTheFirstTickAtOrAfterItsTickAndNeverAgain()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        SendOnce sender = SendOnce.Of(target, new Ping { Value = 7 }, onTick: 2);
        t.SpawnWith(sender);

        t.Run(6);

        // Sent during tick 2, so it arrives during tick 3.
        inbox.Pings().Should().Equal((3L, 7));
        sender.Sent.Should().BeTrue();
    }

    [Fact]
    public void SendOnceCanSendAnyPayloadToAnyTarget()
    {
        using var t = new TestWorld();
        Entity victim = t.SpawnWith();
        t.SpawnWith(SendOnce.Of(victim, new Destroy()));

        t.Run(2);

        t.World.IsAlive(victim).Should().BeFalse();
    }

    [Fact]
    public void SendEverySendsEveryNthTickFromItsStartTick()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        SendEvery sender = SendEvery.Of(target, tick => new Ping { Value = (int)tick }, every: 2, from: 1);
        t.SpawnWith(sender);

        t.Run(7);

        // Sent during ticks 1, 3 and 5, with the number of the sending tick, so received during ticks 2, 4 and 6.
        inbox.Pings().Should().Equal((2L, 1), (4L, 3), (6L, 5));
        sender.Sends.Should().Be(3);
    }

    [Fact]
    public void SendEverySendsTheSamePayloadEachTime()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(SendEvery.Of(target, new Ping { Value = 9 }));

        t.Run(4);

        inbox.Pings().Should().Equal((1L, 9), (2L, 9), (3L, 9));
    }

    [Fact]
    public void SendEveryRefusesAnIntervalBelowOneTick()
    {
        Action make = () => SendEvery.Of(Entity.Null, new Ping(), every: 0);

        make.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ArmorTakesItsRatingOffEachDamageAndDropsADamageThatNothingIsLeftOf()
    {
        using var t = new TestWorld();
        var health = new Health();
        var armor = new Armor(rating: 3);
        Entity entity = t.SpawnWith(armor, health);
        t.World.Post(Message(entity, new Damage { Amount = 10 }));
        t.World.Post(Message(entity, new Damage { Amount = 2 }));

        t.Run(2);

        // 10 - 3 = 7 reaches the health. The 2 is absorbed whole, so the health never sees it.
        health.Current.Should().Be(93);
        armor.Absorbed.Should().Be(5);
    }

    [Fact]
    public void HealthEndsTheEntityWhenDamageBringsItToZero()
    {
        using var t = new TestWorld();
        Entity entity = t.SpawnWith(new Health { Max = 10, Current = 10 });
        t.World.Post(Message(entity, new Damage { Amount = 10 }));

        t.Run(2);

        t.World.IsAlive(entity).Should().BeFalse();
    }

    [Fact]
    public void AnInvulnerableEntityOutlivesTheHealthThatReachedZero()
    {
        using var t = new TestWorld();
        var health = new Health { Max = 10, Current = 10 };
        var shield = new Invulnerable();
        Entity entity = t.SpawnWith(health, shield);
        t.World.Post(Message(entity, new Damage { Amount = 15 }));

        t.Run(3);

        t.World.IsAlive(entity).Should().BeTrue();
        health.Current.Should().Be(-5);
        shield.Blocked.Should().Be(1);
    }

    [Fact]
    public void AnInvulnerableEntityRefusesADestroyMessage()
    {
        using var t = new TestWorld();
        var shield = new Invulnerable();
        Entity entity = t.SpawnWith(shield);
        t.World.Post(Message(entity, new Destroy()));

        t.Run(2);

        t.World.IsAlive(entity).Should().BeTrue();
        shield.Blocked.Should().Be(1);
    }

    [Fact]
    public void HealRestoresHitPointsUpToTheMaximum()
    {
        using var t = new TestWorld();
        var health = new Health { Max = 100, Current = 40 };
        Entity entity = t.SpawnWith(health);
        t.World.Post(Message(entity, new Heal { Amount = 30 }));

        t.Run(2);
        health.Current.Should().Be(70);

        t.World.Post(Message(entity, new Heal { Amount = 50 }));
        t.Run(2);
        health.Current.Should().Be(100);
    }

    [Fact]
    public void ASpawnerWithNoIntervalSpawnsItsChildrenOnceAtTheSpawnersPose()
    {
        using var t = new TestWorld();
        var spawner = new Spawner(() => [new Recorder(EventMask.Of<Spawned>())], count: 2, onTick: 1);
        t.SpawnAt(new(4, 0, 0), spawner);

        t.Run(4);

        spawner.Children.Should().HaveCount(2);
        spawner.Children.Should().OnlyContain(child => t.World.IsAlive(child));
        spawner.Children.Select(child => t.World.PoseInOwnFrame(child).Position.X).Should().Equal(4, 4);
        spawner.Children.Select(t.World.GetFrame).Should().OnlyContain(frame => frame == t.Root);
        // Each child has an instance of its own, and it ran once, on the tick after the spawn.
        List<Recorder> recorders = [.. spawner.Children.Select(child => t.World.GetComponent<Recorder>(child)!)];
        recorders[0].Should().NotBeSameAs(recorders[1]);
        recorders.Select(recorder => recorder.Seen.Single().Tick).Should().Equal(2L, 2L);
    }

    [Fact]
    public void ASpawnerWithAnIntervalSpawnsAgainAndAgain()
    {
        using var t = new TestWorld();
        var spawner = new Spawner(every: 2);
        t.SpawnWith(spawner);

        t.Run(5);

        // Ticks 0, 2 and 4.
        spawner.Children.Should().HaveCount(3);
    }

    [Fact]
    public void ASubscriberHearsATopicBetweenItsSubscribeTickAndItsUnsubscribeTick()
    {
        using var t = new TestWorld();
        Topic topic = Topics.Get("tests.support.news");
        var inbox = new Recorder();
        t.SpawnWith(new Subscriber(topic, subscribeOnTick: 1, unsubscribeOnTick: 3), inbox);
        t.SpawnWith(SendEvery.Of(topic, tick => new Ping { Value = (int)tick }));

        t.Run(7);

        // A change applies at the sync point of the tick that asks for it. So the message sent during tick 1 arrives,
        // the one sent during tick 0 does not, and the one sent during tick 3 does not.
        inbox.Pings().Should().Equal((2L, 1), (3L, 2));
    }

    [Fact]
    public void ALambdaRunsOnlyWhenItsEventsAreInTheListAndCountsItsRuns()
    {
        using var t = new TestWorld();
        Lambda onTick = Lambda.OnTick((self, scoped ref events, scoped ref outbox) => { });
        var onPing = new Lambda(EventMask.Of<Ping>(), (self, scoped ref events, scoped ref outbox) => { });
        t.SpawnWith(onTick, onPing);

        t.Run(3);

        onTick.Runs.Should().Be(3);
        onPing.Runs.Should().Be(0);
    }
}

/// <summary><see cref="TestWorld"/>: the settings it promises and the spawn helpers.</summary>
public class TestWorldTests
{
    [Fact]
    public void ATestWorldRunsOnOneThreadWithDebugChecksAndNoPhysics()
    {
        using var t = new TestWorld();

        t.World.Options.MaxDegreeOfParallelism.Should().Be(1);
        t.World.Options.DebugChecks.Should().BeTrue();
        t.World.Physics.Should().BeNull();
    }

    [Fact]
    public void ATestWorldKeepsTheBackendItIsGiven()
    {
        using TestWorld t = TestWorld.OnJolt(out FaultInjectingBackend faults);

        t.World.Physics.Should().BeSameAs(faults);
    }

    [Fact]
    public void ATestWorldOnJoltStepsRealPhysicsOnABackendThatTheWorldDisposes()
    {
        using TestWorld t =
            TestWorld.OnJolt(out FaultInjectingBackend faults, configure: options => options.TickRate = 32);
        Entity ball = t.World.Spawn(new()
            { Frame = t.Root, LinearVelocity = new(3, 0, 0), Body = WorldTesting.Ball() });

        t.Run(2);

        faults.Steps.Select(step => step.Time).Should().Equal(0, 1.0 / 32);
        t.World.Options.DisposePhysics.Should().BeTrue();
        t.World.PoseInOwnFrame(ball).Position.X.Should()
            .BeApproximately(0.1875, 1e-6, "Jolt moved the ball by 3 m/s over two steps of 1/32 s");
    }

    [Fact]
    public void AnEditOfOneTickTouchesTheOutputOfThatTickOnly()
    {
        using TestWorld t = TestWorld.OnJolt(out FaultInjectingBackend faults);
        t.World.Spawn(new() { Frame = t.Root, LinearVelocity = new(3, 0, 0), Body = WorldTesting.Ball() });
        var ticksEdited = new List<long>();
        faults.EditOutputOfTick(1, output => ticksEdited.Add(faults.Steps[^1].Tick));

        t.Run(3);

        ticksEdited.Should().Equal(1L);
    }

    [Fact]
    public void AnOptionsCallbackChangesTheOptionsBeforeTheWorldExists()
    {
        using var t = new TestWorld(configure: options => options.SpawnPoolSize = 3);

        t.World.Options.SpawnPoolSize.Should().Be(3);
    }

    [Fact]
    public void SpawnWithPutsTheEntityInTheRootFrameAtTheOrigin()
    {
        using var t = new TestWorld();

        Entity entity = t.SpawnWith(new Recorder());

        t.World.GetFrame(entity).Should().Be(t.Root);
        t.World.PoseInOwnFrame(entity).Position.Should().Be(Double3.Zero);
        t.World.GetComponent<Recorder>(entity).Should().NotBeNull();
    }

    [Fact]
    public void SpawnAtPutsTheEntityAtThePositionInTheRootFrame()
    {
        using var t = new TestWorld();

        Entity entity = t.SpawnAt(new(1, 2, 3));

        t.World.PoseInOwnFrame(entity).Position.Should().Be(new Double3(1, 2, 3));
    }

    [Fact]
    public void AStaticFrameIsAChildOfTheRootUnlessAParentIsGiven()
    {
        using var t = new TestWorld();

        FrameHandle bay = t.SpawnStaticFrame(TestWorld.At(100), radius: 50);
        FrameHandle shelf = t.SpawnStaticFrame(TestWorld.At(5), parent: bay);

        t.World.Frames.Parent(bay).Should().Be(t.Root);
        t.World.Frames.Parent(shelf).Should().Be(bay);
        t.World.Frames.LocalPose(bay).Position.Should().Be(new Double3(100, 0, 0));
    }

    [Fact]
    public void RunTicksTheWorldThatManyTimes()
    {
        using var t = new TestWorld();

        t.Run(5);

        t.World.TickNumber.Should().Be(5);
    }

    [Fact]
    public void TheStaticCreateGivesTheSameWorldAsTheInstance()
    {
        using World world = TestWorld.Create(out FrameHandle root, debugChecks: false);

        world.Options.MaxDegreeOfParallelism.Should().Be(1);
        world.Options.DebugChecks.Should().BeFalse();
        world.Frames.IsAlive(root).Should().BeTrue();
    }
}
