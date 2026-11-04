using System.Globalization;
using System.Text;
using Tertius.Rendering;

namespace Tertius.Host.Tests;

/// <summary>
/// ReSTIR's bias against the exact loop on lamp-hall-64 (docs/plans/temporal-renderer.md, section 10, "The tests"): the
/// hall's still under <see cref="ResampledLights"/> and under <see cref="EveryLight"/>, at a small size, after the still's
/// warm-up draws. And the samples of demo-exit's deck at frame 120, which keep their expectation only while the random
/// streams' seeds stay apart.
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class DirectLightBiasTests
{
    /// <summary>
    /// The bound of the hall's bias at 320 × 180: its measured −0.032 (2026-11-03), rounded up (ARCHITECTURE.md,
    /// "ReSTIR DI").
    /// </summary>
    private const double BiasBound = 0.05;

    /// <summary>The bound of the mean relative error of the hall's accumulated history at 320 × 180: its measured 0.160, rounded up.</summary>
    private const double HistoryBound = 0.20;

    [Fact]
    public void OnTheHallOf64LampsTheAccumulatedLightStaysWithinTheBoundOfTheExactLoop()
    {
        DirectView exact = DirectLightViews.Measure(
            script: "lamp-hall-64",
            capture: "hall",
            setting: new EveryLight(),
            width: 320,
            height: 180
        );
        DirectView resampled = DirectLightViews.Measure(
            script: "lamp-hall-64",
            capture: "hall",
            setting: ResampledLights.Default,
            width: 320,
            height: 180
        );

        var error = DirectError.Of(exact, resampled);

        TestContext.Current.TestOutputHelper?.WriteLine(error.ToString());
        error.Pixels.Should().BeGreaterThan(320 * 180 / 3, "the lamps light the hall's floor, walls and pillars");
        Math.Abs(error.Bias).Should().BeLessThan(BiasBound, "the hall's summed light keeps nearly its expectation");
        error.HistoryError.Should().BeLessThan(HistoryBound, "the accumulated history settles near the exact light");
    }

    /// <summary>
    /// The bound of the sample bias of demo-exit's deck at frame 120 at 1280 × 720: its measured −0.030 (2026-11-04),
    /// rounded up. The seeds before b20714c0c measured +0.457 there.
    /// </summary>
    private const double DeckSampleBiasBound = 0.10;

    /// <summary>
    /// The direct pass takes two random streams of one pixel and draw, the combine's choices and the neighbors' places.
    /// Seeds that differed only in the stream's low bits of one word gave the two streams correlated numbers, and the
    /// deck's samples then measured a bias of +0.46 at 1280 × 720 (2026-11-03). Each seed now hashes the stream apart.
    /// The view needs the full size: at 640 × 360 and 320 × 180 the old seeds measured −0.044 and −0.042, near the hashed
    /// seeds' −0.014 and −0.048.
    /// </summary>
    [Fact]
    public void TheRandomStreamsHashTheirSeedsApartSoTheDecksSamplesKeepTheExactLoopsLight()
    {
        DirectView exact = DirectLightViews.Measure(
            script: "demo-exit",
            capture: "deck-000120",
            setting: new EveryLight(),
            width: 1280,
            height: 720
        );
        DirectView resampled = DirectLightViews.Measure(
            script: "demo-exit",
            capture: "deck-000120",
            setting: ResampledLights.Default,
            width: 1280,
            height: 720
        );

        var error = DirectError.Of(exact, resampled);

        TestContext.Current.TestOutputHelper?.WriteLine(error.ToString());
        Math.Abs(error.SampleBias)
            .Should()
            .BeLessThan(DeckSampleBiasBound, $"the deck's samples keep their expectation: {error}");
    }
}

/// <summary>
/// The noise and the bias of ReSTIR against the exact loop in the views of section 7, "The bias": lamp-hall-64's still,
/// demo-exit's deck at frame 120 of its film and the cabin of cabin-light, at 1280 × 720 under a scripted run's options.
/// It is an explicit test, so a normal run reports it as not run. It runs in a Release build, with the variable
/// <see cref="Variable"/> set to the file of its report:
/// <c>TERTIUS_DIRECT_LIGHT_BIAS=report.md dotnet test -c Release --project tests/Tertius.Host.Tests -- --filter-class "*DirectLightBiasMeasurement" --explicit only</c>
/// .
/// </summary>
[Collection(MeasurementCollection.Name)]
public sealed class DirectLightBiasMeasurement
{
    public const string Variable = "TERTIUS_DIRECT_LIGHT_BIAS";

    private static readonly (string Script, string Capture, string View)[] Views =
    [
        ("lamp-hall-64", "hall", "lamp-hall-64, the hall"),
        ("demo-exit", "deck-000120", "demo-exit, the deck at frame 120"),
        ("cabin-light", "cabin", "cabin-light, the cabin"),
    ];

    [Fact(Explicit = true)]
    public void WritesItsReportWhereTheVariableNamesAFile()
    {
        string reportPath = Environment.GetEnvironmentVariable(Variable) is { Length: > 0 } path
            ? path
            : throw new InvalidOperationException($"Set {Variable} to the file of the report.");
        var report = new StringBuilder();
        report.AppendLine("| View | Pixels | Bias | History error | Sample error | Sample bias |");
        report.AppendLine("|---|---:|---:|---:|---:|---:|");
        foreach ((string script, string capture, string view) in Views)
        {
            DirectView exact = DirectLightViews.Measure(
                script: script,
                capture: capture,
                setting: new EveryLight(),
                width: 1280,
                height: 720
            );
            DirectView resampled = DirectLightViews.Measure(
                script: script,
                capture: capture,
                setting: ResampledLights.Default,
                width: 1280,
                height: 720
            );
            var error = DirectError.Of(exact, resampled);
            report.AppendLine(
                CultureInfo.InvariantCulture,
                $"| {view} | {error.Pixels} | {error.Bias:+0.0000;-0.0000} | {error.HistoryError:0.0000} | {error.SampleError:0.0000} | {error.SampleBias:+0.0000;-0.0000} |"
            );
        }

        File.WriteAllText(reportPath, report.ToString());
    }
}
