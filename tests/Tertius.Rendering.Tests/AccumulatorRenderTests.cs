using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The accumulator of a signal (docs/plans/temporal-renderer.md, section 8): a constant sample stays constant, a step of the
/// sample reaches the history within the bound of the anti-lag, and a sample that is not finite counts as no sample. The
/// renderer makes no signal until the screen tracer's (ter-gayt), so the test makes one of two parts with a cap of 16
/// frames, the parameter set that the direct signal had under ReSTIR, and a test pass, its producer, writes a chosen sample
/// into both parts in each frame. The camera stands still before a wall that fills the view, and each frame steps one tick,
/// so each frame continues the last one.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AccumulatorRenderTests
{
    private const int Width = 64,
        Height = 36;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The test signal's parameter set: a history of 16 frames and a fast history of 4.</summary>
    private static readonly SignalParameters Parameters = new(
        Cap: 16,
        FastCap: 4,
        HistoryFixFrames: 4,
        ClampSigmas: 2,
        AntiLagShare: 0.5f
    );

    /// <summary>
    /// The renderer with the test signal and its producer and accumulation after the direct pass, under
    /// <paramref name="exposure"/>, the fixed exposure of the tests without it.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) Accumulating(
        ExposureSetting exposure = default
    )
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, exposure: exposure);
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
        RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        scene.Instances.Add(
            Gpu.Instance(
                mesh: cube,
                material: white,
                position: new(0, 0, -20),
                scale: new(60, 40, 1),
                chainDepth: TestChains.CamerasFrame,
                entity: 1
            )
        );
        var sample = new SamplePass(renderer.CreateSignal(parameters: Parameters, name: "test", parts: 2));
        renderer.Graph.Add(FrameGraph.Stage.DirectLight, sample);
        renderer.Graph.Add(FrameGraph.Stage.DirectLight, new AccumulatePass(sample.Signal));
        return (renderer, sample, scene);
    }

    /// <summary>Draws <paramref name="frames"/> frames of <paramref name="scene"/>, each one tick after the last.</summary>
    private static void Continue(VulkanRenderer renderer, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            scene.Time += Tick;
            renderer.Render(scene, 1f);
        }
    }

    /// <summary>
    /// The test signal's slow history of <paramref name="part"/> after the last frame, RGBA per pixel, row by row from the
    /// top: rgb the light, a the history length in frames.
    /// </summary>
    private static float[] HistoryOf(VulkanRenderer renderer, SamplePass sample, int part) =>
        renderer.ReadImage(sample.Signal.Slow(part));

    /// <summary>The light of the slow history of <paramref name="part"/> at the center pixel, and its length.</summary>
    private static (Vector3 Light, float Length) CenterOf(VulkanRenderer renderer, SamplePass sample, int part)
    {
        float[] history = HistoryOf(renderer, sample, part);
        int at = 4 * (Height / 2 * Width + Width / 2);
        return (new(history[at], history[at + 1], history[at + 2]), history[at + 3]);
    }

    [Fact]
    public void AConstantSampleStaysConstant()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 0.25f, y: 0.5f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);

            Continue(renderer, scene, frames: 20);

            for (int part = 0; part < 2; part++)
            {
                float[] history = HistoryOf(renderer, sample, part);
                for (int pixel = 0; pixel < Width * Height; pixel++)
                    new Vector3(history[4 * pixel], history[4 * pixel + 1], history[4 * pixel + 2])
                        .Should()
                        .Be(new Vector3(0.25f, 0.5f, 1f), "pixel {0} of part {1} saw only that sample", pixel, part);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AfterAStepOfTheSampleTheHistoryReachesNinetyPercentOfTheStepWithinTenFrames()
    {
        // Without the anti-lag, a history of 16 frames reaches 90 % after ln 0.1 / ln(15 / 16) ≈ 36 frames. The clamp
        // follows the fast history of 4 frames, which reaches 90 % after 8.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            CenterOf(renderer, sample, part: 0).Length.Should().Be(16, "the history holds its cap before the step");

            sample.Value = new(x: 2f, y: 2f, z: 2f, w: 1f);
            int frames = 0;
            while (frames < 40 && CenterOf(renderer, sample, part: 0).Light.X < 1.9f)
            {
                Continue(renderer, scene, frames: 1);
                frames++;
            }

            frames.Should().BeInRange(2, 10);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void UntilItsCapTheSlowHistoryIsTheMeanOfItsSamples()
    {
        // The blend weights the sample by 1 / length, so a history of n frames holds the mean of n samples.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            // A camera cut resets the history, and each later frame continues it.
            scene.CameraCuts++;
            foreach (float light in new[] { 1f, 3f, 5f, 7f })
            {
                sample.Value = new(x: light, y: light, z: light, w: 1f);
                Continue(renderer, scene, frames: 1);
            }

            for (int part = 0; part < 2; part++)
            {
                (Vector3 light, float length) = CenterOf(renderer, sample, part);
                length.Should().Be(4, "the cut started the history four frames ago");
                light.Should().Be(new Vector3(4f), "the mean of 1, 3, 5 and 7 is 4");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AStepOfTheSampleShortensTheHistoryByTheClampsMoveOverHalfTheMeansLight()
    {
        // The first frame after the step: the slow history of 16 frames blends to 1 + 1 / 16 = 1.0625, the fast history
        // of 4 to 1.25, and the clamp moves the slow one to the fast one's mean, 1.25. The lag is 0.1875 / 1.25 = 0.15, a
        // share 0.3 of the anti-lag's 0.5, so the length falls from 16 to 16 · 0.7 = 11.2.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            CenterOf(renderer, sample, part: 0).Length.Should().Be(16, "the history holds its cap before the step");

            sample.Value = new(x: 2f, y: 2f, z: 2f, w: 1f);
            Continue(renderer, scene, frames: 1);

            (Vector3 light, float length) = CenterOf(renderer, sample, part: 0);
            light.X.Should().Be(1.25f, "the clamp moves the slow history to the fast history's mean");
            length.Should().BeApproximately(11.2f, 0.01f, "the anti-lag shortens the history by the clamp's move");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APixelWhoseSampleIsInfiniteKeepsItsHistoryAndItsLength()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 6);
            (Vector3 light, float length) before = CenterOf(renderer, sample, part: 1);

            sample.Value = new(x: float.PositiveInfinity, y: float.PositiveInfinity, z: float.PositiveInfinity, w: 1f);
            Continue(renderer, scene, frames: 5);

            CenterOf(renderer, sample, part: 1).Should().Be(before, "an infinite sample is no sample");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ANaNSampleCountsAsNoSample()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 0.5f, y: 0.75f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 6);
            (Vector3 light, float length) before = CenterOf(renderer, sample, part: 0);

            sample.Value = new(x: float.NaN, y: float.NaN, z: float.NaN, w: 1f);
            Continue(renderer, scene, frames: 5);

            CenterOf(renderer, sample, part: 0).Should().Be(before);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APixelWithoutAHistoryAndWithANaNSampleTakesTheClosedFormZero()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: float.NaN, y: float.NaN, z: float.NaN, w: 1f);

            renderer.Render(scene, 1f);

            for (int part = 0; part < 2; part++)
                CenterOf(renderer, sample, part)
                    .Light.Should()
                    .Be(Vector3.Zero, "the still's first draw reset every history");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WhenTheExposureMovesBetweenTwoFramesTheHistoryTakesTheNewExposureInThatFrame()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(AutoExposure.Default);
        try
        {
            // A sample of a constant light, pre-exposed at the exposure that the next frame uses, as a producer writes it.
            void ExposeAndContinue(float light)
            {
                float scale = MathF.Pow(2, renderer.ReadExposureStops().Stops);
                sample.Value = new(x: light * scale, y: light * scale, z: light * scale, w: 1f);
                Continue(renderer, scene, frames: 1);
            }

            sample.Value = new(x: 0.05f, y: 0.05f, z: 0.05f, w: 1f);
            renderer.Render(scene, 1f);
            for (int frame = 0; frame < 20; frame++)
                ExposeAndContinue(0.05f);
            // A frame 64 times as bright: its adapt step lowers the exposure of the next frame. The meter reads the HDR
            // image, which shows the scene's star and not the test signal, so the star brightens with the sample.
            float before = renderer.ReadExposureStops().Stops;
            Gpu.Star(snapshot: scene, irradiance: 64 * Gpu.StarIrradiance(scene));
            ExposeAndContinue(3.2f);
            float after = renderer.ReadExposureStops().Stops;
            float ratio = MathF.Pow(2, after - before);
            ratio.Should().BeLessThan(0.98f, "the bright frame moved the exposure");
            (Vector3 light, float length) history = CenterOf(renderer, sample, part: 0);

            // No sample, so the history holds only its rescaled self.
            sample.Value = new(x: float.NaN, y: float.NaN, z: float.NaN, w: 1f);
            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0)
                .Light.X.Should()
                .BeApproximately(
                    history.light.X * ratio,
                    2e-3f * history.light.X,
                    "the history holds light at the frame's exposure"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The producer of <paramref name="signal"/>: it clears the sample of each part to <see cref="Value"/>.</summary>
    private sealed unsafe class SamplePass(TemporalSignal signal) : RenderPass
    {
        public Vector4 Value { get; set; }

        /// <summary>The signal whose samples the pass writes.</summary>
        public TemporalSignal Signal => signal;

        public override string Name => "test sample";

        public override void Record(FrameContext context)
        {
            for (int part = 0; part < signal.Parts; part++)
                context.Barriers.Image(signal.Sample(part), ImageUse.TransferDestination, discard: true);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
            var color = new ClearColorValue(
                float32_0: Value.X,
                float32_1: Value.Y,
                float32_2: Value.Z,
                float32_3: Value.W
            );
            var whole = new ImageSubresourceRange(
                aspectMask: ImageAspectFlags.ColorBit,
                baseMipLevel: 0,
                levelCount: 1,
                baseArrayLayer: 0,
                layerCount: 1
            );
            for (int part = 0; part < signal.Parts; part++)
                context.Vk.CmdClearColorImage(
                    commandBuffer: context.CommandBuffer,
                    image: signal.Sample(part).Handle,
                    imageLayout: ImageLayout.TransferDstOptimal,
                    pColor: &color,
                    rangeCount: 1,
                    pRanges: &whole
                );
        }
    }
}
