using Tertius.Simulation;

namespace Tertius.Testing;

/// <summary>What a <see cref="CollisionLog"/> heard about a contact: that it started or that it ended.</summary>
public enum ContactNews
{
    Started,
    Ended,
}

/// <summary>
/// Records the tick, the other entity and the impulse of every Collision that its entity hears, the tick and the other
/// entity of every CollisionEnded, and the order of both.
/// </summary>
public sealed class CollisionLog : IComponent
{
    private readonly List<(Entity Other, ContactNews News)> heard = new();

    /// <summary>Every Collision, in the order heard.</summary>
    public List<(long Tick, Entity Other, float Impulse)> Seen { get; } = new();

    /// <summary>Every CollisionEnded, in the order heard.</summary>
    public List<(long Tick, Entity Other)> Ended { get; } = new();

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Collision>() | EventMask.Of<CollisionEnded>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Is<Collision>())
            {
                Collision collision = events[i].As<Collision>();
                Seen.Add((self.Tick, collision.Other, collision.Impulse));
                heard.Add((collision.Other, ContactNews.Started));
            }
            else if (events[i].Is<CollisionEnded>())
            {
                CollisionEnded ended = events[i].As<CollisionEnded>();
                Ended.Add((self.Tick, ended.Other));
                heard.Add((ended.Other, ContactNews.Ended));
            }
        }
    }

    public int CountWith(Entity other) => Seen.Count(seen => seen.Other == other);

    /// <summary>The starts and ends of the contacts with <paramref name="other"/>, in the order heard.</summary>
    public List<ContactNews> NewsOf(Entity other) => heard.Where(h => h.Other == other).Select(h => h.News).ToList();
}
