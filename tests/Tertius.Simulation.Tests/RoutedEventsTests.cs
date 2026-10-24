using AwesomeAssertions;

namespace Tertius.Simulation.Tests;

/// <summary>
/// World.RoutedEvents, the events that the last sync point routed. The scripted runs of the host read it after each tick
/// to fill the events of a state dump.
/// </summary>
public class RoutedEventsTests
{
    private static Event Message<T>(Target target, T payload) where T : unmanaged, IEventPayload =>
        Event.Create(target: target, sender: Entity.Null, payload: payload);

    private static List<int> PingValues(World world)
    {
        var values = new List<int>();
        foreach (Event e in world.RoutedEvents)
            if (e.Is<Ping>())
                values.Add(e.As<Ping>().Value);
        return values;
    }

    [Fact]
    public void AnEventPostedBeforeATickIsRoutedAtTheEndOfThatTick()
    {
        using var t = new TestWorld();
        Entity target = t.SpawnWith(new Recorder());
        t.World.Post(Message(target, new Ping { Value = 7 }));

        t.Run(1);

        t.World.RoutedEvents.Length.Should().Be(1);
        Event routed = t.World.RoutedEvents[0];
        routed.Target.Should().Be(new Target(target));
        routed.As<Ping>().Value.Should().Be(7);
    }

    [Fact]
    public void RoutedEventsHoldOnlyWhatTheLastSyncPointRouted()
    {
        using var t = new TestWorld();
        Entity target = t.SpawnWith(new Recorder());
        t.World.Post(Message(target, new Ping { Value = 1 }));
        t.Run(1);

        t.World.Post(Message(target, new Ping { Value = 2 }));
        t.Run(1);

        PingValues(t.World).Should().Equal(2);
    }

    [Fact]
    public void RoutedEventsAreInRoutingOrderWithEntityMessagesBeforePostedOnes()
    {
        using var t = new TestWorld();
        Entity target = t.SpawnWith(new Recorder());
        t.SpawnWith(SendOnce.Of(target: target, payload: new Ping { Value = 1 }));
        // Posted first in time, and still routed after the message that the entity sends during the tick.
        t.World.Post(Message(target, new Ping { Value = 2 }));

        t.Run(1);

        PingValues(t.World).Should().Equal(1, 2);
    }

    [Fact]
    public void AnEventThatNoEntityWantsIsStillAmongTheRoutedEvents()
    {
        using var t = new TestWorld();
        // The recorder wants Ping only, so the router drops this Note's one recipient.
        Entity target = t.SpawnWith(new Recorder());
        t.World.Post(Message(target, new Note { Value = 3 }));

        t.Run(1);

        t.World.RoutedEvents.Length.Should().Be(1);
        t.World.RoutedEvents[0].As<Note>().Value.Should().Be(3);
    }
}
