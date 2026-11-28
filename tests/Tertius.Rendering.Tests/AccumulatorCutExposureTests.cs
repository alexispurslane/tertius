namespace Tertius.Rendering.Tests;

public sealed partial class AccumulatorRenderTests
{
    /// <summary>Retained light crosses a cut's display exposure change on the following frame.</summary>
    public sealed class CutHistoryExposure
    {
        [Fact]
        public void TheFrameAfterACutRescalesRetainedLightFromItsProducingExposure()
        {
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                exposure: AutoExposure.Default
            );
            try
            {
                Gpu.Star(snapshot: scene, irradiance: 1361);
                sample.Value = new(x: 0.05f, y: 0.05f, z: 0.05f, w: 1);
                renderer.Render(scene, 1f);
                float producingStops = renderer.ReadExposureStops().Stops;
                Gpu.Star(snapshot: scene, irradiance: 3);
                scene.CameraCuts++;
                Continue(renderer, scene, frames: 1);
                float displayedStops = renderer.ReadExposureStops().Stops;
                float ratio = MathF.Pow(2, displayedStops - producingStops);
                ratio.Should().BeGreaterThan(2, "the cut separates display exposure from producing exposure");
                float retained = CenterOf(renderer, sample, part: 0).Light.X;
                retained.Should().BePositive();

                sample.Value = new(x: float.NaN, y: float.NaN, z: float.NaN, w: 1);
                Continue(renderer, scene, frames: 1);

                CenterOf(renderer, sample, part: 0)
                    .Light.X.Should()
                    .BeApproximately(retained * ratio, retained * ratio * 0.003f);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void TheTracerRescalesACutsPreviousReflectionFromItsProducingExposure()
        {
            (VulkanRenderer renderer, _, RenderSnapshot scene) = AccumulatingOn(
                renderer: Gpu.Headless(
                    width: Width,
                    height: Height,
                    exposure: AutoExposure.Default,
                    screenTrace: ScreenTrace.Default,
                    warmUpFrames: 0
                )
            );
            try
            {
                TemporalSignal reflection = renderer.ScreenTraceImages.Specular;
                renderer.Graph.Add(
                    FrameGraph.Stage.ScreenTrace,
                    new SamplePass(reflection, renderer.Allocator) { Value = new(x: 0.25f, y: 0.25f, z: 0.25f, w: 1) }
                );
                renderer.Graph.Add(FrameGraph.Stage.ScreenTrace, new AccumulatePass(reflection));
                Gpu.Star(snapshot: scene, irradiance: 1361);
                renderer.Render(scene, 1f);
                float producingStops = renderer.ReadExposureStops().Stops;
                Gpu.Star(snapshot: scene, irradiance: 3);
                scene.CameraCuts++;
                Continue(renderer, scene, frames: 1);
                float displayedStops = renderer.ReadExposureStops().Stops;
                float ratio = MathF.Pow(2, displayedStops - producingStops);
                ratio.Should().BeGreaterThan(2, "the cut separates display exposure from producing exposure");
                float retained = renderer.ReadImage(
                    renderer.ScreenTraceImages.Specular.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1)
                )[4 * Center];
                retained.Should().BePositive();
                var probe = ScreenTraceProbe.On(renderer, "cut_reflection_history_probe.comp");

                Continue(renderer, scene, frames: 1);

                probe.Read()[4 * Center].Should().BeApproximately(retained * ratio, retained * ratio * 0.003f);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }
}
