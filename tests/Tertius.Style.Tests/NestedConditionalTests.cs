using AwesomeAssertions;
using Microsoft.CodeAnalysis;

namespace Tertius.Style.Tests;

/// <summary>
/// TS0011: a conditional expression whose branch is another conditional expression. The rule reports each chain once,
/// at its outer conditional. A conditional inside a call, a lambda or the condition of another is one simple choice.
/// </summary>
public sealed class NestedConditionalTests
{
    [Fact]
    public async Task AConditionalInTheFalseBranchIsReported() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Pick(bool a, bool b) => a ? 1 : b ? 2 : 3;
                                      }
                                      """)).Should().Equal("TS0011");

    [Fact]
    public async Task AParenthesizedConditionalInTheTrueBranchIsReported() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Pick(bool a, bool b) => a ? (b ? 1 : 2) : 3;
                                      }
                                      """)).Should().Equal("TS0011");

    [Fact]
    public async Task AChainOfThreeIsReportedOnce() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Pick(bool a, bool b, bool c) => a ? 1 : b ? 2 : c ? 3 : 4;
                                      }
                                      """)).Should().Equal("TS0011");

    [Fact]
    public async Task AParenthesizedChainOfThreeIsReportedOnce() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Pick(bool a, bool b, bool c) => a ? 1 : (b ? 2 : (c ? 3 : 4));
                                      }
                                      """)).Should().Equal("TS0011");

    [Fact]
    public async Task AChainInTheConditionOfAnotherConditionalIsReported() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Pick(bool a, bool b, bool c, bool d) => (a ? b : c ? d : a) ? 1 : 2;
                                      }
                                      """)).Should().Equal(["TS0011"],
            "the condition is not a branch, so its chain is its own");

    [Fact]
    public async Task TheReportCoversTheOuterConditional()
    {
        const string source = """
                              public static class C
                              {
                                  public static int Pick(bool a, bool b) => a ? 1 : b ? 2 : 3;
                              }
                              """;

        Diagnostic found = (await Analysis.Diagnostics("TS0011", source)).Single();

        source.Substring(found.Location.SourceSpan.Start, found.Location.SourceSpan.Length).Should()
            .Be("a ? 1 : b ? 2 : 3");
        found.GetMessage().Should().Contain("(STYLE.md, \"Guards\")");
    }

    [Fact]
    public async Task OneConditionalIsNotReported() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Pick(bool a) => a ? 1 : 2;
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task AConditionalInTheConditionIsNotReported() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Pick(bool a, bool b, bool c) => (a ? b : c) ? 1 : 2;
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task AConditionalInsideACallOrALambdaInABranchIsNotReported() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Pick(bool a, bool b) => a ? System.Math.Abs(b ? 1 : -1) : 0;
                                          public static System.Func<bool, int> Make(bool a) => a ? (b => b ? 1 : 2) : (b => 0);
                                      }
                                      """)).Should().BeEmpty();

    [Fact]
    public async Task TwoConditionalsSideBySideAreNotReported() =>
        (await Analysis.Ids("TS0011", """
                                      public static class C
                                      {
                                          public static int Sum(bool a, bool b) => (a ? 1 : 2) + (b ? 3 : 4);
                                      }
                                      """)).Should().BeEmpty();
}
