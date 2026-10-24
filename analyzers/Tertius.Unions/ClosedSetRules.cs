using Microsoft.CodeAnalysis;

namespace Tertius.Unions;

/// <summary>
/// The rules of <see cref="UnionAnalyzer"/>, which steer each closed set to its form (docs/plans/union-tooling.md, "The
/// analyzer"). Each is an error. A class case of a generated union has no rule here: the generator refuses it with
/// <see cref="UnionDiagnostics.ClassCase"/>, which is its one report.
/// </summary>
internal static class ClosedSetRules
{
    private const string Category = "Tertius.Unions";

    public static readonly DiagnosticDescriptor HandWrittenUnion = new("TU0001", "A union struct is generated",
        "The struct '{0}' is a hand-written [Union]. Mark it [GeneratedUnion], so that the generator writes its members " +
        "(docs/plans/union-tooling.md).", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor BoxedCase = new("TU0002", "A built-in union takes class cases only",
        "The built-in union '{0}' boxes its struct case '{1}'. A closed set of structs is a [GeneratedUnion] " +
        "(docs/plans/union-tooling.md, decision 1).", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor OpenHierarchy = new("TU0004",
        "A hierarchy of sealed subtypes is closed",
        "Every subtype of '{0}' in this assembly is sealed. Declare it a closed record, or make the set a union, so that " +
        "a switch over it fails the build at a missing case.", Category, DiagnosticSeverity.Error, true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor KindTag = new("TU0005", "A kind with fields beside it is a union",
        "The type '{0}' tags its other fields with the enum '{1}' Kind. Make it a union of one case per kind. If every " +
        "field means something for every kind, suppress this rule at the type with a reason.", Category,
        DiagnosticSeverity.Error, true);
}
