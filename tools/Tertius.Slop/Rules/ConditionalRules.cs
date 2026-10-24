using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

// The verbosity rules about conditions, ported from the scb-check slop rules (docs/plans/code-quality-tooling.md,
// decision 17). Each finding covers the node that the rewrite replaces.

/// <summary>An else-if that repeats an earlier condition of its chain.</summary>
public sealed class DuplicatedIfCondition : IRule
{
    public string Id => "duplicated-if-condition";
    public string Message => "An else-if repeats an earlier condition of its chain, so its branch never runs: remove it.";
    public string Source => "scb-check conditionals.yaml: duplicated-if-condition";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        // The head of the chain reports every repeat, so that a chain is read once.
        if (node is not IfStatementSyntax { Parent: not ElseClauseSyntax } head)
            return [];
        var earlier = new List<ExpressionSyntax> { head.Condition };
        var repeats = new List<TextSpan>();
        for (IfStatementSyntax? branch = head.Else?.Statement as IfStatementSyntax;
             branch is not null;
             branch = branch.Else?.Statement as IfStatementSyntax)
        {
            if (earlier.Any(condition => condition.SameAs(branch.Condition)))
                repeats.Add(TextSpan.FromBounds(branch.IfKeyword.SpanStart, branch.Statement.Span.End));
            earlier.Add(branch.Condition);
        }

        return repeats;
    }
}

/// <summary>A hand-written minimum or maximum.</summary>
public sealed class ManualMinMax : IRule
{
    public string Id => "manual-min-max";
    public string Message => "A choice of the smaller or larger of two values: use Math.Min or Math.Max.";
    public string Source => "scb-check conditionals.yaml: manual-min-max";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        ConditionalExpressionSyntax choice when ChoosesOneOf(choice.Condition, choice.WhenTrue, choice.WhenFalse) =>
            [choice.Span],
        IfStatementSyntax
            {
                Statement.Single: ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax then },
                Else.Statement.Single: ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax otherwise }
            } branch when then.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                          otherwise.IsKind(SyntaxKind.SimpleAssignmentExpression) && then.Left.SameAs(otherwise.Left) &&
                          ChoosesOneOf(branch.Condition, then.Right, otherwise.Right) => [branch.Span],
        _ => []
    };

    // Whether a comparison of a and b chooses a or b: both values are the two sides, in either order.
    private static bool ChoosesOneOf(ExpressionSyntax condition, ExpressionSyntax first, ExpressionSyntax second)
    {
        if (condition.Unwrapped is not BinaryExpressionSyntax comparison || !IsOrdering(comparison))
            return false;
        bool inOrder = first.SameAs(comparison.Left) && second.SameAs(comparison.Right);
        bool swapped = first.SameAs(comparison.Right) && second.SameAs(comparison.Left);
        return inOrder || swapped;
    }

    private static bool IsOrdering(BinaryExpressionSyntax comparison) =>
        comparison.Kind() is SyntaxKind.GreaterThanExpression or SyntaxKind.LessThanExpression
            or SyntaxKind.GreaterThanOrEqualExpression or SyntaxKind.LessThanOrEqualExpression;
}

/// <summary>An if whose only statement is another if, neither with an else.</summary>
public sealed class NestedIfNoElse : IRule
{
    public string Id => "nested-if-no-else";
    public string Message => "An if whose only statement is another if, neither with an else: join the conditions with &&.";
    public string Source => "scb-check conditionals.yaml: nested-if-no-else";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        IfStatementSyntax { Else: null, Statement.Single: IfStatementSyntax { Else: null } } outer => [outer.Span],
        _ => []
    };
}

/// <summary>A conditional expression with two equal branches.</summary>
public sealed class TernarySameValue : IRule
{
    public string Id => "ternary-same-value";
    public string Message => "Both branches of the conditional expression are the same: drop the condition.";
    public string Source => "scb-check conditionals.yaml: ternary-same-value";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        ConditionalExpressionSyntax choice when choice.WhenTrue.SameAs(choice.WhenFalse) => [choice.Span],
        _ => []
    };
}

/// <summary>An if that returns true or false.</summary>
public sealed class BooleanReturnIfElse : IRule
{
    public string Id => "boolean-return-if-else";
    public string Message => "An if that returns true in one branch and false in the other: return the condition.";
    public string Source => "scb-check conditionals.yaml: boolean-return-if-else, if-return-bool-else";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not IfStatementSyntax { Statement.Single: ReturnStatementSyntax { Expression: { } then } } branch)
            return [];
        if (branch.Else is { Statement.Single: ReturnStatementSyntax { Expression: { } otherwise } })
            return Opposite(then, otherwise) ? [branch.Span] : [];

        // Without an else, the return after the if is the other branch. An if after another if is the end of a list of
        // guards, the form that STYLE.md prefers ("Guards"), so it stays.
        bool endsGuardList = branch.PreviousStatement is IfStatementSyntax;
        return branch is { Else: null, NextStatement: ReturnStatementSyntax { Expression: { } next } nextReturn } &&
               !endsGuardList && Opposite(then, next)
            ? [TextSpan.FromBounds(branch.SpanStart, nextReturn.Span.End)]
            : [];
    }

    private static bool Opposite(ExpressionSyntax first, ExpressionSyntax second) =>
        (first.Kind(), second.Kind()) is (SyntaxKind.TrueLiteralExpression, SyntaxKind.FalseLiteralExpression)
        or (SyntaxKind.FalseLiteralExpression, SyntaxKind.TrueLiteralExpression);
}

/// <summary>A conditional expression that gives true or false.</summary>
public sealed class RedundantBoolTernary : IRule
{
    public string Id => "redundant-bool-ternary";
    public string Message => "A conditional expression that gives true or false: use the condition, or its negation.";
    public string Source => "scb-check boolean_and_comparison.yaml: redundant-bool-ternary";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        ConditionalExpressionSyntax choice when (choice.WhenTrue.Kind(), choice.WhenFalse.Kind()) is
            (SyntaxKind.TrueLiteralExpression, SyntaxKind.FalseLiteralExpression)
            or (SyntaxKind.FalseLiteralExpression, SyntaxKind.TrueLiteralExpression) => [choice.Span],
        _ => []
    };
}

/// <summary>A guard that returns what the next statement returns.</summary>
public sealed class RedundantGuardSameReturn : IRule
{
    public string Id => "redundant-guard-same-return";
    public string Message => "An if that returns what the next statement returns: drop the if.";
    public string Source => "scb-check conditionals.yaml: redundant-guard-same-return";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        IfStatementSyntax
            {
                Else: null, Statement.Single: ReturnStatementSyntax { Expression: { } guarded },
                NextStatement: ReturnStatementSyntax { Expression: { } next }
            } branch when guarded.SameAs(next) => [branch.Span],
        _ => []
    };
}

/// <summary>An else after a branch that returns or throws.</summary>
public sealed class UnnecessaryElse : IRule
{
    public string Id => "unnecessary-else";
    public string Message => "An else after a branch that returns or throws: drop the else and outdent its code.";
    public string Source => "scb-check conditionals.yaml: unnecessary-elif, unnecessary-else-raise";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        IfStatementSyntax { Else: { } otherwise } branch when branch.Statement.Exits => [otherwise.Span],
        _ => []
    };
}

/// <summary>An if with an empty branch and an else.</summary>
public sealed class EmptyIfBranch : IRule
{
    public string Id => "empty-if-branch";
    public string Message => "An if with an empty branch and an else: negate the condition and drop the empty branch.";
    public string Source => "scb-check conditionals.yaml: if-pass-else-action";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        IfStatementSyntax { Statement: BlockSyntax { Statements.Count: 0 } or EmptyStatementSyntax, Else: not null } branch =>
            [branch.Span],
        _ => []
    };
}

/// <summary>Two type tests of the same value in a row.</summary>
public sealed class TypeProbeLadder : IRule
{
    public string Id => "type-probe-ladder";
    public string Message => "Two type tests of the same value in a row: use a switch with type patterns.";
    public string Source => "scb-check defensive.yaml: type-probe-ladder; conditionals.yaml: isinstance-return-ladder";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) =>
        node is IfStatementSyntax branch ? branch.PairedBy(TypeTested) : [];

    // The value that a condition tests for a type, such as s in `s is Circle c`, or null.
    private static ExpressionSyntax? TypeTested(ExpressionSyntax condition) => condition.Unwrapped switch
    {
        BinaryExpressionSyntax test when test.IsKind(SyntaxKind.IsExpression) => test.Left,
        IsPatternExpressionSyntax
            {
                Pattern: DeclarationPatternSyntax or TypePatternSyntax or RecursivePatternSyntax { Type: not null }
            } test => test.Expression,
        _ => null
    };
}

/// <summary>Branches that compare one value with strings.</summary>
public sealed class DispatchByStringEquality : IRule
{
    public string Id => "dispatch-by-string-equality";
    public string Message => "Branches that compare one value with string literals: use a switch.";
    public string Source => "scb-check defensive.yaml: dispatch-by-string-equality";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) =>
        node is IfStatementSyntax branch ? branch.PairedBy(StringCompared) : [];

    // The value that a condition compares with a string literal, such as op in `op == "add"`, or null.
    private static ExpressionSyntax? StringCompared(ExpressionSyntax condition) => condition.Unwrapped switch
    {
        BinaryExpressionSyntax { Right: LiteralExpressionSyntax literal } test
            when test.IsKind(SyntaxKind.EqualsExpression) && literal.IsKind(SyntaxKind.StringLiteralExpression) =>
            test.Left,
        _ => null
    };
}

/// <summary>Three or more branches that return by comparing one value with constants.</summary>
public sealed class ValueCompareReturnLadder : IRule
{
    private const int LeastBranches = 3;

    public string Id => "value-compare-return-ladder";
    public string Message => "Three or more branches that return by comparing one value with constants: use a switch expression.";
    public string Source => "scb-check conditionals.yaml: value-compare-return-ladder";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not IfStatementSyntax head || Compared(head) is not { } value)
            return [];
        // Only the head of a ladder reports it, so that a ladder counts once.
        if (head.PreviousBranch is { } previous && value.SameAs(Compared(previous)))
            return [];
        List<IfStatementSyntax> branches = [head];
        while (branches[^1].NextBranch is { } next && value.SameAs(Compared(next)))
            branches.Add(next);
        return branches.Count >= LeastBranches ? [TextSpan.FromBounds(head.SpanStart, branches[^1].Span.End)] : [];
    }

    // The value that a branch compares with a constant and then returns from, such as k in `if (k == 1) return 10;`.
    private static ExpressionSyntax? Compared(IfStatementSyntax branch) => branch switch
    {
        {
            Condition: BinaryExpressionSyntax { Right: LiteralExpressionSyntax or MemberAccessExpressionSyntax } test,
            Statement.Single: ReturnStatementSyntax
        } when test.IsKind(SyntaxKind.EqualsExpression) => test.Left,
        _ => null
    };
}

/// <summary>An if that assigns a value to a variable that is null.</summary>
public sealed class VerboseNoneDefault : IRule
{
    public string Id => "verbose-none-default";
    public string Message => "An if that assigns a value to a variable when it is null: use ??=.";
    public string Source => "scb-check conditionals.yaml: verbose-none-default";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        IfStatementSyntax
            {
                Else: null, Statement.Single: ExpressionStatementSyntax { Expression: AssignmentExpressionSyntax assignment }
            } branch when assignment.IsKind(SyntaxKind.SimpleAssignmentExpression) &&
                          assignment.Left.SameAs(branch.Condition.NullTested) => [branch.Span],
        _ => []
    };
}

/// <summary>A conditional expression that tests a value for null and gives the value when it is not.</summary>
public sealed class ManualNullCoalescing : IRule
{
    public string Id => "manual-null-coalescing";
    public string Message => "A conditional expression that gives a value unless it is null: use ??.";
    public string Source =>
        "scb-check conditionals.yaml: ternary-none-comparison, return-ternary-or-none, defensive-or-empty";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        ConditionalExpressionSyntax choice when choice.WhenFalse.SameAs(choice.Condition.NullTested) ||
                                                choice.WhenTrue.SameAs(choice.Condition.NotNullTested) => [choice.Span],
        _ => []
    };
}
