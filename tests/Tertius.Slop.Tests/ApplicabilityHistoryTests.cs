using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Tertius.Slop.History;

namespace Tertius.Slop.Tests;

/// <summary>Versioned history preserves measured populations without inventing evidence for old notes.</summary>
public sealed class ApplicabilityHistoryTests
{
    [Fact]
    public void LegacyDetailsDeserializeWithoutInventingApplicabilityOrChangingOldDepth()
    {
        var legacy = JsonNode
            .Parse(
                JsonSerializer.Serialize(
                    StateCohesionTests.Measure("class Payload { public int X; }"),
                    SlopJson.Default.ClassMetrics
                )
            )!
            .AsObject();
        JsonObject summary = legacy["summary"]!.AsObject();
        summary["provenance"]!["version"] = "type-metrics-v2";
        summary.Remove("cohesionPopulation");
        summary.Remove("depthPopulation");
        summary.Remove("unresolvedStateAccessCount");
        JsonObject row = legacy["types"]![0]!.AsObject();
        row.Remove("unresolvedAccesses");
        row.Remove("tccApplicability");
        row.Remove("depthApplicability");
        row["tcc"]!.AsObject().Remove("applicability");
        row["depth"]!.AsObject().Remove("applicability");

        ClassMetrics restored = JsonSerializer.Deserialize(legacy.ToJsonString(), SlopJson.Default.ClassMetrics)!;

        restored.Types.Single().TccApplicability.Should().BeNull();
        restored.Types.Single().DepthApplicability.Should().BeNull();
        restored.Types.Single().UnresolvedAccesses.Should().BeNull();
        restored.Types.Single().Depth.Ratio.Should().Be(0);
        restored.Types.Single().ApiEntries.Should().Be(1);
        restored.Summary.CohesionPopulation.Should().BeNull();
    }

    [Fact]
    public void NotesRoundTripTheIncludedAndExcludedPopulations()
    {
        SlopNote note = Note(
            "class A { int x; public int F() => Missing(); public int G() => x; } class Payload { public int X; }"
        );

        SlopNote restored = SlopNote.Parse(note.ToLine())!;

        restored.ClassMetrics!.CohesionPopulation.Should().Be(new MetricPopulation(0, 1, 1));
        restored.ClassMetrics.DepthPopulation.Should().Be(new MetricPopulation(1, 1, 0));
        string csv = new SlopHistory([SlopHistoryTests.Commit("unknown", restored)]).Csv;
        csv.Should().Contain("cohesionIncludedTypeCount,cohesionNotApplicableTypeCount,cohesionIncompleteTypeCount");
        csv.Split("\n")[1].Should().EndWith(",0,1,1,1,1,1");
    }

    [Fact]
    public void AllIncompleteEvidenceIsNamedInPushSummariesAndCsv()
    {
        SlopNote note = Note("class A { int x; public int F() => Missing(); public int G() => x; }");

        string summary = PushSummary.Of(lastPush: null, now: note, pushed: []).Describe();
        string csv = new SlopHistory([SlopHistoryTests.Commit("unknown", note)]).Csv;

        summary.Should().Contain("TCC incomplete").And.NotContain("TCC 0.0000").And.NotContain("TCC n/a");
        csv.Should().Contain(",incomplete,0,0,");
    }

    [Fact]
    public void LegacyVersionTwoHasUnavailableApplicabilityWithoutFabricatedCounts()
    {
        SlopNote note = SlopNote.Parse(
            """{"erosion":0,"verbosity":0,"sloc":1,"branch":"main","source":"seed","measured":"2026-10-30T12:00:00Z","classMetrics":{"provenance":{"version":"type-metrics-v2","references":"old"},"tcc":{"numerator":0,"denominator":2},"typeCount":3,"bindingErrorCount":0,"depth":{"numerator":1,"denominator":4}}}"""
        )!;

        note.ClassMetrics!.CohesionPopulation.Should().BeNull();
        note.ClassMetrics.DepthPopulation.Should().BeNull();
        note.ClassMetrics.DescribeAgainst(null).Should().Contain("applicability unavailable (legacy note)");
        string csv = new SlopHistory([SlopHistoryTests.Commit("legacy", note)]).Csv;
        csv.Split("\n")[1].Should().EndWith(",,,,,,");
    }

    [Fact]
    public void BackfillCannotReplaceVersionTwoCountsOrAddVersionThreeApplicability()
    {
        SlopNote note = Note("class A { int x; public int F() => x; public int G() => x; }");
        SlopNote legacy = note with
        {
            ClassMetrics = note.ClassMetrics! with
            {
                Provenance = new("type-metrics-v2", note.ClassMetrics!.Provenance.References),
                Depth = null,
                CohesionPopulation = null,
                DepthPopulation = null,
                UnresolvedStateAccessCount = null,
            },
        };
        string original = legacy.ToLine();

        string filled = SlopNote.BackfillText(original, Report("class Payload { public int X; }"));

        filled.Should().Be(original);
        note.ClassMetrics!.DescribeAgainst(legacy.ClassMetrics)
            .Should()
            .Contain("configuration differs")
            .And.NotContain("shared pairs +");
    }

    [Fact]
    public void MatchingVersionBackfillFillsOnlyMissingApplicabilityMetadata()
    {
        SlopReport report = Report("class A { int x; public int F() => x; public int G() => x; }");
        SlopNote complete = SlopNote.Of(
            report: report,
            branch: "topic",
            source: NoteSource.Seed,
            measured: SlopNoteTests.Noon
        );
        SlopNote partial = complete with
        {
            ClassMetrics = complete.ClassMetrics! with
            {
                CohesionPopulation = null,
                DepthPopulation = null,
                UnresolvedStateAccessCount = null,
            },
        };

        SlopNote.NeedsBackfill(partial.ToLine()).Should().BeTrue();
        string filled = SlopNote.BackfillText(partial.ToLine(), report);

        SlopNote.Parse(filled)!.ClassMetrics.Should().Be(complete.ClassMetrics);
        SlopNote.NeedsBackfill(filled).Should().BeFalse();
        SlopNote.BackfillText(filled, report).Should().Be(filled);
        SlopNote.Parse($"{partial.ToLine()}\n{complete.ToLine()}")!.ClassMetrics.Should().Be(complete.ClassMetrics);
    }

    [Fact]
    public void MeasureDetailsKeepKnownPairsAndUnresolvedSourceLocations()
    {
        using var git = new GitFolder();
        git.Write("src/A.cs", "class A { int x; public int F() => x + Missing(); public int G() => x; }");
        git.Commit("source");
        string path = Path.Combine(git.Root, "details.json");
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(
            args: ["-C", git.Root, "measure", "HEAD", "--branch", "topic", "--source", "seed", "--details", path],
            output: output,
            error: error,
            jscpd: "jscpd"
        );

        exit.Should().Be(0, error.ToString());
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement type = json
            .RootElement.GetProperty("report")
            .GetProperty("classMetrics")
            .GetProperty("types")
            .EnumerateArray()
            .Single();
        type.GetProperty("tcc").GetProperty("numerator").GetInt32().Should().Be(1);
        type.GetProperty("tcc").GetProperty("denominator").GetInt32().Should().Be(1);
        type.GetProperty("tcc").GetProperty("ratio").ValueKind.Should().Be(JsonValueKind.Null);
        type.GetProperty("unresolvedAccesses")[0]
            .GetProperty("location")
            .GetProperty("file")
            .GetString()
            .Should()
            .Be("src/A.cs");
        SlopNote.Parse(output.ToString())!.ClassMetrics!.CohesionPopulation.Should().Be(new MetricPopulation(0, 0, 1));
    }

    private static SlopNote Note(string source) =>
        SlopNote.Of(report: Report(source), branch: "topic", source: NoteSource.Seed, measured: SlopNoteTests.Noon);

    private static SlopReport Report(string source) =>
        SlopReport.Of(
            Source.UsedByATest(SourceFile.Parse("src/A.cs", source)),
            Clones.FromReport(VerbosityReportTests.Report())
        );
}
