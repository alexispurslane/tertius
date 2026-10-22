namespace Tertius.Simulation;

/// <summary>A <see cref="Simulation.Sensor"/> relays that <see cref="Other"/> started to touch the entity
/// <see cref="Sensor"/>.</summary>
public struct Entered : IEventPayload
{
    public Entity Sensor;
    public Entity Other;
}

/// <summary>
/// A <see cref="Simulation.Sensor"/> relays that <see cref="Other"/>, whose <see cref="Entered"/> it relayed, stopped
/// touching the entity <see cref="Sensor"/>. <see cref="Other"/> may name an entity that no longer exists, because
/// destroying a body ends its contacts.
/// </summary>
public struct Left : IEventPayload
{
    public Entity Sensor;
    public Entity Other;
}

/// <summary>
/// Relays the touches of its entity, whose body can be a sensor body or a solid one. When the entity hears a
/// <see cref="Collision"/> with an entity that <see cref="Filter"/> matches and that touched it not yet, the sensor
/// sends an <see cref="Entered"/>, and when the last contact with that entity ends with a <see cref="CollisionEnded"/>, a
/// <see cref="Left"/>. Each Entered has exactly one Left. The recipients are the subscribers of <see cref="Topic"/>, or
/// every entity when it is null.
/// </summary>
/// <remarks>
/// <para>
/// The sync point already sends each contact to both of its entities, so the sensor reads its own inbox and searches
/// nothing. A touch in the step of tick N reaches the sensor's entity in tick N + 1 and the recipients in tick N + 2. A
/// message to every entity costs one delivery to each entity that wants <see cref="Entered"/> or <see cref="Left"/>, for
/// each touch, so a busy sensor is cheaper with a topic.
/// </para>
/// <para>
/// The filter decides at the start of a touch: the touch of an entity that the filter does not match then relays
/// nothing, also when the entity matches by the time that the touch ends. The world routes the starts of a step before
/// its ends, so when one contact with an entity ends and another one starts in the same step, the entity hears the new
/// <see cref="Collision"/> first. The sensor counts the open contacts with each entity that it relayed, so such a step
/// relays nothing.
/// </para>
/// </remarks>
public sealed class Sensor : IComponent
{
    // For each entity whose Entered this sensor relayed and whose Left it has not: the number of its contacts with the
    // sensor's entity that started and have not ended.
    private readonly Dictionary<Entity, int> openContacts = [];
    private string? topic;
    private Target recipients = new Broadcast();

    /// <summary>
    /// The name of the topic whose subscribers hear the sensor's <see cref="Entered"/> and <see cref="Left"/>, or null, the
    /// default, for every entity. Content names it.
    /// </summary>
    public string? Topic
    {
        get => topic;
        set
        {
            topic = value;
            recipients = value is null ? new Broadcast() : Topics.Get(value);
        }
    }

    /// <summary>The entities whose touches the sensor relays. The default query matches every entity.</summary>
    public EntityQuery Filter { get; set; }

    public int Priority => 0;

    public EventMask Wants => EventMask.Of<Collision>().And<CollisionEnded>();

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        Snapshot snapshot = self.Snapshot;
        foreach (ref Collision collision in events.Of<Collision>())
        {
            Entity other = collision.Other;
            if (openContacts.TryGetValue(other, out int open))
            {
                openContacts[other] = open + 1;
            }
            else if (snapshot.Matches(Filter, other))
            {
                openContacts.Add(other, 1);
                outbox.Send(recipients, new Entered { Sensor = self.Entity, Other = other });
            }
        }

        foreach (ref CollisionEnded ended in events.Of<CollisionEnded>())
        {
            Entity other = ended.Other;
            if (!openContacts.TryGetValue(other, out int open))
                continue;
            if (open > 1)
            {
                openContacts[other] = open - 1;
            }
            else
            {
                openContacts.Remove(other);
                outbox.Send(recipients, new Left { Sensor = self.Entity, Other = other });
            }
        }
    }
}
