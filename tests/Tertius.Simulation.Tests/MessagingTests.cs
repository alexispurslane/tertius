using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Direct messages and broadcast, through a whole world: when a message arrives, what arrives with it, in what order
/// the messages of one tick land in an inbox, and what becomes of a message whose target is gone or new. Topics are in
/// <see cref="MessagingTopicTests"/>, delayed sends in <see cref="MessagingDelayedTests"/> and messages from outside
/// the simulation in <see cref="MessagingPostTests"/>.
/// </summary>
public sealed class MessagingTests
{
    // ---- Latency ----------------------------------------------------------------------------------------------------

    [Fact]
    public void AMessageSentOnOneTickIsHandledOnTheNext()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity receiver = t.SpawnWith(inbox);
        Entity sender = t.SpawnWith(SendEvery.Of(target: receiver, payloadAt: tick => new Ping { Value = (int)tick }));

        t.Run(4);

        // Ticks 0 to 3 send the values 0 to 3, and each arrives one tick later, so the run ends before the last arrives.
        inbox.Pings().Should().Equal((1L, 0), (2L, 1), (3L, 2));
        inbox.Seen.Should().OnlyContain(seen => seen.Event.Sender == sender);
    }

    [Fact]
    public void AnAnswerToAMessageArrivesTwoTicksAfterTheQuestion()
    {
        using var t = new TestWorld();
        var responder = new Lambda(EventMask.Of<Ping>(), (self, scoped ref events, scoped ref outbox) =>
        {
            for (int i = 0; i < events.Count; i++)
                if (events[i].Is<Ping>())
                    outbox.Send(events[i].Sender, new Ping { Value = events[i].As<Ping>().Value + 100 });
        });
        Entity responderEntity = t.SpawnWith(responder);
        var answers = new Recorder();
        t.SpawnWith(SendOnce.Of(target: responderEntity, payload: new Ping { Value = 1 }), answers);

        t.Run(4);

        // Asked on tick 0, handled and answered on tick 1, and the answer handled on tick 2.
        responder.Runs.Should().Be(1);
        answers.Pings().Should().Equal((2L, 101));
    }

    [Fact]
    public void AnEntityCanSendAMessageToItself()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        t.SpawnWith(inbox, Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                outbox.Send(self.Entity, new Ping { Value = 5 });
        }));

        t.Run(3);

        inbox.Pings().Should().Equal((1L, 5));
    }

    [Fact]
    public void ASenderThatDiesOnTheTickItSendsStillDeliversItsMessage()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity receiver = t.SpawnWith(inbox);
        Entity sender = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            outbox.Send(receiver, new Ping { Value = 5 });
            self.Destroy();
        }));

        t.Run(2);

        t.World.IsAlive(sender).Should().BeFalse();
        inbox.Pings().Should().Equal((1L, 5));
    }

    // ---- What arrives -------------------------------------------------------------------------------------------------

    [Fact]
    public void AMessageArrivesWithItsPayloadItsSenderAndItsExtraData()
    {
        using var t = new TestWorld();
        var inbox = new Recorder(EventMask.Of<Damage>());
        Entity receiver = t.SpawnWith(inbox);
        object extra = new();
        Entity sender = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                outbox.Send(receiver, new Damage { Amount = 2.5f, Kind = 7 }, extra);
        }));

        t.Run(2);

        Event arrived = inbox.Seen.Single().Event;
        arrived.As<Damage>().Amount.Should().Be(2.5f);
        arrived.As<Damage>().Kind.Should().Be(7);
        arrived.Sender.Should().Be(sender);
        arrived.Extra.Should().BeSameAs(extra);
    }

    [Fact]
    public void ASendToNoTargetIsRejectedAndReported()
    {
        using var t = new TestWorld();
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => outbox.Send(default, new Ping())));

        t.World.Tick();

        t.World.Errors.Should().ContainSingle().Which.Should().Contain(nameof(ArgumentException));
        t.World.Stats.RoutedEvents.Should().Be(0);
    }

    // ---- Who receives it -------------------------------------------------------------------------------------------------

    [Fact]
    public void ADirectMessageReachesOnlyItsTarget()
    {
        using var t = new TestWorld();
        var target = new Recorder();
        var bystander = new Recorder();
        Entity targetEntity = t.SpawnWith(target);
        t.SpawnWith(bystander);
        t.SpawnWith(SendOnce.Of(target: targetEntity, payload: new Ping { Value = 1 }));

        t.Run(2);

        target.PingValues().Should().Equal(1);
        bystander.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AMessageSentToAFrameHandleReachesTheEntityThatOwnsTheFrameNotItsMembers()
    {
        using var t = new TestWorld();
        var ownerInbox = new Recorder();
        var memberInbox = new Recorder();
        // A frame is an entity too. Its handle names that entity, and its topic (Frame.Topic) names the frame's contents.
        Entity owner = t.World.Spawn(new()
        {
            Frame = t.Root,
            Components = [ownerInbox],
            AsFrame = new(FrameBounds.Sphere(50))
        });
        FrameHandle ship = t.World.GetOwnedFrame(owner);
        t.World.SpawnWith(ship, memberInbox);
        t.SpawnWith(SendOnce.Of(target: ship, payload: new Ping { Value = 1 }));

        t.Run(2);

        ownerInbox.PingValues().Should().Equal(1);
        memberInbox.Seen.Should().BeEmpty();
    }

    [Fact]
    public void ABroadcastReachesEveryEntityThatWantsItsTypeIncludingTheSender()
    {
        using var t = new TestWorld();
        var first = new Recorder();
        var second = new Recorder();
        var deaf = new Recorder(EventMask.Of<Heal>());
        var senderInbox = new Recorder();
        t.SpawnWith(first);
        t.SpawnWith(second);
        t.SpawnWith(deaf);
        t.SpawnWith(SendOnce.Of(target: new Broadcast(), payload: new Ping { Value = 3 }), senderInbox);

        t.Run(2);

        first.PingValues().Should().Equal(3);
        second.PingValues().Should().Equal(3);
        senderInbox.PingValues().Should().Equal(3);
        deaf.Runs.Should().Be(0);
    }

    [Fact]
    public void ABroadcastReachesEntitiesInEveryFrame()
    {
        using var t = new TestWorld();
        FrameHandle ship = t.SpawnStaticFrame(TestWorld.At(100), radius: 50);
        var onShip = new Recorder();
        var inRoot = new Recorder();
        t.World.SpawnWith(ship, onShip);
        t.SpawnWith(inRoot);
        t.SpawnWith(SendOnce.Of(target: new Broadcast(), payload: new Ping { Value = 1 }));

        t.Run(2);

        onShip.PingValues().Should().Equal(1);
        inRoot.PingValues().Should().Equal(1);
    }

    // ---- The order of an inbox -------------------------------------------------------------------------------------

    [Fact]
    public void AnInboxListsTheMessagesOfOneTickInChunkOrderThenSendOrderThenPostedEvents()
    {
        // Entities run in slot order here, because the world is single-threaded. That the order does not depend on which
        // thread ran which chunk is the router's rule (RouterTests fills the chunks out of order) and the determinism
        // test's (it hashes whole runs with many threads).
        using var t = new TestWorld(chunkSize: 2);
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        for (int sender = 1; sender <= 4; sender++)
            t.SpawnWith(SendOnce.Of(target: target, payload: new Ping { Value = sender * 10 }),
                SendOnce.Of(target: target, payload: new Ping { Value = sender * 10 + 1 }));
        t.World.Post(Event.Create(target: target, sender: Entity.Null, payload: new Ping { Value = 999 }));

        t.Run(2);

        inbox.PingValues().Should().Equal(10, 11, 20, 21, 30, 31, 40, 41, 999);
    }

    [Fact]
    public void AnEngineEventStandsAheadOfTheMessagesOfEntitiesInAnInbox()
    {
        using TestWorld t = TestWorld.OnJolt(faults: out _);
        FrameHandle b = t.SpawnStaticFrame(TestWorld.At(1000), radius: 50);
        var traveller = new Recorder(EventMask.Of<Ping>().And<FrameChanged>());
        // Jolt hands the ball off from the root into the frame b in the step of tick 1.
        Entity body = Scenes.BallApproachingBubble(world: t.World, from: t.Root, into: b, tick: 1, traveller);
        t.SpawnWith(SendEvery.Of(target: body, payload: new Ping { Value = 1 }));

        t.Run(4);

        // The sync point of tick 1 sends the FrameChanged, and routes it together with the Ping that was sent on tick 1.
        string[] runs =
        [
            .. traveller.History.Select(run =>
                $"tick {run.Tick}: {string.Join(", ", run.EventTypes.Select(type => type.Name))}")
        ];
        runs.Should().Equal("tick 1: Ping", "tick 2: FrameChanged, Ping", "tick 3: Ping");
    }

    // ---- A target that is gone, or new --------------------------------------------------------------------------------

    [Fact]
    public void AMessageToAnEntityThatDiesOnTheSameTickIsDropped()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity doomed = t.SpawnWith(inbox,
            Lambda.OnTick((self, scoped ref events, scoped ref outbox) => self.Destroy()));
        t.SpawnWith(SendOnce.Of(target: doomed, payload: new Ping { Value = 1 }));

        t.Run(1);

        // The message was sent and routed, but the entity was gone by the routing, so nothing was written to an inbox.
        t.World.IsAlive(doomed).Should().BeFalse();
        t.World.Stats.RoutedEvents.Should().Be(1);
        t.World.Stats.Deliveries.Should().Be(0);
        t.Run(1);
        inbox.Seen.Should().BeEmpty();
    }

    [Fact]
    public void ABroadcastReachesAnEntityThatIsSpawnedOnTheTickItIsSent()
    {
        using var t = new TestWorld();
        var child = new Recorder();
        t.SpawnWith(new Spawner(childComponents: () => [child]),
            SendOnce.Of(target: new Broadcast(), payload: new Ping { Value = 3 }));

        t.Run(2);

        // The child goes live at the sync point, before the routing, so it is a recipient of the broadcast.
        child.Pings().Should().Equal((1L, 3));
    }

    [Fact]
    public void AMessageToAStaleHandleIsDroppedAndDoesNotReachTheEntityThatReusedTheSlot()
    {
        using var t = new TestWorld();
        var first = new Recorder();
        Entity old = t.SpawnWith(first);
        t.World.Destroy(old);
        var second = new Recorder();
        Entity reused = t.SpawnWith(second);
        reused.Index.Should().Be(old.Index);
        reused.Generation.Should().NotBe(old.Generation);

        t.World.Post(Event.Create(target: old, sender: Entity.Null, payload: new Ping { Value = 1 }));
        t.Run(2);

        first.Seen.Should().BeEmpty();
        second.Seen.Should().BeEmpty();
    }

    [Fact]
    public void ASlotReusedOutsideATickDoesNotInheritTheMessagesOfItsPredecessor()
    {
        using var t = new TestWorld();
        var oldLog = new Recorder();
        Entity old = t.SpawnWith(oldLog);
        t.World.Post(Event.Create(target: old, sender: Entity.Null, payload: new Ping { Value = 1 }));
        t.World.Tick();

        // The Ping now sits in the old entity's inbox. Destroy it and reuse its slot before the next tick.
        t.World.Destroy(old);
        var newLog = new Recorder();
        Entity reused = t.SpawnWith(newLog);
        reused.Index.Should().Be(old.Index);
        t.World.Post(Event.Create(target: reused, sender: Entity.Null, payload: new Ping { Value = 2 }));
        t.Run(2);

        oldLog.Seen.Should().BeEmpty();
        newLog.PingValues().Should().Equal(2);
    }
}
