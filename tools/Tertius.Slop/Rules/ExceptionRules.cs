using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

// The verbosity rules about exceptions and defensive guards, ported from the scb-check slop rules
// (docs/plans/code-quality-tooling.md, decision 17). A rule about a whole function flags the statements that it counts,
// not the whole function: the rest of the function is not what the rule calls verbose.

/// <summary>A function with three or more try statements.</summary>
public sealed class DefensiveTrySoupFunction : IRule
{
    private const int LeastTries = 3;

    public string Id => "defensive-try-soup-function";
    public string Message => "A function with three or more try statements: rethink its contract or split it.";
    public string Source => "scb-check defensive.yaml: defensive-try-soup-function";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (!Function.IsFunction(node))
            return [];
        TryStatementSyntax[] tries = [.. new Function(node).OwnNodes.OfType<TryStatementSyntax>()];
        return tries.Length >= LeastTries ? tries.Select(each => each.Span) : [];
    }
}

/// <summary>A function with two or more clauses that catch every exception.</summary>
public sealed class DefensiveExceptExceptionHeavy : IRule
{
    private const int LeastCatchAlls = 2;

    public string Id => "defensive-except-exception-heavy";

    public string Message =>
        "A function with two or more clauses that catch every exception: catch what can happen, once.";

    public string Source => "scb-check defensive.yaml: defensive-except-exception-heavy";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (!Function.IsFunction(node))
            return [];
        CatchClauseSyntax[] catchAlls =
        [
            .. new Function(node).OwnNodes.OfType<CatchClauseSyntax>().Where(each => each.CatchesAll),
        ];
        return catchAlls.Length >= LeastCatchAlls ? catchAlls.Select(each => each.Span) : [];
    }
}

/// <summary>A clause that catches every exception and only returns a constant.</summary>
public sealed class ExceptReturnStaticSentinel : IRule
{
    public string Id => "except-return-static-sentinel";

    public string Message =>
        "A clause that catches every exception and returns a constant: catch something specific or let it go.";

    public string Source =>
        "scb-check defensive.yaml: except-return-static-sentinel, sentinel-fallback-function, parse-and-fallback-exception";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) =>
        node switch
        {
            CatchClauseSyntax { Block.Statements: [ReturnStatementSyntax { Expression.IsConstantValue: true }] } clause
                when clause.CatchesAll => [clause.Span],
            _ => [],
        };
}

/// <summary>A catch clause that only rethrows.</summary>
public sealed class ExceptBareReraise : IRule
{
    public string Id => "except-bare-reraise";
    public string Message => "A catch clause that only rethrows: drop it.";
    public string Source => "scb-check misc.yaml: except-bare-reraise; the user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) =>
        node switch
        {
            // A rethrow before a later catch keeps that exception out of the later, broader catch, so it stays.
            CatchClauseSyntax { Block.Statements: [ThrowStatementSyntax only], Parent: TryStatementSyntax owner } clause
                when only.Rethrows(clause) && owner.Catches.Last() == clause => [clause.Span],
            _ => [],
        };
}

/// <summary>A catch clause that swallows the error: it neither throws, nor reads the error, nor reports it.</summary>
/// <remarks>
/// A report is a call whose name holds Log, Write, Print, Trace, Report or Warn. A Try method turns an expected failure
/// into false, which STYLE.md asks for ("Expected failures"), so its catch clauses stay.
/// </remarks>
public sealed class ExceptPassSilence : IRule
{
    public string Id => "except-pass-silence";

    public string Message =>
        "A catch clause that swallows the error, with no throw, no read of the error and no report: handle it or let it go.";

    public string Source => "scb-check misc.yaml: except-pass-silence, swallow-exception-pass-multi-stmt; the user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not CatchClauseSyntax clause || SyntaxShapes.IsTryName(clause.HoldingMethodName))
            return [];
        SyntaxNode[] body = [.. clause.Block.DescendantNodes()];
        string? error = clause.Declaration?.Identifier.ValueText;
        bool throws = body.Any(each => each is ThrowStatementSyntax or ThrowExpressionSyntax);
        bool readsError = body.OfType<IdentifierNameSyntax>().Any(name => name.Identifier.ValueText == error);
        bool reports = body.OfType<InvocationExpressionSyntax>().Any(call => call.Reports);
        return throws || readsError || reports ? [] : [clause.Span];
    }
}

/// <summary>A catch clause that reports the error and rethrows it.</summary>
public sealed class LogAndRethrow : IRule
{
    public string Id => "log-and-rethrow";

    public string Message =>
        "A catch clause that reports the error and rethrows it: the caller that handles it reports it, so drop the catch.";

    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (
            node is not CatchClauseSyntax { Block.Statements: [_, .., ThrowStatementSyntax last] } clause
            || !last.Rethrows(clause)
        )
            return [];
        bool onlyReports = clause
            .Block.Statements.SkipLast(1)
            .All(statement =>
                statement is ExpressionStatementSyntax { Expression: InvocationExpressionSyntax call } && call.Reports
            );
        return onlyReports ? [clause.Span] : [];
    }
}

/// <summary>A contract on a value that a guard of the same function also checks.</summary>
public sealed class ContractAndGuard : IRule
{
    public string Id => "contract-and-guard";

    public string Message =>
        "A contract on a value that a guard of the same function checks: keep the guard of a public API, or the contract inside.";

    public string Source => "The user; STYLE.md, \"Errors and contracts\"";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (!Function.IsFunction(node))
            return [];
        SyntaxNode[] own = [.. new Function(node).OwnNodes];
        HashSet<string> guarded = [.. own.SelectMany(GuardedValues)];
        return own.OfType<InvocationExpressionSyntax>()
            .Where(call =>
                call.Expression
                    is MemberAccessExpressionSyntax
                    {
                        Expression: IdentifierNameSyntax { Identifier.ValueText: "Contract" }
                    }
                && ValuesIn(call.ArgumentList).Any(guarded.Contains)
            )
            .Select(call => call.Span);
    }

    // The values that a guard checks: the condition of an if that throws, or the arguments of a ThrowIf method of an
    // exception type, such as ArgumentOutOfRangeException.ThrowIfNegative.
    private static IEnumerable<string> GuardedValues(SyntaxNode node) =>
        node switch
        {
            IfStatementSyntax { Statement.Single: ThrowStatementSyntax } guard => ValuesIn(guard.Condition),
            InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax
                {
                    Expression: IdentifierNameSyntax { Identifier.ValueText: var type },
                    Name.Identifier.ValueText: var name
                }
            } call
                when type.EndsWith("Exception", StringComparison.Ordinal)
                    && name.StartsWith("ThrowIf", StringComparison.Ordinal) => ValuesIn(call.ArgumentList),
            _ => [],
        };

    // The values that an expression reads, as text: each whole chain of names, such as index or world.Limits.Most,
    // that is not the target of a call. Two members of one value are two values.
    private static IEnumerable<string> ValuesIn(SyntaxNode expression) =>
        expression
            .DescendantNodesAndSelf()
            .OfType<ExpressionSyntax>()
            .Where(value =>
                value is IdentifierNameSyntax or MemberAccessExpressionSyntax
                && value.IsChainOfNames
                && value.Parent switch
                {
                    MemberAccessExpressionSyntax => false,
                    InvocationExpressionSyntax call => call.Expression != value,
                    _ => true,
                }
            )
            .Select(value => value.WithoutTrivia().ToString());
}

/// <summary>A function that is not public API with two or more null guards.</summary>
public sealed class DefensiveFunctionNullableHeavy : IRule
{
    private const int LeastGuards = 2;

    public string Id => "defensive-function-nullable-heavy";

    public string Message =>
        "A function with two or more null guards that return or throw: test for null once, at the shell.";

    public string Source => "scb-check defensive.yaml: defensive-function-nullable-heavy; STYLE.md, \"Nulls\"";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        // A public API keeps its guards (STYLE.md, "Errors and contracts").
        if (!Function.IsFunction(node) || node.IsPublicApi)
            return [];
        IfStatementSyntax[] guards =
        [
            .. new Function(node)
                .OwnNodes.OfType<IfStatementSyntax>()
                .Where(branch =>
                    branch.Condition.NullTested is not null
                    && branch.Statement.Single is ReturnStatementSyntax or ThrowStatementSyntax
                ),
        ];
        return guards.Length >= LeastGuards ? guards.Select(each => each.Span) : [];
    }
}
