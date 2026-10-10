namespace Tertius.Simulation.Tests;

public class MessagingTests
{
    private static Lambda Sender(Func<long, (Target Target, int Value)[]> plan) =>
        new(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            foreach ((Target target, int value) in plan(self.Tick))
                outbox.Send(target, new Ping { Value = value });
        });

    [Fact]
    public void AMessageArrivesOnTheNextTick()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var inbox = new Recorder();
        Entity b = world.SpawnWith(root, inbox);
        world.SpawnWith(root, Sender(t => [(b, (int)t)]));

        world.Run(4);

        Assert.Equal([(1L, 0), (2L, 1), (3L, 2)], inbox.Pings());
        Assert.All(inbox.Seen, s => Assert.NotEqual(Entity.Null, s.Event.Sender));
    }

    [Fact]
    public void DirectTopicAndBroadcastDelivery()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Topic topic = Topics.Get("tests.messaging.alarm");
        var subscribed = new Recorder();
        var other = new Recorder();
        var deaf = new Recorder(EventMask.Of<Heal>());
        Entity a = world.Spawn(new SpawnDesc { Frame = root, Components = [subscribed], Subscriptions = [topic] });
        Entity b = world.SpawnWith(root, other);
        world.SpawnWith(root, deaf);
        world.SpawnWith(root, Sender(t => t == 0 ? [(b, 1), (topic, 2), (new Broadcast(), 3)] : []));

        world.Run(2);

        Assert.Equal([2, 3], subscribed.PingValues());
        Assert.Equal([1, 3], other.PingValues());
        Assert.Equal(0, deaf.Runs);
        Assert.True(world.IsAlive(a));
    }

    [Fact]
    public void AFrameTopicReachesEveryEntityInThatFrameOnly()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle shipA = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(100), AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static) }));
        FrameHandle shipB = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(-100), AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static) }));
        var inA1 = new Recorder();
        var inA2 = new Recorder();
        var inB = new Recorder();
        var inRoot = new Recorder();
        world.SpawnWith(shipA, inA1);
        world.SpawnWith(shipA, inA2);
        world.SpawnWith(shipB, inB);
        world.SpawnWith(root, inRoot);
        world.SpawnWith(root, Sender(t => t == 0 ? [(shipA.Topic, 7)] : []));

        world.Run(2);

        Assert.Equal([7], inA1.PingValues());
        Assert.Equal([7], inA2.PingValues());
        Assert.Empty(inB.Seen);
        Assert.Empty(inRoot.Seen);
    }

    [Fact]
    public void FrameTopicMembershipFollowsAHandOff()
    {
        var physics = new FakePhysics { Integrate = false };
        using World world = TestWorld.Create(out FrameHandle root, physics: physics);
        FrameHandle a = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(0), AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static) }));
        FrameHandle b = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, Pose = TestWorld.At(1000), AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static) }));
        var traveller = new Recorder(EventMask.Of<Ping>().And<FrameChanged>());
        Entity body = world.Spawn(new SpawnDesc { Frame = a, Body = TestWorld.Ball, Components = [traveller] });
        var deaf = new Recorder(EventMask.Of<Ping>());
        Entity bystander = world.Spawn(new SpawnDesc { Frame = a, Body = TestWorld.Ball, Components = [deaf] });
        physics.OnStep = (context, output) =>
        {
            if (context.Tick == 1)
            {
                output.HandOffs.Add(new HandOff(body, a, b, TestWorld.At(5), new System.Numerics.Vector3(1, 0, 0), default));
                output.HandOffs.Add(new HandOff(bystander, a, b, TestWorld.At(6), default, default));
            }
        };
        world.SpawnWith(root, Sender(t => [(a.Topic, (int)(t * 10)), (b.Topic, (int)(t * 10 + 1))]));

        world.Run(4);

        Assert.Equal(b, world.GetFrame(body));
        Assert.Equal(5, world.GetPose(body).Position.X);
        Assert.Equal(1, world.GetVelocity(body).X);
        // The hand-off applies before routing at the sync of tick 1, so from the messages of tick 1 on, topic B reaches it.
        Assert.Equal([0, 11, 21], traveller.PingValues());
        FrameChanged changed = traveller.Seen.Single(s => s.Event.Is<FrameChanged>()).Event.As<FrameChanged>();
        Assert.Equal(a, changed.From);
        Assert.Equal(b, changed.To);
        Assert.Equal(2, traveller.Seen.Single(s => s.Event.Is<FrameChanged>()).Tick);
        // FrameChanged is delivered only to entities that want it.
        Assert.DoesNotContain(deaf.Seen, s => s.Event.Is<FrameChanged>());
        Assert.Empty(world.Errors);
    }

    [Fact]
    public void DelayedSendsJoinTheRoutingOfALaterTick()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var inbox = new Recorder();
        Entity target = world.SpawnWith(root, inbox);
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick != 0)
                return;
            outbox.SendAfter(3, target, new Ping { Value = 3 });
            outbox.SendAfter(1, target, new Ping { Value = 1 });
            outbox.Send(target, new Ping { Value = 0 });
            outbox.SendAfter(1, target, new Ping { Value = 11 });
        }));

        world.Run(6);

        Assert.Equal([(1L, 0), (2L, 1), (2L, 11), (4L, 3)], inbox.Pings());
    }

    [Fact]
    public void ADelayOfZeroIsRejected()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Entity target = world.SpawnWith(root, new Recorder());
        world.SpawnWith(root, new Lambda(EventMask.Of<Tick>(),
            (Self self, scoped ref EventList events, scoped ref Outbox outbox) => outbox.SendAfter(0, target, new Ping())));
        world.Tick();
        Assert.Contains(nameof(ArgumentOutOfRangeException), Assert.Single(world.Errors));
    }

    [Fact]
    public void InboxOrderIsChunkOrderThenSendOrderWhateverTheThreads()
    {
        for (int run = 0; run < 3; run++)
        {
            using World world = TestWorld.Create(out FrameHandle root, maxDop: 8, chunkSize: 2);
            var inbox = new Recorder();
            Entity target = world.SpawnWith(root, inbox);
            for (int s = 1; s <= 4; s++)
            {
                int sender = s;
                world.SpawnWith(root, Sender(t => t == 0 ? [(target, sender * 10), (target, sender * 10 + 1)] : []));
            }
            world.Post(Event.Create(target, Entity.Null, new Ping { Value = 999 }));

            world.Run(2);

            Assert.Equal([10, 11, 20, 21, 30, 31, 40, 41, 999], inbox.PingValues());
        }
    }

    [Fact]
    public void PostIsThreadSafeAndJoinsTheNextRouting()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var inbox = new Recorder();
        Entity target = world.SpawnWith(root, inbox);

        Parallel.For(0, 4, t =>
        {
            for (int i = 0; i < 250; i++)
                world.Post(Event.Create(target, Entity.Null, new Ping { Value = t * 1000 + i }));
        });
        world.Tick();
        Assert.Equal(0, inbox.Runs);
        world.Tick();

        Assert.Equal(1, inbox.Runs);
        List<int> values = inbox.PingValues();
        Assert.Equal(1000, values.Count);
        Assert.Equal(1000, values.Distinct().Count());
        Assert.Throws<ArgumentException>(() => world.Post(default));
    }

    [Fact]
    public void SelfSubscribeAndUnsubscribeTakeEffectAtTheSyncPoint()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Topic topic = Topics.Get("tests.messaging.news");
        var inbox = new Recorder(EventMask.Of<Ping>().And<Tick>());
        var manager = new Lambda(EventMask.Of<Tick>(), (Self self, scoped ref EventList events, scoped ref Outbox outbox) =>
        {
            if (self.Tick == 0)
                self.Subscribe(topic);
            if (self.Tick == 2)
                self.Unsubscribe(topic);
        });
        world.SpawnWith(root, manager, inbox);
        world.SpawnWith(root, Sender(t => [(topic, (int)t)]));

        world.Run(5);

        // Subscriptions apply before routing, so the message sent during the subscribing tick already arrives.
        Assert.Equal([(1L, 0), (2L, 1)], inbox.Pings());
    }

    [Fact]
    public void WorldSubscribeOutsideATick()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        Topic topic = Topics.Get("tests.messaging.weather");
        var inbox = new Recorder();
        Entity e = world.SpawnWith(root, inbox);
        world.Subscribe(e, topic);
        world.Post(Event.Create(topic, Entity.Null, new Ping { Value = 1 }));
        world.Run(2);
        world.Unsubscribe(e, topic);
        world.Post(Event.Create(topic, Entity.Null, new Ping { Value = 2 }));
        world.Run(2);
        Assert.Equal([1], inbox.PingValues());
    }

    [Fact]
    public void AnEntityOutsideAFrameCanSubscribeToItsTopicAndNobodyHearsTwice()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        FrameHandle bay = world.GetOwnedFrame(world.Spawn(new SpawnDesc { Frame = root, AsFrame = new FrameSpec(FrameBounds.Sphere(50), MotionDriver.Static) }));
        var observer = new Recorder();
        var member = new Recorder();
        world.Spawn(new SpawnDesc { Frame = root, Components = [observer], Subscriptions = [bay.Topic] });
        // Subscribing to its own frame's topic is allowed and changes nothing.
        world.Spawn(new SpawnDesc { Frame = bay, Components = [member], Subscriptions = [bay.Topic] });
        world.SpawnWith(root, Sender(t => t == 0 ? [(bay.Topic, 9)] : []));

        world.Run(2);

        Assert.Equal([9], observer.PingValues());
        Assert.Equal([9], member.PingValues());
    }

    [Fact]
    public void ASlotReusedOutsideATickDoesNotInheritTheOldInbox()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var oldLog = new Recorder();
        Entity old = world.SpawnWith(root, oldLog);
        world.Post(Event.Create(old, Entity.Null, new Ping { Value = 1 }));
        world.Tick();

        // The Ping now sits in the old entity's inbox. Destroy it and reuse its slot before the next tick.
        world.Destroy(old);
        var newLog = new Recorder();
        Entity reused = world.SpawnWith(root, newLog);
        Assert.Equal(old.Index, reused.Index);
        world.Post(Event.Create(reused, Entity.Null, new Ping { Value = 2 }));
        world.Run(2);

        Assert.Empty(oldLog.Seen);
        Assert.Equal([2], newLog.PingValues());
    }

    [Fact]
    public void MessagesToAStaleHandleAreDropped()
    {
        using World world = TestWorld.Create(out FrameHandle root);
        var first = new Recorder();
        Entity old = world.SpawnWith(root, first);
        world.Destroy(old);
        var second = new Recorder();
        Entity reused = world.SpawnWith(root, second);
        Assert.Equal(old.Index, reused.Index);
        Assert.NotEqual(old.Generation, reused.Generation);

        world.Post(Event.Create(old, Entity.Null, new Ping { Value = 1 }));
        world.Run(2);

        Assert.Empty(first.Seen);
        Assert.Empty(second.Seen);
    }
}
