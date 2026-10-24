using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>48 bytes of 8-byte-aligned storage for one payload struct.</summary>
[InlineArray(6)]
public struct PayloadBytes
{
    private ulong word;
}

/// <summary>
/// The message envelope. <see cref="Type"/> says which payload struct the <see cref="Payload"/> bytes hold.
/// </summary>
public struct Event
{
    public Target Target;
    public Entity Sender;
    public int Type;
    public PayloadBytes Payload;
    public object? Extra;

    public static Event Create<T>(Target target, Entity sender, in T payload, object? extra = null)
        where T : unmanaged, IEventPayload
    {
        var e = new Event
        {
            Target = target,
            Sender = sender,
            Type = EventType<T>.Id,
            Extra = extra,
        };
        Unsafe.As<PayloadBytes, T>(ref e.Payload) = payload;
        return e;
    }

    public readonly bool Is<T>()
        where T : unmanaged, IEventPayload => Type == EventType<T>.Id;

    /// <summary>A reference to the payload, for reading or editing in place. The event must hold a <typeparamref name="T"/>.</summary>
    [UnscopedRef]
    public ref T As<T>()
        where T : unmanaged, IEventPayload
    {
        if (Type != EventType<T>.Id)
            ThrowWrongType(typeof(T));
        return ref Unsafe.As<PayloadBytes, T>(ref Payload);
    }

    /// <summary>A copy of the payload. Null if the event does not hold a <typeparamref name="T"/>.</summary>
    public readonly T? PayloadOf<T>()
        where T : unmanaged, IEventPayload =>
        Type == EventType<T>.Id ? Unsafe.As<PayloadBytes, T>(ref Unsafe.AsRef(in Payload)) : null;

    public override readonly string ToString() => $"Event({EventTypeRegistry.NameOf(Type)}, {Target}, from {Sender})";

    [DoesNotReturn]
    [StackTraceHidden]
    private readonly void ThrowWrongType(Type requested) =>
        throw new InvalidOperationException($"Event holds {EventTypeRegistry.NameOf(Type)}, not {requested.Name}.");
}
