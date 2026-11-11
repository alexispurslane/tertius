namespace Tertius.Rendering.Tests;

/// <summary>
/// The debug switch TERTIUS_DIFFUSE_FILTER (ticket ter-9371, <see cref="DiffuseFilter.SwitchedOn"/>): which diffuse light
/// the composite shows and a ray's hit reads, with the filter and without it.
/// </summary>
[Collection(ProcessEnvironmentCollection.Name)]
public sealed class DiffuseFilterSwitchTests
{
    private const string Switch = "TERTIUS_DIFFUSE_FILTER";

    [Fact]
    public void WithoutTheSwitchTheCompositeAndTheHitsReadTheFilteredLight() =>
        WithTracer(
            null,
            images =>
            {
                GpuImage filtered = images.Filter!.Filtered;
                images.ShownDiffuse.Should().BeSameAs(filtered);
                images.HitDiffuse(0).Should().BeSameAs(filtered);
                images.HitDiffuse(1).Should().BeSameAs(filtered);
            }
        );

    [Fact]
    public void TheOffSettingShowsTheSlowHistoryAndLetsTheHitsReadTheFastHistory() =>
        WithTracer(
            "off",
            images =>
            {
                images.Filter.Should().BeNull();
                images.ShownDiffuse.Should().BeSameAs(images.Diffuse.Slow(0));
                for (int parity = 0; parity < 2; parity++)
                    images.HitDiffuse(parity).Should().BeSameAs(images.Diffuse.HitImages(part: 0, parity).LastFast);
            }
        );

    [Fact]
    public void TheOnSettingKeepsTheFilter() =>
        WithTracer("on", images => images.ShownDiffuse.Should().BeSameAs(images.Filter!.Filtered));

    [Fact]
    public void AnUnknownSettingIsRefused()
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, "sometimes");
        try
        {
            Action select = () => DiffuseFilter.Selected();

            select.Should().Throw<ArgumentException>().WithMessage("*TERTIUS_DIFFUSE_FILTER*");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }

    /// <summary>Runs <paramref name="check"/> on the tracer's images of a renderer made with the switch at <paramref name="value"/>.</summary>
    private static void WithTracer(string? value, Action<ScreenTraceImages> check)
    {
        string? previous = Environment.GetEnvironmentVariable(Switch);
        Environment.SetEnvironmentVariable(Switch, value);
        try
        {
            VulkanRenderer renderer = Gpu.Headless(
                width: 64,
                height: 36,
                screenTrace: ScreenTrace.Default,
                warmUpFrames: 0
            );
            try
            {
                check(renderer.ScreenTraceImages);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable(Switch, previous);
        }
    }
}
