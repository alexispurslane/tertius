using System.Numerics;
using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Topic messages through a whole world. A named topic reaches its subscribers. A frame is a topic too: it reaches the
/// entities in the frame and the entities that subscribed to it from elsewhere. A change of subscription or of frame
/// applies at the sync point, before the routing.
/// </summary>
public sealed class MessagingTopicTests
{
    private static Topic Named(string name) => Topics.Get($"tests.messagingtopic.{name}");

    private static Entity SpawnSubscribed(TestWorld t, Topic topic, params IComponent[] components) =>
        t.World.Spawn(
            new()
            {
                Frame = t.Root,
                Components = components,
                Subscriptions = [topic],
            }
        );

    // ---- Named topics ---------------------------------------------------------------------------------------------

    [Fact]
    public void ATopicMessageReachesItsSubscribersThatWantItsTypeAndNobodyElse()
    {
        using var t = new TestWorld();
        Topic alarm = Named("alarm");
        var subscribed = new Recorder();
        var deafSubscriber = new Recorder(EventMask.Of<Heal>());
        var outsider = new Recorder();
        SpawnSubscribed(t, alarm, subscribed);
        SpawnSubscribed(t, alarm, deafSubscriber);
        t.SpawnWith(outsider);
        t.SpawnWith(SendOnce.Of(target: alarm, payload: new Ping { Value = 2 }));

        t.Run(2);

        subscribed.PingValues().Should().Equal(2);
        deafSubscriber.Runs.Should().Be(0);
        outsider.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AnEntitySubscribedToTwoTopicsHearsBothInTheOrderTheyWereSent()
    {
        using var t = new TestWorld();
        Topic first = Named("first");
        Topic second = Named("second");
        var inbox = new Recorder();
        t.World.Spawn(
            new()
            {
                Frame = t.Root,
                Components = [inbox],
                Subscriptions = [first, second],
            }
        );
        t.SpawnWith(
            SendOnce.Of(target: first, payload: new Ping { Value = 1 }),
            SendOnce.Of(target: second, payload: new Ping { Value = 2 }),
            SendOnce.Of(target: first, payload: new Ping { Value = 3 })
        );

        t.Run(2);

        inbox.PingValues().Should().Equal(1, 2, 3);
    }

    [Fact]
    public void ATopicWithNoSubscribersDeliversToNobody()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        t.SpawnWith(inbox);
        t.SpawnWith(SendOnce.Of(target: Named("empty"), payload: new Ping { Value = 1 }));

        t.Run(1);

        // The counters are those of the last routing, which is the one that handled the message.
        t.World.Stats.RoutedEvents.Should().Be(1);
        t.World.Stats.Deliveries.Should().Be(0);
        t.Run(1);
        inbox.Seen.Should().BeEmpty();
    }

    [Fact]
    public void SubscribingTwiceIsHarmlessAndUnsubscribingKeepsTheOtherTopics()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;
        Topic first = Named("twice-first");
        Topic second = Named("twice-second");
        var inbox = new Recorder();
        Entity e = t.World.Spawn(
            new()
            {
                Frame = t.Root,
                Components = [inbox],
                Subscriptions = [first, first],
            }
        );
        t.World.Subscribe(e, first);
        t.World.Subscribe(e, second);
        t.World.Unsubscribe(e, first);
        t.World.Unsubscribe(e, first);

        t.World.Post(Event.Create(target: first, sender: Entity.Null, payload: new Ping { Value = 1 }));
        t.World.Post(Event.Create(target: second, sender: Entity.Null, payload: new Ping { Value = 2 }));
        t.Run(2);

        inbox.PingValues().Should().Equal(2);
        log.Should().BeEmpty();
    }

    // ---- Changing a subscription -------------------------------------------------------------------------------------

    [Fact]
    public void ASubscriptionAskedForDuringATickAppliesBeforeThatTicksRouting()
    {
        using var t = new TestWorld();
        Topic news = Named("news");
        var inbox = new Recorder();
        t.SpawnWith(new Subscriber(topic: news, subscribeOnTick: 0, unsubscribeOnTick: 2), inbox);
        t.SpawnWith(SendEvery.Of(target: news, payloadAt: tick => new Ping { Value = (int)tick }));

        t.Run(5);

        // The message sent on tick 0 already arrives, because the subscription applied at the end of tick 0.
        // The one sent on tick 2 does not, because the unsubscription applied at the end of tick 2.
        inbox.Pings().Should().Equal((1L, 0), (2L, 1));
    }

    [Fact]
    public void AnEntityThatSubscribesAndThenUnsubscribesInOneRunEndsUnsubscribed()
    {
        using var t = new TestWorld();
        Topic news = Named("flip-off");
        var inbox = new Recorder();
        t.SpawnWith(
            inbox,
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick != 0)
                        return;
                    self.Subscribe(news);
                    self.Unsubscribe(news);
                }
            )
        );
        t.SpawnWith(SendEvery.Of(target: news, payloadAt: tick => new Ping { Value = (int)tick }));

        t.Run(3);

        // The later request wins.
        inbox.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AnEntityThatUnsubscribesAndThenSubscribesInOneRunEndsSubscribed()
    {
        using var t = new TestWorld();
        Topic news = Named("flip-on");
        var inbox = new Recorder();
        t.SpawnWith(
            inbox,
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick != 0)
                        return;
                    self.Unsubscribe(news);
                    self.Subscribe(news);
                }
            )
        );
        t.SpawnWith(SendEvery.Of(target: news, payloadAt: tick => new Ping { Value = (int)tick }));

        t.Run(3);

        inbox.Pings().Should().Equal((1L, 0), (2L, 1));
    }

    [Fact]
    public void AnEntityCanBeSubscribedOutsideATickAndHearsFromTheNextRouting()
    {
        using var t = new TestWorld();
        Topic weather = Named("weather");
        var inbox = new Recorder();
        Entity e = t.SpawnWith(inbox);

        t.World.Subscribe(e, weather);
        t.World.Post(Event.Create(target: weather, sender: Entity.Null, payload: new Ping { Value = 1 }));
        t.Run(2);

        inbox.PingValues().Should().Equal(1);
    }

    [Fact]
    public void AnEntityCanBeUnsubscribedOutsideATickAndStopsHearing()
    {
        using var t = new TestWorld();
        Topic weather = Named("weather-off");
        var inbox = new Recorder();
        Entity e = SpawnSubscribed(t, weather, inbox);

        t.World.Unsubscribe(e, weather).Should().BeTrue();
        t.World.Post(Event.Create(target: weather, sender: Entity.Null, payload: new Ping { Value = 1 }));
        t.Run(2);

        inbox.Seen.Should().BeEmpty();
    }

    // ---- Frame topics --------------------------------------------------------------------------------------------------

    [Fact]
    public void AFrameTopicReachesEveryEntityInThatFrameOnly()
    {
        using var t = new TestWorld();
        FrameHandle shipA = t.SpawnStaticFrame(TestWorld.At(100), radius: 50);
        FrameHandle shipB = t.SpawnStaticFrame(TestWorld.At(-100), radius: 50);
        var inA1 = new Recorder();
        var inA2 = new Recorder();
        var inB = new Recorder();
        var inRoot = new Recorder();
        t.World.SpawnWith(shipA, inA1);
        t.World.SpawnWith(shipA, inA2);
        t.World.SpawnWith(shipB, inB);
        t.SpawnWith(inRoot);
        t.SpawnWith(SendOnce.Of(target: shipA.Topic, payload: new Ping { Value = 7 }));

        t.Run(2);

        inA1.PingValues().Should().Equal(7);
        inA2.PingValues().Should().Equal(7);
        inB.Seen.Should().BeEmpty();
        inRoot.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AnEntityOutsideAFrameCanSubscribeToItsTopicAndAMemberThatSubscribesHearsOnce()
    {
        using var t = new TestWorld();
        FrameHandle bay = t.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        var observer = new Recorder();
        var member = new Recorder();
        t.World.Spawn(
            new()
            {
                Frame = t.Root,
                Components = [observer],
                Subscriptions = [bay.Topic],
            }
        );
        // Subscribing to its own frame's topic is allowed and changes nothing.
        t.World.Spawn(
            new()
            {
                Frame = bay,
                Components = [member],
                Subscriptions = [bay.Topic],
            }
        );
        t.SpawnWith(SendOnce.Of(target: bay.Topic, payload: new Ping { Value = 9 }));

        t.Run(2);

        observer.PingValues().Should().Equal(9);
        member.PingValues().Should().Equal(9);
    }

    [Fact]
    public void UnsubscribingFromTheTopicOfYourOwnFrameDoesNotSilenceIt()
    {
        using var t = new TestWorld();
        FrameHandle bay = t.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        var inbox = new Recorder();
        // Membership is the entity's, and a subscription is an extra. Unsubscribing removes only the extra.
        t.World.SpawnWith(
            bay,
            inbox,
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Unsubscribe(self.Frame.Topic))
        );
        t.SpawnWith(SendEvery.Of(target: bay.Topic, payloadAt: tick => new Ping { Value = (int)tick }));

        t.Run(4);

        inbox.Pings().Should().Equal((1L, 0), (2L, 1), (3L, 2));
    }

    [Fact]
    public void ABodyHandedOffToAnotherFrameHearsTheNewFramesTopicFromThatSyncPointOn()
    {
        using var t = TestWorld.OnJolt(faults: out FaultInjectingBackend faults);
        FrameHandle a = t.Root;
        FrameHandle b = t.SpawnStaticFrame(TestWorld.At(1000), radius: 50);
        var traveller = new Recorder(EventMask.Of<Ping>().And<FrameChanged>());
        // Jolt hands the traveller off from the root into the frame b in the step of tick 1. The stayer rests in the root.
        Entity body = Scenes.BallApproachingBubble(world: t.World, from: a, into: b, tick: 1, traveller);
        var stayer = new Recorder(EventMask.Of<Ping>());
        t.World.Spawn(
            new()
            {
                Frame = a,
                Pose = TestWorld.At(0, 20),
                Body = WorldTesting.Ball(),
                Components = [stayer],
            }
        );
        // Every tick: a message to frame A with the value tick * 10, and one to frame B with the value tick * 10 + 1.
        t.SpawnWith(
            SendEvery.Of(target: a.Topic, payloadAt: tick => new Ping { Value = (int)tick * 10 }),
            SendEvery.Of(target: b.Topic, payloadAt: tick => new Ping { Value = (int)tick * 10 + 1 })
        );

        t.Run(4);

        HandOff handOff = faults.Outputs[1].HandOffs.Should().ContainSingle().Subject;
        handOff.Entity.Should().Be(body);
        t.World.GetFrame(body).Should().Be(b);
        PoseChecks.AssertNear(handOff.LinearVelocity, new(12, 0, 0));
        BodyState latest = faults.Outputs[^1].Bodies.Single(state => state.Entity == body);
        latest.Frame.Should().Be(b);
        t.World.PoseInOwnFrame(body).Should().Be(latest.Pose, "the ball kept flying in the frame b after the hand-off");
        t.World.VelocityInOwnFrame(body).Should().Be(latest.LinearVelocity);
        // The hand-off applies before the routing at the sync point of tick 1, so the messages sent on tick 1 are the
        // first to reach it through B and the last to reach it through A.
        traveller.PingValues().Should().Equal(0, 11, 21);
        stayer.PingValues().Should().Equal(0, 10, 20);
        t.World.Errors.Should().BeEmpty();
    }

    [Fact]
    public void FrameChangedIsDeliveredToAMovedEntityOnlyIfItWantsIt()
    {
        using var t = TestWorld.OnJolt(faults: out _);
        FrameHandle a = t.Root;
        FrameHandle b = t.SpawnStaticFrame(TestWorld.At(1000), radius: 50);
        var wants = new Recorder(EventMask.Of<FrameChanged>());
        var deaf = new Recorder(EventMask.Of<Ping>());
        // Jolt hands both balls off from the root into the frame b in the step of tick 1. They fly side by side.
        Entity moved = Scenes.BallApproachingBubble(world: t.World, from: a, into: b, tick: 1, wants);
        Scenes.Approach beside = Scenes.ApproachToBubble(world: t.World, into: b, tick: 1, lateral: 5);
        t.World.Spawn(
            new()
            {
                Frame = a,
                Pose = beside.Pose,
                LinearVelocity = beside.Velocity,
                Body = WorldTesting.Ball(),
                Components = [deaf],
            }
        );

        t.Run(4);

        (long Tick, Event Event) heard = wants.Seen.Should().ContainSingle().Which;
        heard.Tick.Should().Be(2);
        heard.Event.As<FrameChanged>().From.Should().Be(a);
        heard.Event.As<FrameChanged>().To.Should().Be(b);
        deaf.Seen.Should().BeEmpty();
        t.World.GetFrame(moved).Should().Be(b);
    }

    [Fact]
    public void AFramesOldTopicNeverReachesTheFrameThatReusesItsIndex()
    {
        using var t = new TestWorld();
        FrameHandle old = t.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        Topic stale = old.Topic;
        t.World.Destroy(old.Entity);
        FrameHandle reused = t.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        reused.Index.Should().Be(old.Index);
        reused.Generation.Should().NotBe(old.Generation);

        var inside = new Recorder();
        var watcher = new Recorder();
        t.World.SpawnWith(reused, inside);
        Entity outside = t.SpawnWith(watcher);
        t.World.Subscribe(outside, stale);
        // An entity that kept the old topic sends to it during a tick; the host posts to it from outside.
        t.SpawnWith(
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick != 0)
                        return;
                    outbox.Send(stale, new Ping { Value = 1 });
                    outbox.Send(reused.Topic, new Ping { Value = 2 });
                }
            )
        );
        t.World.Post(Event.Create(target: stale, sender: Entity.Null, payload: new Ping { Value = 3 }));

        t.Run(2);

        inside.PingValues().Should().Equal(2);
        watcher.Seen.Should().BeEmpty();
    }

    [Fact]
    public void SubscribingToTheTopicOfADeadFrameIsRefusedAndLogged()
    {
        using var t = new TestWorld();
        var log = new List<string>();
        t.World.Log += log.Add;
        FrameHandle old = t.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        t.World.Destroy(old.Entity);
        Entity watcher = t.SpawnWith(new Recorder());

        t.World.Subscribe(watcher, old.Topic);

        log.Should().ContainSingle().Which.Should().Contain("cannot subscribe");
    }

    [Fact]
    public void FramesPastTheInitialTableSizesStillRouteTheirTopicsAndAreQueriedAndDrawn()
    {
        using var t = new TestWorld();
        var frames = new List<FrameHandle>();
        var recorders = new List<Recorder>();
        for (int i = 0; i < 40; i++)
        {
            FrameHandle frame = t.SpawnStaticFrame(TestWorld.At(i * 1000), radius: 100);
            var recorder = new Recorder();
            t.World.Spawn(
                new()
                {
                    Frame = frame,
                    Pose = TestWorld.At(1, 2, 3),
                    Components = [recorder],
                    Render = new RenderData { Mesh = new(i), Flags = RenderFlags.Visible },
                }
            );
            frames.Add(frame);
            recorders.Add(recorder);
        }

        t.World.Camera = t.SpawnWith();
        // One message to each frame's topic, with the frame's number as the value.
        for (int i = 0; i < frames.Count; i++)
            t.SpawnWith(SendOnce.Of(target: frames[i].Topic, payload: new Ping { Value = i }));

        t.Run(2);

        // The last frame lies beyond the first 32 frame slots, so the per-frame tables grew along the way.
        frames[^1].Index.Should().BeGreaterThanOrEqualTo(32);
        for (int i = 0; i < frames.Count; i++)
            recorders[i].PingValues().Should().Equal(i);
        var found = new List<Entity>();
        t.World.Snapshot.QueryRadius(frame: frames[^1], center: new(1, 2, 3), radius: 0.5, results: found);
        found.Should().HaveCount(1);
        t.World.RenderSnapshots.TryAcquireLatest(out RenderSnapshot snapshot).Should().BeTrue();
        snapshot.Instances.Count.Should().Be(40);
        RenderInstance last = snapshot.Instances.Single(instance => instance.Mesh == new MeshId(39));
        last.Position.Should().Be(new Vector3(39_001, 2, 3));
    }

    // ---- A slot that is reused -------------------------------------------------------------------------------------------

    [Fact]
    public void AnEntityThatReusesASlotHearsNoneOfItsPredecessorsTopics()
    {
        using var t = new TestWorld();
        Topic topic = Named("inherited");
        FrameHandle bay = t.SpawnStaticFrame(TestWorld.At(0), radius: 50);
        Entity old = t.World.Spawn(
            new()
            {
                Frame = bay,
                Components = [new Recorder()],
                Subscriptions = [topic, t.Root.Topic],
            }
        );
        t.World.Destroy(old);
        var heir = new Recorder();
        Entity reused = t.SpawnWith(heir);
        reused.Index.Should().Be(old.Index);

        t.World.Post(Event.Create(target: topic, sender: Entity.Null, payload: new Ping { Value = 1 }));
        t.World.Post(Event.Create(target: bay.Topic, sender: Entity.Null, payload: new Ping { Value = 2 }));
        t.World.Post(Event.Create(target: t.Root.Topic, sender: Entity.Null, payload: new Ping { Value = 3 }));
        t.Run(2);

        // Only the root topic, as a member of the root frame, and only once.
        heir.PingValues().Should().Equal(3);
    }

    [Fact]
    public void AnEntityThatReusesASlotCanSubscribeToItsPredecessorsTopic()
    {
        using var t = new TestWorld();
        Topic topic = Named("handed-down");
        Entity old = SpawnSubscribed(t, topic, new Recorder());
        t.World.Destroy(old);
        var heir = new Recorder();
        Entity reused = SpawnSubscribed(t, topic, heir);
        reused.Index.Should().Be(old.Index);

        t.World.Post(Event.Create(target: topic, sender: Entity.Null, payload: new Ping { Value = 1 }));
        t.Run(2);

        heir.PingValues().Should().Equal(1);
    }
}
