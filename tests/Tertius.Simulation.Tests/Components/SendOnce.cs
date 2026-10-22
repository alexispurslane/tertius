namespace Tertius.Simulation.Tests;

/// <summary>
/// Puts one message into the outbox. It is a delegate of our own for the same reason as <see cref="HandleFn"/>:
/// <see cref="Outbox"/> is a ref struct. It lets <see cref="SendOnce"/> and <see cref="SendEvery"/> send any payload
/// type without being generic, so that a test can write <c>SendOnce.Of(target, new Ping())</c> and let the compiler
/// infer the payload type, which a constructor call cannot do.
/// </summary>
/// <param name="outbox">The outbox of the running entity.</param>
/// <param name="tick">The number of the tick that is running.</param>
public delegate void SendFn(scoped ref Outbox outbox, long tick);

/// <summary>
/// Sends one message from its entity, on the first tick at or after <c>onTick</c>, and never again. The message
/// arrives one tick later, like every message that an entity sends. Make one with <see cref="Of{T}"/>.
/// </summary>
public sealed class SendOnce : IComponent
{
    private readonly SendFn send;
    private readonly long onTick;

    private SendOnce(SendFn send, long onTick, int priority)
    {
        this.send = send;
        this.onTick = onTick;
        Priority = priority;
    }

    public int Priority { get; }
    public EventMask Wants => EventMask.Of<Tick>();

    /// <summary>True once the message has been sent.</summary>
    public bool Sent { get; private set; }

    /// <summary>A component that sends <paramref name="payload"/> to <paramref name="target"/> once.</summary>
    /// <param name="target">The recipient: an entity, a topic or <see cref="Broadcast"/>.</param>
    /// <param name="payload">The message.</param>
    /// <param name="onTick">The first tick on which it may send. Its default of 0 means the first tick the entity runs.</param>
    /// <param name="priority">Its place in the entity's pipeline. Lower runs first.</param>
    public static SendOnce Of<T>(Target target, T payload, long onTick = 0, int priority = 0)
        where T : unmanaged, IEventPayload =>
        new((scoped ref outbox, _) => outbox.Send(target, payload), onTick, priority);

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        if (Sent || self.Tick < onTick)
            return;
        Sent = true;
        send(ref outbox, self.Tick);
    }
}
