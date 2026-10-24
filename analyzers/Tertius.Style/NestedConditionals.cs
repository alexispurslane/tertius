using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0011: a conditional expression with a branch that is, inside its parentheses, another conditional expression. The
/// rule reports each chain once, at the outer conditional, which is not itself a branch of a conditional. A
/// conditional inside a call, a lambda or the condition of another is not a branch.
/// </summary>
internal static class NestedConditionals
{
    public static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var conditional = (ConditionalExpressionSyntax)context.Node;
        if (IsBranch(conditional))
            return;
        if (Unparenthesized(conditional.WhenTrue) is ConditionalExpressionSyntax ||
            Unparenthesized(conditional.WhenFalse) is ConditionalExpressionSyntax)
            context.ReportDiagnostic(Diagnostic.Create(StyleRules.NestedConditional, conditional.GetLocation()));
    }

    /// <summary>True for a conditional that is a branch of another conditional, inside its parentheses.</summary>
    private static bool IsBranch(ExpressionSyntax expression)
    {
        SyntaxNode node = expression;
        while (node.Parent is ParenthesizedExpressionSyntax parenthesized)
            node = parenthesized;
        return node.Parent is ConditionalExpressionSyntax outer &&
               (outer.WhenTrue == node || outer.WhenFalse == node);
    }

    private static ExpressionSyntax Unparenthesized(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized)
            expression = parenthesized.Expression;
        return expression;
    }
}
