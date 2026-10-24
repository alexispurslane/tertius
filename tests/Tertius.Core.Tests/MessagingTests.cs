using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tertius.Core.Tests;

public struct Damage : IEventPayload
{
    public float Amount;
    public int Kind;
}

public struct Heal : IEventPayload
{
    public float Amount;
}

[StructLayout(LayoutKind.Sequential, Size = 64)]
public struct TooBig : IEventPayload;

public class MessagingTests
{
    [Fact]
    public void TargetMatchesEachCaseWithoutDefaultArm()
    {
        Target[] targets = [new Entity(4, 1), new Topic(3), new Broadcast()];
        string[] kinds =
        [
            .. targets.Select(t => t switch
            {
                Entity => "entity",
                Topic => "topic",
                Broadcast => "broadcast"
            })
        ];
        Assert.Equal(["entity", "topic", "broadcast"], kinds);
    }

    [Fact]
    public void TargetRoundTripsItsValue()
    {
        Target t = new Entity(123, 9);
        Assert.True(t.TryGetValue(out Entity e));
        Assert.Equal(new(123, 9), e);
        Assert.False(t.TryGetValue(out Topic _));

        Target frameTopic = Topic.OfFrame(new(new(1, 1), 42, 1));
        Assert.True(frameTopic.TryGetValue(out Topic topic));
        Assert.True(topic.IsFrameTopic);
        Assert.Equal(42, topic.FrameIndex);
    }

    [Fact]
    public void EventPayloadRoundTripsAndEditsInPlace()
    {
        var e = Event.Create(target: new Entity(1, 1), sender: new(2, 1), payload: new Damage { Amount = 10, Kind = 3 });
        Assert.True(e.Is<Damage>());
        Assert.False(e.Is<Heal>());
        e.As<Damage>().Amount -= 4;
        Assert.True(e.TryGet(out Damage d));
        Assert.Equal(6, d.Amount);
        Assert.Equal(3, d.Kind);
        Assert.Throws<InvalidOperationException>(() => e.As<Heal>());
    }

    [Fact]
    public void PayloadOverLimitIsRejected()
    {
        var ex = Assert.Throws<TypeInitializationException>(() => EventType<TooBig>.Id);
        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void EventTypeIdsAreDenseAndStable()
    {
        int a = EventType<Damage>.Id;
        int b = EventType<Heal>.Id;
        Assert.NotEqual(a, b);
        Assert.Equal(a, EventTypeRegistry.Register<Damage>());
        Assert.Equal(typeof(Damage), EventTypeRegistry.TypeOf(a));
    }

    [Fact]
    public void EventMaskSetOperations()
    {
        EventMask damage = EventMask.Of<Damage>();
        EventMask both = damage.And<Heal>();
        Assert.True(both.Contains<Damage>());
        Assert.True(both.Contains<Heal>());
        Assert.False(damage.Contains<Heal>());
        Assert.True(damage.Overlaps(both));
        Assert.False(EventMask.Of<Heal>().Overlaps(damage));
        Assert.True(EventMask.None.IsEmpty);

        EventMask high = EventMask.OfId(511);
        Assert.True(high.Contains(511));
        Assert.False(high.Contains(510));
        Assert.Equal(high | damage, damage | high);
    }

    [Fact]
    public void EventFitsInOneHundredBytes()
    {
        // Target 16 + Sender 8 + Type 4 (+4 padding) + Payload 48 + Extra 8.
        Assert.True(Unsafe.SizeOf<Event>() <= 96);
    }

    [Fact]
    public void EngineEventsRegister()
    {
        EngineEvents.Register();
        Assert.True(EventType<Tick>.Id >= 0);
        var c = Event.Create(target: Topics.Input, sender: Entity.Null, payload: InputEvent.MouseMove(new(3, 4)));
        Assert.True(c.As<InputEvent>().TryGetValue(out MouseMoved moved));
        Assert.Equal(new(3, 4), moved.Delta);
    }
}
