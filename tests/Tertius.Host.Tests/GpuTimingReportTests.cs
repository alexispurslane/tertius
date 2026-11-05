using System.Text.Json;
using Tertius.Host.Scripting;
using Tertius.Rendering;
using static Tertius.Host.Tests.HostTesting;

namespace Tertius.Host.Tests;

/// <summary>The portable console policy and compatible JSON of synthetic, finished GPU frames, without a GPU dependency.</summary>
public sealed class GpuTimingReportTests : IDisposable
{
    private readonly string folder = NewFolder();

    public void Dispose() => Directory.Delete(folder, recursive: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheConsoleShowsRequestedRunsAndShowsIndividualPassesOnlyWhenTheirTimesAreValid(bool stampsLatchLate)
    {
        var summary = new GpuTimingSummary();
        summary.Add(
            new GpuTimings(
                Frame: 7,
                Passes: new PassTiming[] { new("bloom", 1), new("capture", 2) },
                SpanMilliseconds: 100
            )
        );
        summary.Add(
            new GpuTimings(
                Frame: 9,
                Passes: new PassTiming[] { new("bloom", 4), new("capture", 8) },
                SpanMilliseconds: 200
            )
        );
        using var output = new StringWriter();

        string? problem = ScriptCommand.WriteGpuTimings(
            timings: summary,
            device: "test device",
            stampsLatchLate: stampsLatchLate,
            size: new ScriptWindow(160, 90),
            runs: ["bloom..capture", "capture..capture"],
            outputFolder: folder,
            output: output
        );

        problem.Should().BeNull();
        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        lines[0].Should().Be(Path.Combine(folder, ScriptCommand.GpuTimingsFile));
        string[] passLines = stampsLatchLate
            ? []
            :
            [
                "GPU bloom: mean 2.500 ms, median 2.500 ms, p95 4.000 ms over 2 frames",
                "GPU capture: mean 5.000 ms, median 5.000 ms, p95 8.000 ms over 2 frames",
            ];
        lines
            .Where(line => line.StartsWith("GPU ", StringComparison.Ordinal))
            .Should()
            .Equal([
                .. passLines,
                "GPU frame span: mean 150.000 ms, median 150.000 ms, p95 200.000 ms over 2 frames",
                "GPU run bloom..capture: mean 7.500 ms, median 7.500 ms, p95 12.000 ms over 2 frames",
                "GPU run capture..capture: mean 5.000 ms, median 5.000 ms, p95 8.000 ms over 2 frames",
            ]);
        lines.Contains(ScriptCommand.LateStampsNote).Should().Be(stampsLatchLate);
    }

    [Fact]
    public void ALateStampDriverWithNoRequestedRunsPrintsOnlyTheFrameSpanAndTheExplanation()
    {
        var summary = new GpuTimingSummary();
        summary.Add(new GpuTimings(Frame: 0, Passes: new PassTiming[] { new("capture", 1) }, SpanMilliseconds: 10));
        using var output = new StringWriter();

        string? problem = ScriptCommand.WriteGpuTimings(
            timings: summary,
            device: "test device",
            stampsLatchLate: true,
            size: new ScriptWindow(160, 90),
            runs: [],
            outputFolder: folder,
            output: output
        );

        problem.Should().BeNull();
        output
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Should()
            .Equal(
                Path.Combine(folder, ScriptCommand.GpuTimingsFile),
                ScriptCommand.LateStampsNote,
                "GPU frame span: mean 10.000 ms, median 10.000 ms, p95 10.000 ms over 1 frames"
            );
        ScriptCommand.LateStampsNote.Should().Contain("Individual pass times are omitted");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonPreservesRawPassAndFrameFieldsAlongsideRunStatisticsAndDriverValidity(bool stampsLatchLate)
    {
        var summary = new GpuTimingSummary();
        for (int frame = 0; frame < 20; frame++)
            summary.Add(
                new GpuTimings(
                    Frame: frame,
                    Passes: new PassTiming[] { new("bloom", frame + 1), new("capture", 20 - frame) },
                    SpanMilliseconds: 100 + frame
                )
            );
        using var output = new StringWriter();

        string? problem = ScriptCommand.WriteGpuTimings(
            timings: summary,
            device: "test device",
            stampsLatchLate: stampsLatchLate,
            size: new ScriptWindow(160, 90),
            runs: ["bloom..capture"],
            outputFolder: folder,
            output: output
        );

        problem.Should().BeNull();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, ScriptCommand.GpuTimingsFile)));
        JsonElement report = json.RootElement;
        report.GetProperty("device").GetString().Should().Be("test device");
        report.GetProperty("width").GetInt32().Should().Be(160);
        report.GetProperty("height").GetInt32().Should().Be(90);
        report
            .GetProperty("frame")
            .Deserialize<SpanSummary>(JsonSerializerOptions.Web)
            .Should()
            .Be(new SpanSummary(Frames: 20, MeanMs: 109.5, MedianMs: 109.5, P95Ms: 118));
        report
            .GetProperty("passes")
            .Deserialize<PassSummary[]>(JsonSerializerOptions.Web)
            .Should()
            .Equal(
                new PassSummary(Pass: "bloom", Frames: 20, MeanMs: 10.5, MedianMs: 10.5, P95Ms: 19),
                new PassSummary(Pass: "capture", Frames: 20, MeanMs: 10.5, MedianMs: 10.5, P95Ms: 19)
            );
        report
            .GetProperty("runs")
            .Deserialize<RunSummary[]>(JsonSerializerOptions.Web)
            .Should()
            .Equal(
                new RunSummary("bloom", "capture", new SpanSummary(Frames: 20, MeanMs: 21, MedianMs: 21, P95Ms: 21))
            );
        report.GetProperty("stampsLatchLate").GetBoolean().Should().Be(stampsLatchLate);
        report.GetProperty("warning").GetString().Should().Be(stampsLatchLate ? ScriptCommand.LateStampsNote : null);
    }

    [Theory]
    [InlineData("bloom")]
    [InlineData("unknown..capture")]
    [InlineData("capture..bloom")]
    public void AnInvalidRunReturnsAnActionableErrorWithoutWritingAReport(string run)
    {
        var summary = new GpuTimingSummary();
        summary.Add(
            new GpuTimings(
                Frame: 0,
                Passes: new PassTiming[] { new("bloom", 1), new("capture", 2) },
                SpanMilliseconds: 10
            )
        );
        using var output = new StringWriter();

        string? problem = ScriptCommand.WriteGpuTimings(
            timings: summary,
            device: "test device",
            stampsLatchLate: false,
            size: new ScriptWindow(160, 90),
            runs: [run],
            outputFolder: folder,
            output: output
        );

        problem.Should().Contain("--gpu-run");
        output.ToString().Should().BeEmpty();
        File.Exists(Path.Combine(folder, ScriptCommand.GpuTimingsFile)).Should().BeFalse();
    }

    [Fact]
    public void ARunWithNoGpuFramesAndNoRequestedRangesWritesNothing()
    {
        using var output = new StringWriter();

        string? problem = ScriptCommand.WriteGpuTimings(
            timings: new GpuTimingSummary(),
            device: "test device",
            stampsLatchLate: false,
            size: new ScriptWindow(160, 90),
            runs: [],
            outputFolder: folder,
            output: output
        );

        problem.Should().BeNull();
        output.ToString().Should().BeEmpty();
        File.Exists(Path.Combine(folder, ScriptCommand.GpuTimingsFile)).Should().BeFalse();
    }
}
