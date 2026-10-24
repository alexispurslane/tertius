using System.Numerics;

namespace Tertius.Unions.Tests;

// The unions that the behavior tests use. The generator writes their members when this project builds.

/// <summary>The up of a hemisphere along a direction: one <see cref="Vector3"/>, as <see cref="AwayFrom"/> has.</summary>
public readonly record struct Along(Vector3 Direction);

/// <summary>The up of a hemisphere away from a center: one <see cref="Vector3"/> under another name.</summary>
public readonly record struct AwayFrom(Vector3 Center);

/// <summary>Two cases of one <see cref="Vector3"/> each, under two names: they share one slot.</summary>
[GeneratedUnion(typeof(Along), typeof(AwayFrom))]
public readonly partial struct Up;

/// <summary>A light with two <see cref="Vector3"/> and two <see cref="float"/> values.</summary>
public readonly record struct Point(Vector3 Position, Vector3 Color, float Intensity, float Range);

/// <summary>A light with three <see cref="Vector3"/> and four <see cref="float"/> values.</summary>
public readonly record struct Spot(
    Vector3 Position,
    Vector3 Direction,
    Vector3 Color,
    float Intensity,
    float Range,
    float Inner,
    float Outer);

/// <summary>Three <see cref="Vector3"/> slots and four <see cref="float"/> slots: 52 bytes and the kind.</summary>
[GeneratedUnion(typeof(Point), typeof(Spot))]
public readonly partial struct Light;

/// <summary>A <see cref="double"/>, then an <see cref="int"/>.</summary>
public readonly record struct Wide(double Big, int Small);

/// <summary>An <see cref="int"/>, then a <see cref="double"/>: the same types as <see cref="Wide"/>, in the other order.</summary>
public readonly record struct Narrow(int Small, double Big);

/// <summary>One <see cref="double"/> slot and one <see cref="int"/> slot, whatever the order in each case.</summary>
[GeneratedUnion(typeof(Wide), typeof(Narrow))]
public readonly partial struct Mixed;

/// <summary>The cases of <see cref="Mixed"/>, with the <see cref="int"/> first: the slots still put the double first.</summary>
[GeneratedUnion(typeof(Narrow), typeof(Wide))]
public readonly partial struct NarrowFirst;

/// <summary>A case with a reference.</summary>
public readonly record struct Named(string Name);

/// <summary>A case with a reference and a number.</summary>
public readonly record struct Tagged(string Tag, int Count);

/// <summary>One <see cref="string"/> slot, shared, and one <see cref="int"/> slot.</summary>
[GeneratedUnion(typeof(Named), typeof(Tagged))]
public readonly partial struct Label;

/// <summary>
/// One <see cref="string"/> slot that only <see cref="Named"/> fills, so it holds null while the union holds
/// <see cref="Busy"/>.
/// </summary>
[GeneratedUnion(typeof(Busy), typeof(Named))]
public readonly partial struct MaybeNamed;

/// <summary>A plain struct, which says nothing about how to build it again, so the union stores it whole.</summary>
public struct Plain
{
    public int A;
    public int B;
}

/// <summary>A record with state beside its parameters, so the union stores it whole.</summary>
public readonly record struct Extra(int Count)
{
    public int Hidden { get; init; }
}

/// <summary>
/// A record whose property changes its parameter. Built again from its property, it would count one more, so the union
/// stores it whole.
/// </summary>
public readonly record struct Bumped(int Count)
{
    public int Count { get; } = Count + 1;
}

/// <summary>A case with no values.</summary>
public readonly record struct Empty;

/// <summary>Every case that the union stores whole, and a case with no values.</summary>
[GeneratedUnion(typeof(Plain), typeof(Extra), typeof(Bumped), typeof(Empty))]
public readonly partial struct Odd;

/// <summary>The first state of a machine.</summary>
public readonly record struct Idle;

/// <summary>A later state of a machine.</summary>
public readonly record struct Busy(int Ticks);

/// <summary>A union whose all-zero value is its first state, so it has no empty value.</summary>
[GeneratedUnion(typeof(Idle), typeof(Busy), ZeroCase = typeof(Idle))]
public readonly partial struct Machine;

/// <summary>A type that holds a nested union.</summary>
public static partial class Holder
{
    /// <summary>A union nested in a class.</summary>
    [GeneratedUnion(typeof(Along), typeof(Busy))]
    internal readonly partial struct Inner;
}
