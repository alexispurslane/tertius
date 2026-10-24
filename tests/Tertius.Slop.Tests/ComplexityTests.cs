using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The syntactic cyclomatic complexity of a function (docs/plans/code-quality-tooling.md, decision 16): 1, plus 1 for
/// each construct that branches, and 1 for a whole switch.
/// </summary>
public sealed class ComplexityTests
{
    [Fact]
    public void AFunctionWithNoBranchesHasComplexityOne() =>
        Source.Method("int x = 1; x++;").Complexity.Should().Be(1);

    [Theory]
    [InlineData("if (a) { }")]
    [InlineData("while (a) { }")]
    [InlineData("do { } while (a);")]
    [InlineData("for (int i = 0; i < n; i++) { }")]
    [InlineData("foreach (var x in xs) { }")]
    [InlineData("foreach (var (x, y) in pairs) { }")]
    [InlineData("try { } catch (Exception) { }")]
    [InlineData("var x = a ? 1 : 2;")]
    [InlineData("var x = a ?? b;")]
    [InlineData("a ??= b;")]
    [InlineData("var x = a && b;")]
    [InlineData("var x = a || b;")]
    public void EachBranchingConstructAddsOne(string body) =>
        Source.Method(body).Complexity.Should().Be(2);

    [Theory]
    [InlineData("var x = a?.B;")]
    [InlineData("var x = !a;")]
    [InlineData("var x = a & b;")]
    [InlineData("var x = a | b;")]
    [InlineData("var x = a is not null;")]
    [InlineData("throw new Exception();")]
    [InlineData("try { } finally { }")]
    public void ConstructsThatDoNotBranchAddNothing(string body) =>
        Source.Method(body).Complexity.Should().Be(1);

    [Fact]
    public void AnElseAddsNothingToItsIf() =>
        Source.Method("if (a) { } else { }").Complexity.Should().Be(2);

    [Fact]
    public void EveryIfOfAnElseIfChainAddsOne() =>
        Source.Method("if (a) { } else if (b) { } else if (c) { } else { }").Complexity.Should().Be(4);

    [Fact]
    public void EveryCatchAddsOne() =>
        Source.Method("try { } catch (IOException) { } catch (Exception) { }").Complexity.Should().Be(3);

    [Fact]
    public void ASwitchStatementCountsAsOneBranchWhateverItsSectionsAndGuards() =>
        Source.Method("""
                      switch (x)
                      {
                          case 1:
                          case 2:
                              break;
                          case int n when n > 9 && n < 20:
                              break;
                          case > 100 or < -100:
                              break;
                          default:
                              break;
                      }
                      """).Complexity.Should().Be(2);

    [Fact]
    public void ASwitchExpressionCountsAsOneBranchWhateverItsArmsAndGuards() =>
        Source.Method("""
                      var y = x switch
                      {
                          1 or 2 => "small",
                          > 2 and < 10 => "medium",
                          int n when n > 99 || n < -99 => "far",
                          _ => "other"
                      };
                      """).Complexity.Should().Be(2);

    [Fact]
    public void TheCodeInASwitchSectionStillCounts() =>
        Source.Method("""
                      switch (x)
                      {
                          case 1:
                              if (a) { }
                              break;
                          default:
                              break;
                      }
                      """).Complexity.Should().Be(3, "the switch adds one and the if in its section adds one");

    [Fact]
    public void TheValueOfASwitchArmStillCounts() =>
        Source.Method("""
                      var y = x switch
                      {
                          1 => a ? "a" : "b",
                          _ => "c"
                      };
                      """).Complexity.Should().Be(3,
            "the switch adds one and the conditional expression in its arm adds one");

    [Fact]
    public void TheExpressionThatASwitchStatementReadsStillCounts() =>
        Source.Method("switch (a ?? b) { default: break; }").Complexity.Should().Be(3);

    [Fact]
    public void TheExpressionThatASwitchExpressionReadsStillCounts() =>
        Source.Method("var y = (a ?? b) switch { 1 => 1, _ => 2 };").Complexity.Should().Be(3);

    [Fact]
    public void ANestedSwitchCountsAsItsOwnBranch() =>
        Source.Method("var y = x switch { 1 => z switch { 1 => 1, _ => 2 }, _ => 3 };").Complexity.Should().Be(3);

    [Theory]
    [InlineData("var y = x is 1 or 2;")]
    [InlineData("var y = x is > 0 and < 10;")]
    [InlineData("var y = x is { A: 1 or 2 };")]
    public void APatternCombinatorOutsideASwitchAddsOneLikeALogicalOperator(string body) =>
        Source.Method(body).Complexity.Should().Be(2);

    [Fact]
    public void ALambdaCountsTowardTheFunctionThatHoldsIt() =>
        Source.Method("""
                      Func<int, int> abs = x => x > 0 ? x : -x;
                      if (a) { }
                      """).Complexity.Should().Be(3);

    [Fact]
    public void AnAnonymousMethodCountsTowardTheFunctionThatHoldsIt() =>
        Source.Method("""
                      Func<int, int> abs = delegate (int x) { if (x > 0) return x; return -x; };
                      """).Complexity.Should().Be(2);

    [Fact]
    public void ALocalFunctionIsItsOwnFunction()
    {
        IReadOnlyList<FunctionMeasure> functions = Source.Functions("""
                                                                    class C
                                                                    {
                                                                        void F()
                                                                        {
                                                                            if (a) { }
                                                                            int Twice(int x) => x > 0 && x < 9 ? 2 * x : 0;
                                                                        }
                                                                    }
                                                                    """);

        functions.Select(function => (function.Name, function.Complexity)).Should().Equal(
            ("C.F", 2), ("C.F.Twice", 3));
    }
}
