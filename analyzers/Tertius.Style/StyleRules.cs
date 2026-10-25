using Microsoft.CodeAnalysis;

namespace Tertius.Style;

/// <summary>
/// The rules of <see cref="StyleAnalyzer"/>: the rules of STYLE.md that a tool can check. Each is a warning, as a
/// compiler warning is, and <c>TreatWarningsAsErrors</c> in <c>Directory.Build.props</c> makes it an error in every
/// build. Each message names the section of STYLE.md that gives the reason. TS0003 holds for the code in <c>src</c>
/// only, and <c>.editorconfig</c> turns it off everywhere else.
/// </summary>
internal static class StyleRules
{
    private const string Category = "Tertius.Style";

    public static readonly DiagnosticDescriptor UnnamedTupleElement = new(
        id: "TS0001",
        title: "Every tuple element has a name",
        messageFormat: "The tuple type '{0}' has an element with no name. Name every element, or make the group a "
            + "readonly record struct (STYLE.md, \"Name a type only when it earns a name\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnnamedArgument = new(
        id: "TS0002",
        title: "A call to more than 3 parameters names its arguments",
        messageFormat: "'{0}' takes more than 3 parameters, so every argument at the call has a name "
            + "(STYLE.md, \"Calls and construction\")",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NullForgiving = new(
        id: "TS0003",
        title: "No null-forgiving operator",
        messageFormat: "The operator ! silences the nullable analysis. Test for null once at the outer shell, in a form "
            + "that the flow analysis understands, and pass a value that is not nullable inward "
            + "(STYLE.md, \"Nulls\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor PositionalDeconstruction = new(
        id: "TS0004",
        title: "A type with more than one field is read with a property pattern",
        messageFormat: "This reads '{0}' by the position of its fields, so a new or reordered field breaks it or swaps two values. "
            + "Use a property pattern, such as {{ Name: var name }}. Only a tuple, or a pattern with one element, stays "
            + "positional (STYLE.md, \"Use pattern matching and destructuring\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor DefaultMethodNotInlined = new(
        id: "TS0005",
        title: "A default method of a struct-constrained interface is inlined",
        messageFormat: "'{0}' is a default method of an interface that a struct-constrained type parameter names. A call on the "
            + "struct boxes it unless the JIT inlines the method, so mark it "
            + "[MethodImpl(MethodImplOptions.AggressiveInlining)] (STYLE.md, \"Composition\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd
    );

    public static readonly DiagnosticDescriptor UnsealedClass = new(
        id: "TS0006",
        title: "A class without a subtype is sealed",
        messageFormat: "No class derives from '{0}'. Make it sealed, abstract or static "
            + "(STYLE.md, \"Seal types, and keep them internal\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        customTags: WellKnownDiagnosticTags.CompilationEnd
    );

    public static readonly DiagnosticDescriptor DeepInheritance = new(
        id: "TS0007",
        title: "An inheritance hierarchy is at most 2 levels deep",
        messageFormat: "'{0}' is {1} levels below the root of its hierarchy ({2}). Share behavior through default interface "
            + "methods instead (STYLE.md, \"Composition\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor Finalizer = new(
        id: "TS0008",
        title: "No finalizer",
        messageFormat: "'{0}' has a finalizer. Release the resource in Dispose: a resource that escapes disposal is a bug for a "
            + "test to find, not for the garbage collector to clean up (STYLE.md, \"Disposal and ownership\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor TryMethodShape = new(
        id: "TS0009",
        title: "A method that returns bool and has an out parameter has the Try shape",
        messageFormat: "'{0}' returns bool and has an out parameter, the Try shape. Return T? and let null be the "
            + "failure. If the caller needs the reason, return Result<T, TProblem> (STYLE.md, \"Expected failures\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UndisposedLocal = new(
        id: "TS0010",
        title: "A disposable local is held in a using declaration",
        messageFormat: "The local '{0}' holds an IDisposable that this scope makes and keeps. Hold it in a using declaration "
            + "(STYLE.md, \"Disposal and ownership\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor NestedConditional = new(
        id: "TS0011",
        title: "No conditional expression in a branch of another",
        messageFormat: "A branch of this conditional expression is another conditional expression. Write a list of guards, or a "
            + "switch expression over a closed set (STYLE.md, \"Guards\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor CopiedGenericStruct = new(
        id: "TS0012",
        title: "No call on a readonly struct-constrained type parameter",
        messageFormat: "'{0}' holds the type parameter '{1}' readonly, so this call on it copies the struct first. Hold it in a "
            + "field that is not readonly, or pass it as ref (STYLE.md, \"Polymorphism is an interface\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor BoxedGenericStruct = new(
        id: "TS0013",
        title: "A struct-constrained type parameter is not converted to an interface",
        messageFormat: "The conversion of '{0}' to '{1}' boxes the struct, and every call becomes a virtual call. Pass it on as a "
            + "type parameter (STYLE.md, \"Polymorphism is an interface\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor HelperClass = new(
        id: "TS0014",
        title: "No static helper class",
        messageFormat: "'{0}' is a static class of helpers with no extension member. Move each function into the type that it "
            + "belongs to (STYLE.md, \"Put a function into the type that it belongs to\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );

    public static readonly DiagnosticDescriptor UnnamedCase = new(
        id: "TS0015",
        title: "A switch statement over a closed set names every case",
        messageFormat: "This switch statement over '{0}' {1}. A switch statement over a closed set names each case in a label "
            + "without a guard, and its default section only throws UnreachableException, so that a new case fails the "
            + "build (STYLE.md, \"Switches\").",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true
    );
}
