using System.Collections.Concurrent;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0006 for one compilation: a class or record class that is neither sealed, abstract, static nor closed, and that no
/// class of the compilation derives from. The rule needs every subtype, so each type records its base, each open class
/// records itself, and the end of the compilation reports. A subtype in another assembly is out of sight: a base for
/// another assembly is abstract, so that its own assembly sees no class that it could seal.
/// </summary>
internal sealed class SealedClasses
{
    // Each class of the compilation that is open to subtypes: not sealed, abstract or static.
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> open = new(SymbolEqualityComparer.Default);

    // Each class that a type of the compilation derives from, as its original definition.
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> bases = new(SymbolEqualityComparer.Default);

    public static void Start(CompilationStartAnalysisContext context)
    {
        var rule = new SealedClasses();
        context.RegisterSymbolAction(rule.Record, SymbolKind.NamedType);
        context.RegisterCompilationEndAction(rule.Report);
    }

    private void Record(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.BaseType is { } parent)
            bases[parent.OriginalDefinition] = true;
        // A struct, an enum and a delegate are sealed, and an interface is abstract, so only a class can be open. A
        // closed class, the base of a closed hierarchy, is abstract too.
        if (type is { IsSealed: false, IsAbstract: false, IsStatic: false } && !IsTopLevelProgram(type, context))
            open[type] = true;
    }

    /// <summary>
    /// True for the class that the compiler declares for top-level statements, which code cannot seal. Its declaration
    /// is the file that holds the statements.
    /// </summary>
    private static bool IsTopLevelProgram(INamedTypeSymbol type, SymbolAnalysisContext context) =>
        type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(context.CancellationToken) is CompilationUnitSyntax
        );

    private void Report(CompilationAnalysisContext context)
    {
        foreach (INamedTypeSymbol type in open.Keys.Where(type => !bases.ContainsKey(type)))
            context.ReportDiagnostic(Diagnostic.Create(StyleRules.UnsealedClass, type.Locations[0], type.Name));
    }
}
