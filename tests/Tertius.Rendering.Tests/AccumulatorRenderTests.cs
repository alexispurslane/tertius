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
    /// <paramref name="exposure"/>, the fixed exposure of the tests without it, at <paramref name="width"/> × Height pixels.
    /// The wall fills the view, unless <paramref name="wall"/> gives its center and size: then the sky shows around it.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) Accumulating(
        ExposureSetting exposure = default,
        SignalParameters? parameters = null,
        float roughness = 0.5f,
        (Vector3 Center, Vector3 Size)? wall = null,
        int width = Width
    ) =>
        AccumulatingOn(
            renderer: Gpu.Headless(width: width, height: Height, exposure: exposure),
            parameters: parameters,
            roughness: roughness,
            wall: wall
        );

    /// <summary>
    /// <see cref="Accumulating"/> on <paramref name="renderer"/>: the scene of the wall, and the test signal with its
    /// producer and accumulation after the direct pass.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) AccumulatingOn(
        VulkanRenderer renderer,
        SignalParameters? parameters = null,
        float roughness = 0.5f,
        (Vector3 Center, Vector3 Size)? wall = null
    )
    {
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
        // 0.375 of the anti-lag's 0.5, so the length falls from 16 to 16 · 0.625 = 10. The histories store the half floats
        // nearest 0.95 and 0.8, 0.9502 and 0.7998 (half_store.glsl). So the lag is 0.1880, and the length 9.983, which the
        // store holds as the nearest half float, 9.984375. No store differed on the card. The box's σ did: its plain
        // moments of the 9 equal fast histories cancelled to about 1e-4 there, so the clamp stopped two σ above the
        // mean, and the length read one half-float step higher, 9.9921875. The moments around the pixel's own fast
        // history give a σ of exactly 0 on every driver (ticket tc-8142).
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
            length.Should().Be(9.984375f, "the anti-lag shortens the history by the clamp's move");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightChangeThatTheRaysSawRestartsTheHistoryAtFullRateFrames()
    {
        // The rays saw the light change in this frame (age 0, code 1). The blend gives the doubled sample its normal
        // weight of 1/16, and the box stays wide and holds the step. Then the light evidence cuts the length to
        // FullRateFrames, so each later draw of the evidence blends its sample with the weight 1/9. After the seven draws
        // of the evidence, the history keeps 15/16 × (8/9)^6 = 0.462 of the old light
        // (docs/plans/bounce-light-confidence.md, sections 4 and 8.3).
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
                light.X.Should().BeApproximately(1.0625f, 1e-3f, "the box holds the step in part {0}", part);
                length.Should().Be(FullRateFrames, "the light evidence cuts part {0}", part);
            }

            // The codes 1 to 7 are of a recent light change (sample_age.glsl, RecentLightAge).
            for (uint code = 2; code <= 7; code++)
            {
                sample.AgeCode = code;
                Continue(renderer, scene, frames: 1);
            }

            for (int part = 0; part < 2; part++)
            {
                (Vector3 light, float length) = CenterOf(renderer, sample, part);
                light.X.Should().BeApproximately(1.5376f, 2e-3f, "part {0} follows at the pace of 9 draws", part);
                length.Should().Be(FullRateFrames, "the light evidence still cuts part {0}", part);
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

    [Fact]
    public void APixelWhoseSurfaceLiesOnlyUnderTheFourthTexelOfItsLastPlaceKeepsThatTexelsHistory()
    {
        // A box 10 m ahead, before the wall, has its top left corner at the center of the view, where four pixels meet.
        // For 8 draws it stands 0.9 pixels right of and below that, then it moves there. The point of the pixel right of
        // and below the corner had its last place 0.9 pixels right of and below the pixel's center. Of the 2 × 2 texels
        // around that place, the first three showed the wall 10 m behind, and only the fourth, at (+1, +1), showed the
        // box. That texel holds the weight 0.9 × 0.9 and a history of 8 draws. The draws do not jitter without FSR, so
        // each texel showed what lies at its center, 0.4 pixels from the box's last edge.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId paint = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            float pixelSize = 2 * 10 * MathF.Tan(Gpu.Fov / 2) / Height;
            RenderInstance BoxShiftedBy(float pixels) =>
                Gpu.Instance(
                    mesh: cube,
                    material: paint,
                    position: new Vector3(3, -3, -10.5f) + pixels * pixelSize * new Vector3(1, -1, 0),
                    scale: new(6, 6, 1),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                );
            scene.Instances.Add(BoxShiftedBy(0.9f));
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 7);
            scene.Instances[1] = BoxShiftedBy(0);

            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0).Length.Should().Be(9f, "the fourth texel's history of 8 draws goes on");
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

    /// <summary>
    /// The light of the sample that a still history takes in <see cref="ALongHistoryHoldsTheMeanOfItsSamples"/>: each pixel
    /// takes, in each draw, another value of one fixed set. The set spreads evenly from 0.2 to 0.6. Pixel p in draw d takes
    /// value (p + 355 d) mod n of the n = <see cref="Width"/> · <see cref="Height"/> values, so each draw holds the whole
    /// set once, and each pixel walks the set in steps of about 0.62 of its width. A walk, and not a repeating pair:
    /// the blend of a repeating pair settles in a cycle whose store roundings do not change from draw to draw, and the
    /// first such cycle that a history meets from below lies up to 0.8 % below the pair's mean even under a store that
    /// rounds to the nearest half float.
    /// </summary>
    private static float WalkedLight(int pixel, int frame) => WalkedValue((pixel + 355 * frame) % (Width * Height));

    /// <summary>Value <paramref name="index"/> of the set of <see cref="WalkedLight"/>.</summary>
    private static float WalkedValue(int index) => 0.2f + 0.4f * index / (Width * Height - 1);

    /// <summary>The mean of the set of <see cref="WalkedLight"/> as the sample image holds it, in half floats.</summary>
    private static double WalkedMean() =>
        Enumerable.Range(0, Width * Height).Average(index => (double)(float)(Half)WalkedValue(index));

    /// <summary>Draws <paramref name="frames"/> frames in which each pixel takes its <see cref="WalkedLight"/>.</summary>
    private static void DrawWalkedSamples(VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            int drawn = frame;
            sample.Samples = (column, row) => new Vector4(new Vector3(WalkedLight(row * Width + column, drawn)), 1);
            Continue(renderer, scene, frames: 1);
        }
    }

    [Fact]
    public void ALongHistoryHoldsTheMeanOfItsSamples()
    {
        // Every pixel's history has the same length, so the mean over the pixels of the histories is the blend of the
        // draws' means over the pixels, and each draw's mean is the set's mean. A store that rounds every draw's blend the
        // same way would lose the length, 32, times that rounding: about 1 % under a rounding toward zero.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            DrawWalkedSamples(renderer: renderer, sample: sample, scene: scene, frames: 320);

            float[] history = HistoryOf(renderer, sample, part: 0);
            double mean = Enumerable.Range(0, Width * Height).Average(pixel => (double)history[4 * pixel]);
            (mean / WalkedMean())
                .Should()
                .BeApproximately(1, 0.002, "a long history holds the mean of its samples, {0} of the set's", mean);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFastHistoryHoldsTheMeanOfItsSamples()
    {
        // The fast history is a mean of the weight 1 / 4 that feeds back too: the rays read it at their hits with the
        // filter off. Every pixel's fast history has the same length, so the mean over the pixels is the set's mean. A
        // store that rounds every blend toward zero would lose 4 times that rounding: 0.08 % on a CPU model of this walk,
        // against 0.015 % under the nearest half float.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            DrawWalkedSamples(renderer: renderer, sample: sample, scene: scene, frames: 320);

            float[] fast = FastOf(renderer, sample);
            double mean = Enumerable.Range(0, Width * Height).Average(pixel => (double)fast[4 * pixel]);
            (mean / WalkedMean())
                .Should()
                .BeApproximately(1, 0.0004, "the fast history holds the mean of its samples, {0} of the set's", mean);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALongHistoryRetainsTheDeviationOfItsSamples()
    {
        // The retained deviation is a mean that feeds back too. The reference is its update in double precision over the
        // same samples: the step from the fast history, whose weight is 1 / min(L, 4), and the variance's weight
        // 1 / min(L, 32). A store that rounds every update toward zero would keep it about 1 % low.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            const int Frames = 320;
            DrawWalkedSamples(renderer: renderer, sample: sample, scene: scene, frames: Frames);

            double exact = Enumerable
                .Range(0, Width * Height)
                .Average(pixel => ExactDeviation(WalkedLight, pixel, Frames));
            double retained = RetainedDeviationOf(renderer, sample).Average(deviation => (double)deviation);
            (retained / exact)
                .Should()
                .BeApproximately(1, 0.005, "the deviation of {0} follows the samples' {1}", retained, exact);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0.01f)]
    [InlineData(0.02f)]
    public void ALongHistoryRetainsTheDeviationOfSamplesThatSpreadByAFewPercentOfTheirLight(float variation)
    {
        // The rounding test of the retained deviation zeroes a step that is only the stall of the fast history's blend.
        // That blend has the weight 1 / min(L, 4), so the test weighs the step with it. Weighed with the variance's weight
        // 1 / min(L, 32), at L = 32 it zeroed real steps up to 32 / 1536, about 2.1 % of the light (ticket lin-4ffc): a CPU
        // model of this test gives 0.28 of the exact deviation at a spread of 1 % and 0.90 at 2 %, against 0.999 and 1.000
        // for the fast history's weight.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            const int Frames = 320;
            float SpreadLight(int pixel, int frame) => SpreadSampleLight(variation, pixel, frame);
            for (int frame = 0; frame < Frames; frame++)
            {
                int drawn = frame;
                sample.Samples = (column, row) => new Vector4(new Vector3(SpreadLight(row * Width + column, drawn)), 1);
                Continue(renderer, scene, frames: 1);
            }

            double exact = Enumerable
                .Range(0, Width * Height)
                .Average(pixel => ExactDeviation(SpreadLight, pixel, Frames));
            double retained = RetainedDeviationOf(renderer, sample).Average(deviation => (double)deviation);
            (retained / exact)
                .Should()
                .BeApproximately(1, 0.005, "the deviation of {0} follows the samples' {1}", retained, exact);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The sample of <paramref name="pixel"/> in <paramref name="frame"/> for
    /// <see cref="ALongHistoryRetainsTheDeviationOfSamplesThatSpreadByAFewPercentOfTheirLight"/>: a light of 0.5 whose
    /// coefficient of variation is <paramref name="variation"/>, with uniform noise of √3 · <paramref name="variation"/>
    /// each side, independent between pixels and frames.
    /// </summary>
    private static float SpreadSampleLight(float variation, int pixel, int frame)
    {
        float uniform = Hashed(pixel % Width, pixel / Width, frame) / (float)uint.MaxValue * 2 - 1;
        return 0.5f * (1 + MathF.Sqrt(3) * variation * uniform);
    }

    /// <summary>
    /// The retained deviation of <paramref name="pixel"/> after <paramref name="frames"/> draws of the sample light
    /// <paramref name="lightAt"/> (of a pixel and a frame), by the update of accumulate_temporal.comp in double precision,
    /// with no store's rounding and no rounding test.
    /// </summary>
    private static double ExactDeviation(Func<int, int, float> lightAt, int pixel, int frames)
    {
        double fast = 0;
        double deviation = 0;
        for (int frame = 0; frame < frames; frame++)
        {
            double light = (float)(Half)lightAt(pixel, frame);
            int length = Math.Min(frame + 1, 32);
            double weight = 1.0 / length;
            double step = light - fast;
            deviation = Math.Sqrt((1 - weight) * (deviation * deviation + weight * step * step));
            fast += (light - fast) / Math.Min(length, FastCap);
        }

        return deviation;
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
        // wall, so that a store into a half float, the nearest one (half_store.glsl), moves at most 2^−11 of the excess,
        // not of a bright wall's light: the temporal and the spatial dispatch each store the history once a draw.
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

    [Fact]
    public void ASettledLightThatMovesByFourTenthsOfAPercentCountsAsSpread()
    {
        // 0.25 is a half float, so the fast history settles on it exactly, with no deviation. A move to 0.251, the half
        // float 0.250977, gives a step of 0.39 % of the light, and at the fast history's weight 1 / 4 a weighted step of
        // 0.098 %: above BlendRounding, 1/1536 or 0.065 %, which bounds the rounding of a store to the nearest half float.
        // The older rule for a store that rounds toward zero, 1/768 or 0.13 %, took it for the store's rounding, and so
        // did the test at the variance's weight 1 / 32 (ticket lin-4ffc).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            sample.Value = new(x: 0.25f, y: 0.25f, z: 0.25f, w: 1);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 60);
            RetainedDeviationOf(renderer, sample)[Center].Should().Be(0, "the light was constant");

            sample.Value = new(x: 0.251f, y: 0.251f, z: 0.251f, w: 1);
            Continue(renderer, scene, frames: 1);

            RetainedDeviationOf(renderer, sample)[Center].Should().BePositive("a move of 0.4 % is a change of light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASampleWithoutNoiseLeavesNoDeviationWhenItsFastHistoryStallsShortOfIt()
    {
        // From 0.2, the fast history's blend of weight 1 / 4 toward the half float 0.250488 stops at 0.25, two half-float
        // units short: a quarter of the last step is half a unit, a tie that rounds back to the even 0.25. So every later
        // draw has a step of 0.000488, 0.2 % of the light. It is the stall of the fast history's blend, which the rounding
        // test zeroes up to 4 / 1536, 0.26 %, at the fast weight. Counted as spread, it would hold the retained deviation
        // at its own size. A CPU model of these 560 draws gives 5.0e-6 for the fix, and 4.8e-4 for a test with no weight,
        // abs(step) <= BlendRounding * before, or with no test at all.
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
            parameters: SignalParameters.Diffuse
        );
        try
        {
            sample.Value = new(x: 0.2f, y: 0.2f, z: 0.2f, w: 1);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 59);
            sample.Value = new(x: 0.2505f, y: 0.2505f, z: 0.2505f, w: 1);
            Continue(renderer, scene, frames: 500);

            RetainedDeviationOf(renderer, sample)[Center]
                .Should()
                .BeLessThan(0.0001f * 0.2505f, "the light has not changed for 500 draws");
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

    /// <summary>The fast cap of the bounce light: the fast history's length, and the anti-lag's floor.</summary>
    private const float FastCap = 4;

    /// <summary>
    /// The length to which evidence of a change cuts a history: the shortest that keeps the block's shared ray
    /// (sample_age.glsl, FullRateFrames).
    /// </summary>
    private const float FullRateFrames = 8;

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
    public void ARecentLightCodeInABlockCutsEveryContributingHistoryOfItsBlockToFullRateFrames()
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
                .OnlyContain(length => length == FullRateFrames, "light evidence cuts every history of its block");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARecentLightCodeLeavesTheBoxOfItsBlockWide()
    {
        // Light evidence bounds the length and nothing else: the cut already bounds the lag that the history can hold,
        // and a box without its widening only cuts the history's own rare bright samples (section 8.2).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            BlinkThenDrawWithAges(
                renderer: renderer,
                sample: sample,
                scene: scene,
                codes: (column, row) => (ushort)(IsWitness(column, row) ? 5 : 0)
            );

            BoxNarrowedAt(renderer, sample, Center).Should().BeFalse("light evidence keeps the widening of the box");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightChangeThatLeavesTheSamplesAsTheyWereTakesNoLightFromTheHistoriesThatItCuts()
    {
        // One sample in 256 is 257 and the others are 1, so the histories hold a mean of 2 at their cap of 16, and most
        // pixels have no bright sample near them in the fast histories' few draws. Then one draw brings every sample the
        // light code 3 while the samples go on as before, as the sunlit share of a room's bounce light does when a lamp
        // switches. A box without its widening would cut about half of the histories' rare bright light in that draw: in
        // the orbit cabin the bounce light fell by 4 % in the 4 draws after the lamps came on (ticket t0-f8oc,
        // 2026-11-14).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            static Vector4 Sampled(int column, int row, int frame) =>
                new(new Vector3(Hashed(column, row, frame) % 256 == 0 ? 257f : 1f), 8f);
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 8f);
            renderer.Render(scene, 1f);
            for (int frame = 0; frame < 160; frame++)
            {
                int drawn = frame;
                sample.Samples = (column, row) => Sampled(column, row, drawn);
                Continue(renderer, scene, frames: 1);
            }
            float before = InteriorLightOf(renderer, sample).Average();
            before.Should().BeGreaterThan(1.5f, "the widened box holds the rare bright samples in a still");
            sample.Samples = (column, row) => Sampled(column, row, 160);
            sample.AgeCode = 3;

            Continue(renderer, scene, frames: 1);

            LengthsOf(renderer, sample)
                .Should()
                .OnlyContain(length => length == FullRateFrames, "every block has evidence");
            InteriorLightOf(renderer, sample)
                .Average()
                .Should()
                .BeApproximately(before, 0.1f, "light evidence bounds the length and takes no light");
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
                .OnlyContain(length => length == FullRateFrames, "every block acts on its evidence");
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
        // block to FullRateFrames for 7 draws, after which the step drains at the pace of a short history (section 8.3).
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
                .BeLessThanOrEqualTo(0.5f * 1.05f, "the blocks' evidence restarts their histories at FullRateFrames");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightCodeWithTheMotionBitStillGivesItsBlockLightEvidence()
    {
        // The light code 3 with the motion bit: the stronger level wins, so the block's history is cut to FullRateFrames
        // after the blend, as without the bit (docs/plans/bounce-light-confidence.md, section 8.4).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);

            sample.AgeCode = TemporalSignal.SampleMotionBit | 3u;
            Continue(renderer, scene, frames: 1);

            CenterOf(renderer, sample, part: 0)
                .Length.Should()
                .Be(FullRateFrames, "light evidence cuts after the blend");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightCodeWithTheMotionBitKeepsTheWidenedBoxOfLightEvidence()
    {
        // Light evidence stands above motion, so a block whose samples bring both keeps the widened box, as light
        // evidence alone does, and its length falls to FullRateFrames. No take has measured such a block under a moving
        // view since the cut moved to 8 draws (docs/plans/bounce-light-confidence.md, section 8.2, ticket tc-tc8c).
        (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating();
        try
        {
            BlinkThenDrawWithAges(
                renderer: renderer,
                sample: sample,
                scene: scene,
                codes: (column, row) =>
                    IsWitness(column, row) ? (ushort)(TemporalSignal.SampleMotionBit | 5u) : (ushort)0
            );

            BoxNarrowedAt(renderer, sample, Center).Should().BeFalse("light evidence keeps the widening beside motion");
            CenterOf(renderer, sample, part: 0)
                .Length.Should()
                .Be(FullRateFrames, "light evidence caps the length beside motion");
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
        // 16, and only after the blend does the block's light evidence cut the length to FullRateFrames. A cut before the
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
            cut.Should().Be(FullRateFrames, "the block's light evidence cuts the length after the blend");
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

    // It sets TERTIUS_INDIRECT_LIGHT_PROTOTYPE, which every test beside it would read: the region's tests then ran on the
    // prototype's single history, which keeps no ring.
    [Collection(ProcessEnvironmentCollection.Name)]
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

        [Fact]
        public void ThePrototypesSingleBounceHistoryKeepsNoRegionRing() =>
            // The single history's spatial dispatch has no light-age variant, so it runs no region test
            // (TemporalSignal.TestsRegionDrift).
            WithPrototype(enabled: true, light: SignalLight.Diffuse, (_, sample, _) => sample.Signal.RegionRing(0))
                .Should()
                .BeNull("the prototype's spatial dispatch runs no region test");

        private static (Vector3 Light, float Length) FrameWithPrototype(
            bool enabled,
            SignalLight light,
            Func<int, int, Vector4> samples
        ) =>
            WithPrototype(
                enabled: enabled,
                light: light,
                (renderer, sample, scene) =>
                {
                    sample.Samples = samples;
                    renderer.Render(scene, 1f);
                    return CenterOf(renderer, sample, 0);
                }
            );

        /// <summary>
        /// Runs <paramref name="action"/> on a test signal of <paramref name="light"/> with the indirect light prototype
        /// <paramref name="enabled"/> or not.
        /// </summary>
        private static T WithPrototype<T>(
            bool enabled,
            SignalLight light,
            Func<VulkanRenderer, SamplePass, RenderSnapshot, T> action
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
                return action(renderer, sample, scene);
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

        [Fact]
        public void ALongGuideHoldsTheMeanOfItsHitDistances() =>
            WithHistory(
                (renderer, sample, scene) =>
                {
                    // Each pixel's hit distance walks a set from 3.6 to 4.4 m, as WalkedLight does, close enough that the
                    // reflected place's test keeps every history. Every guide has the length 4, so the mean of the
                    // guides' distances over the pixels is the set's mean. A store that rounds every blend toward zero
                    // would lose 4 times that rounding, about 0.13 % at 4 m.
                    for (int frame = 0; frame < 64; frame++)
                    {
                        int drawn = frame;
                        sample.Samples = (column, row) =>
                            new Vector4(x: 1, y: 1, z: 1, w: WalkedDistance(WalkedLight(row * Width + column, drawn)));
                        Continue(renderer, scene, frames: 1);
                    }

                    float[] guide = renderer.ReadImage(sample.Signal.ReflectionGuide(0));
                    double mean = Enumerable.Range(0, Width * Height).Average(pixel => (double)guide[4 * pixel]);
                    double distances = Enumerable
                        .Range(0, Width * Height)
                        .Average(index => (double)(float)(Half)WalkedDistance(WalkedValue(index)));
                    (mean / distances)
                        .Should()
                        .BeApproximately(1, 0.0005, "the guide holds the mean of its distances, {0} m", mean);
                }
            );

        /// <summary>The hit distance in m of the walk's value <paramref name="light"/>: 3.6 to 4.4 m.</summary>
        private static float WalkedDistance(float light) => 4 + 2 * (light - 0.4f);

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
    // It sets TERTIUS_SPECULAR_HISTORY, which every test beside it would read.
    [Collection(ProcessEnvironmentCollection.Name)]
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
        public void TheOriginalSpecularHistoryKeepsTheNarrowBoxWithoutEvidence() =>
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

        [Fact]
        public void BesideTheOriginalSpecularHistoryTheBounceLightKeepsNoRegionRing() =>
            // The region's drift is light evidence, and the original history's layouts hold no ring's binding. So the
            // bounce light, the one signal that runs the region's test, keeps no ring without a light age.
            WithHistory(
                mode: "original",
                parameters: Parameters,
                (_, sample, _) =>
                    sample.Signal.RegionRing(0).Should().BeNull("the original history has no light evidence")
            );

        [Fact]
        public void WithoutALightAgeTheBounceLightKeepsTheNarrowBox() =>
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

        [Theory]
        [InlineData(null)]
        [InlineData("precise-capped")]
        public void AReflectionWithARecentLightCodeIsCutToFullRateFrames(string? mode) =>
            WithHistory(
                mode,
                parameters: SignalParameters.Specular(0),
                (renderer, sample, scene) =>
                {
                    // The reflected light doubles, and the top left pixel of each block brings the light code 3. The rise
                    // and a drop are alike: the rays saw the change, so every history of the block restarts at
                    // FullRateFrames (section 10). The narrow box and the anti-lag alone would keep a fifth of a long
                    // history.
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
                        .OnlyContain(
                            length => length == FullRateFrames,
                            "light evidence cuts every reflection of its block"
                        );
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

    /// <summary>
    /// The region's drift (region_drift.glsl, docs/plans/bounce-light-confidence.md, section 8.7): an anchor block whose
    /// region's light moves gives every block of its region region evidence in the next draw, which cuts the block's bounce
    /// history to at most FullRateFrames. The scene of 64 × 36 pixels has one anchor, block 0, whose region holds every
    /// block. Its runs of hundreds of draws run beside the rest of the class (tests/STYLE.md, "Long tests").
    /// </summary>
    public sealed class RegionDrift
    {
        /// <summary>The draws of the region's two windows (region_drift.glsl, RegionRingDraws).</summary>
        private const int TwoWindows = TemporalSignal.RegionRingDraws;

        /// <summary>The coefficient of variation of the samples' noise in a region whose light moves.</summary>
        private const float Variation = 0.1f;

        /// <summary>The draws of the steady fall of <see cref="FallThenStand"/>, from 1 to 0.5.</summary>
        private const int FallDraws = 100;

        /// <summary>
        /// Draws one frame whose samples have the mean <paramref name="light"/> and the coefficient of variation
        /// <paramref name="variation"/>. Returns whether region evidence cut a history of part 0 in it: the evidence is a
        /// derived read inside the spatial dispatch, so its one effect shows it. Under region evidence, a history of at least
        /// FullRateFrames draws before the draw ends at exactly FullRateFrames where the anti-lag leaves it at least that
        /// long. So the detector misses a cut that the anti-lag takes further down. Light evidence ends a history there too.
        /// Without evidence the blend lengthens a history, and the anti-lag ends it at the fast cap or at a length that is
        /// not whole.
        /// </summary>
        private static bool DrawNoisy(
            VulkanRenderer renderer,
            SamplePass sample,
            RenderSnapshot scene,
            float light,
            int frame,
            float variation = Variation
        )
        {
            float[] before = LengthsOf(renderer, sample);
            sample.Samples = (column, row) =>
                NoisySample(light: light, variation: variation, column: column, row: row, frame: frame);
            Continue(renderer, scene, frames: 1);
            float[] after = LengthsOf(renderer, sample);
            return Enumerable
                .Range(0, after.Length)
                .Any(pixel => before[pixel] >= FullRateFrames && after[pixel] == FullRateFrames);
        }

        /// <summary>
        /// Draws 100 frames of noisy samples of mean 1, then <see cref="FallDraws"/> frames whose mean falls steadily to
        /// 0.5, then <paramref name="standDraws"/> frames at 0.5. Returns the frames after the fall in which any block had
        /// region evidence.
        /// </summary>
        private static List<int> FallThenStand(
            VulkanRenderer renderer,
            SamplePass sample,
            RenderSnapshot scene,
            int standDraws
        )
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            int frame = 0;
            for (; frame < 100; frame++)
                DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 1, frame: frame);
            for (int fallen = 1; fallen <= FallDraws; fallen++, frame++)
                DrawNoisy(
                    renderer: renderer,
                    sample: sample,
                    scene: scene,
                    light: 1 - 0.5f * fallen / FallDraws,
                    frame: frame
                );
            List<int> withEvidence = [];
            for (int stood = 0; stood < standDraws; stood++, frame++)
                if (DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 0.5f, frame: frame))
                    withEvidence.Add(stood);
            return withEvidence;
        }

        [Fact]
        public void AStillRegionOfNoisySamplesGivesRegionEvidenceInFewOfItsDraws()
        {
            // The test's tolerance is 3 standard deviations of the older window's own spread, so noise alone passes it
            // rarely: about 1 % of a clean still's block draws in the takes of t0-ion0.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
                renderer.Render(scene, 1f);
                int withEvidence = 0;
                for (int frame = 0; frame < 400; frame++)
                    if (
                        DrawNoisy(
                            renderer: renderer,
                            sample: sample,
                            scene: scene,
                            light: 1,
                            frame: frame,
                            variation: 0.3f
                        )
                    )
                        withEvidence++;

                withEvidence.Should().BeLessThanOrEqualTo(12, "noise alone passes the test in few of 400 draws");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void WhileTheRegionsLightFallsItsBounceHistoryTrailsTheSamplesByAtMostNineDrawsOfTheFall()
        {
            // The trail's bound. The fall from 1 to 0.5 in 100 draws takes 0.005 a draw. Region evidence holds the history
            // at FullRateFrames, so each draw blends its sample at the weight 1/9, and a mean of that weight trails a steady
            // fall by 8 draws: 0.04 of light, 8 % of the light at the end. Without the evidence the history of 32 draws
            // trails by about 30 draws, and the widened box holds the noisy samples' spread: 0.647 at the end
            // (docs/plans/bounce-light-confidence.md, section 8.7).
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                FallThenStand(renderer: renderer, sample: sample, scene: scene, standDraws: 0);

                InteriorLightOf(renderer, sample)
                    .Average()
                    .Should()
                    .BeInRange(0.5f, 0.5f + 9 * 0.5f / FallDraws, "the history trails the fall by at most 9 draws");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void ARegionEvidenceAloneCutsTheBounceHistoryToFullRateFrames()
        {
            // At the end of the fall the region evidence has acted for many draws: each blend adds a draw, and the cut
            // takes the history back to FullRateFrames, the shortest length that keeps the block's shared ray.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                FallThenStand(renderer: renderer, sample: sample, scene: scene, standDraws: 0);

                LengthsOf(renderer, sample)
                    .Should()
                    .OnlyContain(length => length == FullRateFrames, "region evidence cuts every history to 8 draws");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void UnderMotionEvidenceARegionEvidenceActsAsTheMotionEvidenceAlone()
        {
            // Motion stands above the region: the narrow box measures the change at the pixel itself, and the anti-lag
            // shortens the history by the clamp's move, not to FullRateFrames (section 8.2). The camera stands still, so
            // the region's taps still count, and the region's light keeps falling in the last draw.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                FallThenStand(renderer: renderer, sample: sample, scene: scene, standDraws: 0);
                sample.AgeCode = TemporalSignal.SampleMotionBit;

                DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 0.495f, frame: 200)
                    .Should()
                    .BeFalse("motion evidence acts");
                LengthsOf(renderer, sample)
                    .Should()
                    .OnlyContain(length => length != FullRateFrames, "the anti-lag sets the length, not the region");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void UnderARegionEvidenceTheAntiLagStillShortensAHistoryBelowTheRegionsCap()
        {
            // Region evidence is one more cap on top of the anti-lag, so it never leaves a history longer than no
            // evidence would. The narrow box holds no widening, and the samples hold no noise, so the clamp sets the
            // slow history onto the fast one. During the fall the clamp moves the slow history only a little in each draw,
            // so the anti-lag leaves the history above the cap, and region evidence holds it at FullRateFrames. Then
            // the light steps from 0.5 to 0.2 in one draw: the slow history blends the step at 1/9 and the fast one at
            // 1/4, so the clamp moves by about 10 % of the light, and the anti-lag takes the history below the cap.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: NarrowParameters
            );
            try
            {
                sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
                renderer.Render(scene, 1f);
                int frame = 0;
                for (; frame < 60; frame++)
                    DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 1, frame: frame, variation: 0);
                for (int fallen = 1; fallen <= 25; fallen++, frame++)
                    DrawNoisy(
                        renderer: renderer,
                        sample: sample,
                        scene: scene,
                        light: 1 - 0.02f * fallen,
                        frame: frame,
                        variation: 0
                    );
                DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 0.2f, frame: frame, variation: 0);

                LengthsOf(renderer, sample)
                    .Should()
                    .OnlyContain(
                        length => length > FastCap && length < FullRateFrames,
                        "the anti-lag shortens the history below the region's cap"
                    );
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void LightEvidenceCutsASettledBounceHistoryToTheLengthOfTheRegionsCut()
        {
            // One rule for both kinds of evidence of a change of unknown size: each cuts a settled history to
            // FullRateFrames, the shortest length that keeps the block's shared ray (docs/plans/bounce-light-confidence.md,
            // section 8.2). At the end of the fall the region evidence holds every history at its cut, and then one draw
            // also brings light evidence, the stronger level.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                FallThenStand(renderer: renderer, sample: sample, scene: scene, standDraws: 0);
                float[] regionCut = LengthsOf(renderer, sample);
                sample.AgeCode = 5;

                DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 0.495f, frame: 200);
                LengthsOf(renderer, sample)
                    .Should()
                    .Equal(regionCut, "light evidence cuts a settled history to the length of the region's cut");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void AfterTheRegionsLightStandsStillItsEvidenceEndsWithinTwoWindows()
        {
            // Once both windows hold only still draws, their means agree within the noise.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                List<int> withEvidence = FallThenStand(
                    renderer: renderer,
                    sample: sample,
                    scene: scene,
                    standDraws: TwoWindows + 40
                );

                withEvidence.Should().NotBeEmpty("the fall's end still lies in the newer window");
                withEvidence.Should().OnlyContain(stood => stood < TwoWindows, "the evidence ends within two windows");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void AfterTheRegionsEvidenceEndsTheBounceHistoryGrowsBackToItsCap()
        {
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                FallThenStand(renderer: renderer, sample: sample, scene: scene, standDraws: TwoWindows + 40);

                LengthsOf(renderer, sample)
                    .Should()
                    .OnlyContain(length => length == 32, "no evidence cuts the history after the light stood still");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void ACameraMoveGivesNoRegionEvidenceDuringItOrForTwoWindowsAfterIt()
        {
            // Every tap's surface moves on screen during the move, so no draw is valid, and the count of valid draws
            // starts again after it. The light falls all the while, which a still camera's region counts as a move
            // (ARegionEvidenceAloneCutsTheBounceHistoryToFullRateFrames).
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
                renderer.Render(scene, 1f);
                int frame = 0;
                for (; frame < 100; frame++)
                    DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 1, frame: frame);
                List<int> withEvidence = [];
                for (int draw = 0; draw < 40 + TwoWindows; draw++, frame++)
                {
                    scene.Camera.PreviousPosition = scene.Camera.Position;
                    if (draw < 40)
                        scene.Camera.Position += new Vector3(0.1f, 0, 0);
                    if (
                        DrawNoisy(
                            renderer: renderer,
                            sample: sample,
                            scene: scene,
                            light: 1 - 0.005f * (draw + 1),
                            frame: frame
                        )
                    )
                        withEvidence.Add(draw);
                }

                withEvidence.Should().BeEmpty("no draw of the move counts, and the count starts again after it");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void AChangeOfTheExposureAloneGivesNoRegionEvidence()
        {
            // The samples hold the same scene light, pre-exposed at the exposure that each frame uses, while a bright
            // star moves the exposure. The tap divides by the draw's exposure scale, so the region's light stays.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse,
                exposure: AutoExposure.Default
            );
            try
            {
                int frame = 0;
                bool ExposeAndContinue() =>
                    DrawNoisy(
                        renderer: renderer,
                        sample: sample,
                        scene: scene,
                        light: 0.05f * MathF.Pow(2, renderer.ReadExposureStops().Stops),
                        frame: frame++
                    );

                sample.Value = new(x: 0.05f, y: 0.05f, z: 0.05f, w: 1f);
                renderer.Render(scene, 1f);
                for (int draw = 0; draw < 100; draw++)
                    ExposeAndContinue();
                float before = renderer.ReadExposureStops().Stops;
                Gpu.Star(snapshot: scene, irradiance: 64 * Gpu.StarIrradiance(scene));
                List<int> withEvidence = [];
                for (int draw = 0; draw < 80; draw++)
                    if (ExposeAndContinue())
                        withEvidence.Add(draw);

                renderer.ReadExposureStops().Stops.Should().BeLessThan(before - 0.5f, "the star moved the exposure");
                withEvidence.Should().BeEmpty("the scene's light did not change");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void TheTapsOfASurfaceThatMovesOnScreenDoNotCountInTheRegionsLight()
        {
            // A box in front of the wall fills the view and moves slowly to the side, by about 1/16 pixel a draw, while
            // its light falls as in FallThenStand. A moving surface shows another surface point in each draw, so its taps
            // do not count. No draw is then valid, and no draw's evidence cuts a history. Counted, the box's falling light
            // would cut every history to FullRateFrames (ARegionEvidenceAloneCutsTheBounceHistoryToFullRateFrames). The
            // samples carry no motion bit, so no motion evidence hides a cut.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
                MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
                Vector3 place = new(0, 0, -10);
                RenderInstance Box(Vector3 position) =>
                    Gpu.Instance(
                        mesh: cube,
                        material: white,
                        position: position,
                        scale: new(60, 40, 1),
                        chainDepth: TestChains.CamerasFrame,
                        entity: 2
                    );
                scene.Instances.Add(Box(place));
                sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
                renderer.Render(scene, 1f);
                List<int> withEvidence = [];
                for (int frame = 0; frame < 100 + FallDraws; frame++)
                {
                    place += new Vector3(0.02f, 0, 0);
                    scene.Instances[^1] = Box(place);
                    float light = frame < 100 ? 1 : 1 - 0.5f * (frame - 99) / FallDraws;
                    if (DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: light, frame: frame))
                        withEvidence.Add(frame);
                }

                withEvidence.Should().BeEmpty("a moving surface's taps do not count, so no draw is valid");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>
        /// The width of the scene of the anchors' tests, in pixels: 24 blocks of 8 pixels, whose anchors are the blocks 0, 8
        /// and 16 of each row. Anchor 0's region holds the blocks 0 to 8, anchor 8's the blocks 0 to 16, and anchor 16's
        /// the blocks 8 to 23.
        /// </summary>
        private const int WideWidth = 192;

        /// <summary>The columns of the wide scene whose light falls in <see cref="FallInTheLeftThird"/>: the blocks 0 to 7.</summary>
        private const int FallingColumns = 64;

        /// <summary>The first column of the wide scene whose light falls in <see cref="FallInTheRightPart"/>: block 17.</summary>
        private const int FirstRightFallingColumn = 136;

        /// <summary>
        /// The distance from the camera to the front face of the scenes' walls, whose center lies 20 units away and whose
        /// depth is 1.
        /// </summary>
        private const float WallFaceDistance = 19.5f;

        /// <summary>The wide scene: the wall fills its view.</summary>
        private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) AccumulatingWide() =>
            Accumulating(
                parameters: SignalParameters.Diffuse,
                wall: (new(0, 0, -20), new(400, 40, 1)),
                width: WideWidth
            );

        /// <summary>
        /// The history lengths of part 0 of the wide scene after the last frame, row by row from the top: 0 for the sky.
        /// </summary>
        private static float[] WideLengthsOf(VulkanRenderer renderer, SamplePass sample)
        {
            float[] history = HistoryOf(renderer, sample, part: 0);
            return [.. Enumerable.Range(0, WideWidth * Height).Select(pixel => history[4 * pixel + 3])];
        }

        /// <summary>The history lengths of part 0 of the wide scene in the columns of block <paramref name="column"/>.</summary>
        private static IEnumerable<float> BlockColumnLengths(float[] lengths, int column) =>
            Enumerable
                .Range(0, lengths.Length)
                .Where(pixel => pixel % WideWidth / 8 == column)
                .Select(pixel => lengths[pixel]);

        /// <summary>
        /// Draws 100 frames of noisy samples of mean 1 in the wide scene, then <paramref name="fallDraws"/> frames in which
        /// the light of the left <see cref="FallingColumns"/> columns falls steadily, by 0.5 over <see cref="FallDraws"/>
        /// draws, while the rest stays at 1. <paramref name="afterEach"/> runs after each frame of the fall, with its index.
        /// </summary>
        private static void FallInTheLeftThird(
            VulkanRenderer renderer,
            SamplePass sample,
            RenderSnapshot scene,
            int fallDraws,
            Action<int>? afterEach = null
        ) =>
            FallIn(
                renderer: renderer,
                sample: sample,
                scene: scene,
                falls: column => column < FallingColumns,
                fallDraws: fallDraws,
                afterEach: afterEach
            );

        /// <summary>
        /// <see cref="FallInTheLeftThird"/> for the columns from <see cref="FirstRightFallingColumn"/> on, the blocks 17 to
        /// 23: they lie in anchor 16's region, the blocks 8 to 23, and in no other anchor's.
        /// </summary>
        private static void FallInTheRightPart(VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) =>
            FallIn(
                renderer: renderer,
                sample: sample,
                scene: scene,
                falls: column => column >= FirstRightFallingColumn,
                fallDraws: FallDraws
            );

        /// <summary>
        /// Draws 100 frames of noisy samples of mean 1 in the wide scene, then <paramref name="fallDraws"/> frames in which
        /// the light of the columns that <paramref name="falls"/> picks falls steadily, by 0.5 over <see cref="FallDraws"/>
        /// draws, while the rest stays at 1. <paramref name="afterEach"/> runs after each frame of the fall, with its index.
        /// </summary>
        private static void FallIn(
            VulkanRenderer renderer,
            SamplePass sample,
            RenderSnapshot scene,
            Func<int, bool> falls,
            int fallDraws,
            Action<int>? afterEach = null
        )
        {
            sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
            renderer.Render(scene, 1f);
            for (int frame = 0; frame < 100 + fallDraws; frame++)
            {
                int drawn = frame;
                float falling = frame < 100 ? 1 : 1 - 0.5f * (frame - 99) / FallDraws;
                sample.Samples = (column, row) =>
                    NoisySample(
                        light: falls(column) ? falling : 1,
                        variation: Variation,
                        column: column,
                        row: row,
                        frame: drawn
                    );
                Continue(renderer, scene, frames: 1);
                if (frame >= 100)
                    afterEach?.Invoke(frame - 100);
            }
        }

        [Fact]
        public void ABlockTakesTheRegionEvidenceOfAnAnchorWhoseRegionHoldsIt()
        {
            // Block 12's own taps and those of every block within 8 of it stand still, so a region centered on it would
            // see no change. But it lies in anchor 8's region, which holds the falling blocks 0 to 7.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingWide();
            try
            {
                FallInTheLeftThird(renderer: renderer, sample: sample, scene: scene, fallDraws: FallDraws);

                BlockColumnLengths(WideLengthsOf(renderer, sample), column: 12)
                    .Should()
                    .OnlyContain(length => length == FullRateFrames, "anchor 8's region light falls");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void ABlockOutsideTheRegionOfEveryAnchorWhoseLightMovedKeepsItsHistory()
        {
            // Block 20 lies only in anchor 16's region, the blocks 8 to 23, whose light stands still.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingWide();
            try
            {
                FallInTheLeftThird(renderer: renderer, sample: sample, scene: scene, fallDraws: FallDraws);

                BlockColumnLengths(WideLengthsOf(renderer, sample), column: 20)
                    .Should()
                    .OnlyContain(length => length == 32, "no anchor whose region holds block 20 saw its light move");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void TheRegionEvidenceActsInTheDrawAfterTheAnchorsTest()
        {
            // The anchor's workgroup writes its answer into this draw's ring layer, and every block reads the layer of the
            // draw before. So the first draw whose test finds the move cuts no history, and the draw after it does.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingWide();
            try
            {
                int? firstMoved = null;
                bool cutInFirst = false;
                bool cutAfterFirst = false;
                float[] before = [];
                FallInTheLeftThird(
                    renderer: renderer,
                    sample: sample,
                    scene: scene,
                    fallDraws: FallDraws,
                    afterEach: fallen =>
                    {
                        float[] lengths = WideLengthsOf(renderer, sample);
                        bool cut =
                            before.Length > 0
                            && Enumerable
                                .Range(0, lengths.Length)
                                .Any(pixel => before[pixel] >= FullRateFrames && lengths[pixel] == FullRateFrames);
                        before = lengths;
                        if (firstMoved is { } first)
                        {
                            if (fallen == first + 1)
                                cutAfterFirst = cut;
                            return;
                        }
                        // The draw's index since the reset is 100 + fallen + 1, its ring layer that index modulo 36.
                        int layer = (100 + fallen + 1) % TemporalSignal.RegionRingDraws;
                        float[] ring = renderer.ReadImageLayer(sample.Signal.RegionRing(0)!, layer);
                        if (ring[2] != 0)
                        {
                            firstMoved = fallen;
                            cutInFirst = cut;
                        }
                    }
                );

                firstMoved.Should().NotBeNull("anchor 0's region light falls");
                cutInFirst.Should().BeFalse("the test's own draw reads the answers of the draw before");
                cutAfterFirst.Should().BeTrue("the next draw takes the answer");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>
        /// The mean of the taps of the wide scene's blocks within 8 blocks of block (<paramref name="anchor"/>, 0), the
        /// region of that anchor, with the light <paramref name="lightAt"/> of each pixel. A tap is the sample at pixel
        /// (4, 4) of its block. The taps of the last block row lie below the image's 36 rows, so its blocks have none.
        /// </summary>
        private static float TapMeanAround(int anchor, Func<int, int, float> lightAt)
        {
            int[] columns = [.. Enumerable.Range(0, WideWidth / 8).Where(block => Math.Abs(block - anchor) <= 8)];
            int[] rows = [.. Enumerable.Range(0, (Height - 4 + 7) / 8)];
            return columns.SelectMany(column => rows.Select(row => lightAt(8 * column + 4, 8 * row + 4))).Average();
        }

        [Fact]
        public void AnAnchorsRingHoldsTheMeanOfTheTapsOfItsOwnRegion()
        {
            // The light differs from block to block in both directions, so each anchor's region has its own mean. The
            // test compares the anchors' light with each other, so that the exposure's scale, which divides every tap,
            // drops out.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingWide();
            try
            {
                static float LightAt(int column, int row) => 1 + column / 64f + row / 12f;
                sample.Samples = (column, row) => new Vector4(new Vector3(LightAt(column, row)), 1);
                renderer.Render(scene, 1f);
                Continue(renderer, scene, frames: 3);

                // The last draw's index since the reset is 3, and the ring's texels are the anchors 0, 8 and 16 of row 0.
                float[] ring = renderer.ReadImageLayer(sample.Signal.RegionRing(0)!, 3);
                float[] expected = [.. new[] { 0, 8, 16 }.Select(anchor => TapMeanAround(anchor, LightAt))];
                (ring[4] / ring[0])
                    .Should()
                    .BeApproximately(expected[1] / expected[0], 1e-4f, "anchor 8 holds its own mean");
                (ring[8] / ring[0])
                    .Should()
                    .BeApproximately(expected[2] / expected[0], 1e-4f, "anchor 16 holds its own mean");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void ABlockOneStepFromAnAnchorTakesThatAnchorsEvidence()
        {
            // Block 8 lies on anchor 8's column and at the edge of anchor 16's region, the blocks 8 to 23. Only anchor
            // 16's region holds the falling blocks 17 to 23.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingWide();
            try
            {
                FallInTheRightPart(renderer, sample, scene);

                BlockColumnLengths(WideLengthsOf(renderer, sample), column: 8)
                    .Should()
                    .OnlyContain(length => length == FullRateFrames, "anchor 16's region holds block 8");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void ABlockMoreThanOneStepFromAnAnchorDoesNotTakeThatAnchorsEvidence()
        {
            // Block 7 lies 9 blocks from anchor 16, one block outside its region. The regions of anchors 0 and 8, which
            // hold block 7, end at block 16, before the falling blocks 17 to 23.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingWide();
            try
            {
                FallInTheRightPart(renderer, sample, scene);

                BlockColumnLengths(WideLengthsOf(renderer, sample), column: 7)
                    .Should()
                    .OnlyContain(length => length == 32, "no anchor whose region holds block 7 saw its light move");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>
        /// The center and the size of a wall of the wide scene that shows in the columns of the blocks
        /// <paramref name="first"/> to <paramref name="last"/>, its edges on the blocks' edges.
        /// </summary>
        private static (Vector3 Center, Vector3 Size) WallOverBlocks(int first, int last)
        {
            float halfWidth = WallFaceDistance * MathF.Tan(Gpu.Fov / 2) * WideWidth / Height;
            float XOf(int column) => (2f * column / WideWidth - 1) * halfWidth;
            float left = XOf(8 * first);
            float right = XOf(8 * (last + 1));
            return (new((left + right) / 2, 0, -20), new(right - left, 40, 1));
        }

        /// <summary>
        /// The wide scene with the sky around two walls, over the blocks 0 to 11 and 17 to 19. So the taps of anchor 8's
        /// region, the blocks 0 to 16, show a surface in 12 of its 17 columns, and those of anchor 16's region, the blocks
        /// 8 to 23, in 7 of its 16.
        /// </summary>
        private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) AccumulatingTwoWalls()
        {
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse,
                wall: WallOverBlocks(first: 0, last: 11),
                width: WideWidth
            );
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            (Vector3 center, Vector3 size) = WallOverBlocks(first: 17, last: 19);
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: center,
                    scale: size,
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );
            return (renderer, sample, scene);
        }

        [Fact]
        public void ARegionWhoseTapsShowASurfaceInAtLeastHalfGivesEvidence()
        {
            // Block 10 lies in the regions of anchors 8 and 16. The light falls everywhere, and anchor 8's taps show a
            // surface in 12 of its 17 columns, so its draws are valid.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingTwoWalls();
            try
            {
                FallIn(renderer: renderer, sample: sample, scene: scene, falls: _ => true, fallDraws: FallDraws);

                BlockColumnLengths(WideLengthsOf(renderer, sample), column: 10)
                    .Should()
                    .OnlyContain(length => length == FullRateFrames, "anchor 8's region light falls");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void ARegionWhoseTapsShowASurfaceInLessThanHalfHasNoValidDraw()
        {
            // Anchor 16's taps show a surface in 7 of its 16 columns: the sky's taps do not count, so no draw is valid,
            // and the test never runs, although the light falls everywhere. The ring's count of valid draws shows it. The
            // lengths cannot: the anti-lag shortens the falling blocks' histories without any evidence.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingTwoWalls();
            try
            {
                FallIn(renderer: renderer, sample: sample, scene: scene, falls: _ => true, fallDraws: FallDraws);

                // The last draw's index since the reset is 100 + FallDraws, and anchor 16's texel is the third of row 0.
                float[] ring = renderer.ReadImageLayer(
                    sample.Signal.RegionRing(0)!,
                    (100 + FallDraws) % TemporalSignal.RegionRingDraws
                );
                ring[4 + 1].Should().BeGreaterThan(0, "anchor 8's taps show a surface in 12 of its 17 columns");
                ring[8 + 1].Should().Be(0, "anchor 16's region has no valid draw");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void AfterACameraCutARegionGivesNoEvidenceUntilBothWindowsHoldOnlyDrawsAfterIt()
        {
            // A camera cut resets every history and the count of valid draws. The light is 1 before the cut and 0.5
            // after it, so a test whose windows reached back across the cut would see a move.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Diffuse
            );
            try
            {
                sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
                renderer.Render(scene, 1f);
                int frame = 0;
                for (; frame < 100; frame++)
                    DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 1, frame: frame);
                scene.CameraCuts++;
                List<int> withEvidence = [];
                for (int draw = 0; draw < TwoWindows + 40; draw++, frame++)
                    if (DrawNoisy(renderer: renderer, sample: sample, scene: scene, light: 0.5f, frame: frame))
                        withEvidence.Add(draw);

                withEvidence.Should().BeEmpty("the count of valid draws starts again at the cut");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        [Fact]
        public void AMovedRegionDoesNotShortenTheReflectionsHistory()
        {
            // Only the bounce light runs the region's test: the reflections' test fired in still views and brought
            // bright specks into the bay film (docs/plans/bounce-light-confidence.md, section 8.7). After the same fall,
            // the bounce light's region evidence lasts into the stand (AfterTheRegionsLightStandsStillItsEvidenceEndsWithin
            // TwoWindows).
            // In the stand the reflections' histories grow past FullRateFrames, so a region cut would end one at exactly
            // FullRateFrames, and none does.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = Accumulating(
                parameters: SignalParameters.Specular(0)
            );
            try
            {
                sample.Signal.RegionRing(0).Should().BeNull("the reflections keep no anchors' ring");
                List<int> cut = FallThenStand(renderer: renderer, sample: sample, scene: scene, standDraws: TwoWindows);

                LengthsOf(renderer, sample)
                    .Should()
                    .Contain(length => length > FullRateFrames, "in the stand a region cut would show");
                cut.Should().BeEmpty("no region evidence acts on the reflections");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        /// <summary>The window of <see cref="AccumulatingInAWindow"/>: at its largest render size 48 × 9 blocks.</summary>
        private const int WindowWidth = 384,
            WindowHeight = 72;

        /// <summary>
        /// The test signal of the bounce light in a window's renderer with FSR 3.1 and a frame-time target, so that its
        /// render size can fall below its images' size, which is the largest render size. The wall fills the view.
        /// </summary>
        private static (VulkanRenderer Renderer, SamplePass Sample, RenderSnapshot Scene) AccumulatingInAWindow() =>
            AccumulatingOn(
                renderer: VulkanRenderer.CreateOffscreen(
                    options: new()
                    {
                        Width = WindowWidth,
                        Height = WindowHeight,
                        FramesInFlight = 1,
                        Validation = true,
                        Impostors = false,
                        EnvironmentMap = new NoEnvironmentMap(),
                        Exposure = new FixedExposure(0),
                        Bloom = new NoBloom(),
                        Upscaling = new Fsr3Upscaling(FrameTimeTarget.Default, 0),
                        WarmUpFrames = 0,
                    },
                    createOutput: (device, allocator, extent) =>
                        new HeadlessTarget(device: device, allocator: allocator, extent: extent, bgra: false),
                    headless: false
                ),
                parameters: SignalParameters.Diffuse,
                wall: (new(0, 0, -20), new(400, 40, 1))
            );

        /// <summary>
        /// The history lengths of part 0 of the window after the last frame, row by row from the top, over the images'
        /// whole size: the pixels beyond the render size keep the lengths of an earlier draw.
        /// </summary>
        private static float[] WindowLengthsOf(VulkanRenderer renderer, SamplePass sample)
        {
            float[] history = HistoryOf(renderer, sample, part: 0);
            return [.. Enumerable.Range(0, WindowWidth * WindowHeight).Select(pixel => history[4 * pixel + 3])];
        }

        /// <summary>
        /// Draws one frame of noisy samples of mean <paramref name="light"/>, and then reports its timings
        /// (<see cref="Report"/>): over the budget, the render scale falls to its least value, a half, and stays there,
        /// and under it, the scale stays at its largest value, 1. The render size is the scale times the window's size,
        /// rounded down to whole blocks.
        /// Returns whether region evidence cut a history inside the render size: a length of at least FullRateFrames
        /// before the draw that ends at exactly FullRateFrames. Like <see cref="DrawNoisy"/>, it misses a cut
        /// that the anti-lag takes below FullRateFrames.
        /// </summary>
        private static bool DrawInTheWindow(
            VulkanRenderer renderer,
            SamplePass sample,
            RenderSnapshot scene,
            float light,
            int frame,
            bool overBudget
        )
        {
            float[] before = WindowLengthsOf(renderer, sample);
            sample.Samples = (column, row) =>
                NoisySample(light: light, variation: Variation, column: column, row: row, frame: frame);
            Continue(renderer, scene, frames: 1);
            Report(renderer, overBudget);
            float[] after = WindowLengthsOf(renderer, sample);
            CameraView camera = renderer.LastScene.Camera;
            return Enumerable
                .Range(0, after.Length)
                .Where(pixel => pixel % WindowWidth < camera.Width && pixel / WindowWidth < camera.Height)
                .Any(pixel => before[pixel] >= FullRateFrames && after[pixel] == FullRateFrames);
        }

        /// <summary>
        /// Reports the last frame's timings to the render scale's controller: far over the frame's budget where
        /// <paramref name="overBudget"/>, else far under it. The report comes before the renderer collects the frame's
        /// own timings, which it then ignores.
        /// </summary>
        private static void Report(VulkanRenderer renderer, bool overBudget)
        {
            double cost = overBudget ? 100 : 0.1;
            renderer.ScaleController!.Measure(
                new(
                    renderer.DrawnFrames - 1,
                    new PassTiming[]
                    {
                        new("G-buffer", cost) { Stage = FrameGraph.Stage.GBuffer },
                        new("exposure", 0) { Stage = FrameGraph.Stage.Exposure },
                        new("FSR 3.1", 1) { Stage = FrameGraph.Stage.Upscale },
                        new("bloom", 1) { Stage = FrameGraph.Stage.Bloom },
                        new("tone map", cost) { Stage = FrameGraph.Stage.ToneMap },
                    },
                    2 * cost + 2
                )
            );
        }

        [Fact]
        public void AfterTheRenderSizeFallsNoBlockTakesAnAnswerThatNoAnchorGaveAtThatSize()
        {
            // At the largest render size the light falls everywhere, so every anchor's ring holds moves. At half the
            // size, 192 × 32 pixels, the dispatch covers the anchors 0, 8 and 16 of row 0 only, and the ring's other
            // texels keep the moves of the larger size. A change of the render size also moves the scene under the taps,
            // so the count of valid draws restarts. The light then stands still, so no answer at the half size is a move.
            (VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene) = AccumulatingInAWindow();
            try
            {
                sample.Value = new(x: 1f, y: 1f, z: 1f, w: 1f);
                renderer.Render(scene, 1f);
                Report(renderer, overBudget: false);
                int frame = 0;
                for (; frame < 100 + FallDraws; frame++)
                {
                    float light = frame < 100 ? 1 : 1 - 0.5f * (frame - 99) / FallDraws;
                    DrawInTheWindow(
                        renderer: renderer,
                        sample: sample,
                        scene: scene,
                        light: light,
                        frame: frame,
                        overBudget: false
                    );
                }
                renderer
                    .LastScene.Camera.Width.Should()
                    .Be(WindowWidth, "the reports kept the render size at its largest");
                // A draw whose render size changed reads no answer of the draw before.
                List<int> changedWithEvidence = [];
                for (int draw = 0; draw < 40 && renderer.LastScene.Camera.Width > WindowWidth / 2; draw++, frame++)
                {
                    int widthBefore = renderer.LastScene.Camera.Width;
                    bool cut = DrawInTheWindow(
                        renderer: renderer,
                        sample: sample,
                        scene: scene,
                        light: 0.5f,
                        frame: frame,
                        overBudget: true
                    );
                    if (cut && renderer.LastScene.Camera.Width != widthBefore)
                        changedWithEvidence.Add(draw);
                }
                renderer
                    .LastScene.Camera.Width.Should()
                    .Be(WindowWidth / 2, "the reports lowered the render size to half");
                changedWithEvidence
                    .Should()
                    .BeEmpty("a draw whose render size changed takes no answer of the draw before");

                List<int> withEvidence = [];
                for (int draw = 0; draw < TwoWindows + 40; draw++, frame++)
                    if (
                        DrawInTheWindow(
                            renderer: renderer,
                            sample: sample,
                            scene: scene,
                            light: 0.5f,
                            frame: frame,
                            overBudget: true
                        )
                    )
                        withEvidence.Add(draw);

                renderer.LastScene.Camera.Width.Should().Be(WindowWidth / 2, "the render size stayed at half");
                withEvidence.Should().BeEmpty("no anchor at the half size saw its light move");
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }
    }
}
