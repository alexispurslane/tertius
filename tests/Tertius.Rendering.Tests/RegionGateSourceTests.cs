using System.Globalization;
using System.Text.RegularExpressions;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The link between the C# numbers of the region's change gate and region_drift.glsl, which holds its own copy of the
/// gate's window: no render test sees a copy that drifts by a few draws.
/// </summary>
public sealed class RegionGateSourceTests
{
    [Fact]
    public void TheRegionShaderKeepsTheGateOpenForRegionCauseWindowDraws()
    {
        string regionDrift = File.ReadAllText(
            Path.Combine(Gpu.RepositoryRoot, "src", "Tertius.Rendering", "Shaders", "region_drift.glsl")
        );

        Match window = Regex.Match(regionDrift, @"const\s+float\s+RegionCauseWindowDraws\s*=\s*([0-9.]+)");

        window.Success.Should().BeTrue("region_drift.glsl declares the gate's window");
        float.Parse(window.Groups[1].Value, CultureInfo.InvariantCulture)
            .Should()
            .Be(TemporalSignal.RegionCauseWindowDraws);
    }

    [Fact]
    public void TheGatesWindowIsTheBounceLightsCapOf128Draws()
    {
        // A cause's cut grows back over up to the cap's draws, so the gate stays open as long (t1-yi11).
        SignalParameters.Diffuse.Cap.Should().Be(128, "the user chose a cap of 128 draws (t1-yi11)");
        SignalParameters.Diffuse.SmoothCap.Should().Be(128, "a smooth surface takes the same cap");
        TemporalSignal.RegionCauseWindowDraws.Should().Be(SignalParameters.Diffuse.Cap);
    }
}
