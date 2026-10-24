using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>
/// Records what the scheduler hands it: every event it handles, and one <see cref="RunRecord"/> for each run. It does
/// nothing else, so it is the way to ask "which events reached this entity, on which tick, in which order".
/// A recorder belongs to one entity, like every component, and a test reads it after the tick has ended.
/// </summary>
public sealed class Recorder(EventMask wants, int priority = 0) : IComponent
{
    /// <summary>Every event it handled, with the number of the tick it handled it on, in the order handled.</summary>
    public readonly List<(long Tick, Event Event)> Seen = [];

    /// <summary>One entry for each run, in order. The scheduler skips a component when none of the types it wants is in the list, and a skipped run has no entry.</summary>
    public readonly List<RunRecord> History = [];

    /// <summary>A recorder that wants <see cref="Ping"/>.</summary>
    public Recorder()
        : this(EventMask.Of<Ping>()) { }

    public int Priority { get; } = priority;
    public EventMask Wants { get; } = wants;

    /// <summary>How many times the scheduler has run it.</summary>
    public int Runs => History.Count;

    /// <summary>One run of the entity: the tick it ran on, the entity, and the type of each event it handled, in list order.</summary>
    public readonly record struct RunRecord(long Tick, Entity Entity, Type[] EventTypes);

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        var types = new Type[events.Count];
        for (int i = 0; i < events.Count; i++)
        {
            Seen.Add((self.Tick, events[i]));
            types[i] = EventTypeRegistry.TypeOf(events[i].Type);
        }

        History.Add(new(self.Tick, self.Entity, types));
    }

    /// <summary>The value of each <see cref="Ping"/> handled, in the order handled.</summary>
    public List<int> PingValues() => [.. Seen.Where(s => s.Event.Is<Ping>()).Select(s => s.Event.As<Ping>().Value)];

    /// <summary>The tick and the value of each <see cref="Ping"/> handled, in the order handled.</summary>
    public List<(long Tick, int Value)> Pings() =>
        [.. Seen.Where(s => s.Event.Is<Ping>()).Select(s => (s.Tick, s.Event.As<Ping>().Value))];
}
