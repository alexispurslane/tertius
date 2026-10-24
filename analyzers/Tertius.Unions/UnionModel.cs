using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace Tertius.Unions;

/// <summary>
/// What the generator writes for one union: its place, its cases and its slots. Every member compares by value, so that
/// the incremental pipeline writes the file again only when the model changes.
/// </summary>
/// <param name="Namespace">The namespace of the union, or null for the global namespace.</param>
/// <param name="Containers">The types that hold the union, outermost first, each as its partial declaration head.</param>
/// <param name="Name">The name of the union struct.</param>
/// <param name="IsReadOnly">True when the user declared the struct <c>readonly</c>.</param>
/// <param name="Cases">The cases, in the order of their kinds.</param>
/// <param name="Slots">The slots, in the order of their fields.</param>
/// <param name="HasZeroCase">True when kind 0 is a case, so that the union has no empty value.</param>
/// <param name="HintName">The name of the generated file.</param>
internal sealed record UnionModel(
    string? Namespace,
    EquatableArray<string> Containers,
    string Name,
    bool IsReadOnly,
    EquatableArray<CaseModel> Cases,
    EquatableArray<SlotModel> Slots,
    bool HasZeroCase,
    string HintName);

/// <summary>One case of a union.</summary>
/// <param name="Type">The fully qualified case type.</param>
/// <param name="Display">The short name of the case type, as <c>ToString</c> and the kind constant use it.</param>
/// <param name="Kind">The kind number of the case.</param>
/// <param name="Values">The values of the case, in the order of its constructor's parameters, or one value for the whole case.</param>
/// <param name="Whole">True when the union stores the case as one value of its own type.</param>
internal sealed record CaseModel(string Type, string Display, int Kind, EquatableArray<ValueModel> Values, bool Whole)
{
    /// <summary>The name of the constant that holds <see cref="Kind"/>.</summary>
    public string KindName => "Kind" + Display;
}

/// <summary>One value of a case.</summary>
/// <param name="Property">The property of the case that gives the value, or null when the value is the whole case.</param>
/// <param name="Slot">The slot that holds the value.</param>
internal sealed record ValueModel(string? Property, int Slot);

/// <summary>One slot: a private field that the cases share by type.</summary>
/// <param name="Type">The fully qualified type of the values that the slot holds, with their nullable annotation.</param>
/// <param name="IsReference">
/// True when the values are references. The field is then nullable, because it holds null while the union holds a case
/// that does not fill it.
/// </param>
/// <param name="Holds">The case values that the slot holds, such as <c>Along.Direction</c>, for a comment.</param>
internal sealed record SlotModel(string Type, bool IsReference, string Holds)
{
    /// <summary>The type of the field: <see cref="Type"/>, made nullable for a reference.</summary>
    public string FieldType => IsReference && !Type.EndsWith("?") ? Type + "?" : Type;
}

/// <summary>An immutable array that compares by its items, for the models of the incremental pipeline.</summary>
internal readonly struct EquatableArray<T>(ImmutableArray<T> items) : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly ImmutableArray<T> items = items;

    public int Count => items.IsDefault ? 0 : items.Length;

    public T this[int index] => items[index];

    public bool Equals(EquatableArray<T> other) => this.SequenceEqual(other);

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        int hash = 17;
        foreach (T item in this)
            hash = unchecked((hash * 31) + item.GetHashCode());
        return hash;
    }

    public IEnumerator<T> GetEnumerator() =>
        ((IEnumerable<T>)(items.IsDefault ? ImmutableArray<T>.Empty : items)).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
