namespace Tertius.Simulation.Tests;

/// <summary>
/// Subscribes its entity to a topic while it runs, and optionally unsubscribes later. It uses <see cref="Self.Subscribe"/>
/// and <see cref="Self.Unsubscribe"/>, so a change takes effect at the sync point of the tick that asked for it: a
/// message sent to the topic during that same tick already reaches the entity, or stops reaching it, one tick later.
/// It does not record what the entity hears. Put a <see cref="Recorder"/> on the entity for that.
/// </summary>
/// <param name="topic">The topic to subscribe to.</param>
/// <param name="subscribeOnTick">The first tick on which it subscribes. Its default of 0 means the first tick the entity runs.</param>
/// <param name="unsubscribeOnTick">The first tick on which it unsubscribes, or null to stay subscribed. It acts only after it has subscribed.</param>
/// <param name="priority">Its place in the entity's pipeline. Lower runs first.</param>
public sealed class Subscriber(Topic topic, long subscribeOnTick = 0, long? unsubscribeOnTick = null, int priority = 0) : IComponent
{
    public int Priority => priority;
    public EventMask Wants => EventMask.Of<Tick>();

    /// <summary>True once it has asked to subscribe.</summary>
    public bool Subscribed { get; private set; }

    /// <summary>True once it has asked to unsubscribe.</summary>
    public bool Unsubscribed { get; private set; }

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (!Subscribed && self.Tick >= subscribeOnTick)
        {
            self.Subscribe(topic);
            Subscribed = true;
        }
        else if (Subscribed && !Unsubscribed && unsubscribeOnTick is { } until && self.Tick >= until)
        {
            self.Unsubscribe(topic);
            Unsubscribed = true;
        }
    }
}
