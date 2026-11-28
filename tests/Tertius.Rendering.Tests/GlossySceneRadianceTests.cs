using System.Numerics;
using static Tertius.Rendering.Tests.ReflectionTesting;

namespace Tertius.Rendering.Tests;

/// <summary>Real reflected emission retains physical radiance through hit, ray, sample and temporal storage.</summary>
[Collection(GpuCollection.Name)]
public sealed class GlossySceneRadianceTests
{
    [Theory]
    [InlineData(-64f, 1e-20f)]
    [InlineData(64f, 1e-20f)]
    [InlineData(-64f, 1e20f)]
    [InlineData(64f, 1e20f)]
    [InlineData(-64f, 1e30f)]
    [InlineData(64f, 1e30f)]
    public void AMirrorCarriesItsEmittersSceneRadianceThroughOrdinaryFrames(float stops, float radiance)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            exposure: new FixedExposure(stops),
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            lightVolume: false
        );
        try
        {
            var center = new Vector3(0, -0.5f, -12);
            RenderSnapshot scene = Dark();
            scene.Instances.Add(Floor(renderer, Mirror()));
            scene.Instances.Add(
                Block(
                    box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                    material: Glow(renderer, new(radiance)),
                    center: center,
                    size: new(3, 2, 0.2f)
                )
            );
            for (int frame = 0; frame < 10; frame++)
            {
                scene.Time += 1.0 / 60;
                renderer.Render(scene, 1);
                float source = At(renderer.ReadHdr(), PixelOf(center)).X;
                float boundedSource = MathF.Min(radiance, ShaderInput.RadianceLimit);
                source
                    .Should()
                    .BeApproximately(
                        boundedSource,
                        boundedSource * 1e-5f,
                        "the public emission input retains its existing per-source bound"
                    );
                TemporalSignal signal = renderer.ScreenTraceImages.Specular;
                float[] sample = renderer.ReadImage(signal.Sample(0));
                (int x, int y) = MirrorPixel(center);
                sample[4 * (y * Width + x) + 3].Should().BePositive("the ray must actually hit the emitting panel");
                At(sample, (x, y)).X.Should().BeApproximately(source, source * 0.05f);
                At(renderer.ReadImage(signal.Slow(0)), (x, y)).X.Should().BeApproximately(source, source * 0.05f);
                foreach (
                    GpuImage image in new[] { signal.Sample(0), signal.Slow(0), signal.Fast(0, 0), signal.Fast(0, 1) }
                )
                    renderer.ReadImage(image).Should().OnlyContain(value => float.IsFinite(value));
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void TheFirstReflectedEmitterMatchesAnIndependentDestinationFromBothExposureHistories(bool bright, bool fsr)
    {
        (Vector3 expected, float exposure) = IndependentEmitter(fsr);
        VulkanRenderer renderer = AutomaticRenderer(fsr);
        try
        {
            RenderSnapshot scene = Dark();
            ExposureScenes.AddGlowingSquare(
                renderer: renderer,
                snapshot: scene,
                position: ExposureScenes.Ahead,
                halfSize: ExposureScenes.FillsTheView,
                radiance: bright ? new(ShaderInput.RadianceLimit) : Vector3.Zero
            );
            Capture(renderer, scene, 0);
            renderer.LastFrameStats.Exposure!.Value.Stops.Should().Be(bright ? -64 : 64);
            scene.Instances.Clear();
            AddEmitter(renderer, scene);
            scene.CameraCuts++;
            long before = renderer.DrawnFrames;

            Capture(renderer, scene, 1.0 / 60);

            renderer.DrawnFrames.Should().Be(before + 1);
            renderer.LastFrameStats.Exposure!.Value.Stops.Should().BeApproximately(exposure, 1f / 16);
            Vector3 actual = At(renderer.ReadHdr(), MirrorPixel(EmitterCenter));
            for (int channel = 0; channel < 3; channel++)
                actual[channel].Should().BeApproximately(expected[channel], expected[channel] * 0.005f);
            float[] samples = renderer.ReadImage(renderer.ScreenTraceImages.Specular.Sample(0));
            (int x, int y) = MirrorPixel(EmitterCenter);
            samples[4 * (y * Width + x) + 3].Should().BePositive();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static readonly Vector3 EmitterCenter = new(0, -0.5f, -12);

    private static VulkanRenderer AutomaticRenderer(bool fsr) =>
        Gpu.Headless(
            width: Width,
            height: Height,
            framesInFlight: 1,
            exposure: AutoExposure.Default with
            {
                MinStops = -64,
                MaxStops = 64,
            },
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0,
            lightVolume: false,
            upscaling: fsr ? new Fsr3Upscaling(new FixedRenderScale(1), 0) : new NoUpscaling()
        );

    private static void AddEmitter(VulkanRenderer renderer, RenderSnapshot scene)
    {
        scene.Instances.Add(Floor(renderer, Mirror()));
        scene.Instances.Add(
            Block(
                box: renderer.CreateMesh(Primitives.Box(new(0.5f))),
                material: Glow(renderer, Vector3.One),
                center: EmitterCenter,
                size: new(3, 2, 0.2f)
            )
        );
    }

    private static void Capture(VulkanRenderer renderer, RenderSnapshot scene, double time)
    {
        scene.Time = time;
        Gpu.RenderAndCapture(renderer: renderer, snapshot: scene, name: "glossy reflected emitter cut");
    }

    private static (Vector3 Raw, float Stops) IndependentEmitter(bool fsr)
    {
        VulkanRenderer renderer = AutomaticRenderer(fsr);
        try
        {
            RenderSnapshot scene = Dark();
            AddEmitter(renderer, scene);
            for (int frame = 0; frame < 4; frame++)
                Capture(renderer, scene, 0);
            float expectedStops = renderer.LastFrameStats.Exposure!.Value.Stops;
            scene.CameraCuts++;
            long before = renderer.DrawnFrames;
            Capture(renderer, scene, 1.0 / 60);
            renderer.DrawnFrames.Should().Be(before + 1);
            renderer.LastFrameStats.Exposure!.Value.Stops.Should().BeApproximately(expectedStops, 1f / 16);
            return (At(renderer.ReadHdr(), MirrorPixel(EmitterCenter)), expectedStops);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
