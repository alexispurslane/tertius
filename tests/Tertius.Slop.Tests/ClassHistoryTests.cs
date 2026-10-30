using System.Text.Json;
using AwesomeAssertions;
using Tertius.Slop.History;

namespace Tertius.Slop.Tests;

/// <summary>Class counts travel through measured notes, merged history and resumable backfill without changing old data.</summary>
public sealed class ClassHistoryTests
{
    [Fact]
    public void AnUnknownNoteSourceCannotBecomeMeasurementMetadata()
    {
        SlopNote.SourceNamed("unknown hook").Should().BeNull();
    }

    [Fact]
    public void PartialBindingIsVisibleInTheTextSummary()
    {
        var summary = Report().ClassMetrics!.Summary with { BindingErrorCount = 3 };

        summary.DescribeAgainst(null).Should().Contain("Binding errors: 3; counts use available symbols");
    }

    [Fact]
    public void NewNotesKeepCountsAndConfigurationWithoutTypeDetails()
    {
        var report = Report();
        var note = SlopNote.Of(report: report, branch: "topic", source: NoteSource.Seed, measured: SlopNoteTests.Noon);
        using var json = JsonDocument.Parse(note.ToLine());

        json.RootElement.TryGetProperty("classMetrics", out JsonElement metrics).Should().BeTrue();
        metrics.GetProperty("tcc").GetProperty("denominator").GetInt32().Should().Be(1);
        metrics.GetProperty("depth").GetProperty("numerator").GetInt32().Should().Be(2);
        metrics.GetProperty("depth").GetProperty("denominator").GetInt32().Should().Be(2);
        metrics.GetProperty("provenance").GetProperty("version").GetString().Should().NotBeNullOrEmpty();
        metrics.TryGetProperty("types", out _).Should().BeFalse();
        metrics.TryGetProperty("methodGroupCount", out _).Should().BeFalse();
    }

    [Fact]
    public void BackfillEnrichesEveryLineAndPreservesExistingCountsAndUnknownMetadata()
    {
        string old = SlopNoteTests.Note(erosion: 0.4, verbosity: 0.3).ToLine();
        string partial = old.Replace(
            "\"sloc\":",
            "\"highCcMass\":123,\"future\":{\"tag\":42},\"sloc\":",
            StringComparison.Ordinal
        );
        string original = $"other text\n{old}\n{partial}\nkeep this";

        string filled = SlopNote.BackfillText(original, Report());

        SlopNote.NeedsBackfill(filled).Should().BeFalse();
        string[] lines = filled.Split('\n');
        (lines[0], lines[^1]).Should().Be(("other text", "keep this"));
        using var json = JsonDocument.Parse(lines[2]);
        json.RootElement.GetProperty("highCcMass").GetDouble().Should().Be(123);
        json.RootElement.GetProperty("future").GetProperty("tag").GetInt32().Should().Be(42);
        json.RootElement.TryGetProperty("classMetrics", out _).Should().BeTrue();
        SlopNote.Parse(filled)!.Erosion.Should().Be(0.4);
        SlopNote.BackfillText(filled, Report()).Should().Be(filled);
    }

    [Fact]
    public void ACompleteClassLineWinsOverALegacyLineWithAbsoluteCounts()
    {
        string old = SlopNoteTests.Note(erosion: 0.4, verbosity: 0.3).ToLine();
        string legacy = old.Replace(
            "\"sloc\":",
            "\"highCcMass\":123,\"highCcFunctionCount\":2,\"verboseLines\":4,\"sloc\":",
            StringComparison.Ordinal
        );
        string complete = SlopNote.BackfillText(old, Report());

        SlopNote.Parse($"{legacy}\n{complete}")!.ToLine().Should().Be(complete);
    }

    [Fact]
    public void CoherenceMeasuresHistoricalSourceAndKeepsNamedDetail()
    {
        using var git = new GitFolder();
        git.Write("src/A.cs", "class A { int x; public int F() => x; public int G() => x; }");
        git.Commit("source");
        git.Write("src/A.cs", "class Changed {}");
        using var output = new StringWriter();
        using var error = new StringWriter();
        string path = Path.Combine(git.Root, "details.json");

        int exit = Program.Run(
            args: ["-C", git.Root, "measure", "HEAD", "--branch", "topic", "--source", "seed", "--details", path],
            output: output,
            error: error,
            jscpd: "jscpd"
        );

        exit.Should().Be(0, error.ToString());
        using var note = JsonDocument.Parse(output.ToString());
        using var details = JsonDocument.Parse(File.ReadAllText(path));
        note.RootElement.GetProperty("classMetrics")
            .GetProperty("tcc")
            .GetProperty("numerator")
            .GetInt32()
            .Should()
            .Be(1);
        details
            .RootElement.GetProperty("report")
            .GetProperty("classMetrics")
            .GetProperty("types")
            .EnumerateArray()
            .Single()
            .GetProperty("sharedPairs")
            .GetArrayLength()
            .Should()
            .Be(1);
    }

    [Fact]
    public void HistoryKeepsPooledCountsAlongsideLegacyColumns()
    {
        var now = SlopNote.Of(report: Report(), branch: "topic", source: NoteSource.Seed, measured: SlopNoteTests.Noon);
        var commit = new Commit
        {
            Sha = "abcdefg",
            FirstParent = null,
            Date = SlopNoteTests.Noon,
            Message = "test",
            Note = now,
        };

        string csv = new SlopHistory([commit]).Csv;

        csv.Should().Contain("sharedMethodPairs").And.Contain("classMetricVersion").And.NotContain("methodGroupCount");
        csv.Split('\n')[1].Should().Contain("1.0000,1,1");
        string summary = PushSummary.Of(lastPush: SlopNoteTests.Note(0, 0), now: now, pushed: []).Describe();
        summary
            .Should()
            .Contain("TCC 1.0000")
            .And.Contain("Depth 1.0000 (2/2 WMC/API entries)")
            .And.NotContain("Method groups");
        csv.Should().Contain("depth,wmc,apiEntries");
    }

    [Fact]
    public void PushSummaryRefusesDeltasAcrossDifferentReferences()
    {
        var now = SlopNote.Of(report: Report(), branch: "topic", source: NoteSource.Seed, measured: SlopNoteTests.Noon);
        var before = now with
        {
            ClassMetrics = now.ClassMetrics! with
            {
                Provenance = now.ClassMetrics!.Provenance with { References = "other metadata" },
            },
        };

        string text = PushSummary.Of(lastPush: before, now: now, pushed: []).Describe();

        text.Should().Contain("configuration differs").And.NotContain("shared pairs +");
    }

    [Fact]
    public void LegacyNotesWithAllOlderCountsStillNeedClassBackfill()
    {
        string legacy = SlopNoteTests
            .Note(erosion: 0.4, verbosity: 0.3)
            .ToLine()
            .Replace(
                "\"sloc\":",
                "\"highCcMass\":123,\"highCcFunctionCount\":2,\"verboseLines\":4,\"sloc\":",
                StringComparison.Ordinal
            );

        SlopNote.NeedsBackfill(legacy).Should().BeTrue();
        string filled = SlopNote.BackfillText(legacy, Report());

        SlopNote.Parse(filled)!.ClassMetrics.Should().NotBeNull();
        SlopNote.Parse(filled)!.HighCcMass.Should().Be(123);
        SlopNote.NeedsBackfill(filled).Should().BeFalse();
    }

    [Fact]
    public void BackfillAddsMissingDepthWithoutReplacingExistingCohesion()
    {
        var report = Report();
        var note = SlopNote.Of(report: report, branch: "topic", source: NoteSource.Seed, measured: SlopNoteTests.Noon);
        string partial = (
            note with
            {
                ClassMetrics = note.ClassMetrics! with { Depth = null, Tcc = new(0, 7) },
            }
        ).ToLine();

        SlopNote.NeedsBackfill(partial).Should().BeTrue();
        string filled = SlopNote.BackfillText(partial, report);

        SlopNote.Parse(filled)!.ClassMetrics!.Depth.Should().Be(new ClassRatio(2, 2));
        SlopNote.Parse(filled)!.ClassMetrics!.Tcc.Should().Be(new ClassRatio(0, 7));
        SlopNote.NeedsBackfill(filled).Should().BeFalse();
        SlopNote.BackfillText(filled, report).Should().Be(filled);
    }

    [Fact]
    public void DepthShowsRawChangesButLegacyDepthHasNoComparison()
    {
        var now = Report().ClassMetrics!.Summary;
        var before = now with { Depth = new(1, 1) };

        now.DescribeAgainst(before)
            .Should()
            .Contain("Depth 1.0000 (2/2 WMC/API entries) (+0.0000) (WMC +1, API entries +1)");
        now.DescribeAgainst(before with { Depth = null }).Should().NotContain("WMC +");
        (now with { Depth = null }).DescribeAgainst(before).Should().Contain("Depth unavailable (legacy note)");
    }

    [Fact]
    public void CompleteDepthWinsOverAnEarlierPartialSummary()
    {
        var note = SlopNote.Of(
            report: Report(),
            branch: "topic",
            source: NoteSource.Seed,
            measured: SlopNoteTests.Noon
        );
        string partial = (note with { ClassMetrics = note.ClassMetrics! with { Depth = null } }).ToLine();

        SlopNote.Parse($"{partial}\n{note.ToLine()}")!.ClassMetrics!.Depth.Should().Be(new ClassRatio(2, 2));
    }

    [Fact]
    public void ZeroSurfaceKeepsRawWeightInNotesAndCsv()
    {
        var report = SlopReport.Of(
            Source.UsedByATest(SourceFile.Parse("src/A.cs", "class A { private void F() {} }")),
            Clones.FromReport(VerbosityReportTests.Report())
        );
        var note = SlopNote.Of(report: report, branch: "topic", source: NoteSource.Seed, measured: SlopNoteTests.Noon);

        SlopNote.Parse(note.ToLine())!.ClassMetrics!.Depth.Should().Be(new ClassRatio(1, 0));
        string csv = new SlopHistory([SlopHistoryTests.Commit("empty surface", note)]).Csv;
        csv.Should().Contain("n/a,0,0,n/a,1,0,");
        note.ClassMetrics!.DescribeAgainst(null).Should().Contain("Depth n/a (1/0 WMC/API entries)");
    }

    private static SlopReport Report() =>
        SlopReport.Of(
            Source.UsedByATest(
                SourceFile.Parse("src/A.cs", "class A { int x; public int F() => x; public int G() => x; }")
            ),
            Clones.FromReport(VerbosityReportTests.Report())
        );
}
