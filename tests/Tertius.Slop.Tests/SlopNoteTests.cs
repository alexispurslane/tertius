using System.Text.Json;
using AwesomeAssertions;
using Tertius.Slop.History;

namespace Tertius.Slop.Tests;

/// <summary>
/// The note that records the slop metrics of a commit under refs/notes/slop (docs/plans/code-quality-tooling.md,
/// decision 18): one JSON line with the measures of the commit's tree and where they came from.
/// </summary>
public sealed class SlopNoteTests
{
    internal static readonly DateTimeOffset Noon = new(
        year: 2026,
        month: 10,
        day: 24,
        hour: 12,
        minute: 0,
        second: 0,
        offset: TimeSpan.Zero
    );

    [Fact]
    public void ANoteIsOneJsonLineWithTheMeasuresAndWhereTheyCameFrom() =>
        MeasuredNote()
            .ToLine()
            .Should()
            .Be(
                """{"erosion":0.365,"verbosity":0.036,"highCcMass":12041.5,"highCcFunctionCount":92,"verboseLines":1531,"sloc":34450,"branch":"main","source":"pre-commit","measured":"2026-10-24T12:00:00+00:00"}"""
            );

    [Fact]
    public void AnOldNoteKeepsItsRatiosWithoutInventingAbsoluteCounts()
    {
        const string old =
            """{"erosion":0.365,"verbosity":0.036,"sloc":34450,"branch":"main","source":"pre-commit","measured":"2026-10-24T12:00:00+00:00"}""";

        var note = SlopNote.Parse(old);

        note.Should().NotBeNull();
        note!.ToLine().Should().Be(old);
    }

    [Fact]
    public void ANoteReadsBackFromItsLine() =>
        SlopNote
            .Parse(Note(erosion: 0.365, verbosity: 0.036).ToLine())
            .Should()
            .Be(Note(erosion: 0.365, verbosity: 0.036));

    [Fact]
    public void TheFirstLineThatHoldsANoteIsTheNote() =>
        SlopNote
            .Parse(
                $"not a note\n{Note(erosion: 0.1, verbosity: 0.2).ToLine()}\n{Note(erosion: 0.3, verbosity: 0.4).ToLine()}\n"
            )
            .Should()
            .Be(Note(erosion: 0.1, verbosity: 0.2));

    [Fact]
    public void ABackfilledLineWinsWhenGitMergesItWithAnOldLine()
    {
        string old = Note(erosion: 0.1, verbosity: 0.2).ToLine();
        string enriched = MeasuredNote().ToLine();

        SlopNote.Parse($"{old}\n{enriched}")!.ToLine().Should().Be(enriched);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a note from another tool")]
    [InlineData("""{"erosion":0.1}""")]
    public void TextWithoutANoteHoldsNone(string text) => SlopNote.Parse(text).Should().BeNull();

    [Fact]
    public void ANoteTakesItsMeasuresFromTheReportOfTheTree()
    {
        string branches = string.Concat(Enumerable.Range(0, 11).Select(i => $"if (x == {i}) return {i}; "));
        Codebase codebase = Source.UsedByATest(
            SourceFile.Parse("src/A.cs", $"class A {{ int F(int x) {{ {branches} return 0; }} }}")
        );
        var report = SlopReport.Of(codebase, Clones.FromReport(VerbosityReportTests.Report()));
        report.Erosion.HighCcMass.Should().BeGreaterThan(0);

        var note = SlopNote.Of(report: report, branch: "main", source: NoteSource.PrePush, measured: Noon);

        using var line = JsonDocument.Parse(note.ToLine());
        (
            line.RootElement.GetProperty("highCcMass").GetDouble(),
            line.RootElement.GetProperty("highCcFunctionCount").GetInt32(),
            line.RootElement.GetProperty("verboseLines").GetInt32()
        )
            .Should()
            .Be((report.Erosion.HighCcMass, report.Erosion.HighCcFunctionCount, report.Verbosity.VerboseLines));
    }

    [Theory]
    [InlineData(NoteSource.PreCommit, "pre-commit")]
    [InlineData(NoteSource.PrePush, "pre-push")]
    [InlineData(NoteSource.Seed, "seed")]
    public void TheSourceOfANoteIsNamedInLowerCaseWithDashes(NoteSource source, string name) =>
        (Note(erosion: 0, verbosity: 0) with { Source = source }).ToLine().Should().Contain($"\"source\":\"{name}\"");

    internal static SlopNote Note(double erosion, double verbosity, int sloc = 34450) =>
        new()
        {
            Erosion = erosion,
            Verbosity = verbosity,
            Sloc = sloc,
            Branch = "main",
            Source = NoteSource.PreCommit,
            Measured = Noon,
        };

    private static SlopNote MeasuredNote() =>
        SlopNote.Parse(
            """{"erosion":0.365,"verbosity":0.036,"highCcMass":12041.5,"highCcFunctionCount":92,"verboseLines":1531,"sloc":34450,"branch":"main","source":"pre-commit","measured":"2026-10-24T12:00:00+00:00"}"""
        )!;
}
