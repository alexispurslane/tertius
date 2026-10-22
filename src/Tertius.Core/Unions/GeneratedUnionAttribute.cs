namespace Tertius;

/// <summary>
/// Makes a partial struct a custom C# 15 union of <paramref name="cases"/>, whose members the union generator in
/// <c>analyzers/Tertius.Unions</c> writes: a constructor and an implicit conversion per case, <c>HasValue</c>,
/// <c>Value</c>, a <c>TryGetValue</c> per case, equality and <c>ToString</c> (docs/plans/union-tooling.md).
/// </summary>
/// <param name="cases">The case types, all structs, in the order of their kinds.</param>
[AttributeUsage(AttributeTargets.Struct, Inherited = false)]
public sealed class GeneratedUnionAttribute(params Type[] cases) : Attribute
{
    /// <summary>The case types, in the order of their kinds.</summary>
    public IReadOnlyList<Type> Cases { get; } = cases;

    /// <summary>
    /// The case that the all-zero value holds, or null for a union whose all-zero value holds no case. With a zero case,
    /// the union has no empty value, and its <c>HasValue</c> is always true.
    /// </summary>
    public Type? ZeroCase { get; set; }
}
