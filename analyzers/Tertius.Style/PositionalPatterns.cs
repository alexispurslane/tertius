using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tertius.Style;

/// <summary>
/// TS0004: a read of a type that this solution declares (<see cref="SolutionTypes"/>) by the position of its fields.
/// That is a positional pattern with more than one element, and a deconstruction, in an assignment or a
/// <c>foreach</c>, that calls a <c>Deconstruct</c> method. A deconstruction always has more than one element. A tuple
/// and a framework type such as <c>KeyValuePair</c> are not types of the solution: a tuple has no names to bind by, and
/// a framework type never reorders its fields, which is the misbehavior that the rule prevents.
/// </summary>
internal static class PositionalPatterns
{
    /// <summary>Reports a positional pattern with more than one element of a type of the solution.</summary>
    public static void AnalyzePattern(SyntaxNodeAnalysisContext context)
    {
        var clause = (PositionalPatternClauseSyntax)context.Node;
        if (clause.Subpatterns.Count < 2 || clause.Parent is not RecursivePatternSyntax pattern ||
            context.SemanticModel.GetOperation(pattern, context.CancellationToken) is not IRecursivePatternOperation
            {
                MatchedType: { } matched
            } || !SolutionTypes.Declares(matched, context.Compilation))
            return;
        context.ReportDiagnostic(Diagnostic.Create(StyleRules.PositionalDeconstruction, clause.GetLocation(),
            matched.ToDisplayString()));
    }

    /// <summary>Reports an assignment or a <c>foreach</c> that deconstructs a type of the solution.</summary>
    public static void AnalyzeDeconstruction(SyntaxNodeAnalysisContext context)
    {
        DeconstructionInfo? info = context.Node switch
        {
            AssignmentExpressionSyntax { Left: TupleExpressionSyntax or DeclarationExpressionSyntax } assignment =>
                context.SemanticModel.GetDeconstructionInfo(assignment),
            ForEachVariableStatementSyntax each => context.SemanticModel.GetDeconstructionInfo(each),
            _ => null
        };
        if (info is { } deconstruction && DeconstructedTypes(deconstruction).FirstOrDefault(type =>
                SolutionTypes.Declares(type, context.Compilation)) is { } read)
            context.ReportDiagnostic(Diagnostic.Create(StyleRules.PositionalDeconstruction,
                context.Node.GetLocation(), read.ToDisplayString()));
    }

    /// <summary>
    /// The type of each value that a deconstruction takes apart with a <c>Deconstruct</c> method, at its top level and
    /// in its nested deconstructions, in order. A tuple needs no method.
    /// </summary>
    private static IEnumerable<ITypeSymbol> DeconstructedTypes(DeconstructionInfo info)
    {
        if (info.Method is { } method)
            yield return method.ReceiverType ?? method.ContainingType;
        foreach (ITypeSymbol nested in info.Nested.SelectMany(DeconstructedTypes))
            yield return nested;
    }
}
