using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>Records the tick and the other entity of every Collision that its entity hears.</summary>
public sealed class CollisionLog : IComponent
{
    public List<(long Tick, Entity Other)> Seen { get; } = new();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Collision>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
            if (events[i].Is<Collision>())
                Seen.Add((self.Tick, events[i].As<Collision>().Other));
    }

    public int CountWith(Entity other) => Seen.Count(seen => seen.Other == other);
}
