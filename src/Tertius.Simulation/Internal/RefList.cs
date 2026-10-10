using System.Runtime.CompilerServices;

namespace Tertius.Simulation;

/// <summary>
/// A growable array with ref access to its items. <see cref="Clear"/> keeps the storage, so a list that is filled
/// and cleared every tick stops allocating once it has reached its largest size.
/// </summary>
internal sealed class RefList<T>
{
    private T[] items;
    private int count;

    public RefList(int capacity = 16) => items = new T[Math.Max(capacity, 4)];

    public int Count => count;

    /// <summary>The backing array. It is valid until the next add that grows the list.</summary>
    public T[] Items => items;

    public ref T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)count)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"The list has {count} items.");
            return ref items[index];
        }
    }

    public Span<T> AsSpan() => items.AsSpan(0, count);

    public ReadOnlyMemory<T> AsMemory() => new(items, 0, count);

    /// <summary>Appends a slot and returns a reference to it. The slot holds whatever the storage held before.</summary>
    public ref T AddRef()
    {
        if (count == items.Length)
            Array.Resize(ref items, items.Length * 2);
        return ref items[count++];
    }

    public void Add(in T item) => AddRef() = item;

    public void AddRange(ReadOnlySpan<T> source)
    {
        if (count + source.Length > items.Length)
            Array.Resize(ref items, Math.Max(count + source.Length, items.Length * 2));
        source.CopyTo(items.AsSpan(count));
        count += source.Length;
    }

    /// <summary>Keeps the first <paramref name="newCount"/> items.</summary>
    public void Truncate(int newCount)
    {
        if ((uint)newCount > (uint)count)
            throw new ArgumentOutOfRangeException(nameof(newCount));
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            Array.Clear(items, newCount, count - newCount);
        count = newCount;
    }

    public void Clear() => Truncate(0);
}
