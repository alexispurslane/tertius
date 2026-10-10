using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// Outbox.SendAfter, through a whole world. A message sent on tick N with a delay of k ticks joins the routing of tick
/// N + k, like a message that an entity sends on that tick, and arrives on tick N + k + 1. The recipients are found when
/// it joins that routing, not when it was sent.
/// </summary>
public class MessagingDelayedTests
{
    /// <summary>A component that, on one tick, sends one <see cref="Ping"/> with a delay.</summary>
    private static Lambda SendsAfter(long onTick, int delay, Target target, int value) =>
        Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == onTick)
                outbox.SendAfter(delay, target, new Ping { Value = value });
        });

    // ---- When it arrives ----------------------------------------------------------------------------------------------

    [Fact]
    public void ADelayedSendArrivesOneTickAfterItsDueTick()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(SendsAfter(0, 1, target, 1), SendsAfter(0, 2, target, 2), SendsAfter(0, 3, target, 3));

        t.Run(6);

        // Sent on tick 0 with a delay of k: due on tick k, so it arrives on tick k + 1.
        inbox.Pings().Should().Equal((2L, 1), (3L, 2), (4L, 3));
    }

    [Fact]
    public void ATimerIsReusedWithoutReplayingItsOldMessages()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick is 0 or 2 or 4)
                outbox.SendAfter(1, target, new Ping { Value = (int)self.Tick });
        }));

        t.Run(7);

        inbox.Pings().Should().Equal((2L, 0), (4L, 2), (6L, 4));
    }

    // ---- The order of an inbox -------------------------------------------------------------------------------------

    [Fact]
    public void DelayedSendsDueOnTheSameTickArriveInTheOrderTheyWereSent()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick != 0)
                return;
            outbox.SendAfter(2, target, new Ping { Value = 21 });
            outbox.Send(target, new Ping { Value = 0 });
            outbox.SendAfter(2, target, new Ping { Value = 22 });
        }));

        t.Run(5);

        inbox.Pings().Should().Equal((1L, 0), (3L, 21), (3L, 22));
    }

    [Fact]
    public void ADirectMessageSentOnTheDueTickStandsAheadOfTheDelayedOneEvenThoughItWasSentLater()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                outbox.SendAfter(1, target, new Ping { Value = 11 });
            if (self.Tick == 1)
                outbox.Send(target, new Ping { Value = 22 });
        }));

        t.Run(4);

        // Both join the routing of tick 1, and the outboxes come before the delayed sends that are due.
        inbox.Pings().Should().Equal((2L, 22), (2L, 11));
    }

    [Fact]
    public void DelayedSendsOfEntitiesInDifferentChunksSentOnTheSameTickArriveInChunkOrder()
    {
        // One slot per chunk, so chunk order is slot order.
        using var t = new TestWorld(chunkSize: 1);
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(SendsAfter(0, 1, target, 10), SendsAfter(0, 1, target, 11));
        t.SpawnWith(SendsAfter(0, 1, target, 20), SendsAfter(0, 1, target, 21));

        t.Run(4);

        inbox.Pings().Should().Equal((2L, 10), (2L, 11), (2L, 20), (2L, 21));
    }

    [Fact]
    public void ADelayedSendSentEarlierStandsAheadOfOneSentLaterAndDueOnTheSameTickWhateverTheirChunks()
    {
        using var t = new TestWorld(chunkSize: 1);
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        // Spawned first, so in the lower chunk, but it sends later.
        t.SpawnWith(SendsAfter(1, 2, target, 2));
        t.SpawnWith(SendsAfter(0, 3, target, 1));

        t.Run(6);

        // Both are due on tick 3. Chunk order alone would put the value 2 first.
        inbox.Pings().Should().Equal((4L, 1), (4L, 2));
    }

    // ---- Who receives it -------------------------------------------------------------------------------------------

    [Fact]
    public void ADelayedMessageIsDroppedWhenItsTargetIsDestroyedBeforeItIsDueAndNeverReachesWhoeverReusesTheSlot()
    {
        using var t = new TestWorld();
        var oldInbox = new Recorder();
        Entity target = t.SpawnWith(oldInbox);
        t.SpawnWith(SendsAfter(0, 3, target, 1));
        t.Run(1);

        t.World.Destroy(target);
        var heirInbox = new Recorder();
        Entity heir = t.SpawnWith(heirInbox);
        heir.Index.Should().Be(target.Index);
        t.Run(6);

        oldInbox.Seen.Should().BeEmpty();
        heirInbox.Seen.Should().BeEmpty();
    }

    [Fact]
    public void ADelayedTopicMessageReachesWhoeverIsSubscribedWhenItIsDue()
    {
        using var t = new TestWorld();
        Topic topic = Topics.Get("tests.messagingdelayed.topic");
        var stays = new Recorder();
        var leaves = new Recorder();
        var joins = new Recorder();
        t.World.Spawn(new SpawnDesc { Frame = t.Root, Components = [stays], Subscriptions = [topic] });
        Entity leavesEntity = t.World.Spawn(new SpawnDesc { Frame = t.Root, Components = [leaves], Subscriptions = [topic] });
        Entity joinsEntity = t.SpawnWith(joins);
        t.SpawnWith(SendsAfter(0, 3, topic, 7));
        t.Run(1);

        // Between the send and the due tick, one subscriber leaves and one joins.
        t.World.Unsubscribe(leavesEntity, topic).Should().BeTrue();
        t.World.Subscribe(joinsEntity, topic);
        t.Run(5);

        stays.Pings().Should().Equal((4L, 7));
        joins.Pings().Should().Equal((4L, 7));
        leaves.Seen.Should().BeEmpty();
    }

    [Fact]
    public void ADelayedBroadcastReachesAnEntityThatWasSpawnedAfterItWasSent()
    {
        using var t = new TestWorld();
        t.SpawnWith(SendsAfter(0, 3, new Broadcast(), 7));
        t.Run(1);

        var late = new Recorder();
        t.SpawnWith(late);
        t.Run(5);

        late.Pings().Should().Equal((4L, 7));
    }

    [Fact]
    public void ADelayedMessageFromASenderThatDiesBeforeItIsDueStillArrives()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            outbox.SendAfter(3, target, new Ping { Value = 4 });
            self.Destroy();
        }));

        t.Run(6);

        inbox.Pings().Should().Equal((4L, 4));
    }

    // ---- What arrives --------------------------------------------------------------------------------------------------

    [Fact]
    public void ADelayedMessageArrivesWithItsPayloadItsSenderAndItsExtraData()
    {
        using var t = new TestWorld();
        var inbox = new Recorder(EventMask.Of<Damage>());
        Entity target = t.SpawnWith(inbox);
        var extra = new object();
        Entity sender = t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) =>
        {
            if (self.Tick == 0)
                outbox.SendAfter(2, target, new Damage { Amount = 2.5f, Kind = 7 }, extra);
        }));

        t.Run(5);

        Event arrived = inbox.Seen.Single().Event;
        arrived.As<Damage>().Amount.Should().Be(2.5f);
        arrived.As<Damage>().Kind.Should().Be(7);
        arrived.Sender.Should().Be(sender);
        arrived.Extra.Should().BeSameAs(extra);
    }

    // ---- What is refused -----------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ADelayOfLessThanOneTickIsRejectedAndReported(int delay)
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(SendsAfter(0, delay, target, 1));

        t.Run(4);

        t.World.Errors.Should().ContainSingle().Which.Should().Contain(nameof(ArgumentOutOfRangeException));
        inbox.Seen.Should().BeEmpty();
    }

    [Fact]
    public void ADelayedSendToNoTargetIsRejectedAndReported()
    {
        using var t = new TestWorld();
        t.SpawnWith(Lambda.OnTick((self, scoped ref events, scoped ref outbox) => outbox.SendAfter(1, default(Target), new Ping())));

        t.World.Tick();

        t.World.Errors.Should().ContainSingle().Which.Should().Contain(nameof(ArgumentException));
    }
}
