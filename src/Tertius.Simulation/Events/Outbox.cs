using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// Messages that the running entity sends. They go into the outbox of the entity's chunk and are routed at the sync
/// point, so a message sent during tick N arrives during tick N + 1. The sender is always the running entity.
/// </summary>
public ref struct Outbox
{
    private readonly ChunkState chunk;
    private readonly Entity sender;
    private readonly long tick;

    internal Outbox(ChunkState chunk, Entity sender, long tick)
    {
        this.chunk = chunk;
        this.sender = sender;
        this.tick = tick;
    }

    /// <summary>The running entity, which is the sender of every message.</summary>
    public readonly Entity Sender => sender;

    /// <summary>Sends a message that arrives on the next tick.</summary>
    public readonly void Send<T>(Target target, in T payload, object? extra = null) where T : unmanaged, IEventPayload
    {
        if (!target.HasValue)
            ThrowNoTarget();
        Write(ref chunk.Outbox.AddRef(), target, payload, extra);
    }

    /// <summary>
    /// Sends a message that joins the routing <paramref name="ticks"/> ticks later than <see cref="Send{T}"/> would.
    /// With <paramref name="ticks"/> = 1, a message sent during tick N arrives during tick N + 2.
    /// </summary>
    public readonly void SendAfter<T>(int ticks, Target target, in T payload, object? extra = null) where T : unmanaged, IEventPayload
    {
        if (ticks < 1)
            throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "A delay is at least one tick. Use Send for the next tick.");
        if (!target.HasValue)
            ThrowNoTarget();
        ref DelayedEvent delayed = ref chunk.Delayed.AddRef();
        delayed.Due = tick + ticks;
        Write(ref delayed.Event, target, payload, extra);
    }

    private readonly void Write<T>(ref Event e, Target target, in T payload, object? extra) where T : unmanaged, IEventPayload
    {
        e = default;
        e.Target = target;
        e.Sender = sender;
        e.Type = EventType<T>.Id;
        e.Extra = extra;
        Unsafe.As<PayloadBytes, T>(ref e.Payload) = payload;
    }

    [DoesNotReturn]
    private static void ThrowNoTarget() => throw new ArgumentException("The message target has no value.", "target");
}
