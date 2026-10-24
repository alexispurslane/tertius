using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// World.Post, the way for code outside the simulation (the host, input) to send a message: when it joins a routing,
/// where it stands in the inbox, and that it is safe to call from many threads.
/// </summary>
public sealed class MessagingPostTests
{
    private static Event Message<T>(Target target, T payload)
        where T : unmanaged, IEventPayload => Event.Create(target: target, sender: Entity.Null, payload: payload);

    [Fact]
    public void AMessagePostedBeforeATickIsHandledOneTickAfterIt()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.World.Post(Message(target, new Ping { Value = 1 }));

        t.Run(3);

        // It joins the routing at the end of tick 0, like a message that an entity sent during tick 0.
        inbox.Pings().Should().Equal((1L, 1));
    }

    [Fact]
    public void AMessagePostedBetweenTwoTicksJoinsTheRoutingOfTheSecond()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.Run(1);

        t.World.Post(Message(target, new Ping { Value = 1 }));
        t.Run(3);

        inbox.Pings().Should().Equal((2L, 1));
    }

    [Fact]
    public void APostedMessageStandsAfterEveryMessageThatEntitiesSentInTheSameRouting()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        t.SpawnWith(SendOnce.Of(target: target, payload: new Ping { Value = 1 }));
        // Posted first in time, and still last in the inbox.
        t.World.Post(Message(target, new Ping { Value = 2 }));

        t.Run(2);

        inbox.PingValues().Should().Equal(1, 2);
    }

    [Fact]
    public void AMessagePostedFromInsideATickJoinsThatTicksRoutingAfterTheEntityMessages()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);
        World world = t.World;
        t.SpawnWith(
            Lambda.OnTick(
                (self, scoped ref events, scoped ref outbox) =>
                {
                    if (self.Tick != 0)
                        return;
                    // Posted first inside the same call, and still after the message in the outbox.
                    world.Post(Message(target, new Ping { Value = 2 }));
                    outbox.Send(target, new Ping { Value = 1 });
                }
            )
        );

        t.Run(2);

        inbox.Pings().Should().Equal((1L, 1), (1L, 2));
    }

    [Fact]
    public void AMessagePostedToATopicReachesItsSubscribers()
    {
        using var t = new TestWorld();
        Topic topic = Topics.Get("tests.messagingpost.topic");
        var subscriber = new Recorder();
        var bystander = new Recorder();
        t.World.Spawn(
            new()
            {
                Frame = t.Root,
                Components = [subscriber],
                Subscriptions = [topic],
            }
        );
        t.SpawnWith(bystander);

        t.World.Post(Message(topic, new Ping { Value = 1 }));
        t.Run(2);

        subscriber.PingValues().Should().Equal(1);
        bystander.Seen.Should().BeEmpty();
    }

    [Fact]
    public void AMessagePostedToEveryoneReachesEveryEntityThatWantsIt()
    {
        using var t = new TestWorld();
        var first = new Recorder();
        var second = new Recorder();
        t.SpawnWith(first);
        t.SpawnWith(second);

        t.World.Post(Message(new Broadcast(), new Ping { Value = 1 }));
        t.Run(2);

        first.PingValues().Should().Equal(1);
        second.PingValues().Should().Equal(1);
    }

    [Fact]
    public void PostRefusesAMessageWithNoTargetAndQueuesNothing()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        t.SpawnWith(inbox);

        Action post = () => t.World.Post(default);
        post.Should().Throw<ArgumentException>();
        t.Run(2);

        inbox.Seen.Should().BeEmpty();
        t.World.Stats.RoutedEvents.Should().Be(0);
    }

    [Fact]
    public void ManyThreadsCanPostAtOnceAndNoMessageIsLostOrDoubled()
    {
        using var t = new TestWorld();
        var inbox = new Recorder();
        Entity target = t.SpawnWith(inbox);

        Parallel.For(
            0,
            4,
            thread =>
            {
                for (int i = 0; i < 250; i++)
                    t.World.Post(Message(target, new Ping { Value = thread * 1000 + i }));
            }
        );
        t.World.Tick();
        inbox.Runs.Should().Be(0);
        t.World.Tick();

        // All 1000 arrive in one inbox, in one run of the entity, each once.
        inbox.Runs.Should().Be(1);
        List<int> values = inbox.PingValues();
        values.Should().HaveCount(1000);
        values.Distinct().Should().HaveCount(1000);
    }
}
