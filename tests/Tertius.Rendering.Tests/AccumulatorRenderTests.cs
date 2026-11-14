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

    /// <summary>
    /// The test signal's parameter set with the narrow box of the reflections (<see cref="NarrowBox"/>): the clamp's box
    /// holds only the spread of the fast histories around the pixel.
    /// </summary>
    private static readonly SignalParameters NarrowParameters = Parameters with { Confidence = new NarrowBox() };

    /// <summary>The ambient light of the scenes, the same from the sky and from the ground, so that the environment's
    /// closed form is this light in every direction.</summary>
    private static readonly Vector3 Ambient = new(0.2f, 0.3f, 0.4f);

    /// <summary>
    /// The renderer with the test signal and its producer and accumulation after the direct pass, under
    /// <paramref name="exposure"/>, the fixed exposure of the tests without it. The wall fills the view, unless
    /// <paramref name="wall"/> gives its center and size: then the sky shows around it.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) Accumulating(
        ExposureSetting exposure = default,
        SignalParameters? parameters = null,
        float roughness = 0.5f,
        (Vector3 Center, Vector3 Size)? wall = null
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
                position: wall?.Center ?? new(0, 0, -20),
                scale: wall?.Size ?? new(60, 40, 1),
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
    public void UnderTheNarrowBoxTheHistoryReachesNinetyPercentOfADimmingStepWithinTenFrames()
    {
        // Without the anti-lag, a history of 16 frames reaches 90 % after ln 0.1 / ln(15 / 16) ≈ 36 frames. The clamp
        // follows the fast history of 4 frames, which reaches 90 %, 0.118, after 8. No ray saw a change, so only the clamp
        // follows the step. The bounce light's widened box holds the step's own spread.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(parameters: NarrowParameters);
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            CenterOf(renderer, sample, part: 0).Length.Should().Be(16, "the history holds its cap before the step");

            FramesUntilTheCenterFollowsADimming(renderer: renderer, sample: sample, scene: scene)
                .Should()
                .BeInRange(2, 10);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Steps the sample from 1 down to 0.02 and draws until the center's slow history of part 0 holds 90 % of the step,
    /// 0.118 or less: the frames that it took, 40 when it did not within 40.
    /// </summary>
    private static int FramesUntilTheCenterFollowsADimming(
        VulkanRenderer renderer,
        SamplePass sample,
        RenderSnapshot scene,
        Action? beforeEachFrame = null
    )
    {
        sample.Value = new(x: 0.02f, y: 0.02f, z: 0.02f, w: 1f);
        int frames = 0;
        while (frames < 40 && (frames == 0 || CenterOf(renderer, sample, part: 0).Light.X > 0.118f))
        {
            beforeEachFrame?.Invoke();
            Continue(renderer, scene, frames: 1);
            frames++;
        }
        return frames;
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
    public void UnderTheNarrowBoxADimmingStepShortensTheHistoryByTheClampsMoveOverHalfTheMeansLight()
    {
        // The first frame after the step: the slow history of 16 frames blends to 1 − 0.8 / 16 = 0.95, the fast history of
        // 4 to 0.8, and the clamp moves the slow one to the fast one's mean, 0.8. The lag is 0.15 / 0.8 = 0.1875, a share
        // 0.375 of the anti-lag's 0.5, so the length falls from 16 to 16 · 0.625 = 10.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(parameters: NarrowParameters);
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            CenterOf(renderer, sample, part: 0).Length.Should().Be(16, "the history holds its cap before the step");

            sample.Value = new(x: 0.2f, y: 0.2f, z: 0.2f, w: 1f);
            Continue(renderer, scene, frames: 1);

            (Vector3 light, float length) = CenterOf(renderer, sample, part: 0);
            light
                .X.Should()
                .BeApproximately(0.8f, 0.001f, "the clamp moves the slow history to the fast history's mean");
            length.Should().BeApproximately(10f, 0.01f, "the anti-lag shortens the history by the clamp's move");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightChangeThatTheRaysSawMovesTheHistoryToTheFastWindow()
    {
        // The rays saw the light change in this frame (age 0, code 1). The blend gives the doubled sample its normal
        // weight, then the light evidence cuts the length to the fast cap, and the narrow box moves the slow history to
        // the fast one, 1.25 after one doubled sample of four (docs/plans/bounce-light-confidence.md, sections 4 and 8).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);

            sample.AgeCode = 1;
            sample.Value = new(x: 2f, y: 2f, z: 2f, w: 1f);
            Continue(renderer, scene, frames: 1);

            for (int part = 0; part < 2; part++)
            {
                (Vector3 light, float length) = CenterOf(renderer, sample, part);
                light
                    .X.Should()
                    .BeApproximately(1.25f, 1e-3f, "the slow history follows the fast one in part {0}", part);
                length.Should().Be(FastCap, "the light evidence cuts part {0}", part);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(17u)]
    [InlineData(21u)]
    public void ALightChangeAsOldAsTheHistoryOrOlderLeavesTheHistoryAsItIs(uint code)
    {
        // A change 16 frames ago (code 17) or more is as old as the history of 16 frames or older: the history holds no
        // frame from before it, so the age cuts nothing, and the step's own spread widens the box around the history. A
        // light age of 16 or more is no recent change either, so the doubling is no evidence for its block.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);

            sample.AgeCode = code;
            sample.Value = new(x: 2f, y: 2f, z: 2f, w: 1f);
            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0)
                .Should()
                .Be((new Vector3(1.0625f), 16f), "the sample blends in with 1 / 16");
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

    /// <summary>
    /// Whether the pixel at <paramref name="column"/> and <paramref name="row"/> takes a rare bright sample in
    /// <paramref name="frame"/>: one pixel in <see cref="RareShare"/>, in a pattern that a hash of the three spreads
    /// over the image and over the frames.
    /// </summary>
    private static bool RareAt(int column, int row, int frame) => Hashed(column, row, frame) % RareShare == 0;

    /// <summary>A hash of three integers that spreads over the image and over the frames.</summary>
    private static uint Hashed(int column, int row, int frame)
    {
        uint hash = (uint)(column * 73856093 ^ row * 19349663 ^ frame * 83492791);
        hash ^= hash >> 13;
        hash *= 0x5bd1e995;
        hash ^= hash >> 15;
        return hash;
    }

    /// <summary>The share of the samples that are rare and bright: one in this many.</summary>
    private const uint RareShare = 32;

    /// <summary>The light of a rare bright sample, over a base light of 1: the samples' mean is 2.</summary>
    private const float RareLight = 1 + RareShare;

    /// <summary>
    /// Draws <paramref name="frames"/> frames of a still view whose samples are 1, but for one pixel in
    /// <see cref="RareShare"/> in each frame, whose sample is <see cref="RareLight"/>.
    /// </summary>
    private static void DrawRareBrightSamples(
        VulkanRenderer renderer,
        SamplePass sample,
        RenderSnapshot scene,
        int frames
    )
    {
        sample.Value = new(x: 1f, y: 1f, z: 1f, w: 8f);
        renderer.Render(scene, 1f);
        for (int frame = 0; frame < frames; frame++)
        {
            int drawn = frame;
            sample.Samples = (column, row) => new Vector4(new Vector3(RareAt(column, row, drawn) ? RareLight : 1f), 8f);
            Continue(renderer, scene, frames: 1);
        }
    }

    /// <summary>The slow history's light of part 0 at the pixels at least 2 pixels inside the image.</summary>
    private static float[] InteriorLightOf(VulkanRenderer renderer, SamplePass sample)
    {
        float[] history = HistoryOf(renderer, sample, part: 0);
        List<float> light = [];
        for (int row = 2; row < Height - 2; row++)
        for (int column = 2; column < Width - 2; column++)
            light.Add(history[4 * (row * Width + column)]);
        return [.. light];
    }

    [Fact]
    public void InAStillViewRareBrightDiffuseSamplesKeepTheirShareOfTheSlowHistorysLight()
    {
        // The samples' mean is 1 + 32 / 32 = 2. A clamp into the box of the fast histories alone cuts the rare bright
        // samples and reads low (ticket ter-18hc). The retained deviation widens the box to hold them
        // (docs/plans/bounce-light-confidence.md, section 7).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            DrawRareBrightSamples(renderer: renderer, sample: sample, scene: scene, frames: 160);

            InteriorLightOf(renderer, sample)
                .Average()
                .Should()
                .BeApproximately(2f, 0.1f, "the history holds the mean of its samples");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The retained deviation of part 0 after the last frame: the alpha of its fast history, per pixel.</summary>
    private static float[] RetainedDeviationOf(VulkanRenderer renderer, SamplePass sample)
    {
        float[] fast = renderer.ReadImage(sample.Signal.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1));
        float[] deviation = new float[Width * Height];
        for (int pixel = 0; pixel < deviation.Length; pixel++)
            deviation[pixel] = fast[4 * pixel + 3];
        return deviation;
    }

    /// <summary>The center pixel's index, row by row from the top.</summary>
    private const int Center = Height / 2 * Width + Width / 2;

    /// <summary>
    /// Whether the pixel at <paramref name="column"/> and <paramref name="row"/> lies in the ray block at the center: the
    /// 2 × 2 pixels at even coordinates that share one ray.
    /// </summary>
    private static bool InCenterRayBlock(int column, int row) => column / 2 == Width / 4 && row / 2 == Height / 4;

    [Fact]
    public void ASingleBrightRayBlockSampleFadesFromTheSlowHistoryAtTheSlowBlendsPace()
    {
        // The bounce light's cap of 32 takes the sample of 100 with the weight 1 / 32, so its four pixels hold an excess
        // of 4 · 100 / 32 = 12.5 at its draw, and each later draw keeps 31 / 32 of it, as long as no clamp cuts it. A dark
        // wall, so that a store into a half float, which may round toward zero, loses at most 2^−10 of the excess, not of
        // a bright wall's light: the temporal and the spatial dispatch each store the history once a draw.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            sample.Value = new(x: 0, y: 0, z: 0, w: 1);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            sample.Samples = (column, row) => new Vector4(new Vector3(InCenterRayBlock(column, row) ? 100 : 0), 1);
            Continue(renderer, scene, frames: 1);
            sample.Samples = null;

            for (int draw = 0; draw <= 32; draw++)
            {
                if (draw > 0)
                    Continue(renderer, scene, frames: 1);
                float[] history = HistoryOf(renderer, sample, part: 0);
                float excess = Enumerable
                    .Range(0, Width * Height)
                    .Where(pixel => InCenterRayBlock(pixel % Width, pixel / Width))
                    .Sum(pixel => history[4 * pixel]);
                float expected = 12.5f * MathF.Pow(31f / 32, draw);
                float rounding = 2 * (draw + 1) * MathF.Pow(2, -10);
                excess
                    .Should()
                    .BeInRange(
                        (1 - 0.02f - rounding) * expected,
                        1.02f * expected,
                        "{0} draws after the sample, the slow blend alone shortens the excess",
                        draw
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Light 0 or 2 at every pixel, the same in a frame and the other in the next: a deviation of 1.</summary>
    private static Func<int, int, Vector4> Blink(int frame) => (_, _) => new Vector4(new Vector3((frame & 1) * 2), 1);

    [Fact]
    public void TheRetainedDeviationFadesWithTheSlowBlendNotTheFastOne()
    {
        // After the samples 0 and 2 in turn, 16 samples of their mean 1 add almost no spread. The retained deviation's
        // variance keeps 31 / 32 a draw, as the slow history of 32 draws weighs its samples: (31 / 32)^8 ≈ 0.78 of the
        // deviation. A deviation over the fast history's 4 draws would keep (3 / 4)^8 ≈ 0.1 of it.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            for (int frame = 0; frame < 40; frame++)
            {
                sample.Samples = Blink(frame);
                Continue(renderer, scene, frames: 1);
            }
            float before = RetainedDeviationOf(renderer, sample)[Center];
            before.Should().BeGreaterThan(0.5f, "the samples deviate by 1");
            sample.Samples = null;
            sample.Value = new(x: 1, y: 1, z: 1, w: 1);

            Continue(renderer, scene, frames: 16);

            RetainedDeviationOf(renderer, sample)[Center]
                .Should()
                .BeGreaterThanOrEqualTo(0.7f * before, "the deviation keeps the slow history's window");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheRetainedDeviationTakesTheExposureRatio()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            exposure: AutoExposure.Default
        );
        try
        {
            // Samples of 0.025 and 0.075 in turn, pre-exposed at the exposure that the next frame uses.
            void ExposeAndContinue(float light)
            {
                float scale = MathF.Pow(2, renderer.ReadExposureStops().Stops);
                sample.Value = new(x: light * scale, y: light * scale, z: light * scale, w: 1f);
                Continue(renderer, scene, frames: 1);
            }

            sample.Value = new(x: 0.05f, y: 0.05f, z: 0.05f, w: 1f);
            renderer.Render(scene, 1f);
            for (int frame = 0; frame < 20; frame++)
                ExposeAndContinue((frame & 1) == 0 ? 0.025f : 0.075f);
            // A bright frame lowers the exposure of the next frame, as in
            // WhenTheExposureMovesBetweenTwoFramesTheHistoryTakesTheNewExposureInThatFrame.
            float before = renderer.ReadExposureStops().Stops;
            Gpu.Star(snapshot: scene, irradiance: 64 * Gpu.StarIrradiance(scene));
            ExposeAndContinue(3.2f);
            float ratio = MathF.Pow(2, renderer.ReadExposureStops().Stops - before);
            ratio.Should().BeLessThan(0.98f, "the bright frame moved the exposure");
            float deviation = RetainedDeviationOf(renderer, sample)[Center];
            deviation.Should().BePositive("the samples spread");

            // No sample, so the deviation holds only its rescaled self.
            sample.Value = new(x: float.NaN, y: float.NaN, z: float.NaN, w: 1f);
            Continue(renderer, scene, frames: 1);

            RetainedDeviationOf(renderer, sample)[Center]
                .Should()
                .BeApproximately(deviation * ratio, 2e-3f * deviation, "the deviation has the history's units");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARejectedHistoryStartsItsRetainedDeviationAtZero()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            for (int frame = 0; frame < 20; frame++)
            {
                sample.Samples = Blink(frame);
                Continue(renderer, scene, frames: 1);
            }
            RetainedDeviationOf(renderer, sample)[Center].Should().BePositive("the samples spread");

            // A camera cut rejects every history, so each pixel's first sample is all that it holds.
            scene.CameraCuts++;
            sample.Samples = Blink(20);
            Continue(renderer, scene, frames: 1);

            RetainedDeviationOf(renderer, sample).Should().OnlyContain(deviation => deviation == 0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AConstantSampleKeepsARetainedDeviationOfExactlyZero()
    {
        // 0.3 is no half float: the history stops a half-float step or so from it, and the step left counts as the store's
        // rounding, not as noise.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            sample.Value = new(x: 0.3f, y: 0.3f, z: 0.3f, w: 1);
            renderer.Render(scene, 1f);

            Continue(renderer, scene, frames: 60);

            RetainedDeviationOf(renderer, sample).Should().OnlyContain(deviation => deviation == 0);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The history length of part 0 at each pixel after the last frame, row by row from the top: 0 for the sky.</summary>
    private static float[] LengthsOf(VulkanRenderer renderer, SamplePass sample)
    {
        float[] history = HistoryOf(renderer, sample, part: 0);
        float[] lengths = new float[Width * Height];
        for (int pixel = 0; pixel < lengths.Length; pixel++)
            lengths[pixel] = history[4 * pixel + 3];
        return lengths;
    }

    /// <summary>
    /// A sample of the mean <paramref name="light"/> whose coefficient of variation is <paramref name="variation"/>: the
    /// same at the four pixels of each ray block, the 2 × 2 pixels at even coordinates that share one ray, and
    /// independent between ray blocks and frames. The noise is uniform, √3 · <paramref name="variation"/> each side.
    /// </summary>
    private static Vector4 NoisySample(float light, float variation, int column, int row, int frame)
    {
        float uniform = Hashed(column / 2, row / 2, frame) / (float)uint.MaxValue * 2 - 1;
        return new Vector4(new Vector3(light * (1 + MathF.Sqrt(3) * variation * uniform)), 1);
    }

    /// <summary>The fast cap of the bounce light: the length to which light evidence cuts a history.</summary>
    private const float FastCap = 4;

    /// <summary>
    /// The block of 8 × 8 pixels, one workgroup of the spatial dispatch, that holds the center pixel: columns 32 to 39 and
    /// rows 16 to 23.
    /// </summary>
    private static bool InCenterBlock(int pixel) =>
        pixel % Width / 8 == Width / 2 / 8 && pixel / Width / 8 == Height / 2 / 8;

    /// <summary>A pixel of the center block other than the center itself, whose sample brings the block its evidence.</summary>
    private static bool IsWitness(int column, int row) => column == Width / 2 + 6 && row == Height / 2 + 4;

    /// <summary>The fast history of part 0 that the last frame wrote, RGBA per pixel.</summary>
    private static float[] FastOf(VulkanRenderer renderer, SamplePass sample) =>
        renderer.ReadImage(sample.Signal.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1));

    /// <summary>
    /// Whether the clamp moved the slow history of <paramref name="pixel"/> to its fast history in the last frame: the box
    /// of samples that blink together has no spatial spread, so the narrow box is the fast history itself, while the
    /// widened box holds the slow history about 0.1 away from it.
    /// </summary>
    private static bool BoxNarrowedAt(VulkanRenderer renderer, SamplePass sample, int pixel) =>
        MathF.Abs(HistoryOf(renderer, sample, part: 0)[4 * pixel] - FastOf(renderer, sample)[4 * pixel]) <= 1e-3f;

    /// <summary>
    /// Draws 40 frames whose samples blink between 0 and 2 (<see cref="Blink"/>), so that every history holds a retained
    /// deviation of about 1 and its cap of 16, and then one more frame in which the samples' ages are
    /// <paramref name="codes"/>.
    /// </summary>
    private static void BlinkThenDrawWithAges(
        VulkanRenderer renderer,
        SamplePass sample,
        RenderSnapshot scene,
        Func<int, int, ushort> codes
    )
    {
        for (int frame = 0; frame < 40; frame++)
        {
            sample.Samples = Blink(frame);
            Continue(renderer, scene, frames: 1);
        }
        CenterOf(renderer, sample, part: 0).Length.Should().Be(16, "the history holds its cap before the evidence");
        BoxNarrowedAt(renderer, sample, Center).Should().BeFalse("the widened box holds the blinking samples");
        sample.Samples = Blink(40);
        sample.AgeCodes = codes;
        Continue(renderer, scene, frames: 1);
    }

    [Fact]
    public void ARecentLightCodeInABlockNarrowsTheBoxAndCutsEveryContributingHistoryOfItsBlockToTheFastCap()
    {
        // One pixel of the center block brings the light code 5, a light that changed 4 draws ago: light evidence for
        // the whole block (docs/plans/bounce-light-confidence.md, section 8.2).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            BlinkThenDrawWithAges(
                renderer: renderer,
                sample: sample,
                scene: scene,
                codes: (column, row) => (ushort)(IsWitness(column, row) ? 5 : 0)
            );

            float[] lengths = LengthsOf(renderer, sample);
            Enumerable
                .Range(0, lengths.Length)
                .Where(InCenterBlock)
                .Select(pixel => lengths[pixel])
                .Should()
                .OnlyContain(length => length == FastCap, "light evidence cuts every history of its block");
            BoxNarrowedAt(renderer, sample, Center).Should().BeTrue("light evidence drops the widening of the box");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(100)]
    public void ALightAgeOfSevenOrOlderGivesTheBlockNoEvidence(int code)
    {
        // A light age of 7 or more, the codes from 8, began at least 11 draws ago, and the fast history already holds it
        // (section 8.3). The witness's own history is cut to the age, its neighbors' are not.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            BlinkThenDrawWithAges(
                renderer: renderer,
                sample: sample,
                scene: scene,
                codes: (column, row) => (ushort)(IsWitness(column, row) ? code : 0)
            );

            float[] lengths = LengthsOf(renderer, sample);
            Enumerable
                .Range(0, lengths.Length)
                .Where(pixel => InCenterBlock(pixel) && !IsWitness(pixel % Width, pixel / Width))
                .Select(pixel => lengths[pixel])
                .Should()
                .OnlyContain(length => length == 16, "an old light change is no evidence");
            BoxNarrowedAt(renderer, sample, Center).Should().BeFalse("without evidence the box stays wide");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASampleThatSawMotionNarrowsTheBoxOfItsBlockAndKeepsItsLength()
    {
        // Motion has no known size, so its evidence narrows the box and leaves the cut to the anti-lag: the clamp moves the
        // slow history about 0.1 to the fast history, a lag of about a tenth, which shortens the history of 16 by about a
        // fifth (section 8.2).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            BlinkThenDrawWithAges(
                renderer: renderer,
                sample: sample,
                scene: scene,
                codes: (column, row) => IsWitness(column, row) ? TemporalSignal.SampleMotionBit : (ushort)0
            );

            BoxNarrowedAt(renderer, sample, Center).Should().BeTrue("motion evidence drops the widening of the box");
            CenterOf(renderer, sample, part: 0)
                .Length.Should()
                .BeGreaterThan(FastCap, "motion evidence does not cut")
                .And.BeLessThan(16, "the anti-lag shortens the history by the clamp's move");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EvidenceStaysInsideItsBlock()
    {
        // The block left of the center block holds no sample with evidence, so it keeps the widened box and its lengths.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            BlinkThenDrawWithAges(
                renderer: renderer,
                sample: sample,
                scene: scene,
                codes: (column, row) => (ushort)(IsWitness(column, row) ? 5 : 0)
            );

            float[] lengths = LengthsOf(renderer, sample);
            int[] left = [.. Enumerable.Range(0, lengths.Length).Where(pixel => InCenterBlock(pixel + 8))];
            left.Select(pixel => lengths[pixel]).Should().OnlyContain(length => length == 16);
            left.Should().OnlyContain(pixel => !BoxNarrowedAt(renderer, sample, pixel), "its box stays wide");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void BlocksWithSkyOrOffImagePixelsStillActOnTheirEvidence()
    {
        // The wall's right edge lies inside a block column, so the blocks there hold sky pixels, and the image's 36 rows
        // end halfway through the last block row, so those blocks hold pixels outside the image. Each invocation still
        // reaches the workgroup's barriers. The top left pixel of each block lies on the wall and brings the light code 3.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            wall: (new Vector3(-22, 0, -20), new Vector3(60, 40, 1))
        );
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            float[] before = LengthsOf(renderer, sample);
            int[] lit = [.. Enumerable.Range(0, before.Length).Where(pixel => before[pixel] > 0)];
            lit.Should()
                .Contain(pixel => pixel % Width % 8 != 7 && before[pixel + 1] == 0, "a block holds the wall's edge");
            lit.Should().Contain(pixel => pixel / Width == Height - 1, "the last block row holds lit pixels");
            sample.AgeCodes = (column, row) => (ushort)(column % 8 == 0 && row % 8 == 0 ? 3 : 0);

            Continue(renderer, scene, frames: 1);

            float[] lengths = LengthsOf(renderer, sample);
            lit.Where(pixel => pixel % Width % 8 != 0 || pixel / Width % 8 != 0)
                .Select(pixel => lengths[pixel])
                .Should()
                .OnlyContain(length => length == FastCap, "every block acts on its evidence");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AHalfDimmingWhoseRaysSeeItsLightAgeSettlesWithinFivePercentInSixtyDraws()
    {
        // A region of 32 × 32 pixels, four by four blocks, whose ray blocks' samples spread with a coefficient of variation
        // of 0.3, dims from 1 to 0.5. In each block one ray block's rays see the light's age as the tracer gives it: no
        // code for the 4 draws that the record waits, then the ages 0, 1 and on. The widened box alone lets the slow blend
        // drain the step: (31 / 32)^60 ≈ 0.15 of it is left after 60 draws. The block's evidence cuts every history of the
        // block for 7 draws, after which the step drains at the pace of a short history (section 8.3).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            static bool InRegion(int column, int row) => column is >= 16 and < 48 && row < 32;
            float dimmed = 1;
            int frame = 0;
            int sinceDimming = -1;
            sample.Samples = (column, row) =>
                NoisySample(
                    light: InRegion(column, row) ? dimmed : 1,
                    variation: 0.3f,
                    column: column,
                    row: row,
                    frame: frame
                );
            sample.AgeCodes = (column, row) =>
                (ushort)(
                    InRegion(column, row) && column % 8 / 2 == 1 && row % 8 / 2 == 1 && sinceDimming >= 4
                        ? sinceDimming - 3
                        : 0
                );
            renderer.Render(scene, 1f);
            for (; frame < 64; frame++)
                Continue(renderer, scene, frames: 1);
            dimmed = 0.5f;

            for (sinceDimming = 0; sinceDimming < 60; sinceDimming++, frame++)
                Continue(renderer, scene, frames: 1);

            float[] history = HistoryOf(renderer, sample, part: 0);
            Enumerable
                .Range(0, Width * Height)
                .Where(pixel => InRegion(pixel % Width, pixel / Width))
                .Average(pixel => history[4 * pixel])
                .Should()
                .BeLessThanOrEqualTo(
                    0.5f * 1.05f,
                    "the blocks' evidence restarts their histories from the fast window"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightCodeWithTheMotionBitStillGivesItsBlockLightEvidence()
    {
        // The light code 3 with the motion bit: the stronger level wins, so the block's history is cut to the fast cap
        // after the blend, as without the bit (docs/plans/bounce-light-confidence.md, section 8.4).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);

            sample.AgeCode = TemporalSignal.SampleMotionBit | 3u;
            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0).Length.Should().Be(FastCap, "light evidence cuts after the blend");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ABrightSampleWithAYoungLightCodeTakesTheNormalWeightInItsDraw()
    {
        // A still pixel's sample is 25 times its history's light and brings the light code 1, as a ray that hits a sun
        // strip whose record the jitter keeps young. The blend gives the sample its normal weight, 1 / 16 at the cap of
        // 16, and only after the blend does the block's light evidence cut the length to the fast cap. A cut before the
        // blend would give exactly this bright sample the weight 1, so the weight would depend on the sample's own value
        // and bias the history up (docs/plans/bounce-light-confidence.md, section 4). The box is wide enough that the
        // clamp moves nothing.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: Parameters with
            {
                ClampSigmas = 1000,
            }
        );
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            (Vector3 before, float length) = CenterOf(renderer, sample, part: 0);
            length.Should().Be(16, "the history fills to its cap");
            sample.Samples = (column, row) =>
                column == Width / 2 && row == Height / 2 ? new(x: 25f, y: 25f, z: 25f, w: 1f) : sample.Value;
            sample.AgeCodes = (column, row) => (ushort)(column == Width / 2 && row == Height / 2 ? 1 : 0);

            Continue(renderer, scene, frames: 1);

            (Vector3 after, float cut) = CenterOf(renderer, sample, part: 0);
            after.X.Should().BeApproximately(before.X * (1 + 24f / 16), 1e-3f * after.X, "the sample weighs 1 / 16");
            cut.Should().Be(FastCap, "the block's light evidence cuts the length after the blend");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AMovingWallNarrowsTheBoxOfItsBlocksWhileItMoves()
    {
        // The camera moves by about a third of a pixel per frame, so the resolve sets every sample's motion bit, and the
        // wall dims while it moves. No ray saw a change of light. The step's own spread widens the box, which alone would
        // hold the old light for about 36 frames. Motion evidence narrows it, so the clamp follows the fast history, as
        // UnderTheNarrowBoxTheHistoryReachesNinetyPercentOfADimmingStepWithinTenFrames does (section 8.2).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            sample.AgeCode = TemporalSignal.SampleMotionBit;

            FramesUntilTheCenterFollowsADimming(
                    renderer: renderer,
                    sample: sample,
                    scene: scene,
                    beforeEachFrame: () =>
                    {
                        scene.Camera.PreviousPosition = scene.Camera.Position;
                        scene.Camera.Position += new Vector3(0.1f, 0, 0);
                    }
                )
                .Should()
                .BeInRange(2, 10, "the narrow box follows the fast history while the wall moves");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AfterTheMotionStopsTheNarrowBoxsLossReturnsWithinFivePercentInSixtyDraws()
    {
        // Rare bright samples on a wall, whose mean is 2. While the wall moves for 40 draws, motion evidence narrows the box,
        // which cuts the rare samples and loses light. After the wall stops, the widened box holds them again, and the loss
        // drains at the slow blend: a loss of 23 % lies within 5 % after 47 draws (section 16).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            DrawRareBrightSamples(renderer: renderer, sample: sample, scene: scene, frames: 160);
            int frame = 160;
            sample.Samples = (column, row) => new Vector4(new Vector3(RareAt(column, row, frame) ? RareLight : 1f), 8f);
            sample.AgeCode = TemporalSignal.SampleMotionBit;
            for (int moved = 0; moved < 40; moved++, frame++)
            {
                scene.Camera.PreviousPosition = scene.Camera.Position;
                scene.Camera.Position += new Vector3(0.1f, 0, 0);
                Continue(renderer, scene, frames: 1);
            }
            InteriorLightOf(renderer, sample)
                .Average()
                .Should()
                .BeLessThan(1.9f, "the narrow box cut rare samples while the wall moved");
            sample.AgeCode = 0;
            scene.Camera.PreviousPosition = scene.Camera.Position;

            for (int still = 0; still < 60; still++, frame++)
                Continue(renderer, scene, frames: 1);

            InteriorLightOf(renderer, sample)
                .Average()
                .Should()
                .BeGreaterThanOrEqualTo(2f * 0.95f, "the widened box keeps the rare samples again");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WhenTheExposureMovesBetweenTwoFramesTheHistoryTakesTheNewExposureInThatFrame()
    {
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            exposure: AutoExposure.Default
        );
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

            (Vector3 Light, float Length) limited = FrameWithPrototype(true, light, samples);
            (Vector3 Light, float Length) control = FrameWithPrototype(false, light, samples);

            // The diffuse prototype's spatial pass stores the weighted mean of equal neighbors, which 32-bit float can
            // leave one step below them, into the half-float history. Vulkan lets the driver round that conversion
            // toward zero, as NVIDIA's does, so a channel may lie one half-float step low (ticket t8-v31a). A limit of
            // the region would scale it by far more.
            limited.Length.Should().Be(control.Length);
            limited.Light.X.Should().BeApproximately(control.Light.X, HalfStep(control.Light.X));
            limited.Light.Y.Should().BeApproximately(control.Light.Y, HalfStep(control.Light.Y));
            limited.Light.Z.Should().BeApproximately(control.Light.Z, HalfStep(control.Light.Z));
        }

        /// <summary>The spacing of the half floats at <paramref name="value"/>, a normal half float above 0.</summary>
        private static float HalfStep(float value) => MathF.Pow(2, MathF.Floor(MathF.Log2(value)) - 10);

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
                // The narrow box, whose rules fit either light: the reflections keep no deviation in the fast alpha.
                (VulkanRenderer created, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: NarrowParameters with
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

    [Fact]
    public void TheReflectionsRefuseAWidenedBox()
    {
        // The reflections' fast alpha holds the hit distance, so a widened box would widen by it
        // (docs/plans/bounce-light-confidence.md, section 13).
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            SignalParameters parameters = SignalParameters.Specular(0) with
            {
                Confidence = new WidenedBox(RetainedSigmas: 2),
            };

            Action create = () => renderer.CreateSignal(parameters: parameters, name: "test", parts: 1);

            create.Should().Throw<ArgumentException>();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The evidence under each specular history (docs/plans/bounce-light-confidence.md, sections 8 and 10): the reflections
    /// beside a precise history act on the light codes of their rays, and a history without a light age keeps the narrow
    /// box, which no evidence could narrow again.
    /// </summary>
    [Collection(ProcessEnvironmentCollection.Name)]
    public sealed class EvidenceUnderEachHistory
    {
        /// <summary>The roughness of a rough reflection, whose precise history has no cap.</summary>
        private const float Rough = 0.8f;

        [Fact]
        public void TheOriginalSpecularHistoryKeepsTheNarrowBoxWithoutEvidence()
        {
            // The original history keeps H13's shaders and storage, which hold no deviation and no light age.
            WithHistory(
                mode: "original",
                parameters: SignalParameters.Specular(0),
                (_, sample, _) =>
                {
                    (sample.Signal.Parameters.Confidence is NarrowBox)
                        .Should()
                        .BeTrue("the original history has no deviation");
                    sample.Signal.SampleAge(0).Should().BeNull("the original history has no light age");
                }
            );
        }

        [Fact]
        public void WithoutALightAgeTheBounceLightKeepsTheNarrowBox()
        {
            // Beside the original specular history, the samples have no light age, so no evidence could narrow a widened
            // box again. The samples blink between 0 and 2 at every pixel, so the narrow box is the fast history itself,
            // and the clamp moves the slow history there.
            WithHistory(
                mode: "original",
                parameters: Parameters,
                (renderer, sample, scene) =>
                {
                    for (int frame = 0; frame < 40; frame++)
                    {
                        sample.Samples = Blink(frame);
                        Continue(renderer, scene, frames: 1);
                    }

                    BoxNarrowedAt(renderer, sample, Center).Should().BeTrue("the box holds no retained deviation");
                }
            );
        }

        [Theory]
        [InlineData(null)]
        [InlineData("precise-capped")]
        public void AReflectionWithARecentLightCodeIsCutToTheFastCap(string? mode) =>
            WithHistory(
                mode,
                parameters: SignalParameters.Specular(0),
                (renderer, sample, scene) =>
                {
                    // The reflected light doubles, and the top left pixel of each block brings the light code 3. The rise
                    // and a drop are alike: the rays saw the change, so every history of the block restarts from the fast
                    // window (section 10). The narrow box and the anti-lag alone would keep a fifth of a long history.
                    sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
                    renderer.Render(scene, 1f);
                    Continue(renderer, scene, frames: 100);
                    static bool Witness(int pixel) => pixel % Width % 8 == 0 && pixel / Width % 8 == 0;
                    sample.Value = new(x: 2f, y: 2f, z: 2f, w: 1f);
                    sample.AgeCodes = (column, row) => (ushort)(Witness(row * Width + column) ? 3 : 0);

                    Continue(renderer, scene, frames: 1);

                    float[] lengths = LengthsOf(renderer, sample);
                    Enumerable
                        .Range(0, lengths.Length)
                        .Where(pixel => !Witness(pixel))
                        .Select(pixel => lengths[pixel])
                        .Should()
                        .OnlyContain(length => length == FastCap, "light evidence cuts every reflection of its block");
                }
            );

        /// <summary>
        /// Runs <paramref name="action"/> on a rough surface's signal of <paramref name="parameters"/> beside the specular
        /// history <paramref name="mode"/>, null for the default.
        /// </summary>
        private static void WithHistory(
            string? mode,
            SignalParameters parameters,
            Action<VulkanRenderer, SamplePass, RenderSnapshot> action
        )
        {
            string? previous = Environment.GetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY");
            VulkanRenderer? renderer = null;
            try
            {
                Environment.SetEnvironmentVariable("TERTIUS_SPECULAR_HISTORY", mode);
                (VulkanRenderer created, SamplePass sample, RenderSnapshot scene) = Accumulating(
                    parameters: parameters,
                    roughness: Rough
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
