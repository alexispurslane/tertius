using AwesomeAssertions;
using Tertius.Slop.Rules;

namespace Tertius.Slop.Tests;

/// <summary>
/// The verbosity of a codebase (docs/plans/code-quality-tooling.md, decision 17): the share of its lines of code that a
/// rule flags or that jscpd finds in a clone. A covered line counts once, and a blank line never counts.
/// </summary>
public sealed class VerbosityReportTests
{
    // Lines 1 to 9. Line 6 is blank, so the file has 8 lines of code. manual-min-max flags line 5.
    private const string MinMax = """
        class A
        {
            int F(int a, int b)
            {
                if (a > b) m = a; else m = b;

                return m;
            }
        }
        """;

    [Fact]
    public void TheVerbosityIsTheShareOfTheLinesOfCodeThatAreFlaggedOrCloned()
    {
        // The clone covers lines 4 to 6: line 5 is flagged too, and line 6 is blank.
        Codebase codebase = Source.UsedByATest(SourceFile.Parse("src/A.cs", MinMax));

        var report = VerbosityReport.Of(codebase, Clones.FromReport(Report(("A.cs", 4, 6), ("B.cs", 1, 3))));

        (report.Verbosity, report.VerboseLines, report.FlaggedLines, report.CloneLines, report.Sloc, report.FileCount)
            .Should()
            .Be((2.0 / 8.0, 2, 1, 2, 8, 1));
    }

    [Fact]
    public void TheBlankLinesInsideAFindingDoNotCount()
    {
        Codebase codebase = Source.UsedByATest(
            SourceFile.Parse(
                "src/A.cs",
                """
                class A
                {
                    int F(int a, int b)
                    {
                        if (a > b)

                            m = a;
                        else
                            m = b;
                        return m;
                    }
                }
                """
            )
        );

        var report = VerbosityReport.Of(codebase, Clones.FromReport(Report()));

        (report.FlaggedLines, report.Sloc).Should().Be((4, 11));
    }

    [Fact]
    public void TheLinesOfARepeatingCommentCountAlthoughTheyHoldNoCode()
    {
        Codebase codebase = Source.UsedByATest(
            SourceFile.Parse(
                "src/A.cs",
                """
                class A
                {
                    void F()
                    {
                        Prepare();
                        // Add the body to the world.
                        world.Add(body);
                    }
                }
                """
            )
        );

        var report = VerbosityReport.Of(codebase, Clones.FromReport(Report()));

        (report.FlaggedLines, report.Sloc).Should().Be((1, 8));
    }

    [Fact]
    public void TheCloneLinesOfAFileOutsideTheCodebaseDoNotCount()
    {
        Codebase codebase = Source.UsedByATest(SourceFile.Parse("src/A.cs", MinMax));

        var report = VerbosityReport.Of(codebase, Clones.FromReport(Report(("Other.cs", 1, 9), ("Another.cs", 1, 9))));

        report.CloneLines.Should().Be(0);
    }

    [Fact]
    public void ACodebaseWithNoCodeHasNoVerbosity() =>
        VerbosityReport.Of(new([], Codebase.NamesIn("")), Clones.FromReport(Report())).Verbosity.Should().Be(0);

    [Fact]
    public void TheReportCountsTheFindingsAndLinesOfEveryRuleIncludingTheRulesThatFoundNothing()
    {
        Codebase codebase = Source.UsedByATest(SourceFile.Parse("src/A.cs", MinMax));

        var report = VerbosityReport.Of(codebase, Clones.FromReport(Report()));

        report
            .Rules.Select(rule => rule.Id)
            .Should()
            .Equal(IRule.All.Select(rule => rule.Id).Order(StringComparer.Ordinal));
        report
            .Rules.Should()
            .ContainSingle(rule => rule.Findings > 0)
            .Which.Should()
            .Match<RuleCount>(rule => rule.Id == "manual-min-max" && rule.Findings == 1 && rule.Lines == 1);
    }

    [Fact]
    public void TheTextNamesOnlyTheRulesThatFoundSomething()
    {
        Codebase codebase = Source.UsedByATest(SourceFile.Parse("src/A.cs", MinMax));

        string text = VerbosityReport.Of(codebase, Clones.FromReport(Report())).Describe();

        text.Should().Contain("manual-min-max").And.NotContain("ternary-same-value");
    }

    [Fact]
    public void TheTopFindingsAreTheLongestFirstThenByFileAndLine()
    {
        Codebase codebase = Source.UsedByATest(
            SourceFile.Parse("src/B.cs", "class B { int F() { return a ? x : x; } }"),
            SourceFile.Parse(
                "src/A.cs",
                """
                class A
                {
                    int F() { return a ? x : x; }
                    int G()
                    {
                        if (a > b)
                            m = a;
                        else
                            m = b;
                        return m;
                    }
                }
                """
            )
        );

        var report = VerbosityReport.Of(codebase, Clones.FromReport(Report()));

        report
            .TopFindings.Select(finding => (finding.File, finding.Line, finding.Rule))
            .Should()
            .Equal(
                ("src/A.cs", 6, "manual-min-max"),
                ("src/A.cs", 3, "ternary-same-value"),
                ("src/B.cs", 1, "ternary-same-value")
            );
    }

    [Fact]
    public void TheReportHoldsEveryFindingInTheOrderOfTheSource()
    {
        // 21 findings, one more than the top findings hold.
        string methods = string.Concat(Enumerable.Range(0, 21).Select(i => $"    int F{i}() => a ? x : x;\n"));
        Codebase codebase = Source.UsedByATest(SourceFile.Parse("src/A.cs", $"class A\n{{\n{methods}}}\n"));

        var report = VerbosityReport.Of(codebase, Clones.FromReport(Report()));

        report
            .Findings.Select(finding => (finding.File, finding.Line, finding.EndLine, finding.Rule))
            .Should()
            .Equal(Enumerable.Range(3, 21).Select(line => ("src/A.cs", line, line, "ternary-same-value")));
    }

    [Fact]
    public void TheReportHoldsEveryCloneWithBothOfItsCopies()
    {
        Codebase codebase = Source.UsedByATest(SourceFile.Parse("src/A.cs", MinMax));

        var report = VerbosityReport.Of(codebase, Clones.FromReport(Report(("A.cs", 4, 6), ("B.cs", 1, 3))));

        report
            .Clones.Should()
            .Equal(
                new Clone(
                    new CloneCopy
                    {
                        File = "src/A.cs",
                        Line = 4,
                        EndLine = 6,
                    },
                    new CloneCopy
                    {
                        File = "src/B.cs",
                        Line = 1,
                        EndLine = 3,
                    }
                )
            );
    }

    /// <summary>A jscpd JSON report with one clone per pair of fragments, each a file under src and a line range.</summary>
    internal static string Report(params (string Name, int Start, int End)[] fragments)
    {
        IEnumerable<string> duplicates = fragments
            .Chunk(2)
            .Select(pair =>
                $$"""
                    { "firstFile": { "name": "{{pair[0].Name}}", "start": {{pair[0].Start}}, "end": {{pair[0].End}} },
                      "secondFile": { "name": "{{pair[1].Name}}", "start": {{pair[1].Start}}, "end": {{pair[1].End}} } }
                    """
            );
        return $$"""{ "duplicates": [{{string.Join(", ", duplicates)}}], "statistics": {} }""";
    }
}
