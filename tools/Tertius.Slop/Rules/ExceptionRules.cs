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
    public string Message => "A function with two or more clauses that catch every exception: catch what can happen, once.";
    public string Source => "scb-check defensive.yaml: defensive-except-exception-heavy";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (!Function.IsFunction(node))
            return [];
        CatchClauseSyntax[] catchAlls = [.. new Function(node).OwnNodes.OfType<CatchClauseSyntax>().Where(each => each.CatchesAll)];
        return catchAlls.Length >= LeastCatchAlls ? catchAlls.Select(each => each.Span) : [];
    }
}

/// <summary>A clause that catches every exception and only returns a constant.</summary>
public sealed class ExceptReturnStaticSentinel : IRule
{
    public string Id => "except-return-static-sentinel";
    public string Message => "A clause that catches every exception and returns a constant: catch something specific or let it go.";
    public string Source =>
        "scb-check defensive.yaml: except-return-static-sentinel, sentinel-fallback-function, parse-and-fallback-exception";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        CatchClauseSyntax { Block.Statements: [ReturnStatementSyntax { Expression: { IsConstantValue: true } }] } clause
            when clause.CatchesAll => [clause.Span],
        _ => []
    };
}

/// <summary>A catch clause that only rethrows.</summary>
public sealed class ExceptBareReraise : IRule
{
    public string Id => "except-bare-reraise";
    public string Message => "A catch clause that only rethrows: drop it.";
    public string Source => "scb-check misc.yaml: except-bare-reraise";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        // A rethrow before a later catch keeps that exception out of the later, broader catch, so it stays.
        CatchClauseSyntax { Block.Statements: [ThrowStatementSyntax { Expression: null }], Parent: TryStatementSyntax owner } clause
            when owner.Catches.Last() == clause => [clause.Span],
        _ => []
    };
}

/// <summary>An empty catch clause.</summary>
public sealed class ExceptPassSilence : IRule
{
    public string Id => "except-pass-silence";
    public string Message => "An empty catch clause swallows the error: handle it or let it go.";
    public string Source => "scb-check misc.yaml: except-pass-silence, swallow-exception-pass-multi-stmt";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        CatchClauseSyntax { Block.Statements.Count: 0 } clause => [clause.Span],
        _ => []
    };
}

/// <summary>A function that is not public API with two or more null guards.</summary>
public sealed class DefensiveFunctionNullableHeavy : IRule
{
    private const int LeastGuards = 2;

    public string Id => "defensive-function-nullable-heavy";
    public string Message => "A function with two or more null guards that return or throw: test for null once, at the shell.";
    public string Source => "scb-check defensive.yaml: defensive-function-nullable-heavy; STYLE.md, \"Nulls\"";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        // A public API keeps its guards (STYLE.md, "Errors and contracts").
        if (!Function.IsFunction(node) || node.IsPublicApi)
            return [];
        IfStatementSyntax[] guards =
        [
            .. new Function(node).OwnNodes.OfType<IfStatementSyntax>().Where(branch =>
                branch.Condition.NullTested is not null && branch.Statement.Single is ReturnStatementSyntax or ThrowStatementSyntax)
        ];
        return guards.Length >= LeastGuards ? guards.Select(each => each.Span) : [];
    }
}
