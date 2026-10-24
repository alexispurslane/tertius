using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0001: every element of a tuple type has a name. The rule looks at each tuple type that the code writes, and at
/// each declaration whose type the compiler infers: a <c>var</c> local, a <c>foreach</c> variable, a variable that a
/// pattern or an <c>out var</c> declares, and a lambda parameter without a type. A declaration with a written type is
/// left to its written tuple type, so each tuple is reported once. A deconstruction declares one variable per element,
/// so it has no tuple to report, and a tuple literal takes the names of the type that receives it.
/// </summary>
internal static class TupleNames
{
    /// <summary>Reports a written tuple type with an element that has no name.</summary>
    public static void AnalyzeWrittenType(SyntaxNodeAnalysisContext context)
    {
        var tuple = (TupleTypeSyntax)context.Node;
        if (tuple.Elements.Any(element => element.Identifier.IsKind(SyntaxKind.None)))
            context.ReportDiagnostic(
                Diagnostic.Create(StyleRules.UnnamedTupleElement, tuple.GetLocation(), tuple.ToString())
            );
    }

    /// <summary>Reports a declaration whose inferred type holds a tuple with an element that has no name.</summary>
    public static void AnalyzeInferredDeclaration(SyntaxNodeAnalysisContext context)
    {
        if (!IsInferred(context.Node))
            return;
        ITypeSymbol? type = context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol parameter => parameter.Type,
            _ => null,
        };
        if (type is null || UnnamedTuple(type) is not { } tuple)
            return;
        context.ReportDiagnostic(
            Diagnostic.Create(
                StyleRules.UnnamedTupleElement,
                Identifier(context.Node).GetLocation(),
                tuple.ToDisplayString()
            )
        );
    }

    /// <summary>
    /// True for a declaration whose type the code does not write. A declaration pattern and a declaration expression
    /// with a type other than <c>var</c> write it.
    /// </summary>
    private static bool IsInferred(SyntaxNode declaration) =>
        declaration switch
        {
            VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax variables } => variables.Type.IsVar,
            ForEachStatementSyntax each => each.Type.IsVar,
            ParameterSyntax parameter => parameter.Type is null,
            SingleVariableDesignationSyntax designation => OwnerOf(designation) switch
            {
                DeclarationExpressionSyntax expression => expression.Type.IsVar,
                VarPatternSyntax => true,
                RecursivePatternSyntax pattern => pattern.Type is null,
                _ => false,
            },
            _ => false,
        };

    /// <summary>The node that declares a designation: the first parent that is not a parenthesized designation.</summary>
    private static SyntaxNode? OwnerOf(SyntaxNode designation)
    {
        SyntaxNode? owner = designation.Parent;
        while (owner is ParenthesizedVariableDesignationSyntax)
            owner = owner.Parent;
        return owner;
    }

    private static SyntaxToken Identifier(SyntaxNode declaration) =>
        declaration switch
        {
            VariableDeclaratorSyntax variable => variable.Identifier,
            ForEachStatementSyntax each => each.Identifier,
            ParameterSyntax parameter => parameter.Identifier,
            SingleVariableDesignationSyntax designation => designation.Identifier,
            _ => declaration.GetFirstToken(),
        };

    /// <summary>
    /// The first tuple with an element that has no name in <paramref name="type"/>: the type itself, an element of it,
    /// a type argument of it or the element type of an array, or null.
    /// </summary>
    private static INamedTypeSymbol? UnnamedTuple(ITypeSymbol type) =>
        type switch
        {
            INamedTypeSymbol { IsTupleType: true } tuple
                when tuple.TupleElements.Any(element => !element.IsExplicitlyNamedTupleElement) => tuple,
            INamedTypeSymbol named => named
                .TypeArguments.Select(UnnamedTuple)
                .FirstOrDefault(found => found is not null),
            IArrayTypeSymbol array => UnnamedTuple(array.ElementType),
            _ => null,
        };
}
