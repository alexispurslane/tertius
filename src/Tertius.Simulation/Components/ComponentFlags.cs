using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// A set of component types, as a 512-bit mask: bit <c>id</c> for the component type with that id (see
/// <see cref="ComponentTypes"/>). The snapshot row of each entity carries one, its component flags: the types of the
/// components that it had at the end of the previous tick. The default value is the empty set.
/// </summary>
public struct ComponentFlags : IEquatable<ComponentFlags>
{
    private const int Words = ComponentTypes.Capacity / 64;

    [InlineArray(Words)]
    private struct Bits
    {
        private ulong word;
    }

    private Bits bits;

    public static ComponentFlags None => default;

    /// <summary>The set with the type of each component of <paramref name="components"/>.</summary>
    /// <exception cref="ArgumentException">A component's type is generic (see <see cref="ComponentTypes.IdOf"/>).</exception>
    /// <exception cref="InvalidOperationException">A component's type would pass the cap of component types.</exception>
    public static ComponentFlags Of(ReadOnlySpan<IComponent> components)
    {
        ComponentFlags flags = default;
        foreach (IComponent component in components)
            flags.Add(ComponentTypes.IdOf(component.GetType()));
        return flags;
    }

    internal static ComponentFlags OfId(int id)
    {
        ComponentFlags flags = default;
        flags.Add(id);
        return flags;
    }

    /// <summary>True if the set holds the type <typeparamref name="T"/>.</summary>
    public readonly bool Contains<T>() where T : IComponent => Contains(ComponentType<T>.Id);

    public readonly bool Contains(int id) => (bits[id >> 6] & (1UL << (id & 63))) != 0;

    public void Add(int id) => bits[id >> 6] |= 1UL << (id & 63);

    public void Remove(int id) => bits[id >> 6] &= ~(1UL << (id & 63));

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

    /// <summary>True when every type of <paramref name="other"/> is in this set.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool ContainsAll(in ComponentFlags other)
    {
        for (int i = 0; i < Words; i++)
            if ((bits[i] & other.bits[i]) != other.bits[i])
                return false;
        return true;
    }

    /// <summary>True when this set and <paramref name="other"/> share a type.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly bool Overlaps(in ComponentFlags other)
    {
        for (int i = 0; i < Words; i++)
            if ((bits[i] & other.bits[i]) != 0)
                return true;
        return false;
    }

    /// <summary>The types of this set that <paramref name="other"/> does not hold.</summary>
    public readonly ComponentFlags Except(in ComponentFlags other)
    {
        ComponentFlags result = this;
        for (int i = 0; i < Words; i++)
            result.bits[i] &= ~other.bits[i];
        return result;
    }

    /// <summary>The ids of the types in the set, from the lowest up. Use it with <c>foreach</c>; it allocates nothing.</summary>
    public readonly Enumerator GetEnumerator() => new(this);

    public readonly bool Equals(ComponentFlags other)
    {
        for (int i = 0; i < Words; i++)
            if (bits[i] != other.bits[i])
                return false;
        return true;
    }

    public override readonly bool Equals(object? obj) => obj is ComponentFlags other && Equals(other);

    public override readonly int GetHashCode()
    {
        HashCode hash = default;
        for (int i = 0; i < Words; i++)
            hash.Add(bits[i]);
        return hash.ToHashCode();
    }

    public static bool operator ==(ComponentFlags a, ComponentFlags b) => a.Equals(b);
    public static bool operator !=(ComponentFlags a, ComponentFlags b) => !a.Equals(b);

    public override readonly string ToString()
    {
        var names = new List<string>();
        foreach (int id in this)
            names.Add(ComponentTypes.NameOf(id));
        return $"ComponentFlags[{string.Join(", ", names)}]";
    }

    /// <summary>Walks the set bits of a copy of the set, from the lowest up.</summary>
    public struct Enumerator
    {
        private readonly ComponentFlags flags;
        private int word;
        private ulong rest;
        private int current;

        internal Enumerator(in ComponentFlags flags)
        {
            this.flags = flags;
            word = 0;
            rest = flags.bits[0];
            current = -1;
        }

        public readonly int Current => current;

        public bool MoveNext()
        {
            while (rest == 0)
            {
                if (++word == Words)
                    return false;
                rest = flags.bits[word];
            }
            int bit = BitOperations.TrailingZeroCount(rest);
            rest &= rest - 1;
            current = (word << 6) | bit;
            return true;
        }
    }
}

/// <summary>
/// A filter over component types: the types that an entity must all have (<see cref="With"/>) and the types that it
/// must not have (<see cref="Without"/>). A mask whose two parts share a type matches nothing, and the query words that
/// build one refuse it (see <see cref="EntityQuery.With{T}"/>). The default value has no condition.
/// </summary>
public readonly record struct ComponentMask
{
    /// <summary>The types that a matching entity must all have.</summary>
    public ComponentFlags With { get; private init; }

    /// <summary>The types that a matching entity must not have.</summary>
    public ComponentFlags Without { get; private init; }

    /// <summary>This mask, which also needs the type with id <paramref name="id"/>.</summary>
    internal ComponentMask AndWith(int id)
    {
        ComponentFlags types = With;
        types.Add(id);
        return this with { With = types };
    }

    /// <summary>This mask, which also refuses the type with id <paramref name="id"/>.</summary>
    internal ComponentMask AndWithout(int id)
    {
        ComponentFlags types = Without;
        types.Add(id);
        return this with { Without = types };
    }

    /// <summary>True when <paramref name="flags"/> holds every type of <see cref="With"/> and no type of <see cref="Without"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Matches(in ComponentFlags flags) => flags.ContainsAll(With) && !flags.Overlaps(Without);
}
