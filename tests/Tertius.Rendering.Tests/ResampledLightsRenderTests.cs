using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// ReSTIR DI against the exact loop (docs/plans/temporal-renderer.md, section 7): one light gives the exact light, a light
/// with a cascade set stays exact beside it, in a closed box lit by its lamps, under a bright light with a cascade set that
/// the box shadows, the box's light matches <see cref="EveryLight"/> with the noise of its lamps alone, and the temporal
/// reuse alone and the default reuse keep a lamp's shadow. Most tests draw one still of the same snapshot under each setting, with the shadow atlas
/// on, and compare the direct signal of the shown draws: the samples, which ReSTIR estimates, or the histories, which the
/// accumulator settles.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ResampledLightsRenderTests
{
    private const int Width = 160,
        Height = 90;

    /// <summary>
    /// The bound of the bias of the accumulated light against the exact loop after a still's warm-up, the relative error of
    /// its sum: the bias that the lamp hall of 64 lamps measured, rounded up (ARCHITECTURE.md, "ReSTIR DI").
    /// </summary>
    private const double BiasBound = 0.05;

    /// <summary>
    /// The bound of the mean relative error of the box's samples against the exact loop's: its measured 0.308 (2026-11-03),
    /// rounded up. Decision 28 makes the star exact because a sampled star, whose light the box shadows, would take
    /// candidates from the lamps and raise this noise.
    /// </summary>
    private const double SampleErrorBound = 0.40;

    /// <summary>
    /// The mean over the pixels that <paramref name="counts"/> keeps of the relative error of <paramref name="resampled"/>'s
    /// light against <paramref name="exact"/>'s, both summed over the three channels: the noise of a single draw.
    /// </summary>
    private static double SampleErrorOf(Vector3[] exact, Vector3[] resampled, Func<int, bool> counts)
    {
        double error = 0;
        int pixels = 0;
        for (int pixel = 0; pixel < exact.Length; pixel++)
        {
            if (!counts(pixel))
                continue;
            float reference = exact[pixel].X + exact[pixel].Y + exact[pixel].Z;
            error += Math.Abs(resampled[pixel].X + resampled[pixel].Y + resampled[pixel].Z - reference) / reference;
            pixels++;
        }
        return error / pixels;
    }

    /// <summary>
    /// The relative error of the sum of <paramref name="resampled"/>'s light against the sum of <paramref name="exact"/>'s,
    /// both parts and all three channels, over the pixels that <paramref name="counts"/> keeps, and how many those are.
    /// </summary>
    private static (double Bias, int Pixels) BiasOf(Vector3[] exact, Vector3[] resampled, Func<int, bool> counts)
    {
        double exactSum = 0,
            resampledSum = 0;
        int pixels = 0;
        for (int pixel = 0; pixel < exact.Length; pixel++)
        {
            if (!counts(pixel))
                continue;
            exactSum += exact[pixel].X + exact[pixel].Y + exact[pixel].Z;
            resampledSum += resampled[pixel].X + resampled[pixel].Y + resampled[pixel].Z;
            pixels++;
        }
        return (resampledSum / exactSum - 1, pixels);
    }

    /// <summary>
    /// The demodulated direct light of the shown draw of one still of <paramref name="build"/>, both parts added, per
    /// pixel: the samples if <paramref name="samples"/>, else the slow histories.
    /// </summary>
    private static Vector3[] DirectOf(DirectLightSetting setting, Func<VulkanRenderer, RenderSnapshot> build, bool samples)
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            localShadows: LocalShadowAtlas.Default,
            directLight: setting
        );
        try
        {
            RenderSnapshot snapshot = build(renderer);
            renderer.Render(snapshot, 1f);
            float[] diffuse = samples ? renderer.ReadDirectSample(0) : renderer.ReadDirectHistory(0);
            float[] specular = samples ? renderer.ReadDirectSample(1) : renderer.ReadDirectHistory(1);
            var light = new Vector3[Width * Height];
            for (int pixel = 0; pixel < light.Length; pixel++)
                light[pixel] =
                    new Vector3(diffuse[4 * pixel], diffuse[4 * pixel + 1], diffuse[4 * pixel + 2])
                    + new Vector3(specular[4 * pixel], specular[4 * pixel + 1], specular[4 * pixel + 2]);
            return light;
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A wall 8 m ahead that fills the view, lit by one lamp of entity 2, with no star.</summary>
    private static RenderSnapshot OneLamp(VulkanRenderer renderer) => OneLamp(renderer, star: false, lamp: new(1, 0.5f, -5));

    /// <summary>
    /// A wall 8 m ahead that fills the view, lit by one lamp of entity 2 at <paramref name="lamp"/>, and, if
    /// <paramref name="star"/>, by the test star, which holds the cascade set.
    /// </summary>
    private static RenderSnapshot OneLamp(VulkanRenderer renderer, bool star, Vector3 lamp)
    {
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId paint = renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.8f, y: 0.6f, z: 0.4f, w: 1f), roughness: 0.4f));
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f), starIrradiance: 20f);
        if (!star)
            snapshot.Lights.Clear();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: cube,
                material: paint,
                position: new(0, 0, -8),
                scale: new(30, 20, 1),
                chainDepth: TestChains.CamerasFrame,
                entity: 1
            )
        );
        snapshot.Lights.Add(
            Gpu.Lamp(position: lamp, color: Vector3.One, intensity: 40, chainDepth: TestChains.CamerasFrame)
                with
                {
                    EntityIndex = 2,
                }
        );
        return snapshot;
    }

    /// <summary>
    /// A closed box of 6 × 4 × 8 m around the camera, lit inside by three lamps, under the test star, which holds the cascade
    /// set and lights the box's outside.
    /// </summary>
    private static RenderSnapshot LitBox(VulkanRenderer renderer)
    {
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId wall = renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.7f, y: 0.7f, z: 0.65f, w: 1f), roughness: 0.6f));
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f), starIrradiance: 1000f);
        (Vector3 Position, Vector3 Scale)[] walls =
        [
            (new(0, -2.1f, -3), new(6.4f, 0.2f, 8.4f)),
            (new(0, 2.1f, -3), new(6.4f, 0.2f, 8.4f)),
            (new(-3.1f, 0, -3), new(0.2f, 4.4f, 8.4f)),
            (new(3.1f, 0, -3), new(0.2f, 4.4f, 8.4f)),
            (new(0, 0, -7.1f), new(6.4f, 4.4f, 0.2f)),
            (new(0, 0, 1.1f), new(6.4f, 4.4f, 0.2f)),
        ];
        for (int i = 0; i < walls.Length; i++)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: wall,
                    position: walls[i].Position,
                    scale: walls[i].Scale,
                    chainDepth: TestChains.CamerasFrame,
                    flags: RenderFlags.CastsShadow,
                    entity: (uint)(10 + i)
                )
            );
        Vector3[] lamps = [new(-2, 1.5f, -5), new(2, 1.5f, -2), new(0, -1, -6)];
        for (int i = 0; i < lamps.Length; i++)
            snapshot.Lights.Add(
                Gpu.Lamp(position: lamps[i], color: Vector3.One, intensity: 8, chainDepth: TestChains.CamerasFrame) with
                {
                    EntityIndex = (uint)(20 + i),
                }
            );
        return snapshot;
    }

    /// <summary>
    /// A wall 8 m ahead that fills the view, lit from above and from below by two lamps that cast shadows, and, if
    /// <paramref name="slats"/>, three slats above the view that shadow stripes of the wall from the upper lamp only.
    /// </summary>
    private static RenderSnapshot ShadowedWall(VulkanRenderer renderer, bool slats)
    {
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId paint = renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.7f, y: 0.7f, z: 0.7f, w: 1f), roughness: 0.6f));
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        snapshot.Lights.Clear();
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: cube,
                material: paint,
                position: new(0, 0, -8),
                scale: new(30, 20, 1),
                chainDepth: TestChains.CamerasFrame,
                entity: 1
            )
        );
        for (int i = 0; slats && i < 3; i++)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: paint,
                    position: new(0, 5, -5.4f - 0.6f * i),
                    scale: new(30, 0.3f, 0.3f),
                    chainDepth: TestChains.CamerasFrame,
                    flags: RenderFlags.CastsShadow,
                    entity: (uint)(2 + i)
                )
            );
        Vector3[] lamps = [new(0, 8, -4), new(0, -8, -4)];
        for (int i = 0; i < lamps.Length; i++)
            snapshot.Lights.Add(
                Gpu.Lamp(position: lamps[i], color: Vector3.One, intensity: 40, chainDepth: TestChains.CamerasFrame) with
                {
                    EntityIndex = (uint)(20 + i),
                    CastsShadow = true,
                }
            );
        return snapshot;
    }

    [Fact]
    public void OneLightGivesTheExactLight()
    {
        Vector3[] exact = DirectOf(new EveryLight(), OneLamp, samples: true);

        Vector3[] resampled = DirectOf(ResampledLights.Default, OneLamp, samples: true);

        for (int pixel = 0; pixel < exact.Length; pixel++)
            resampled[pixel]
                .X.Should()
                .BeApproximately(exact[pixel].X, 2e-3f * MathF.Max(exact[pixel].X, 1e-3f), "pixel {0}", pixel);
    }

    [Fact]
    public void ALightWithACascadeSetStaysExactBesideASampledLamp()
    {
        Vector3[] exact = DirectOf(new EveryLight(), r => OneLamp(r, star: true, lamp: new(1, 0.5f, -5)), samples: true);

        Vector3[] resampled = DirectOf(
            ResampledLights.Default,
            r => OneLamp(r, star: true, lamp: new(1, 0.5f, -5)),
            samples: true
        );

        for (int pixel = 0; pixel < exact.Length; pixel++)
            resampled[pixel]
                .X.Should()
                .BeApproximately(
                    exact[pixel].X,
                    2e-3f * MathF.Max(exact[pixel].X, 1e-3f),
                    "pixel {0} takes the star exactly and the one sampled lamp with W = 1",
                    pixel
                );
    }

    [Fact]
    public void ABoxLitByItsLampsUnderAStarWithACascadeSetThatItShadowsMatchesTheExactLoop()
    {
        Vector3[] exact = DirectOf(new EveryLight(), LitBox, samples: false);

        Vector3[] resampled = DirectOf(ResampledLights.Default, LitBox, samples: false);

        (double bias, int lit) = BiasOf(exact, resampled, pixel => exact[pixel].X + exact[pixel].Y + exact[pixel].Z > 1e-3f);
        lit.Should().BeGreaterThan(exact.Length / 2, "the lamps light most of the box");
        bias.Should().BeInRange(-BiasBound, BiasBound, "the star's cascade set makes it an exact light");
    }

    [Fact]
    public void InABoxUnderAStarWithACascadeSetTheSamplesHoldOnlyTheNoiseOfTheLamps()
    {
        Vector3[] exact = DirectOf(new EveryLight(), LitBox, samples: true);

        Vector3[] resampled = DirectOf(ResampledLights.Default, LitBox, samples: true);

        double error = SampleErrorOf(exact, resampled, pixel => exact[pixel].X + exact[pixel].Y + exact[pixel].Z > 1e-3f);
        TestContext.Current.TestOutputHelper?.WriteLine($"The box's sample error: {error:0.0000}.");
        error.Should().BeLessThan(SampleErrorBound, "no candidate goes to the star, whose light the box shadows");
    }

    [Fact]
    public void AReservoirWhoseCandidatesFoundNoLightStillStandsForThemInTheNextFrame()
    {
        // The lamp lies behind the wall, so every candidate at every pixel has a target of 0.
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            localShadows: LocalShadowAtlas.Default,
            directLight: ResampledLights.Default
        );
        try
        {
            RenderSnapshot snapshot = OneLamp(renderer, star: false, lamp: new(0, 0, -12));
            renderer.Render(snapshot, 1f);
            for (int frame = 0; frame < 2; frame++)
            {
                snapshot.Time += 1.0 / 60.0;
                renderer.Render(snapshot, 1f);
            }

            (int light, int m, float w) = renderer.ReadCandidateReservoirs()[Height / 2 * Width + Width / 2];

            w.Should().Be(0, "no candidate found light");
            m.Should().Be(4 + 80, "the 4 candidates of this frame, and the last frame's reservoir capped at 80 (light {0})", light);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheSignalTheReservoirsAndTheLightTableKeepTheirInvariantsThroughFramesAndAResize()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            localShadows: LocalShadowAtlas.Default,
            directLight: ResampledLights.Default
        );
        try
        {
            RenderSnapshot snapshot = LitBox(renderer);
            for (int frame = 0; frame < 3; frame++)
            {
                snapshot.Time += 1.0 / 60.0;
                renderer.Render(snapshot, 1f);
            }
            AssertDirectInvariants(renderer);

            renderer.Resize(Width / 2, Height / 2);
            snapshot.Time += 1.0 / 60.0;
            renderer.Render(snapshot, 1f);

            AssertDirectInvariants(renderer);
            renderer.DirectSignal.Slow(0).Extent.Width.Should().Be(Width / 2, "the resize reached the signal");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    private static void AssertDirectInvariants(VulkanRenderer renderer)
    {
        renderer.DirectSignal.AssertInvariants();
        renderer.Reservoirs!.AssertInvariants();
        renderer.LastScene.LightTable.AssertInvariants();
    }

    [Fact]
    public void UnderTemporalReuseAloneTheStripesThatOneOfTwoLampsShadowsKeepTheOthersLight()
    {
        Vector3[] open = DirectOf(new EveryLight(), renderer => ShadowedWall(renderer, slats: false), samples: false);
        Vector3[] exact = DirectOf(new EveryLight(), renderer => ShadowedWall(renderer, slats: true), samples: false);

        Vector3[] resampled = DirectOf(
            ResampledLights.Default with { SpatialNeighbors = 0 },
            renderer => ShadowedWall(renderer, slats: true),
            samples: false
        );

        (double bias, int shadowed) = BiasOf(exact, resampled, pixel => exact[pixel].X < 0.8f * open[pixel].X);
        shadowed.Should().BeGreaterThan(Width * 4, "the slats shadow stripes across the wall from the upper lamp");
        bias.Should().BeInRange(-BiasBound, BiasBound, "the stripes hold the lower lamp's light alone");
    }

    /// <summary>
    /// The spatial reuse's combine tests the visibility of a neighbor's light at this pixel: today's stripes measured a
    /// bias of 0.011 (2026-11-03), and the biased combine before it, which took a neighbor's light with no such test,
    /// −0.102.
    /// </summary>
    [Fact]
    public void UnderTheSpatialReuseTheStripesThatOneOfTwoLampsShadowsKeepTheOthersLight()
    {
        Vector3[] open = DirectOf(new EveryLight(), renderer => ShadowedWall(renderer, slats: false), samples: false);
        Vector3[] exact = DirectOf(new EveryLight(), renderer => ShadowedWall(renderer, slats: true), samples: false);

        Vector3[] resampled = DirectOf(
            ResampledLights.Default,
            renderer => ShadowedWall(renderer, slats: true),
            samples: false
        );

        (double bias, int shadowed) = BiasOf(exact, resampled, pixel => exact[pixel].X < 0.8f * open[pixel].X);
        TestContext.Current.TestOutputHelper?.WriteLine($"The stripes' bias: {bias:0.0000}.");
        shadowed.Should().BeGreaterThan(Width * 4, "the slats shadow stripes across the wall from the upper lamp");
        bias.Should().BeInRange(-BiasBound, BiasBound, "the stripes hold the lower lamp's light alone");
    }
}
