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
    public void AShortHistoryRepairsAnIsolatedSampleFromItsSameSurfaceNeighbors()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Samples = (column, row) =>
                new Vector4(new Vector3(column == Width / 2 + 4 && row == Height / 2 ? 9 : 0), 1);

            renderer.Render(scene, 1f);

            for (int part = 0; part < 2; part++)
            {
                (Vector3 light, float length) = CenterOf(renderer, sample, part);
                light.Should().Be(Vector3.One, "one of the nine equally weighted surface samples holds light 9");
                length.Should().Be(1);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
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
            // Within one half-float step of 1.25, 2^-10: a driver can round the float result toward zero when it stores
            // the half-float history, as NVIDIA's does (ticket t8-en95). Without the clamp the value is 1.0625, 192 steps
            // away.
            light
                .X.Should()
                .BeApproximately(1.25f, 1f / 1024, "the clamp moves the slow history to the fast history's mean");
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
    [InlineData(0.05f)]
    [InlineData(0.4f)]
    [InlineData(0.8f)]
    public void TheDefaultSpecularHistoryKeepsAcceptedFramesAtEveryRoughness(float roughness)
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Specular(0.4f),
            roughness: roughness
        );
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 0f);
            renderer.Render(scene, 1f);

            Continue(renderer, scene, frames: 40);

            CenterOf(renderer, sample, part: 0).Length.Should().Be(41);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(4.5f, 12f)]
    [InlineData(3.5f, 12f)]
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

            CenterOf(renderer, sample, part: 0).Length.Should().Be(12f);
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
    [InlineData(true, 0.05f, 20f, 8f)]
    [InlineData(true, 0.05f, 39f, 8f)]
    [InlineData(true, 0.05f, 41f, 1f)]
    [InlineData(false, 0.05f, 20f, 1f)]
    [InlineData(true, 0.38f, 20f, 8f)]
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

    [Collection(GpuCollection.Name)]
    public sealed class NeighborLimiterPrototype
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ThePrototypeLimitsAnIsolatedBrightRayBlockInEitherLightComponent(bool reflected)
        {
            SignalLight light = reflected ? SignalLight.Specular : SignalLight.Diffuse;
            static Vector4 samples(int column, int row) =>
                new(new Vector3(4, 2, 1) * (column is >= 32 and <= 33 && row is >= 18 and <= 19 ? 500 : 1), 7);
            (Vector3 Light, float Length) control = FrameWithPrototype(false, light, samples);
            (Vector3 Light, float Length) limited = FrameWithPrototype(true, light, samples);

            limited
                .Light.X.Should()
                .BeLessThan(
                    control.Light.X * 0.2f,
                    "the selected prototype includes limiting without a second setting"
                );
            limited.Length.Should().Be(1);
            (limited.Light.X / limited.Light.Y).Should().BeApproximately(2, 0.01f);
            (limited.Light.Y / limited.Light.Z).Should().BeApproximately(2, 0.01f);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AUniformBrightRegionIsUnchangedByLimiting(bool reflected)
        {
            SignalLight light = reflected ? SignalLight.Specular : SignalLight.Diffuse;
            static Vector4 samples(int _1, int _2) => new(new Vector3(2000, 1000, 500), 7);

            FrameWithPrototype(true, light, samples).Should().Be(FrameWithPrototype(false, light, samples));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AMostlyDarkNeighborhoodLimitsAdjacentBrightRayBlocks(bool reflected)
        {
            SignalLight light = reflected ? SignalLight.Specular : SignalLight.Diffuse;
            static Vector4 samples(int column, int row) =>
                new(new Vector3(column is >= 32 and <= 35 && row is >= 18 and <= 19 ? 2000 : 1), 7);
            (Vector3 Light, float Length) control = FrameWithPrototype(false, light, samples);
            (Vector3 Light, float Length) limited = FrameWithPrototype(true, light, samples);

            limited
                .Light.X.Should()
                .BeLessThan(
                    control.Light.X * 0.2f,
                    "one bright neighboring ray block must not protect the center from the mostly dark neighborhood"
                );
        }

        [Theory]
        [InlineData(4, 3f)]
        [InlineData(5, 4f)]
        [InlineData(6, 3.5f)]
        [InlineData(7, 4f)]
        [InlineData(8, 4.5f)]
        public void LimitingUsesTheMiddleBrightnessAmongTheFiniteNeighbors(int support, float middleBrightness)
        {
            float[] brightness = [1000, 2, 1, 4, 7, 3, 5, 6];
            Vector4 samples(int column, int row)
            {
                if (column == Width / 2 && row == Height / 2)
                    return new(new Vector3(1000), 7);
                int neighbor = (column - Width / 2, row - Height / 2) switch
                {
                    (-2, -2) => 0,
                    (0, -2) => 1,
                    (2, -2) => 2,
                    (-2, 0) => 3,
                    (2, 0) => 4,
                    (-2, 2) => 5,
                    (0, 2) => 6,
                    (2, 2) => 7,
                    _ => -1,
                };
                return new(new Vector3(neighbor >= 0 && neighbor < support ? brightness[neighbor] : float.NaN), 7);
            }
            float ratio = 4 * middleBrightness / 1000;
            float expected = 1000 * ratio * (2 - ratio);

            FrameWithPrototype(true, SignalLight.Specular, samples).Light.X.Should().BeApproximately(expected, 0.04f);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TheOffControlPreservesAnIsolatedBrightEstimate(bool reflected)
        {
            SignalLight light = reflected ? SignalLight.Specular : SignalLight.Diffuse;
            static Vector4 samples(int column, int row) =>
                new(new Vector3(column is >= 32 and <= 33 && row is >= 18 and <= 19 ? 2000 : 1), 7);

            FrameWithPrototype(false, light, samples).Light.X.Should().Be(2000);
        }

        [Fact]
        public void TooFewFiniteNeighborsLeaveTheEstimateUnchanged()
        {
            static Vector4 samples(int column, int row) =>
                new(column == Width / 2 && row == Height / 2 ? new Vector3(2000) : new Vector3(float.NaN), 7);

            FrameWithPrototype(true, SignalLight.Specular, samples)
                .Should()
                .Be(FrameWithPrototype(false, SignalLight.Specular, samples));
        }

        [Fact]
        public void ScalingAllEstimatesScalesTheLimitedResultByTheSameRatio()
        {
            static Vector4 samples(int column, int row) =>
                new(new Vector3(column is >= 32 and <= 33 && row is >= 18 and <= 19 ? 1000 : 1), 7);
            static Vector4 scaledSamples(int column, int row)
            {
                Vector4 value = samples(column, row);
                return new(new Vector3(value.X, value.Y, value.Z) * 8, value.W);
            }

            (Vector3 Light, float Length) original = FrameWithPrototype(true, SignalLight.Specular, samples);
            (Vector3 Light, float Length) scaled = FrameWithPrototype(true, SignalLight.Specular, scaledSamples);

            scaled.Light.X.Should().BeApproximately(original.Light.X * 8, 0.05f);
            original.Light.X.Should().BeLessThan(10, "the isolated estimate must actually be limited");
        }

        private static (Vector3 Light, float Length) FrameWithPrototype(
            bool enabled,
            SignalLight light,
            Func<int, int, Vector4> samples
        )
        {
            string? oldPrototype = Environment.GetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_PROTOTYPE");
            VulkanRenderer? renderer = null;
            try
            {
                Environment.SetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_PROTOTYPE", enabled ? "1" : null);
                (VulkanRenderer created, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: Parameters with
                    {
                        Light = light,
                        HistoryFixFrames = 0,
                    }
                );
                renderer = created;
                sample.Samples = samples;
                renderer.Render(scene, 1f);
                return CenterOf(renderer, sample, 0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TERTIUS_INDIRECT_LIGHT_PROTOTYPE", oldPrototype);
                if (renderer is not null)
                    Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }

    [Collection(ProcessEnvironmentCollection.Name)]
    public sealed class ReflectionDistanceHistory
    {
        [Fact]
        public void AnAcceptedPastHitStillDefinesDistanceWhenTheLatestRayMisses() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    sample.Value = new(x: 1, y: 1, z: 1, w: 0);

                    Continue(renderer, scene, frames: 1);

                    GuideAt(renderer, sample).Distance.Should().BeApproximately(8, 0.01f);
                    GuideAt(renderer, sample).Support.Should().BeApproximately(0.5f, 0.001f);
                }
            );

        [Fact]
        public void KnownDistancesUseTheSameAcceptedFrameWeightsAsLight() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    sample.Value = new(x: 1, y: 1, z: 1, w: 24);

                    Continue(renderer, scene, frames: 1);

                    GuideAt(renderer, sample).Distance.Should().BeApproximately(16, 0.01f);
                    GuideAt(renderer, sample).Support.Should().Be(1);
                }
            );

        [Fact]
        public void ContinuingMissesReduceSupportWithoutBecomingZeroMetreHits() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    sample.Value = new(x: 1, y: 1, z: 1, w: 0);

                    Continue(renderer, scene, frames: 2);

                    GuideAt(renderer, sample).Distance.Should().BeApproximately(8, 0.01f);
                    GuideAt(renderer, sample).Support.Should().BeApproximately(1f / 3, 0.001f);
                }
            );

        [Fact]
        public void EarlierMissesDoNotPullTheFirstKnownDistanceTowardTheReceiver() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 0);
                    renderer.Render(scene, 1);
                    sample.Value = new(x: 1, y: 1, z: 1, w: 16);

                    Continue(renderer, scene, frames: 1);

                    GuideAt(renderer, sample).Distance.Should().BeApproximately(16, 0.01f);
                    GuideAt(renderer, sample).Support.Should().BeApproximately(0.5f, 0.001f);
                }
            );

        [Fact]
        public void ANonfiniteLightSampleDoesNotReplaceAnAcceptedDistance() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    sample.Value = new(x: float.NaN, y: float.NaN, z: float.NaN, w: 24);

                    Continue(renderer, scene, frames: 1);

                    GuideAt(renderer, sample).Distance.Should().BeApproximately(8, 0.01f);
                    GuideAt(renderer, sample).Support.Should().Be(1);
                    CenterOf(renderer, sample, part: 0).Length.Should().Be(1);
                }
            );

        [Fact]
        public void ACameraCutDiscardsDistanceAlongWithItsLightHistory() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    scene.CameraCuts++;
                    sample.Value = new(x: 1, y: 1, z: 1, w: 0);

                    Continue(renderer, scene, frames: 1);

                    GuideAt(renderer, sample).Support.Should().Be(0);
                    CenterOf(renderer, sample, part: 0).Length.Should().Be(1);
                }
            );

        [Fact]
        public void AChangedReceiverDoesNotInheritTheOldSurfacesDistance() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    RenderInstance receiver = scene.Instances[0];
                    receiver.Material = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.8f));
                    scene.Instances[0] = receiver;
                    sample.Value = new(x: 1, y: 1, z: 1, w: 0);

                    Continue(renderer, scene, frames: 1);

                    GuideAt(renderer, sample).Support.Should().Be(0);
                    CenterOf(renderer, sample, part: 0).Length.Should().Be(1);
                }
            );

        [Fact]
        public void BrighterSamplesDoNotReceiveExtraDistanceWeight() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    sample.Value = new(x: 64, y: 64, z: 64, w: 24);

                    Continue(renderer, scene, frames: 1);

                    GuideAt(renderer, sample).Distance.Should().BeApproximately(16, 0.01f);
                    CenterOf(renderer, sample, part: 0).Light.X.Should().BeApproximately(32.5f, 0.01f);
                }
            );

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(65504)]
        public void AnUnknownOrSaturatedDistanceAddsNoFalseHitSupport(float distance) =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    sample.Value = new(x: 1, y: 1, z: 1, w: distance);

                    Continue(renderer, scene, frames: 1);

                    GuideAt(renderer, sample).Distance.Should().BeApproximately(8, 0.01f);
                    GuideAt(renderer, sample).Support.Should().BeApproximately(0.5f, 0.001f);
                }
            );

        [Fact]
        public void AHistoryWithoutAnyKnownHitHasNoDistanceSupport() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 0);

                    renderer.Render(scene, 1);
                    Continue(renderer, scene, frames: 4);

                    GuideAt(renderer, sample).Support.Should().Be(0);
                    CenterOf(renderer, sample, part: 0).Light.Should().Be(Vector3.One);
                }
            );

        [Fact]
        public void RetainingDistanceDoesNotChangeTheAccumulatedLight()
        {
            Vector3 enabled = LightAfterSequence(enabled: true);
            Vector3 disabled = LightAfterSequence(enabled: false);

            enabled.Should().Be(disabled);
        }

        [Fact]
        public void DistanceSupportFollowsTheExistingShortLightHistoryRepair() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Samples = (column, row) =>
                        new Vector4(Vector3.One, column == Width / 2 && row == Height / 2 ? 0 : 8);

                    renderer.Render(scene, 1);

                    GuideAt(renderer, sample).Distance.Should().BeApproximately(8, 0.01f);
                    GuideAt(renderer, sample).Support.Should().BeApproximately(8f / 9, 0.001f);
                    CenterOf(renderer, sample, part: 0).Light.Should().Be(Vector3.One);
                },
                repair: true
            );

        private static Vector3 LightAfterSequence(bool enabled)
        {
            Vector3 result = default;
            WithHistory(
                (renderer, sample, scene) =>
                {
                    sample.Value = new(x: 1, y: 1, z: 1, w: 8);
                    renderer.Render(scene, 1);
                    sample.Value = new(x: 3, y: 3, z: 3, w: 0);
                    Continue(renderer, scene, frames: 1);
                    sample.Value = new(x: 7, y: 7, z: 7, w: 16);
                    Continue(renderer, scene, frames: 1);
                    result = CenterOf(renderer, sample, part: 0).Light;
                },
                enabled
            );
            return result;
        }

        private static (float Distance, float Support) GuideAt(VulkanRenderer renderer, SamplePass sample)
        {
            float[] guide = renderer.ReadImage(sample.Signal.ReflectionGuide(0));
            int center = 4 * (Height / 2 * Width + Width / 2);
            float support = guide[center + 1];
            return (support > 0 ? guide[center] / support : 0, support);
        }

        private static void WithHistory(
            Action<VulkanRenderer, SamplePass, RenderSnapshot> action,
            bool enabled = true,
            bool repair = false
        )
        {
            string? previous = Environment.GetEnvironmentVariable("TERTIUS_REFLECTION_SHARING");
            VulkanRenderer? renderer = null;
            try
            {
                Environment.SetEnvironmentVariable("TERTIUS_REFLECTION_SHARING", enabled ? "1" : null);
                (VulkanRenderer created, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: SignalParameters.Specular(0) with
                    {
                        Cap = 4,
                        SmoothCap = 4,
                        HistoryFixFrames = repair ? 4 : 0,
                    }
                );
                renderer = created;
                action(renderer, sample, scene);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TERTIUS_REFLECTION_SHARING", previous);
                if (renderer is not null)
                    Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }

    /// <summary>The precise specular experiment's accepted history and unchanged rejection and anti-lag.</summary>
    [Collection(GpuCollection.Name)]
    public sealed class PreciseSpecularHistory
    {
        [Theory]
        [InlineData(null, 0.045f)]
        [InlineData(null, 0.8f)]
        [InlineData("", 0.045f)]
        [InlineData("", 0.8f)]
        [InlineData("precise-uncapped", 0.045f)]
        [InlineData("precise-uncapped", 0.8f)]
        public void AnUncappedConstantHistoryGrowsPastTwoThousandAndFortyEightFrames(string? mode, float roughness) =>
            WithHistory(
                mode,
                roughness,
                (renderer, sample, scene) =>
                {
                    sample.Value = Vector4.One;
                    renderer.Render(scene, 1);
                    Continue(renderer, scene, frames: 63);
                    CenterOf(renderer, sample, part: 0)
                        .Length.Should()
                        .Be(64, "accepted specular frames have no configured age cap");

                    Continue(renderer, scene, frames: 2048);

                    for (int part = 0; part < 2; part++)
                    {
                        (Vector3 light, float length) = CenterOf(renderer, sample, part);
                        length.Should().Be(2112, "the count must not stop at half precision's unit-increment limit");
                        light.Should().Be(Vector3.One);
                        Console.WriteLine(
                            $"Uncapped accepted history: roughness={roughness:R}, part={part}, age={length:R}, radiance={light.X:R}"
                        );
                    }
                }
            );

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("precise-uncapped")]
        public void ALongAcceptedHistoryRetainsRadianceUpdatesSmallerThanHalfPrecisionCanStore(string? mode) =>
            WithHistory(
                mode,
                0.8f,
                (renderer, sample, scene) =>
                {
                    sample.Samples = NeighborhoodSamples(1);
                    renderer.Render(scene, 1);
                    Continue(renderer, scene, frames: 63);
                    CenterOf(renderer, sample, part: 0).Length.Should().Be(64);
                    Continue(renderer, scene, frames: 2048);
                    float before = CenterOf(renderer, sample, part: 0).Light.X;
                    sample.Samples = NeighborhoodSamples(1.125f);

                    Continue(renderer, scene, frames: 8);

                    (Vector3 light, float length) = CenterOf(renderer, sample, part: 0);
                    length.Should().Be(2120);
                    Console.WriteLine(
                        $"Small updates after long history: before={before:R}, after={light.X:R}, age={length:R}"
                    );
                    light
                        .X.Should()
                        .BeGreaterThan(
                            before,
                            "eight accepted small updates must not round back to the old half float"
                        );
                    light
                        .X.Should()
                        .BeApproximately(
                            (before * 2112 + 1.125f * 8) / 2120,
                            0.00001f,
                            "the broad fast-neighborhood variance leaves this running mean unclamped"
                        );
                }
            );

        [Theory]
        [InlineData(null)]
        [InlineData("precise-uncapped")]
        public void AChangedSurfaceStillDiscardsAnUncappedHistory(string? mode) =>
            WithHistory(
                mode,
                0.8f,
                (renderer, sample, scene) =>
                {
                    sample.Value = Vector4.One;
                    renderer.Render(scene, 1);
                    Continue(renderer, scene, frames: 63);
                    CenterOf(renderer, sample, part: 0).Length.Should().Be(64);
                    RenderInstance receiver = scene.Instances[0];
                    receiver.Material = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.2f));
                    scene.Instances[0] = receiver;
                    sample.Value = new Vector4(new Vector3(2), 1);

                    Continue(renderer, scene, frames: 1);

                    CenterOf(renderer, sample, part: 0).Length.Should().Be(1);
                    CenterOf(renderer, sample, part: 0).Light.Should().Be(new Vector3(2));
                }
            );

        [Theory]
        [InlineData(null)]
        [InlineData("precise-uncapped")]
        public void AUniformIlluminationStepAfterLongHistoryStillFollowsTheFastHistoryClamp(string? mode) =>
            WithHistory(
                mode,
                0.8f,
                (renderer, sample, scene) =>
                {
                    sample.Value = Vector4.One;
                    renderer.Render(scene, 1);
                    Continue(renderer, scene, frames: 63);
                    CenterOf(renderer, sample, part: 0).Length.Should().Be(64);
                    Continue(renderer, scene, frames: 2048);
                    sample.Value = new Vector4(new Vector3(2), 1);

                    Continue(renderer, scene, frames: 10);

                    (Vector3 light, float length) = CenterOf(renderer, sample, part: 0);
                    Console.WriteLine(
                        $"Long-history step after 10 frames: radiance={light.X:R}, accepted age={length:R}"
                    );
                    light.X.Should().BeInRange(1.9f, 2);
                    length
                        .Should()
                        .BeLessThan(2112, "the existing anti-lag still shortens history when the clamp moves it");
                }
            );

        [Theory]
        [InlineData("original", 0.045f, 4)]
        [InlineData("original", 0.4f, 18)]
        [InlineData("original", 0.8f, 32)]
        [InlineData("precise-capped", 0.045f, 4)]
        [InlineData("precise-capped", 0.4f, 18)]
        [InlineData("precise-capped", 0.8f, 32)]
        public void TheExplicitCappedControlsRetainTheOriginalRoughnessDependentAgeCap(
            string mode,
            float roughness,
            int cap
        ) =>
            WithHistory(
                mode,
                roughness,
                (renderer, sample, scene) =>
                {
                    sample.Value = Vector4.One;
                    renderer.Render(scene, 1);

                    Continue(renderer, scene, frames: 63);

                    // Within 0.001 frames: the shader reads the roughness as its 8-bit code over 255, a division that
                    // Vulkan lets be 2.5 ulps off (ticket ter-8esk). The cap rises about 39 frames per unit of roughness,
                    // so an ulp moves it by about 1e-6 frames, and a wrong cap by whole frames.
                    CenterOf(renderer, sample, part: 0).Length.Should().BeApproximately(cap, 0.001f);
                    sample
                        .Signal.Slow(0)
                        .Format.Should()
                        .Be(mode == "original" ? Format.R16G16B16A16Sfloat : Format.R32G32B32A32Sfloat);
                    sample.Signal.Sample(0).Format.Should().Be(Format.R16G16B16A16Sfloat);
                    sample.Signal.HitImages(0, 0).LastFast.Format.Should().Be(Format.R16G16B16A16Sfloat);
                }
            );

        private static Func<int, int, Vector4> NeighborhoodSamples(float center) =>
            (column, row) =>
                new Vector4(new Vector3(column == Width / 2 && row == Height / 2 ? center : (column + row) % 2 * 2), 1);

        private static void WithHistory(
            string? mode,
            float roughness,
            Action<VulkanRenderer, SamplePass, RenderSnapshot> action
        )
        {
            string? previous = Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY");
            VulkanRenderer? renderer = null;
            try
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", mode);
                (VulkanRenderer created, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: SignalParameters.Specular(0),
                    roughness: roughness
                );
                renderer = created;
                action(renderer, sample, scene);
            }
            finally
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", previous);
                if (renderer is not null)
                    Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }
}
