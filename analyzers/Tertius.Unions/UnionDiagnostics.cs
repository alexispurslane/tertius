using Microsoft.CodeAnalysis;

namespace Tertius.Unions;

/// <summary>The declarations that the union generator refuses. Each is an error, and the generator writes nothing for it.</summary>
internal static class UnionDiagnostics
{
    private const string Category = "Tertius.Unions";

    public static readonly DiagnosticDescriptor NotPartial = new(id: "TU1001",
        title: "A generated union must be partial",
        messageFormat: "The union '{0}' must be declared partial, so that the generator can write its members",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ClassCase = new(id: "TU1002",
        title: "A generated union takes struct cases only",
        messageFormat: "The case '{0}' of the union '{1}' is a class. A closed set of classes is a built-in union " +
                       "(docs/plans/union-tooling.md, decision 1).", category: Category,
        defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoField = new(id: "TU1003", title: "A case must fit in a field",
        messageFormat:
        "The case '{0}' of the union '{1}' cannot be held in a field, as a ref struct or a pointer cannot",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DuplicateCase = new(id: "TU1004", title: "A case is named twice",
        messageFormat: "The union '{1}' names the case '{0}' twice", category: Category,
        defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ZeroCaseNotACase = new(id: "TU1005",
        title: "The zero case must be a case",
        messageFormat: "The zero case '{0}' of the union '{1}' is not one of its cases", category: Category,
        defaultSeverity: DiagnosticSeverity.Error, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContainerNotPartial = new(id: "TU1006",
        title: "A type that holds a generated union must be partial",
        messageFormat: "The type '{0}' holds the union '{1}', so it must be declared partial", category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NoCases = new(id: "TU1007", title: "A generated union needs a case",
        messageFormat: "The union '{0}' names no case", category: Category, defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);
}
