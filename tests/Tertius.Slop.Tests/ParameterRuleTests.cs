using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity rules of the user's about parameters and what a function returns (docs/plans/code-quality-tooling.md,
/// decision 17): for each rule, code that it flags and a near miss.
/// </summary>
public sealed class ParameterRuleTests
{
    [Theory]
    [InlineData("int Find(int x) { if (x > 0) return x; return -1; }")]
    [InlineData("string Name(int x) { if (x > 0) return names[x]; return \"\"; }")]
    [InlineData("string Name(int x) { if (x > 0) return names[x]; return string.Empty; }")]
    public void ASentinelReturnOutsideATryMethodIsFlagged(string method) =>
        Source.RulesIn($"class C {{ {method} }}").Should().Equal("sentinel-return");

    [Theory]
    [InlineData("int TryFind(int x) { if (x > 0) return x; return -1; }")]
    [InlineData("int CompareTo(C other) { if (other.x > x) return 1; return -1; }")]
    [InlineData("int Offset(int x) { return x - 1; }")]
    [InlineData("string Name(int x) { if (x > 0) return names[x]; return \"none\"; }")]
    public void ATryMethodACompareMethodOrAReturnThatIsNoSentinelIsNotFlagged(string method) =>
        Source.RulesIn($"class C {{ {method} }}").Should().BeEmpty();

    [Fact]
    public void AParameterThatEveryCallSitePassesTheSameConstantIsFlagged() =>
        Source
            .Findings(
                """
                class C
                {
                    void F() { Move(1, true); Move(2, wake: true); }
                    void Move(int x, bool wake) { Use(x, wake, 0); }
                }
                """
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "constant-argument",
                    File = Source.FileName,
                    Line = 4,
                    EndLine = 4,
                }
            );

    [Fact]
    public void AnInternalMethodThatNoFileOutsideSrcNamesIsFlaggedForAConstantArgument() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F() { Move(1, true); Move(2, true); }
                    internal void Move(int x, bool wake) { Use(x, wake, 0); }
                }
                """,
                outside: ""
            )
            .Should()
            .Contain("constant-argument");

    [Theory]
    [InlineData("void F() { Move(1, true); Move(2, false); }")]
    [InlineData("void F(bool w) { Move(1, w); Move(2, true); }")]
    [InlineData("void F(bool w) { Move(1, w); Move(2, w); }")]
    [InlineData("void F() { Move(1, true); Move(2, true); Use(Move); }")]
    public void AParameterWithDifferentArgumentsOrAMethodGroupIsNotFlagged(string caller) =>
        Source
            .RulesIn(
                $$"""
                class C
                {
                    {{caller}}
                    void Move(int x, bool wake) { Use(x, wake, 0); }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void AParameterOfAFunctionWithOneCallIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F() { Move(1, true); }
                    void Move(int x, bool wake) { Use(x, wake, 0); }
                }
                """
            )
            .Should()
            .NotContain("constant-argument", "a second caller may pass another value");

    [Fact]
    public void AParameterOfThePublicApiIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F() { Move(1, true); Move(2, true); }
                    public void Move(int x, bool wake) { Use(x, wake, 0); }
                }
                """,
                outside: ""
            )
            .Should()
            .NotContain("constant-argument", "callers outside the code may vary it");

    [Fact]
    public void AParameterOfAnInternalMethodThatAFileOutsideSrcNamesIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F() { Move(1, true); Move(2, true); }
                    internal void Move(int x, bool wake) { Use(x, wake, 0); }
                }
                """,
                outside: "c.Move(3, false);"
            )
            .Should()
            .BeEmpty("a test may pass another value");

    [Fact]
    public void AnOptionalParameterThatNoCallSiteSetsIsFlagged() =>
        Source
            .Findings(
                """
                class C
                {
                    void F() { Move(1); Move(2); }
                    void Move(int x, bool wake = false) { Use(x, wake, 0); }
                }
                """
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Be(
                new Finding
                {
                    Rule = "unused-optional-parameter",
                    File = Source.FileName,
                    Line = 4,
                    EndLine = 4,
                }
            );

    [Theory]
    [InlineData("void F() { Move(1); Move(2, wake: true); }")]
    [InlineData("void F() { Move(1); Move(2, true); }")]
    [InlineData("void F() { Move(1); Use(Move); }")]
    public void AnOptionalParameterThatACallSiteSetsOrAMethodGroupCanSetIsNotFlagged(string caller) =>
        Source
            .RulesIn(
                $$"""
                class C
                {
                    {{caller}}
                    void Move(int x, bool wake = false) { Use(x, wake, 0); }
                }
                """
            )
            .Should()
            .BeEmpty();

    [Fact]
    public void AnOptionalParameterOfThePublicApiIsNotFlagged() =>
        Source
            .RulesIn(
                """
                class C
                {
                    void F() { Move(1); Move(2); }
                    public void Move(int x, bool wake = false) { Use(x, wake, 0); }
                }
                """,
                outside: ""
            )
            .Should()
            .NotContain("unused-optional-parameter");
}
