using AwesomeAssertions;
using Tertius.Slop.History;

namespace Tertius.Slop.Tests;

/// <summary>
/// The summary that the pre-push hook prints (docs/plans/code-quality-tooling.md, decision 18): the metrics at the last
/// push against now, and the pushed commits whose notes raised a metric the most.
/// </summary>
public class PushSummaryTests
{
    [Fact]
    public void TheSummaryComparesTheLastPushWithNow()
    {
        PushSummary summary = PushSummary.Of(lastPush: SlopNoteTests.Note(erosion: 0.365, verbosity: 0.036, sloc: 34450),
            now: SlopNoteTests.Note(erosion: 0.371, verbosity: 0.035, sloc: 34500), pushed: []);

        summary.Describe().Should()
            .Contain("erosion    0.3650 -> 0.3710  +0.0060").And
            .Contain("verbosity  0.0360 -> 0.0350  -0.0010").And
            .Contain("sloc       34450 -> 34500  +50");
    }

    [Fact]
    public void ACommitRaisesAMetricByTheChangeFromTheNearestNoteBeforeIt()
    {
        Commit first = Pushed("1", "ter-aaaa: first", erosion: 0.30);
        Commit unnoted = Pushed("2", "docs only", erosion: null);
        Commit third = Pushed("3", "ter-cccc: third", erosion: 0.32);

        PushSummary summary = PushSummary.Of(lastPush: SlopNoteTests.Note(erosion: 0.31, verbosity: 0),
            now: third.Note!, pushed: [first, unnoted, third]);

        summary.ErosionRaises.Should().Equal(new CommitRaise(third, 0.32 - 0.30));
    }

    [Fact]
    public void AtMostThreeCommitsThatRaiseAMetricShowTheLargestFirst()
    {
        Commit[] pushed =
        [
            Pushed("1", "a", erosion: 0.31), Pushed("2", "b", erosion: 0.35), Pushed("3", "c", erosion: 0.36),
            Pushed("4", "d", erosion: 0.38), Pushed("5", "e", erosion: 0.385)
        ];

        PushSummary summary = PushSummary.Of(lastPush: SlopNoteTests.Note(erosion: 0.30, verbosity: 0),
            now: pushed[^1].Note!, pushed: pushed);

        summary.ErosionRaises.Select(raise => raise.Commit.Sha).Should().Equal("2", "4", "1");
    }

    [Fact]
    public void TheSummaryNamesTheTicketsAndTheSubjectOfACommitThatRaisedAMetric()
    {
        Commit raiser = Pushed("abcdef123456", "ter-2x99: the verbosity", erosion: 0.40);

        string text = PushSummary.Of(lastPush: SlopNoteTests.Note(erosion: 0.30, verbosity: 0), now: raiser.Note!,
            pushed: [raiser]).Describe();

        text.Should().Contain("+0.1000  abcdef1  ter-2x99  ter-2x99: the verbosity");
    }

    [Fact]
    public void WithoutAnEarlierNoteTheSummaryShowsNowAndNoRaises()
    {
        Commit only = Pushed("1", "first", erosion: 0.40);

        PushSummary summary = PushSummary.Of(lastPush: null, now: only.Note!, pushed: [only]);

        summary.ErosionRaises.Should().BeEmpty();
        summary.VerbosityRaises.Should().BeEmpty();
        summary.Describe().Should().Contain("erosion 0.4000").And.Contain("no note before");
    }

    private static Commit Pushed(string sha, string message, double? erosion) =>
        SlopHistoryTests.Commit(message, erosion is { } value ? SlopNoteTests.Note(erosion: value, verbosity: 0) : null)
            with { Sha = sha };
}
