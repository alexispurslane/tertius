namespace Tertius;

/// <summary>
/// A collision group: a named group that a body can be in. Content names the groups in a
/// <see cref="CollisionGroupRegistry"/>, which turns each name into one of <see cref="Capacity"/> groups. Jolt's own
/// <c>CollisionGroup</c> and <c>GroupFilterTable</c> are a different mechanism, which Tertius does not use.
/// </summary>
public readonly record struct CollisionGroup
{
    /// <summary>
    /// How many collision groups content can have. A Jolt object layer has 16 group bits, and the engine keeps 4 of them
    /// for its own roles.
    /// </summary>
    public const int Capacity = 12;

    /// <summary>The default group, which every body starts in. Every registry has it first, under the name "Solid".</summary>
    public static CollisionGroup Solid => default;

    private CollisionGroup(int index)
    {
        Index = index;
    }

    /// <summary>The bit of this group in a <see cref="CollisionGroups"/> set, from 0 to <see cref="Capacity"/> - 1.</summary>
    public int Index { get; }

    internal static CollisionGroup FromIndex(int index)
    {
        Contract.Requires((uint)index < Capacity);
        return new(index);
    }
}

/// <summary>A set of collision groups. The default value is the empty set.</summary>
public readonly record struct CollisionGroups
{
    public static CollisionGroups None => default;

    private CollisionGroups(ushort bits)
    {
        Bits = bits;
    }

    /// <summary>One bit for each group in the set: bit <see cref="CollisionGroup.Index"/>. Only the low 12 bits are ever set.</summary>
    public ushort Bits { get; }

    public bool IsEmpty => Bits == 0;

    public static CollisionGroups Of(CollisionGroup group) => None.And(group);

    /// <summary>This set with <paramref name="group"/> added.</summary>
    public CollisionGroups And(CollisionGroup group) => new((ushort)(Bits | (1 << group.Index)));

    public bool Contains(CollisionGroup group) => (Bits & (1 << group.Index)) != 0;

    /// <summary>True when this set and <paramref name="other"/> share a group.</summary>
    public bool Overlaps(CollisionGroups other) => (Bits & other.Bits) != 0;

    /// <summary>True when every group of <paramref name="other"/> is in this set.</summary>
    public bool ContainsAll(CollisionGroups other) => (Bits & other.Bits) == other.Bits;
}

/// <summary>
/// A filter over collision groups. It has an all-of part, a none-of part and one any-of set, and each part that is
/// empty is no condition. A query has one, and so has a body, whose mask holds only an any-of set. The default value is
/// <see cref="Empty"/>.
/// </summary>
public readonly record struct CollisionMask
{
    /// <summary>The mask with no condition, which every set of groups matches.</summary>
    public static CollisionMask Empty => default;

    /// <summary>The groups that a matching set must all have.</summary>
    public CollisionGroups AllOf { get; private init; }

    /// <summary>The groups that a matching set must not have.</summary>
    public CollisionGroups NoneOf { get; private init; }

    /// <summary>The groups of which a matching set must have at least one, unless this set is empty.</summary>
    public CollisionGroups AnyOf { get; private init; }

    /// <summary>This mask, which also needs <paramref name="group"/>.</summary>
    public CollisionMask InAll(CollisionGroup group) => this with { AllOf = AllOf.And(group) };

    /// <summary>This mask, which also refuses <paramref name="group"/>.</summary>
    public CollisionMask InNone(CollisionGroup group) => this with { NoneOf = NoneOf.And(group) };

    /// <summary>
    /// This mask with <paramref name="group"/> added to its one any-of set. Each call widens that set: a set of groups
    /// that has any one of its groups matches it.
    /// </summary>
    public CollisionMask InAny(CollisionGroup group) => this with { AnyOf = AnyOf.And(group) };

    /// <summary>
    /// True when <paramref name="groups"/> has every group of the all-of part, no group of the none-of part, and at
    /// least one group of the any-of set, unless that set is empty.
    /// </summary>
    public bool Matches(CollisionGroups groups) =>
        groups.ContainsAll(AllOf) && !groups.Overlaps(NoneOf) && (AnyOf.IsEmpty || groups.Overlaps(AnyOf));

    /// <summary>
    /// Why a body cannot have this mask, or null when it can. Jolt's pair filter tests only an any-of set, and the other
    /// parts would need a managed callback for each contact pair. The message completes a sentence that names the mask.
    /// </summary>
    internal string? BodyProblem() =>
        !AllOf.IsEmpty ? "has an all-of part, but a body's collision mask can hold only an any-of set"
        : !NoneOf.IsEmpty ? "has a none-of part, but a body's collision mask can hold only an any-of set"
        : null;
}

/// <summary>
/// The names of a game's collision groups. Content and mods register the names when they load, and blueprints name
/// groups by these names. A registry starts with <see cref="CollisionGroup.Solid"/> under the name "Solid", and it holds
/// at most <see cref="CollisionGroup.Capacity"/> groups. Names are compared ordinally, so they are case-sensitive.
/// </summary>
public sealed class CollisionGroupRegistry
{
    private readonly List<string> names = ["Solid"];

    /// <summary>How many groups have a name, Solid included.</summary>
    public int Count => names.Count;

    /// <summary>The group named <paramref name="name"/>. Throws <see cref="KeyNotFoundException"/> when no group has the name.</summary>
    public CollisionGroup this[string name] =>
        TryGet(name, out CollisionGroup group)
            ? group
            : throw new KeyNotFoundException($"No collision group is named \"{name}\".");

    /// <summary>
    /// The group named <paramref name="name"/>. A new name gets the next free group. A name that is already registered
    /// keeps its group, so two mods that name the same group share it. Throws <see cref="ArgumentException"/> for a blank
    /// name, and <see cref="InvalidOperationException"/> for a new name when every group already has a name.
    /// </summary>
    public CollisionGroup Register(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (TryGet(name, out CollisionGroup group))
            return group;
        if (names.Count == CollisionGroup.Capacity)
            throw new InvalidOperationException(
                $"The collision group \"{name}\" cannot be registered: a game has at most {CollisionGroup.Capacity} collision groups, and every one already has a name.");
        names.Add(name);
        return CollisionGroup.FromIndex(names.Count - 1);
    }

    /// <summary>The group named <paramref name="name"/>, if one is registered.</summary>
    public bool TryGet(string name, out CollisionGroup group)
    {
        int index = names.IndexOf(name);
        group = index < 0 ? default : CollisionGroup.FromIndex(index);
        return index >= 0;
    }
}
