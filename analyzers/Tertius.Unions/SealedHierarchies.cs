using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Unions;

/// <summary>
/// Rule TU0004 for one compilation: an abstract class or record of this assembly, not <c>closed</c>, with no abstract
/// or virtual member of its own, whose direct subtypes in the assembly are all sealed, with at least one. A base with an
/// abstract or virtual member is an open set that its callers use through virtual dispatch, not an algebraic data type. Every direct subtype that is sealed has no subtype of
/// its own, so "all direct subtypes are sealed" is "all subtypes are sealed". The rule needs every subtype, so each
/// type records itself under its base, and the end of the compilation reports.
/// </summary>
internal sealed class SealedHierarchies
{
    // Each abstract base of this assembly with a subtype, and whether every subtype seen so far is sealed.
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> allSealed = new(SymbolEqualityComparer.Default);

    public static void Start(CompilationStartAnalysisContext context)
    {
        var hierarchies = new SealedHierarchies();
        context.RegisterSymbolAction(hierarchies.Record, SymbolKind.NamedType);
        context.RegisterCompilationEndAction(hierarchies.Report);
    }

    private void Record(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (type.BaseType?.OriginalDefinition is not { IsAbstract: true } parent ||
            !SymbolEqualityComparer.Default.Equals(parent.ContainingAssembly, context.Compilation.Assembly))
            return;
        allSealed.AddOrUpdate(parent, type.IsSealed, (_, sealedSoFar) => sealedSoFar && type.IsSealed);
    }

    private void Report(CompilationAnalysisContext context)
    {
        foreach (KeyValuePair<INamedTypeSymbol, bool> hierarchy in allSealed)
            if (hierarchy.Value && !IsClosed(hierarchy.Key, context) && !DeclaresBehavior(hierarchy.Key))
                context.ReportDiagnostic(Diagnostic.Create(ClosedSetRules.OpenHierarchy, hierarchy.Key.Locations[0],
                    hierarchy.Key.Name));
    }

    /// <summary>
    /// True for a type that declares an abstract or virtual member, which its subtypes implement. The virtual members that
    /// the compiler writes for a record do not count, and neither does an override of an inherited member.
    /// </summary>
    private static bool DeclaresBehavior(INamedTypeSymbol type) =>
        type.GetMembers().Any(member => !member.IsImplicitlyDeclared && (member.IsAbstract || member.IsVirtual));

    /// <summary>
    /// True for a type declared <c>closed</c>. Microsoft.CodeAnalysis 4.14 has no symbol property for it, so the rule
    /// reads the modifier.
    /// </summary>
    private static bool IsClosed(INamedTypeSymbol type, CompilationAnalysisContext context) =>
        type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax declaration &&
            declaration.Modifiers.Any(modifier => modifier.ValueText == "closed"));
}
