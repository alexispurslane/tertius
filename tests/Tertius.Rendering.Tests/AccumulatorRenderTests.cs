using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The accumulator of a signal (docs/plans/temporal-renderer.md, section 8): a constant sample stays constant, a step of the
/// sample reaches the history within the bound of the anti-lag, a sample that is not finite counts as no sample, a pixel
/// without a sample or a history takes the closed form, and the specular signal's cap and own tests. The test makes a
/// signal of two parts, and a test pass, its producer, writes a chosen sample into both parts in each frame. The camera stands still before a wall that fills the view, and each frame steps one tick,
/// so each frame continues the last one.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AccumulatorRenderTests
{
    private const int Width = 64,
        Height = 36;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The test signal's parameter set: a diffuse signal with a history of 16 frames and a fast history of 4.
    /// </summary>
    private static readonly SignalParameters Parameters = SignalParameters.Diffuse with { Cap = 16, SmoothCap = 16 };

    /// <summary>The ambient light of the scenes, the same from the sky and from the ground, so that the environment's
    /// closed form is this light in every direction.</summary>
    private static readonly Vector3 Ambient = new(0.2f, 0.3f, 0.4f);

    /// <summary>
    /// The renderer with the test signal and its producer and accumulation after the direct pass, under
    /// <paramref name="exposure"/>, the fixed exposure of the tests without it.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) Accumulating(
        ExposureSetting exposure = default,
        SignalParameters? parameters = null,
        float roughness = 0.5f
    )
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, exposure: exposure);
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: roughness));
        RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        scene.Ambient = new()
        {
            SkyColor = Ambient,
            GroundColor = Ambient,
            Intensity = 1f,
        };
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
        var sample = new SamplePass(
            renderer.CreateSignal(parameters: parameters ?? Parameters, name: "test", parts: 2),
            renderer.Allocator
        );
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APixelWithoutASampleAndWithoutAHistoryTakesTheEnvironmentsClosedForm(bool specular)
    {
        // The ambient light is the same in every direction, so its irradiance term and its specular term are both that
        // light, pre-exposed.
        SignalParameters parameters = specular ? SignalParameters.Specular(0.4f) : Parameters;
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(parameters: parameters);
        try
        {
            sample.Value = new(x: float.NaN, y: float.NaN, z: float.NaN, w: 1f);

            renderer.Render(scene, 1f);

            float scale = MathF.Pow(2, renderer.ReadExposureStops().Stops);
            Vector3 closedForm = CenterOf(renderer, sample, part: 0).Light;
            closedForm.X.Should().BeApproximately(Ambient.X * scale, 2e-3f * Ambient.X * scale);
            closedForm.Y.Should().BeApproximately(Ambient.Y * scale, 2e-3f * Ambient.Y * scale);
            closedForm.Z.Should().BeApproximately(Ambient.Z * scale, 2e-3f * Ambient.Z * scale);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0.05f, 4f)]
    [InlineData(0.4f, 18f)]
    [InlineData(0.8f, 32f)]
    public void TheSpecularSignalsCapFollowsTheRoughness(float roughness, float cap)
    {
        // 4 frames up to a roughness of 0.1, then linear to 32 frames at 0.7.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: roughness
        );
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 0f);
            renderer.Render(scene, 1f);

            Continue(renderer, scene, frames: 40);

            CenterOf(renderer, sample, part: 0).Length.Should().BeApproximately(cap, 1e-3f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(4.5f, 4f)]
    [InlineData(3.5f, 4f)]
    public void ARejectedReflectedLocationDoesNotDiscardUnchangedLightAtTheSurfaceLocation(
        float hitDistance,
        float length
    )
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: 0.05f
        );
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 2f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 10);

            sample.Value = new(x: 1f, y: 1f, z: 1f, w: hitDistance);
            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0).Length.Should().Be(length);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void IndependentRoughReflectionDistancesDoNotRestartAnUnchangedLightsHistory()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: 0.38f
        );
        try
        {
            for (int frame = 0; frame < 12; frame++)
            {
                sample.Value = new(x: 1f, y: 1f, z: 1f, w: frame % 2 == 0 ? 2f : 8f);
                Continue(renderer, scene, frames: 1);
            }

            renderer.ReadSurfaceTest()[Height / 2 * Width + Width / 2].Should().Be(255);
            CenterOf(renderer, sample, part: 0)
                .Length.Should()
                .Be(
                    12f,
                    "independent reflection rays at roughness 0.38 see two depths, but the camera, surface and light never change"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void IndependentSmoothReflectionDistancesDoNotRestartAnUnchangedLightsHistory()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: 0.05f
        );
        try
        {
            for (int frame = 0; frame < 12; frame++)
            {
                sample.Value = new(x: 1f, y: 1f, z: 1f, w: frame % 2 == 0 ? 2f : 8f);
                Continue(renderer, scene, frames: 1);
            }

            CenterOf(renderer, sample, part: 0).Length.Should().Be(4f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0.05f, 1f, 0f)]
    [InlineData(0.05f, 0f, 1f)]
    [InlineData(0.38f, 1f, 0f)]
    [InlineData(0.38f, 0f, 1f)]
    public void AChangedReflectedLightReplacesNinetyPercentOfTheOldLightWithinTenFrames(
        float roughness,
        float before,
        float after
    )
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: roughness
        );
        try
        {
            sample.Value = new(x: before, y: before, z: before, w: 2f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);

            sample.Value = new(x: after, y: after, z: after, w: 8f);
            Continue(renderer, scene, frames: 10);

            CenterOf(renderer, sample, part: 0).Light.X.Should().BeApproximately(after, 0.1f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0.6f, 0.38f)]
    [InlineData(0f, 0.05f)]
    [InlineData(0f, 0.8f)]
    public void AReceiverWhoseNormalOrRoughnessChangedDoesNotKeepOldReflectedLight(float turn, float roughness)
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: 0.38f
        );
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 2f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 12);
            RenderInstance receiver = scene.Instances[0];
            receiver.Material = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: roughness));
            receiver.PreviousRotation = receiver.Rotation;
            receiver.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn);
            scene.Instances[0] = receiver;
            sample.Value = new(x: 4f, y: 4f, z: 4f, w: 2f);

            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0).Length.Should().Be(1f);
            CenterOf(renderer, sample, part: 0).Light.Should().Be(new Vector3(4f));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnUncoveredReceiverDoesNotInheritTheOccludersReflectedLight()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: 0.38f
        );
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId paint = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.38f));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: paint,
                    position: new(0, 0, -6),
                    scale: new(60, 40, 1),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 2f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 12);
            scene.Instances.RemoveAt(1);
            sample.Value = new(x: 4f, y: 4f, z: 4f, w: 2f);

            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0).Length.Should().Be(1f);
            CenterOf(renderer, sample, part: 0).Light.Should().Be(new Vector3(4f));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(true, 0.05f, 20f, 4f)]
    [InlineData(true, 0.05f, 39f, 4f)]
    [InlineData(true, 0.05f, 41f, 1f)]
    [InlineData(false, 0.05f, 20f, 1f)]
    [InlineData(true, 0.38f, 20f, 4f)]
    [InlineData(true, 0.45f, 20f, 1f)]
    public void AMirrorPixelWhoseSurfaceLeftTheLastImageKeepsItsHistoryThroughTheReflectedPoint(
        bool specular,
        float roughness,
        float hitDistance,
        float length
    )
    {
        // The camera steps 1 m to the right along a mirror 20 m away, whose hits lie 20 m behind it. The mirror's points
        // move 1.56 px to the left, the reflected points 40 m away 0.78 px. So the surface point of the column before the
        // last lay past the last image's edge, and its reflected point inside it. The diffuse signal has the surface
        // place alone.
        SignalParameters parameters = specular ? SignalParameters.Specular(0.4f) : Parameters;
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: parameters with
            {
                Cap = 4,
                SmoothCap = 4,
            },
            roughness: roughness
        );
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 20f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 6);

            scene.Camera.PreviousPosition = scene.Camera.Position;
            scene.Camera.Position += new Vector3(1f, 0, 0);
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: hitDistance);
            Continue(renderer, scene, frames: 1);

            float[] history = HistoryOf(renderer, sample, part: 0);
            history[4 * (Height / 2 * Width + Width - 2) + 3].Should().Be(length);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void LightOnANeighboringSurfaceDoesNotChooseTheReceiversReflectionHistory()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: 0.05f
        );
        try
        {
            sample.Samples = (column, _) => new Vector4(column >= 37 ? Vector3.One : new Vector3(4f), 20f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId paint = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.05f));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: paint,
                    position: new(7.1f, 0, -6),
                    scale: new(5.8f, 40, 1),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );
            scene.Camera.PreviousPosition = scene.Camera.Position;
            scene.Camera.Position += new Vector3(4f, 0, 0);
            sample.Samples = (column, _) => new Vector4(column > Width / 2 ? new Vector3(100f) : Vector3.One, 20f);

            Continue(renderer, scene, frames: 1);

            float[] distances = renderer.ReadDistance();
            distances[Height / 2 * Width + Width / 2].Should().BeInRange(19f, 20f);
            distances[Height / 2 * Width + Width / 2 + 1].Should().BeInRange(5f, 6f);
            CenterOf(renderer, sample, part: 0).Light.X.Should().BeApproximately(1f, 0.1f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OneBrightSampleDoesNotRestartAnOtherwiseUnchangedRoughReflectionsHistory()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: 0.38f
        );
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 2f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 12);
            sample.Samples = (column, row) =>
                new Vector4(column == Width / 2 && row == Height / 2 ? new Vector3(20f) : Vector3.One, 8f);

            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0).Length.Should().BeGreaterThanOrEqualTo(8f);
            CenterOf(renderer, sample, part: 0).Light.X.Should().BeLessThan(3f);
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

    /// <summary>The producer of <paramref name="signal"/>: it clears each part to <see cref="Value"/> or uploads
    /// <see cref="Samples"/> per pixel. Uploads stay alive until the pass is disposed.</summary>
    private sealed unsafe class SamplePass(TemporalSignal signal, DeviceAllocator allocator) : RenderPass
    {
        private readonly List<GpuBuffer> uploads = [];

        public Vector4 Value { get; set; }

        public Func<int, int, Vector4>? Samples { get; set; }

        /// <summary>The signal whose samples the pass writes.</summary>
        public TemporalSignal Signal => signal;

        public override string Name => "test sample";

        public override void Record(FrameContext context)
        {
            for (int part = 0; part < signal.Parts; part++)
                context.Barriers.Image(signal.Sample(part), ImageUse.TransferDestination, discard: true);
            context.Barriers.Flush(context.Vk, context.CommandBuffer);
            if (Samples is { } samples)
            {
                Upload(context, samples);
                return;
            }
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

        private void Upload(FrameContext context, Func<int, int, Vector4> samples)
        {
            var texels = new Half[4 * Width * Height];
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width; column++)
            {
                Vector4 value = samples(column, row);
                int at = 4 * (row * Width + column);
                texels[at] = (Half)value.X;
                texels[at + 1] = (Half)value.Y;
                texels[at + 2] = (Half)value.Z;
                texels[at + 3] = (Half)value.W;
            }
            var staging = GpuBuffer.Create(
                device: context.Device,
                allocator: allocator,
                size: (ulong)(texels.Length * sizeof(Half)),
                usage: BufferUsageFlags.TransferSrcBit,
                memoryUsage: MemoryUsage.Upload,
                name: "accumulator test samples"
            );
            uploads.Add(staging);
            staging.Write<Half>(texels);
            var region = new BufferImageCopy
            {
                ImageSubresource = new(
                    aspectMask: ImageAspectFlags.ColorBit,
                    mipLevel: 0,
                    baseArrayLayer: 0,
                    layerCount: 1
                ),
                ImageExtent = new(Width, Height, 1),
            };
            for (int part = 0; part < signal.Parts; part++)
                context.Vk.CmdCopyBufferToImage(
                    commandBuffer: context.CommandBuffer,
                    srcBuffer: staging.Handle,
                    dstImage: signal.Sample(part).Handle,
                    dstImageLayout: ImageLayout.TransferDstOptimal,
                    regionCount: 1,
                    pRegions: &region
                );
        }

        public override void Dispose()
        {
            foreach (GpuBuffer upload in uploads)
                upload.Dispose();
            base.Dispose();
        }
    }
}
