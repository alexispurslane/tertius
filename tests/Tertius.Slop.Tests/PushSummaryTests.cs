using AwesomeAssertions;
using Tertius.Slop.History;

namespace Tertius.Slop.Tests;

/// <summary>
/// The summary that the pre-push hook prints (docs/plans/code-quality-tooling.md, decision 18): the metrics at the last
/// push against now, and the pushed commits whose notes raised a metric the most.
/// </summary>
public sealed class PushSummaryTests
{
    [Fact]
    public void TheSummaryComparesTheLastPushWithNow()
    {
        var summary = PushSummary.Of(
            lastPush: SlopNoteTests.Note(erosion: 0.365, verbosity: 0.036, sloc: 34450),
            now: SlopNoteTests.Note(erosion: 0.371, verbosity: 0.035, sloc: 34500),
            pushed: []
        );

        summary
            .Describe()
            .Should()
            .Contain("Erosion 0.3710 (+0.0060)")
            .And.Contain("Verbosity 0.0350 (-0.0010)")
            .And.Contain("SLOC 34500 [+50]");
    }

    [Fact]
    public void ACommitRaisesAMetricByTheChangeFromTheNearestNoteBeforeIt()
    {
        Commit first = Pushed("1", "ter-aaaa: first", highCcMass: 30);
        Commit unnoted = Pushed("2", "docs only", highCcMass: null);
        Commit third = Pushed("3", "ter-cccc: third", highCcMass: 32);

        var summary = PushSummary.Of(
            lastPush: AbsoluteNote(
                erosion: 0.31,
                verbosity: 0,
                highCcMass: 31,
                highCcFunctionCount: 10,
                verboseLines: 30
            ),
            now: third.Note!,
            pushed: [first, unnoted, third]
        );

        summary.HighCcMassRaises.Should().Equal(new CommitRaise(third, 2));
    }

    [Fact]
    public void AtMostThreeCommitsThatRaiseAMetricShowTheLargestFirst()
    {
        Commit[] pushed =
        [
            Pushed("1", "a", highCcMass: 31),
            Pushed("2", "b", highCcMass: 35),
            Pushed("3", "c", highCcMass: 36),
            Pushed("4", "d", highCcMass: 38),
            Pushed("5", "e", highCcMass: 38.5),
        ];

        var summary = PushSummary.Of(
            lastPush: AbsoluteNote(
                erosion: 0.30,
                verbosity: 0,
                highCcMass: 30,
                highCcFunctionCount: 10,
                verboseLines: 30
            ),
            now: pushed[^1].Note!,
            pushed: pushed
        );

        summary.HighCcMassRaises.Select(raise => raise.Commit.Sha).Should().Equal("2", "4", "1");
    }

    [Fact]
    public void ARaiseTooSmallToShowInTheSummaryIsNoRaise()
    {
        Commit tiny = Pushed("1", "a", highCcMass: 30.04);
        Commit shown = Pushed("2", "b", highCcMass: 30.10);

        var summary = PushSummary.Of(
            lastPush: AbsoluteNote(
                erosion: 0.30,
                verbosity: 0,
                highCcMass: 30,
                highCcFunctionCount: 10,
                verboseLines: 30
            ),
            now: shown.Note!,
            pushed: [tiny, shown]
        );

        summary.HighCcMassRaises.Select(raise => raise.Commit.Sha).Should().Equal("2");
    }

    [Fact]
    public void TheSummaryNamesTheTicketsAndTheSubjectOfACommitThatRaisedAMetric()
    {
        Commit raiser = Pushed("abcdef123456", "ter-2x99: the verbosity", highCcMass: 40);

        string text = PushSummary
            .Of(
                lastPush: AbsoluteNote(
                    erosion: 0.30,
                    verbosity: 0,
                    highCcMass: 30,
                    highCcFunctionCount: 10,
                    verboseLines: 30
                ),
                now: raiser.Note!,
                pushed: [raiser]
            )
            .Describe();

        text.Should().Contain("+10.0  abcdef1  ter-2x99  ter-2x99: the verbosity");
    }

    [Fact]
    public void ACommitRaisesTheVerbosityByTheChangeOfTheVerbosityAlone()
    {
        Commit raiser = SlopHistoryTests.Commit(
            "ter-2x99: more words",
            AbsoluteNote(erosion: 0.30, verbosity: 0.05, highCcMass: 30, highCcFunctionCount: 10, verboseLines: 50)
        ) with
        {
            Sha = "abcdef123456",
        };

        var summary = PushSummary.Of(
            lastPush: AbsoluteNote(
                erosion: 0.30,
                verbosity: 0.03,
                highCcMass: 30,
                highCcFunctionCount: 10,
                verboseLines: 30
            ),
            now: raiser.Note!,
            pushed: [raiser]
        );

        summary.HighCcMassRaises.Should().BeEmpty();
        summary
            .VerboseLineRaises.Select(raise => (raise.Commit.Sha, raise.Change))
            .Should()
            .Equal(("abcdef123456", 20.0));
    }

    [Fact]
    public void TheSummarySaysSoWhenACommitThatRaisedAMetricNamesNoTicket()
    {
        Commit raiser = Pushed("abcdef123456", "Clean up code before push", highCcMass: 40);

        string text = PushSummary
            .Of(
                lastPush: AbsoluteNote(
                    erosion: 0.30,
                    verbosity: 0,
                    highCcMass: 30,
                    highCcFunctionCount: 10,
                    verboseLines: 30
                ),
                now: raiser.Note!,
                pushed: [raiser]
            )
            .Describe();

        text.Should().Contain("+10.0  abcdef1  (no ticket)  Clean up code before push");
    }

    [Fact]
    public void WithoutAnEarlierNoteTheSummaryShowsNowAndNoRaises()
    {
        Commit only = Pushed("1", "first", highCcMass: 40);

        var summary = PushSummary.Of(lastPush: null, now: only.Note!, pushed: [only]);

        summary.HighCcMassRaises.Should().BeEmpty();
        summary.VerboseLineRaises.Should().BeEmpty();
        summary.Describe().Should().Contain("high CC mass 40.0").And.Contain("no note before");
    }

    [Fact]
    public void TheSummaryShowsAbsoluteCountsBesideRatiosAndTheirChanges()
    {
        SlopNote before = AbsoluteNote(
            erosion: 0.30,
            verbosity: 0.04,
            highCcMass: 100,
            highCcFunctionCount: 10,
            verboseLines: 30
        );
        SlopNote after = AbsoluteNote(
            erosion: 0.25,
            verbosity: 0.03,
            highCcMass: 120,
            highCcFunctionCount: 12,
            verboseLines: 40
        );

        string text = PushSummary.Of(lastPush: before, now: after, pushed: []).Describe();

        text.Should()
            .Contain("Erosion 0.2500 (-0.0500) (high CC mass 120.0 [+20.0]; high CC functions 12 [+2])")
            .And.Contain("Verbosity 0.0300 (-0.0100) (verbose lines 40 [+10])");
    }

    [Fact]
    public void TheSummaryRanksAbsoluteRisesEvenWhenDilutionLowersBothRatios()
    {
        SlopNote before = AbsoluteNote(
            erosion: 0.30,
            verbosity: 0.04,
            highCcMass: 100,
            highCcFunctionCount: 10,
            verboseLines: 30
        );
        Commit diluted = SlopHistoryTests.Commit(
            "ter-ab12: more code",
            AbsoluteNote(erosion: 0.25, verbosity: 0.03, highCcMass: 120, highCcFunctionCount: 12, verboseLines: 40)
        ) with
        {
            Sha = "abcdef123456",
        };

        string text = PushSummary.Of(lastPush: before, now: diluted.Note!, pushed: [diluted]).Describe();

        text.Should()
            .Contain("the commits that raised the high CC mass the most:")
            .And.Contain("the commits that raised the verbose lines the most:")
            .And.Contain("+20.0  abcdef1")
            .And.Contain("+10  abcdef1");
    }

    [Fact]
    public void AnOldBaselineShowsNewAbsoluteCountsWithoutAFalseChange()
    {
        SlopNote now = AbsoluteNote(
            erosion: 0.25,
            verbosity: 0.03,
            highCcMass: 120,
            highCcFunctionCount: 12,
            verboseLines: 40
        );
        Commit first = SlopHistoryTests.Commit("ter-ab12: first new note", now);

        string text = PushSummary
            .Of(lastPush: SlopNoteTests.Note(erosion: 0.30, verbosity: 0.04), now: now, pushed: [first])
            .Describe();

        text.Should()
            .Contain("high CC mass 120.0")
            .And.Contain("verbose lines 40")
            .And.NotContain("high CC mass 0.0 ->")
            .And.NotContain("the commits that raised the high CC mass");
    }

    [Fact]
    public void AllFourRatiosLeadWithTheirChangesBeforeTheCountsAndWarnings()
    {
        SlopNote now = SlopNoteTests.Note(0.25, 0.03) with
        {
            ClassMetrics = new(
                Provenance: new("v1", "refs"),
                Tcc: new(2, 5),
                TypeCount: 2,
                BindingErrorCount: 7,
                Depth: new(8, 4)
            ),
        };
        SlopNote before = now with { ClassMetrics = now.ClassMetrics with { Tcc = new(1, 4), Depth = new(6, 3) } };

        string text = PushSummary.Of(lastPush: before, now: now, pushed: []).Describe();

        text.Should()
            .StartWith("Project metrics since the last push:")
            .And.Contain("TCC 0.4000 (+0.1500) (2/5 shared/possible method pairs; shared pairs +1, possible pairs +1)")
            .And.Contain("Depth 2.0000 (+0.0000) (8/4 WMC/API entries; WMC +2, API entries +1)");
        text.IndexOf("Erosion", StringComparison.Ordinal)
            .Should()
            .BeLessThan(text.IndexOf("Verbosity", StringComparison.Ordinal));
        text.IndexOf("Verbosity", StringComparison.Ordinal)
            .Should()
            .BeLessThan(text.IndexOf("TCC", StringComparison.Ordinal));
        text.IndexOf("TCC", StringComparison.Ordinal)
            .Should()
            .BeLessThan(text.IndexOf("Depth", StringComparison.Ordinal));
        text.IndexOf("Depth", StringComparison.Ordinal)
            .Should()
            .BeLessThan(text.IndexOf("Binding errors", StringComparison.Ordinal));
        text.Split("Binding errors").Length.Should().Be(2);
    }

    private static SlopNote AbsoluteNote(
        double erosion,
        double verbosity,
        double highCcMass,
        int highCcFunctionCount,
        int verboseLines
    ) =>
        SlopNote.Parse(
            $$"""{"erosion":{{erosion}},"verbosity":{{verbosity}},"highCcMass":{{highCcMass}},"highCcFunctionCount":{{highCcFunctionCount}},"verboseLines":{{verboseLines}},"sloc":34450,"branch":"main","source":"pre-commit","measured":"2026-10-24T12:00:00+00:00"}"""
        )!;

    private static Commit Pushed(string sha, string message, double? highCcMass) =>
        SlopHistoryTests.Commit(
            message,
            highCcMass is { } value
                ? AbsoluteNote(
                    erosion: 0.30,
                    verbosity: 0,
                    highCcMass: value,
                    highCcFunctionCount: 10,
                    verboseLines: 30
                )
                : null
        ) with
        {
            Sha = sha,
        };
}
