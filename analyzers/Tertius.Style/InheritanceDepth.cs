using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0007: a class more than <see cref="MostLevels"/> levels below the root of its hierarchy. The levels count only the
/// classes that this solution declares (<see cref="SolutionTypes"/>). The first base from outside the solution is the
/// root, as <c>object</c> is. A framework base such as <c>JsonConverter&lt;T&gt;</c> derives from a class of its own,
/// which the code cannot change, and the framework asks for that base to plug the class in, so its levels are not a
/// hierarchy that the code builds.
/// </summary>
internal static class InheritanceDepth
{
    /// <summary>The most levels that a class may sit below the root of its hierarchy.</summary>
    public const int MostLevels = 2;

    public static void Analyze(SymbolAnalysisContext context)
    {
        // A struct or an enum derives from a framework type, and an interface from none, so only a class can be deep.
        var type = (INamedTypeSymbol)context.Symbol;
        List<INamedTypeSymbol> chain = [type];
        for (
            INamedTypeSymbol? parent = type.BaseType;
            parent != null && SolutionTypes.Declares(parent, context.Compilation);
            parent = parent.BaseType
        )
            chain.Add(parent);
        if (chain.Count <= MostLevels)
            return;
        context.ReportDiagnostic(
            Diagnostic.Create(
                StyleRules.DeepInheritance,
                type.Locations[0],
                type.Name,
                chain.Count,
                string.Join(" : ", chain.Select(level => level.Name))
            )
        );
    }
}
