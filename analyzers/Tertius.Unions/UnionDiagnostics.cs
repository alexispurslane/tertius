using Microsoft.CodeAnalysis;

namespace Tertius.Unions;

/// <summary>The declarations that the union generator refuses. Each is an error, and the generator writes nothing for it.</summary>
internal static class UnionDiagnostics
{
    private const string Category = "Tertius.Unions";

    public static readonly DiagnosticDescriptor NotPartial = new("TU1001", "A generated union must be partial",
        "The union '{0}' must be declared partial, so that the generator can write its members", Category,
        DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ClassCase = new("TU1002", "A generated union takes struct cases only",
        "The case '{0}' of the union '{1}' is a class. A closed set of classes is a built-in union " +
        "(docs/plans/union-tooling.md, decision 1).", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor NoField = new("TU1003", "A case must fit in a field",
        "The case '{0}' of the union '{1}' cannot be held in a field, as a ref struct or a pointer cannot", Category,
        DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor DuplicateCase = new("TU1004", "A case is named twice",
        "The union '{1}' names the case '{0}' twice", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ZeroCaseNotACase = new("TU1005", "The zero case must be a case",
        "The zero case '{0}' of the union '{1}' is not one of its cases", Category, DiagnosticSeverity.Error, true);

    public static readonly DiagnosticDescriptor ContainerNotPartial = new("TU1006",
        "A type that holds a generated union must be partial",
        "The type '{0}' holds the union '{1}', so it must be declared partial", Category, DiagnosticSeverity.Error,
        true);

    public static readonly DiagnosticDescriptor NoCases = new("TU1007", "A generated union needs a case",
        "The union '{0}' names no case", Category, DiagnosticSeverity.Error, true);
}
