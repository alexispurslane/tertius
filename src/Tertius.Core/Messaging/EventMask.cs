using System.Runtime.CompilerServices;

namespace Tertius;

/// <summary>A set of event types, as a 512-bit mask.</summary>
public struct EventMask : IEquatable<EventMask>
{
    public const int Capacity = 512;
    private const int Words = Capacity / 64;

    [InlineArray(Words)]
    private struct Bits
    {
        private ulong word;
    }

    private Bits bits;

    public static EventMask None => default;

    public static EventMask Of<T>() where T : unmanaged, IEventPayload => default(EventMask).And<T>();

    public static EventMask OfId(int typeId)
    {
        EventMask mask = default;
        mask.Add(typeId);
        return mask;
    }

    /// <summary>Returns a copy of this mask that also contains <typeparamref name="T"/>.</summary>
    public readonly EventMask And<T>() where T : unmanaged, IEventPayload
    {
        EventMask copy = this;
        copy.Add(EventType<T>.Id);
        return copy;
    }

    public void Add(int typeId) => bits[typeId >> 6] |= 1UL << (typeId & 63);

    public void Remove(int typeId) => bits[typeId >> 6] &= ~(1UL << (typeId & 63));

    public readonly bool Contains(int typeId) => (bits[typeId >> 6] & (1UL << (typeId & 63))) != 0;

    public readonly bool Contains<T>() where T : unmanaged, IEventPayload => Contains(EventType<T>.Id);

    public readonly bool Overlaps(in EventMask other)
    {
        for (int i = 0; i < Words; i++)
            if ((bits[i] & other.bits[i]) != 0)
                return true;
        return false;
    }

    public readonly bool IsEmpty
    {
        get
        {
            for (int i = 0; i < Words; i++)
                if (bits[i] != 0)
                    return false;
            return true;
        }
    }

    public void UnionWith(in EventMask other)
    {
        for (int i = 0; i < Words; i++)
            bits[i] |= other.bits[i];
    }

    public static EventMask operator |(EventMask a, in EventMask b)
    {
        a.UnionWith(b);
        return a;
    }

    public void Clear() => bits = default;

    public readonly bool Equals(EventMask other)
    {
        for (int i = 0; i < Words; i++)
            if (bits[i] != other.bits[i])
                return false;
        return true;
    }

    public override readonly bool Equals(object? obj) => obj is EventMask other && Equals(other);

    public override readonly int GetHashCode()
    {
        HashCode hash = default;
        for (int i = 0; i < Words; i++)
            hash.Add(bits[i]);
        return hash.ToHashCode();
    }

    public static bool operator ==(EventMask a, EventMask b) => a.Equals(b);
    public static bool operator !=(EventMask a, EventMask b) => !a.Equals(b);

    public override readonly string ToString()
    {
        var names = new List<string>();
        for (int id = 0; id < Capacity; id++)
            if (Contains(id))
                names.Add(EventTypeRegistry.NameOf(id));
        return $"EventMask[{string.Join(", ", names)}]";
    }
}
