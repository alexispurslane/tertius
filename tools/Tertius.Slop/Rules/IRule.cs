using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

/// <summary>A verbosity rule.</summary>
public interface IRule
{
    /// <summary>
    /// Every rule: the ports of the scb-check slop rules that have a meaning in C#, then the rules of the user's
    /// (docs/plans/code-quality-tooling.md, decision 17, lists the rules that were left out and why).
    /// </summary>
    static readonly IReadOnlyList<IRule> All =
    [
        new DuplicatedIfCondition(), new ManualMinMax(), new NestedIfNoElse(), new TernarySameValue(),
        new BooleanReturnIfElse(), new RedundantBoolTernary(), new RedundantGuardSameReturn(), new UnnecessaryElse(),
        new EmptyIfBranch(), new TypeProbeLadder(), new DispatchByStringEquality(), new ValueCompareReturnLadder(),
        new VerboseNoneDefault(), new ManualNullCoalescing(), new DefensiveTrySoupFunction(),
        new DefensiveExceptExceptionHeavy(), new ExceptReturnStaticSentinel(), new ExceptBareReraise(),
        new ExceptPassSilence(), new DefensiveFunctionNullableHeavy(), new UnnecessaryLambda(), new RedundantContinue(),
        new RedundantReturn(), new EmptyConstructor(), new ManualStrJoin(), new LongTupleUnpacking(),
        new LowUseShortFunction(), new SectionBannerComment(), new PassThroughMethod(), new SingleUseLocal(),
        new CommentRepeatsCode(), new GuardRepeatsCallerCheck()
    ];

    /// <summary>The id.</summary>
    string Id { get; }

    /// <summary>What the rule flags and what to write instead, in one line.</summary>
    string Message { get; }

    /// <summary>Where the rule comes from.</summary>
    string Source { get; }

    /// <summary>The spans that the rule flags at <paramref name="node"/>.</summary>
    IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context);
}
