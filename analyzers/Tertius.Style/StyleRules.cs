using Microsoft.CodeAnalysis;

namespace Tertius.Style;

/// <summary>
/// The rules of <see cref="StyleAnalyzer"/>: the rules of STYLE.md that a tool can check. Each is a warning, as a
/// compiler warning is, and <c>TreatWarningsAsErrors</c> in <c>Directory.Build.props</c> makes it an error in every
/// build. TS0003 holds for the code in <c>src</c> only, and <c>.editorconfig</c> turns it off everywhere else.
/// </summary>
internal static class StyleRules
{
    private const string Category = "Tertius.Style";

    public static readonly DiagnosticDescriptor UnnamedTupleElement = new(id: "TS0001",
        title: "Every tuple element has a name",
        messageFormat: "The tuple type '{0}' has an element with no name. Name every element, or make the group a " +
                       "readonly record struct (STYLE.md, \"Name a type only when it earns a name\").",
        category: Category, defaultSeverity: DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor UnnamedArgument = new(id: "TS0002",
        title: "A call to more than 3 parameters names its arguments",
        messageFormat: "'{0}' takes more than 3 parameters, so every argument at the call has a name " +
                       "(STYLE.md, \"Calls and construction\")",
        category: Category, defaultSeverity: DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor NullForgiving = new(id: "TS0003",
        title: "No null-forgiving operator",
        messageFormat:
        "The operator ! silences the nullable analysis. Test for null once at the outer shell, in a form " +
        "that the flow analysis understands, and pass a value that is not nullable inward " +
        "(STYLE.md, \"Nulls\").",
        category: Category, defaultSeverity: DiagnosticSeverity.Warning, isEnabledByDefault: true);
}
