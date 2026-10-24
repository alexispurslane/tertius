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

    public static readonly DiagnosticDescriptor HandWrittenUnion = new(id: "TU0001", title: "A union struct is generated",
        messageFormat: "The struct '{0}' is a hand-written [Union]. Mark it [GeneratedUnion], so that the generator writes its members " +
        "(docs/plans/union-tooling.md).", category: Category, defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor BoxedCase = new(id: "TU0002", title: "A built-in union takes class cases only",
        messageFormat: "The built-in union '{0}' boxes its struct case '{1}'. A closed set of structs is a [GeneratedUnion] " +
        "(docs/plans/union-tooling.md, decision 1).", category: Category, defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor OpenHierarchy = new(id: "TU0004",
        title: "A hierarchy of sealed subtypes is closed",
        messageFormat: "Every subtype of '{0}' in this assembly is sealed. Declare it a closed record, or make the set a union, so that " +
        "a switch over it fails the build at a missing case.", category: Category, defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd);

    public static readonly DiagnosticDescriptor KindTag = new(id: "TU0005", title: "A kind with fields beside it is a union",
        messageFormat: "The type '{0}' tags its other fields with the enum '{1}' Kind. Make it a union of one case per kind. If every " +
        "field means something for every kind, suppress this rule at the type with a reason.", category: Category,
        defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);
}
