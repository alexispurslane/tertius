using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>
/// Marks a struct as a message payload. A payload must be <c>unmanaged</c> and at most
/// <see cref="EventTypeRegistry.MaxPayloadSize"/> bytes. Managed data goes in <see cref="Event.Extra"/>.
/// </summary>
public interface IEventPayload;

/// <summary>The dense integer id of the payload type <typeparamref name="T"/>, assigned on first use.</summary>
public static class EventType<T>
    where T : unmanaged, IEventPayload
{
    public static readonly int Id = EventTypeRegistry.Register(typeof(T), Unsafe.SizeOf<T>());
}

/// <summary>Assigns ids to payload types. Register types at startup to make ids independent of thread timing.</summary>
public static class EventTypeRegistry
{
    public const int MaxTypes = EventMask.Capacity;
    public const int MaxPayloadSize = 48;

    private static readonly Type[] Types = new Type[MaxTypes];
    private static readonly Lock Gate = new();
    private static int count;

    public static int Count => Volatile.Read(ref count);

    /// <summary>Forces id assignment for <typeparamref name="T"/> now.</summary>
    public static int Register<T>()
        where T : unmanaged, IEventPayload => EventType<T>.Id;

    public static Type TypeOf(int id) =>
        (uint)id < (uint)Count
            ? Types[id]
            : throw new ArgumentOutOfRangeException(nameof(id), id, "unknown event type id");

    public static string NameOf(int id) => (uint)id < (uint)Count ? Types[id].Name : $"event:{id}";

    internal static int Register(Type type, int size)
    {
        if (size > MaxPayloadSize)
            throw new InvalidOperationException(
                $"Payload {type.Name} is {size} bytes. The limit is {MaxPayloadSize}. Put large or managed data in Event.Extra."
            );
        lock (Gate)
        {
            for (int i = 0; i < count; i++)
                if (Types[i] == type)
                    return i;
            if (count == MaxTypes)
                throw new InvalidOperationException($"More than {MaxTypes} event types.");
            int id = count;
            Types[id] = type;
            Volatile.Write(ref count, id + 1);
            return id;
        }
    }
}
