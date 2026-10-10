namespace Tertius.Simulation.Tests;

/// <summary>
/// Sends a message from its entity every <c>every</c> ticks, starting at tick <c>from</c>. Each message arrives one
/// tick after it is sent. Make one with <see cref="Of{T}(Target, T, int, long, int)"/> for the same payload each time, or
/// with <see cref="Of{T}(Target, Func{long, T}, int, long, int)"/> for a payload that depends on the tick.
/// </summary>
public sealed class SendEvery : IComponent
{
    private readonly SendFn send;
    private readonly int every;
    private readonly long from;

    private SendEvery(SendFn send, int every, long from, int priority)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(every, 1);
        this.send = send;
        this.every = every;
        this.from = from;
        Priority = priority;
    }

    public int Priority { get; }
    public EventMask Wants => EventMask.Of<Tick>();

    /// <summary>How many messages it has sent.</summary>
    public int Sends { get; private set; }

    /// <summary>A component that sends <paramref name="payload"/> to <paramref name="target"/> again and again.</summary>
    /// <param name="target">The recipient: an entity, a topic or <see cref="Broadcast"/>.</param>
    /// <param name="payload">The message. Every send is a copy of it.</param>
    /// <param name="every">The number of ticks between two sends. It is at least 1.</param>
    /// <param name="from">The first tick on which it sends.</param>
    /// <param name="priority">Its place in the entity's pipeline. Lower runs first.</param>
    public static SendEvery Of<T>(Target target, T payload, int every = 1, long from = 0, int priority = 0) where T : unmanaged, IEventPayload =>
        new((scoped ref Outbox outbox, long _) => outbox.Send(target, payload), every, from, priority);

    /// <summary>A component that sends the message that <paramref name="payloadAt"/> makes for the running tick, again and again.</summary>
    /// <param name="target">The recipient: an entity, a topic or <see cref="Broadcast"/>.</param>
    /// <param name="payloadAt">Makes the message from the number of the tick that is running.</param>
    /// <param name="every">The number of ticks between two sends. It is at least 1.</param>
    /// <param name="from">The first tick on which it sends.</param>
    /// <param name="priority">Its place in the entity's pipeline. Lower runs first.</param>
    public static SendEvery Of<T>(Target target, Func<long, T> payloadAt, int every = 1, long from = 0, int priority = 0) where T : unmanaged, IEventPayload =>
        new((scoped ref Outbox outbox, long tick) => outbox.Send(target, payloadAt(tick)), every, from, priority);

    public void Handle(Self self, scoped ref EventList events, scoped ref Outbox outbox)
    {
        // The test is on the tick number and not on a count of runs, so an entity that starts late still sends on the same ticks.
        if (self.Tick < from || (self.Tick - from) % every != 0)
            return;
        Sends++;
        send(ref outbox, self.Tick);
    }
}
