using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity rules about conditions, ported from the scb-check slop rules (docs/plans/code-quality-tooling.md,
/// decision 17): for each rule, code that it flags and a near miss that it leaves alone.
/// </summary>
public class ConditionalRuleTests
{
    [Fact]
    public void AnElseIfThatRepeatsAnEarlierConditionOfItsChainIsFlagged() =>
        Source.RulesInMethod("if (a) { x(); } else if (b) { y(); } else if (a) { z(); }")
            .Should().Equal("duplicated-if-condition");

    [Fact]
    public void AnElseIfChainWithDistinctConditionsIsNotFlagged() =>
        Source.RulesInMethod("if (a) { x(); } else if (b) { y(); }").Should().BeEmpty();

    [Theory]
    [InlineData("m = a > b ? a : b;")]
    [InlineData("m = a < b ? a : b;")]
    [InlineData("m = a >= b ? b : a;")]
    [InlineData("if (a > b) m = a; else m = b;")]
    [InlineData("if (a <= b) { m = b; } else { m = a; }")]
    public void AHandWrittenMinimumOrMaximumIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("manual-min-max");

    [Theory]
    [InlineData("m = a > b ? b + 1 : a;")]
    [InlineData("m = a > b ? a : c;")]
    [InlineData("if (a > b) m = a; else n = b;")]
    [InlineData("m = a == b ? a : b;")]
    public void AChoiceThatIsNotAMinimumOrMaximumIsNotFlaggedAsOne(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();

    [Theory]
    [InlineData("if (a) { if (b) { x(); } }")]
    [InlineData("if (a) if (b) x();")]
    public void AnIfWhoseOnlyStatementIsAnotherIfWithoutElseIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("nested-if-no-else");

    [Theory]
    [InlineData("if (a) { if (b) { x(); } y(); }")]
    [InlineData("if (a) { if (b) { x(); } else { y(); } }")]
    [InlineData("if (a) { if (b) { x(); } } else { y(); }")]
    public void ANestedIfWithASiblingOrAnElseIsNotFlagged(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();

    [Fact]
    public void AConditionalExpressionWithTwoEqualBranchesIsFlagged() =>
        Source.RulesInMethod("y = a ? x + 1 : x + 1;").Should().Equal("ternary-same-value");

    [Fact]
    public void AConditionalExpressionWithTwoDifferentBranchesIsNotFlagged() =>
        Source.RulesInMethod("y = a ? x + 1 : x + 2;").Should().BeEmpty();

    [Theory]
    [InlineData("if (a) return true; else return false;")]
    [InlineData("if (a) { return false; } else { return true; }")]
    [InlineData("x(); if (a) return true; return false;")]
    public void AnIfThatReturnsTrueOrFalseIsFlagged(string body) =>
        Source.RulesIn($$"""
                         class C
                         {
                             bool F()
                             {
                                 {{body}}
                             }
                         }
                         """).Should().Contain("boolean-return-if-else");

    [Theory]
    [InlineData("if (a) return true; return b;")]
    [InlineData("if (!a) return false; if (!b) return false; return true;")]
    public void AnIfThatReturnsABooleanBesideAnotherValueOrAtTheEndOfAGuardListIsNotFlagged(string body) =>
        Source.RulesIn($$"""
                         class C
                         {
                             bool F()
                             {
                                 {{body}}
                             }
                         }
                         """).Should().BeEmpty("a list of guards is the form that STYLE.md prefers");

    [Theory]
    [InlineData("return a ? true : false;")]
    [InlineData("return a ? false : true;")]
    public void AConditionalExpressionThatGivesTrueOrFalseIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("redundant-bool-ternary");

    [Fact]
    public void AConditionalExpressionWithOneBooleanLiteralIsNotFlagged() =>
        Source.RulesInMethod("return a ? true : b;").Should().BeEmpty();

    [Theory]
    [InlineData("if (a) return x; return x;")]
    [InlineData("if (a) { return x + 1; } return x + 1;")]
    public void AGuardThatReturnsWhatTheNextStatementReturnsIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("redundant-guard-same-return");

    [Fact]
    public void AGuardThatReturnsSomethingElseIsNotFlagged() =>
        Source.RulesInMethod("if (a) return x; return y;").Should().BeEmpty();

    [Theory]
    [InlineData("if (a) { return 1; } else { x(); }")]
    [InlineData("if (a) throw new E(); else x();")]
    public void AnElseAfterABranchThatReturnsOrThrowsIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("unnecessary-else");

    [Fact]
    public void AnElseAfterABranchThatFallsThroughIsNotFlagged() =>
        Source.RulesInMethod("if (a) { y(); } else { x(); }").Should().BeEmpty();

    [Fact]
    public void AnIfWithAnEmptyBranchAndAnElseIsFlagged() =>
        Source.RulesInMethod("if (a) { } else { x(); }").Should().Equal("empty-if-branch");

    [Fact]
    public void AnIfWithStatementsInBothBranchesIsNotFlaggedAsEmpty() =>
        Source.RulesInMethod("if (a) { y(); } else { x(); }").Should().BeEmpty();

    [Theory]
    [InlineData("if (s is Circle c) { x(c); } else if (s is Square q) { y(q); }")]
    [InlineData("if (s is Circle) x(); if (s is Square { Side: > 1 }) y();")]
    public void TwoTypeTestsOfTheSameValueInARowAreFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("type-probe-ladder");

    [Theory]
    [InlineData("if (s is Circle) x(); if (t is Square) y();")]
    [InlineData("if (s is null) x(); if (s is { Length: > 0 }) y();")]
    [InlineData("if (s is Circle) x(); z(); if (s is Square) y();")]
    public void TypeTestsOfDifferentValuesOrNotInARowAreNotFlagged(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();

    [Theory]
    [InlineData("if (op == \"add\") x(); else if (op == \"sub\") y();")]
    [InlineData("if (op == \"add\") x(); if (op == \"sub\") y();")]
    public void BranchesThatCompareOneValueWithStringsAreFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("dispatch-by-string-equality");

    [Fact]
    public void BranchesThatCompareDifferentValuesWithStringsAreNotFlagged() =>
        Source.RulesInMethod("if (op == \"add\") x(); else if (mode == \"sub\") y();").Should().BeEmpty();

    [Theory]
    [InlineData("if (k == 1) return 10; if (k == 2) return 20; if (k == 3) return 30; return 0;")]
    [InlineData("if (k == Kind.A) { return 10; } else if (k == Kind.B) { return 20; } else if (k == Kind.C) { return 30; }")]
    public void ThreeBranchesThatReturnByComparingOneValueAreFlagged(string body) =>
        Source.RulesInMethod(body).Should().Contain("value-compare-return-ladder");

    [Fact]
    public void TwoBranchesThatReturnByComparingOneValueAreNotFlagged() =>
        Source.RulesInMethod("if (k == 1) return 10; if (k == 2) return 20; return 0;").Should().BeEmpty();

    [Theory]
    [InlineData("if (x is null) x = y;")]
    [InlineData("if (x == null) { x = y; }")]
    public void AnIfThatAssignsANullVariableIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("verbose-none-default");

    [Theory]
    [InlineData("if (x is null) z = y;")]
    [InlineData("if (x is null) { x = y; z(); }")]
    public void AnIfThatAssignsAnotherVariableOrDoesMoreIsNotFlagged(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();

    [Theory]
    [InlineData("y = x != null ? x : d;")]
    [InlineData("y = x is null ? d : x;")]
    [InlineData("y = x == null ? d : x;")]
    [InlineData("y = x is not null ? x : d;")]
    public void AConditionalExpressionThatTestsForNullAndKeepsTheValueIsFlagged(string body) =>
        Source.RulesInMethod(body).Should().Equal("manual-null-coalescing");

    [Theory]
    [InlineData("y = x != null ? x.Value : d;")]
    [InlineData("y = x != null ? d : x;")]
    public void AConditionalExpressionThatTestsForNullAndChangesTheValueIsNotFlagged(string body) =>
        Source.RulesInMethod(body).Should().BeEmpty();
}
