using System.Numerics;
using Silk.NET.Vulkan;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The diffuse filter (ticket ter-9371, <see cref="DiffuseFilter"/>): it spreads a bright sample over its neighbors on the
/// same surface and keeps its light, it lets no light cross a depth edge or a crease, it leaves a smooth image without
/// noise as it is, and a ray's hit reads its output. A test signal and its producer stand for the diffuse signal, and the
/// filter runs after their accumulation. The tests run the default thin kernel unless they name one. The camera stands
/// still, and each frame steps one tick.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class DiffuseFilterRenderTests(ITestOutputHelper output)
{
    private const int Width = 128,
        Height = 72;

    /// <summary>The step of each frame after the first: one tick, which continues the last frame.</summary>
    private const double Tick = 1.0 / 60.0;

    /// <summary>The test signal's parameter set: the diffuse signal's, with a history of 16 frames.</summary>
    private static readonly SignalParameters Parameters = SignalParameters.Diffuse with { Cap = 16, SmoothCap = 16 };

    /// <summary>
    /// The renderer with the test signal, its producer, its accumulation and its filter of the thin kernel after the direct
    /// pass, before the white boxes of <paramref name="boxes"/>, each a center, a rotation about +Y in radians and a scale.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, DiffuseFilter Filter, RenderSnapshot Scene) Filtering(
        params (Vector3 Center, float Turn, Vector3 Scale)[] boxes
    ) => Filtering(filtered: true, DiffuseFilterKernel.Thin, boxes);

    /// <summary>
    /// The renderer of <see cref="Filtering(ValueTuple{Vector3, float, Vector3}[])"/> with a filter of
    /// <paramref name="kernel"/>, which runs only when <paramref name="filtered"/>.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, DiffuseFilter Filter, RenderSnapshot Scene) Filtering(
        bool filtered,
        DiffuseFilterKernel kernel,
        params (Vector3 Center, float Turn, Vector3 Scale)[] boxes
    ) => Filtering(filtered: filtered, kernel: kernel, parameters: Parameters, boxes: boxes);

    /// <summary>
    /// The renderer of <see cref="Filtering(ValueTuple{Vector3, float, Vector3}[])"/> before a wall that fills the view,
    /// with the bounce light's own parameter set: its cap of 32 and its widened box.
    /// </summary>
    private static (
        VulkanRenderer Renderer,
        SamplePass Sample,
        DiffuseFilter Filter,
        RenderSnapshot Scene
    ) FilteringBounceLight(bool filtered = true) =>
        Filtering(
            filtered: filtered,
            kernel: DiffuseFilterKernel.Thin,
            parameters: SignalParameters.Diffuse,
            boxes: [Wall]
        );

    /// <summary>
    /// The renderer of <see cref="Filtering(bool, DiffuseFilterKernel, ValueTuple{Vector3, float, Vector3}[])"/> with the
    /// test signal's parameter set <paramref name="parameters"/>.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, DiffuseFilter Filter, RenderSnapshot Scene) Filtering(
        bool filtered,
        DiffuseFilterKernel kernel,
        SignalParameters parameters,
        (Vector3 Center, float Turn, Vector3 Scale)[] boxes
    )
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
        RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
        uint entity = 1;
        foreach ((Vector3 center, float turn, Vector3 scale) in boxes)
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: center,
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn),
                    scale: scale,
                    chainDepth: TestChains.CamerasFrame,
                    entity: entity++
                )
            );
        TemporalSignal signal = renderer.CreateSignal(parameters: parameters, name: "test", parts: 1);
        var sample = new SamplePass(signal, renderer.Allocator);
        DiffuseFilter filter = renderer.CreateDiffuseFilter(signal, kernel);
        renderer.Graph.Add(FrameGraph.Stage.DirectLight, sample);
        renderer.Graph.Add(FrameGraph.Stage.DirectLight, new AccumulatePass(signal));
        if (filtered)
            renderer.Graph.Add(FrameGraph.Stage.DirectLight, new DiffuseFilterPass(filter));
        return (renderer, sample, filter, scene);
    }

    /// <summary>A wall that fills the view, 20 m before the eye.</summary>
    private static readonly (Vector3 Center, float Turn, Vector3 Scale) Wall = (new(0, 0, -20), 0, new(60, 40, 1));

    /// <summary>Draws <paramref name="frames"/> frames of <paramref name="scene"/>, each one tick after the last.</summary>
    private static void Continue(VulkanRenderer renderer, RenderSnapshot scene, int frames)
    {
        for (int frame = 0; frame < frames; frame++)
        {
            scene.Time += Tick;
            renderer.Render(scene, 1f);
        }
    }

    /// <summary>The red channel of the filtered light at each pixel, row by row from the top.</summary>
    private static float[] FilteredRed(VulkanRenderer renderer, DiffuseFilter filter)
    {
        filter.AssertInvariants();
        float[] filtered = renderer.ReadImage(filter.Filtered);
        float[] red = new float[Width * Height];
        for (int pixel = 0; pixel < red.Length; pixel++)
            red[pixel] = filtered[4 * pixel];
        return red;
    }

    /// <summary>The center pixel's index, row by row from the top.</summary>
    private const int Center = Height / 2 * Width + Width / 2;

    /// <summary>
    /// The retained deviation of each pixel after the last frame, row by row from the top: the alpha of the fast history
    /// that the frame wrote, after the filter's feedback step.
    /// </summary>
    private static float[] RetainedDeviationOf(VulkanRenderer renderer, SamplePass sample)
    {
        float[] fast = renderer.ReadImage(sample.Signal.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1));
        float[] deviation = new float[Width * Height];
        for (int pixel = 0; pixel < deviation.Length; pixel++)
            deviation[pixel] = fast[4 * pixel + 3];
        return deviation;
    }

    /// <summary>
    /// Light 2 or 0 on a checker whose squares swap in every frame: a sample with a mean of 1 and much noise.
    /// </summary>
    private static Func<int, int, Vector4> Flicker(Func<int> frame) =>
        (column, row) => new Vector4(new Vector3(((column + row + frame()) & 1) * 2), 1);

    [Fact]
    public void ABrightSampleOnAFlatWallSpreadsOverItsNeighborsAndKeepsItsLight()
    {
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            // A dark wall, so that a store into a half float, which may round toward zero, loses at most 2^−10 of the
            // spread light, not of a bright wall's.
            sample.Value = new(x: 0, y: 0, z: 0, w: 1);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 30);
            const float bright = 16;
            sample.Samples = (column, row) =>
                new Vector4(new Vector3(column == Width / 2 && row == Height / 2 ? bright : 0), 1);

            Continue(renderer, scene, frames: 1);

            float[] red = FilteredRed(renderer, filter);
            // The history of 16 frames takes the bright sample with the weight 1 / 16.
            const float light = bright / 16;
            red.Sum().Should().BeApproximately(light, 0.02f * light, "the filter only moves light");
            red[Height / 2 * Width + Width / 2]
                .Should()
                .BeLessThan(0.25f * light, "the bright pixel's light spreads over its neighbors");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void LightThatMovesIntoAQuietRegionComesFromItsNoisyNeighbors()
    {
        // The left half holds a gradient without noise, so that no two of its pixels weigh each other, and the right half
        // a noisy light. A filter that divided each pixel by its own weights would give a left pixel next to the noise
        // nearly the noise's light, and take it from no one. The slow history holds the first step's light, and every
        // later step must keep its total.
        int frame = 0;
        Func<int, int, Vector4> flicker = Flicker(() => frame);
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = (column, row) =>
                column < Width / 2 ? new(new Vector3(0.1f + 0.002f * column + 0.001f * row), 1) : flicker(column, row);

            for (frame = 0; frame < 40; frame++)
                Continue(renderer, scene, frames: 1);

            float[] history = renderer.ReadImage(sample.Signal.Slow(0));
            float light = Enumerable.Range(0, Width * Height).Sum(pixel => history[4 * pixel]);
            FilteredRed(renderer, filter)
                .Sum()
                .Should()
                .BeApproximately(light, 0.001f * light, "the later steps only move light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnImageWithoutNoiseKeepsItsFineDetail()
    {
        // A pattern of a few pixels per period, which a blur over a few pixels flattens by more than 0.01, from 0.25 to
        // 0.5, where a half float's step is 2^−12.
        static Vector4 Light(int column, int row) =>
            new(new Vector3(0.375f + 0.12f * MathF.Sin(column / 2f) * MathF.Cos(row / 2f)), 1);
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(Wall);
        (VulkanRenderer unfiltered, SamplePass unfilteredSample, _, RenderSnapshot unfilteredScene) = Filtering(
            filtered: false,
            DiffuseFilterKernel.Thin,
            Wall
        );
        try
        {
            sample.Samples = Light;
            unfilteredSample.Samples = Light;
            renderer.Render(scene, 1f);
            unfiltered.Render(unfilteredScene, 1f);

            // The first frames estimate their noise from their neighbors, so the filter softens them, and the history
            // forgets that by 15 / 16 in each frame.
            Continue(renderer, scene, frames: 240);
            Continue(unfiltered, unfilteredScene, frames: 240);

            float[] red = FilteredRed(renderer, filter);
            float[] history = unfiltered.ReadImage(unfilteredSample.Signal.Slow(0));
            // Each history's blend stops within 16 half-float steps of the sample, 4e-3, because a store into a half float
            // may round toward zero, so the two histories can differ by that.
            for (int pixel = 0; pixel < Width * Height; pixel++)
                red[pixel].Should().BeApproximately(history[4 * pixel], 4.5e-3f, "pixel {0} saw no noise", pixel);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
            Gpu.DisposeAndAssertClean(unfiltered);
        }
    }

    [Fact]
    public void NoLightCrossesADepthEdge()
    {
        // The near box covers the left half of the view: its right edge, at x = 0, lies between the middle columns.
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(
            Wall,
            (new(-10, 0, -10), 0, new(20, 40, 1))
        );
        try
        {
            int frame = 0;
            Func<int, int, Vector4> flicker = Flicker(() => frame);
            sample.Samples = (column, row) =>
                column < Width / 2 ? new Vector4(x: 0, y: 0, z: 0, w: 1) : flicker(column, row);

            for (frame = 0; frame < 30; frame++)
                Continue(renderer, scene, frames: 1);

            float[] red = FilteredRed(renderer, filter);
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width / 2; column++)
                red[row * Width + column]
                    .Should()
                    .BeLessThan(1e-4f, "the near pixel ({0}, {1}) saw only darkness", column, row);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Two walls that meet at x = 0, 20 m before the eye, each turned by <paramref name="turn"/> toward the eye, whose
    /// seam lies between the middle columns.
    /// </summary>
    private static (Vector3 Center, float Turn, Vector3 Scale)[] Crease(float turn)
    {
        var left = new Vector3(-MathF.Cos(turn), 0, MathF.Sin(turn));
        var right = new Vector3(MathF.Cos(turn), 0, MathF.Sin(turn));
        var seam = new Vector3(0, 0, -20);
        return
        [
            (seam + 30 * left - 0.5f * new Vector3(MathF.Sin(turn), 0, MathF.Cos(turn)), turn, new(60, 200, 1)),
            (seam + 30 * right - 0.5f * new Vector3(-MathF.Sin(turn), 0, MathF.Cos(turn)), -turn, new(60, 200, 1)),
        ];
    }

    [Fact]
    public void NoLightCrossesAShallowCrease()
    {
        // Each wall turns by 15°, so their normals differ by 30°.
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(
            Crease(MathF.PI / 12)
        );
        try
        {
            int frame = 0;
            Func<int, int, Vector4> flicker = Flicker(() => frame);
            sample.Samples = (column, row) =>
                column < Width / 2 ? new Vector4(x: 0, y: 0, z: 0, w: 1) : flicker(column, row);

            for (frame = 0; frame < 30; frame++)
                Continue(renderer, scene, frames: 1);

            float[] red = FilteredRed(renderer, filter);
            for (int row = 0; row < Height; row++)
            for (int column = 0; column < Width / 2; column++)
                red[row * Width + column]
                    .Should()
                    .BeLessThan(1e-4f, "the left wall's pixel ({0}, {1}) saw only darkness", column, row);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ARaysHitReadsTheFilteredDiffuseLight()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            // A white wall and floor in the sun: a diffuse ray from the floor hits the sunlit wall or misses, so the
            // diffuse samples are noisy and the filtered light differs from the fast history.
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            MaterialId white = renderer.CreateMaterial(MaterialDesc.Color(Vector4.One, roughness: 0.5f));
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: new(0, 0, -10),
                    scale: new(60, 40, 1),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 1
                )
            );
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: white,
                    position: new(0, -2, -10),
                    scale: new(60, 1, 40),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 2
                )
            );
            var probe = ScreenTraceProbe.On(renderer, "diffuse_hit_history_probe.comp");
            renderer.Render(scene, 1f);

            Continue(renderer, scene, frames: 12);

            float[] history = probe.Read();
            DiffuseFilter filter = renderer.ScreenTraceImages.Filter!;
            float[] filtered = renderer.ReadImage(filter.Filtered);
            float[] fast = renderer.ReadImage(
                renderer.ScreenTraceSignals.Diffuse.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1))
            );
            int differing = 0;
            for (int at = 0; at < 4 * Width * Height; at += 4)
            {
                history[at].Should().BeApproximately(filtered[at], 2e-3f + 2e-3f * filtered[at], "texel {0}", at / 4);
                if (MathF.Abs(filtered[at] - fast[at]) > 0.05f * filtered[at])
                    differing++;
            }
            differing.Should().BePositive("the scene's noise makes the filtered light differ from the fast history");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheNextFramesHistoryHoldsTheFirstStepsLight()
    {
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Value = new(x: 0, y: 0, z: 0, w: 1);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 30);
            const float bright = 16;
            sample.Samples = (column, row) =>
                new Vector4(new Vector3(column == Width / 2 && row == Height / 2 ? bright : 0), 1);

            Continue(renderer, scene, frames: 1);

            float[] history = renderer.ReadImage(sample.Signal.Slow(0));
            int center = Height / 2 * Width + Width / 2;
            // Unfiltered, the history of 16 frames holds bright / 16 = 1 at the bright pixel and 0 around it. The first
            // step's kernel gives the pixel at most its own weight, (1 / 2)², and its neighbor some of the light.
            history[4 * center]
                .Should()
                .BeLessThan(0.5f, "the history holds the first step's light, not the sample's");
            history[4 * center]
                .Should()
                .BeGreaterThan(0.1f, "the later steps, which spread the light far wider, do not write the history");
            history[4 * (center + 1)].Should().BePositive("the first step moved light to the neighbor");
            history[4 * center + 3].Should().Be(16, "the feedback keeps the history's length");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AYoungHistoryTakesItsNoiseFromItsNeighbors()
    {
        // In the first frame, every history is one sample long, and its fast history holds no deviation yet.
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = Flicker(() => 0);

            renderer.Render(scene, 1f);

            float[] red = FilteredRed(renderer, filter);
            float mean = red.Average();
            float spread = red.Average(light => MathF.Abs(light - mean));
            spread.Should().BeLessThan(0.2f, "the filter smooths the checker of 0 and 2, whose spread is 1");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AfterAStepOfASampleWithoutNoiseTheRetainedDeviationHalvesWithin45DrawsOfTheHistoryReachingIt()
    {
        // The step of 0.0505 counts as spread until the history of 32 draws comes within 32 / 768 of its light, 0.0104,
        // after ln(0.0104 / 0.0505) / ln(31 / 32) ≈ 50 draws. From then on the history's last half-float steps toward
        // the sample are the store's rounding, and the variance keeps 31 / 32 a draw: the deviation halves in 44 draws.
        // Counted as spread, they would keep a deviation of their own size.
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = FilteringBounceLight();
        try
        {
            sample.Value = new(x: 0.2f, y: 0.2f, z: 0.2f, w: 1);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 40);
            sample.Value = new(x: 0.2505f, y: 0.2505f, z: 0.2505f, w: 1);
            // The history reaches the step by about draw 51, and 60 leaves a margin.
            Continue(renderer, scene, frames: 60);
            float[] reached = RetainedDeviationOf(renderer, sample);
            reached[Center].Should().BePositive("the step spread the samples");

            Continue(renderer, scene, frames: 45);

            float[] after = RetainedDeviationOf(renderer, sample);
            for (int pixel = 0; pixel < Width * Height; pixel++)
                after[pixel]
                    .Should()
                    .BeLessThanOrEqualTo(0.5f * reached[pixel], "pixel {0} saw one constant sample since", pixel);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFeedbackStepStoresTheDeviationOfTheLightThatItMoves()
    {
        // The left half holds the noisy checker's mean of 1 without noise, so the luminance stop lets the noisy right
        // half's light across the middle. The quiet pixel left of the middle takes the share 0.0625 + 0.125 + 0.0625 of
        // each of its three noisy taps' light, and so as much of their deviation.
        int frame = 0;
        Func<int, int, Vector4> flicker = Flicker(() => frame);
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = (column, row) => column < Width / 2 ? new(Vector3.One, 1) : flicker(column, row);

            for (frame = 0; frame < 40; frame++)
                Continue(renderer, scene, frames: 1);

            float[] deviation = RetainedDeviationOf(renderer, sample);
            float noisy = deviation[Center + 8];
            noisy.Should().BeGreaterThan(0.5f, "the checker's samples deviate by 1");
            deviation[Center - 1]
                .Should()
                .BeGreaterThanOrEqualTo(0.125f * noisy, "the quiet pixel took light from its noisy taps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AYoungHistoryKeepsItsOwnRetainedDeviation()
    {
        // After three samples of the checker, each history holds 0, 2 and 0 or 2, 0 and 2: its own deviation is
        // √(8 / 9) ≈ 0.943. Its young estimate, the spread of the fast histories of 2 / 3 and 4 / 3 over 7 × 7 pixels
        // raised by √(4 / 3), is 0.385, and it holds the light's own edges, so the store never takes it.
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = Flicker(() => frame);

            for (frame = 0; frame < 3; frame++)
                Continue(renderer, scene, frames: 1);

            RetainedDeviationOf(renderer, sample)[Center]
                .Should()
                .BeApproximately(MathF.Sqrt(8f / 9), 0.01f, "the history's own samples deviate so");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Whether the pixel at <paramref name="column"/> and <paramref name="row"/> lies in the ray block at the center: the
    /// 2 × 2 pixels at even coordinates that share one ray.
    /// </summary>
    private static bool InCenterRayBlock(int column, int row) => column / 2 == Width / 4 && row / 2 == Height / 4;

    /// <summary>
    /// Draws a mature dark wall of the bounce light's parameter set, then one draw in which the center's ray block holds
    /// the sample 100, then dark samples again. A dark wall, so that a store into a half float, which may round toward
    /// zero, loses at most 2^−10 of the excess, not of a bright wall's light.
    /// </summary>
    private static void DrawABrightRayBlockSample(VulkanRenderer renderer, SamplePass sample, RenderSnapshot scene)
    {
        sample.Value = new(x: 0, y: 0, z: 0, w: 1);
        renderer.Render(scene, 1f);
        Continue(renderer, scene, frames: 40);
        sample.Samples = (column, row) => new Vector4(new Vector3(InCenterRayBlock(column, row) ? 100 : 0), 1);
        Continue(renderer, scene, frames: 1);
        sample.Samples = null;
    }

    [Fact]
    public void ASingleBrightRayBlockSampleNeverGrowsMoreConspicuousOverThe32DrawsAfterIt()
    {
        // A luminance stop whose deviation shrinks faster than the light that it describes concentrates the old excess
        // again: the peak grew 42 times in the CPU model (docs/plans/bounce-light-confidence.md, section 7). Carried
        // with the light, the deviation lets the peak rise by 1.7 % in the next draw and then only fall.
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) =
            FilteringBounceLight();
        try
        {
            DrawABrightRayBlockSample(renderer, sample, scene);
            float first = FilteredRed(renderer, filter).Max();
            first.Should().BePositive("the filter shows the sample's excess");

            float most = first;
            float last = first;
            for (int draw = 1; draw <= 32; draw++)
            {
                Continue(renderer, scene, frames: 1);
                last = FilteredRed(renderer, filter).Max();
                most = MathF.Max(most, last);
            }

            output.WriteLine($"Peak excess: {first} at the sample's draw, {most} at most, {last} after 32 draws.");
            most.Should().BeLessThanOrEqualTo(1.05f * first, "the filter only spreads the excess");
            last.Should().BeLessThan(0.5f * first, "the slow blend and the filter shrink the excess");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheExcessOfASingleBrightRayBlockSampleFadesAtTheSlowBlendsPace()
    {
        // The four pixels hold 4 · 100 / 32 = 12.5 at the sample's draw. The filter moves that light and makes none, and
        // each draw keeps 31 / 32 of it, as long as no clamp cuts the light that the filter moved into a quiet pixel. The
        // accumulation's two stores and the feedback's one may each round the light toward zero.
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = FilteringBounceLight();
        try
        {
            DrawABrightRayBlockSample(renderer, sample, scene);

            for (int draw = 0; draw <= 32; draw++)
            {
                if (draw > 0)
                    Continue(renderer, scene, frames: 1);
                float[] history = renderer.ReadImage(sample.Signal.Slow(0));
                float excess = Enumerable.Range(0, Width * Height).Sum(pixel => history[4 * pixel]);
                float expected = 12.5f * MathF.Pow(31f / 32, draw);
                float rounding = 3 * (draw + 1) * MathF.Pow(2, -10);
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

    /// <summary>
    /// A hash of the ray block at <paramref name="column"/> and <paramref name="row"/> and of <paramref name="frame"/>,
    /// which spreads a pattern over the image and over the frames.
    /// </summary>
    private static uint RayBlockHash(int column, int row, int frame)
    {
        uint hash = (uint)(column / 2 * 73856093 ^ row / 2 * 19349663 ^ frame * 83492791);
        hash ^= hash >> 13;
        hash *= 0x5bd1e995;
        hash ^= hash >> 15;
        return hash;
    }

    /// <summary>
    /// Whether the ray block at <paramref name="column"/> and <paramref name="row"/> takes a rare bright sample in
    /// <paramref name="frame"/>: one ray block in 32.
    /// </summary>
    private static bool RareAt(int column, int row, int frame) => RayBlockHash(column, row, frame) % 32 == 0;

    [Fact]
    public void AQuietPixelKeepsTheLightThatTheFilterMovedIntoIt()
    {
        // One ray block in 32 takes 33 in a draw, the others 1. The filter moves a rare sample's light into the quiet
        // pixels around it, whose own fast histories form a narrow box. The retained deviation moves with the light, so
        // the clamp never moves a history (ticket td-hjn6), and every history keeps its cap of 32: the image's history
        // follows its samples' blend. The slow history's three half-float stores a draw may each round toward zero, as
        // the spec allows, and lose half a half-float step on average, 2^−11 of the light: 3 · 32 · 2^−11 ≈ 4.7 % at the
        // blend's weight. This GPU loses 1.8 %, and 0.9 % without the filter's store. The rare samples start with the
        // histories: a step from steady light to them raises the expected light, which the block test rightly finds. While
        // the histories first fill, their slow light is no mean of 32 draws yet, and blocks may find that difference: so
        // the test begins 96 draws after them, past the 28 draws in which a cut history grows back to its cap.
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = FilteringBounceLight();
        try
        {
            int drawn = -96;
            sample.Samples = (column, row) => new Vector4(new Vector3(RareAt(column, row, drawn) ? 33 : 1), 1);
            renderer.Render(scene, 1f);
            for (drawn = -95; drawn < 0; drawn++)
                Continue(renderer, scene, frames: 1);
            float[] warm = renderer.ReadImage(sample.Signal.Slow(0));
            double expected = Enumerable.Range(0, Width * Height).Average(pixel => (double)warm[4 * pixel]);
            double shown = 0;
            for (int frame = 0; frame < 164; frame++)
            {
                drawn = frame;
                int rare = Enumerable
                    .Range(0, Width * Height)
                    .Count(pixel => RareAt(pixel % Width, pixel / Width, drawn));
                double mean = 1 + 32.0 * rare / (Width * Height);
                // Every history holds 32 draws, so the image's mean blends its samples' mean with the weight 1 / 32.
                expected += (mean - expected) / 32;
                Continue(renderer, scene, frames: 1);
                float[] history = renderer.ReadImage(sample.Signal.Slow(0));
                Enumerable
                    .Range(0, Width * Height)
                    .Count(pixel => history[4 * pixel + 3] != 32)
                    .Should()
                    .Be(0, "the clamp moved no history in draw {0}", frame);
                if (frame >= 64)
                    shown +=
                        Enumerable.Range(0, Width * Height).Average(pixel => (double)history[4 * pixel])
                        / expected
                        / 100;
            }

            output.WriteLine($"The image's history over its samples' blend: {shown:F4}.");
            shown.Should().BeInRange(1 - 0.047, 1.01, "the filter moves light and the clamp cuts none of it");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AStableEdgeOfLightUnderNoiseKeepsItsWidth()
    {
        // Halves of light 1 and 2, each ray block's sample 50 % above or below its half's light in each draw. A deviation
        // carried by the shares in every step widened the CPU model's 10 to 90 % rise to 32 pixels; with the paper's rule
        // after the feedback step it stays near 12 (docs/plans/bounce-light-confidence.md, section 7). The design's bound
        // was 16 pixels. The CPU model with this test's image and measure gives 15, this GPU 17 to 18, and the filter
        // before the retained deviation at most 16: 20 holds the measured width until the user decides (t0-anp3).
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) =
            FilteringBounceLight();
        try
        {
            sample.Samples = (column, row) =>
            {
                float light = column < Width / 2 ? 1 : 2;
                float noise = (RayBlockHash(column, row, frame) & 0x100) == 0 ? 0.5f : -0.5f;
                return new Vector4(new Vector3(light * (1 + noise)), 1);
            };

            float[] profile = new float[Width];
            for (frame = 0; frame < 200; frame++)
            {
                Continue(renderer, scene, frames: 1);
                if (frame < 168)
                    continue;
                float[] red = FilteredRed(renderer, filter);
                for (int row = 0; row < Height; row++)
                for (int column = 0; column < Width; column++)
                    profile[column] += red[row * Width + column] / (32 * Height);
            }

            int low = Array.FindLastIndex(profile, light => light <= 1.1f);
            int high = Array.FindIndex(profile, light => light >= 1.9f);
            output.WriteLine($"10 to 90 % rise: {high - low} pixels.");
            (high - low).Should().BeLessThanOrEqualTo(20, "the luminance stop holds the edge of real light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AStillBandOfLightThatTheFilterBlursIsNoEvidenceOfAChange()
    {
        // A band of light 4, one block wide, on a wall of light 1, each ray block's sample spread with a coefficient of
        // variation of 0.3. The filter's feedback step blurs the band into its neighbors and writes that light into the
        // slow history, so the band's slow history lies well below the mean of its own samples, and its fast history
        // above it. The block test compares the fast history with that own-sample mean, which the filter never writes
        // (docs/plans/bounce-light-confidence.md, section 8). Compared with the slow history, the band's block fired in
        // every draw. The count starts after the histories have filled to their cap of 32.
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = FilteringBounceLight();
        try
        {
            sample.Samples = (column, row) =>
            {
                float uniform = RayBlockHash(column, row, frame) / (float)uint.MaxValue * 2 - 1;
                float light = column is >= Width / 2 and < Width / 2 + 8 ? 4 : 1;
                return new Vector4(new Vector3(light * (1 + MathF.Sqrt(3) * 0.3f * uniform)), 1);
            };

            int cut = 0;
            for (frame = 0; frame < 300; frame++)
            {
                Continue(renderer, scene, frames: 1);
                if (frame >= 100)
                    cut += BlocksCutWhole(renderer.ReadImage(sample.Signal.Slow(0)));
            }

            cut.Should().Be(0, "the filter's blur of a still band of light is no change of its light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The blocks of 8 × 8 pixels inside the image whose every pixel has a history length of at most the fast cap of 4
    /// in <paramref name="slow"/>, a slow history read row by row from the top: the blocks whose histories the block
    /// test's evidence cut at once. The anti-lag cuts single pixels.
    /// </summary>
    private static int BlocksCutWhole(float[] slow)
    {
        int cut = 0;
        for (int top = 0; top + 8 <= Height; top += 8)
        for (int left = 0; left + 8 <= Width; left += 8)
        {
            bool allCut = true;
            for (int y = top; y < top + 8; y++)
            for (int x = left; x < left + 8; x++)
                allCut &= slow[4 * (y * Width + x) + 3] is > 0 and <= 4;
            cut += allCut ? 1 : 0;
        }
        return cut;
    }

    /// <summary>The kernels by name, and how far each one's steps together reach along an axis, in pixels.</summary>
    public static TheoryData<string, int> Reaches =>
        new()
        {
            // 1 + 2 + 4 + 8 + 16 + 32: six steps of one tap to each side.
            { nameof(DiffuseFilterKernel.Thin), 63 },
            // 2 × (1 + 2 + 4 + 8 + 16): five steps of two taps to each side.
            { nameof(DiffuseFilterKernel.Wide), 62 },
        };

    private static DiffuseFilterKernel KernelNamed(string name) =>
        name switch
        {
            nameof(DiffuseFilterKernel.Thin) => DiffuseFilterKernel.Thin,
            nameof(DiffuseFilterKernel.Wide) => DiffuseFilterKernel.Wide,
            _ => throw new ArgumentException($"No kernel is named {name}.", nameof(name)),
        };

    [Theory]
    [MemberData(nameof(Reaches))]
    public void ASpecksLightReachesAsFarAsTheKernelsStepsTogetherAndNoFarther(string kernel, int reach)
    {
        // The speck is a column in the image's left half, so that its reach and one pixel more lie inside the image. A
        // column, so that the light that reaches the farthest pixel adds up over its rows to a normal half float: the
        // wide kernel's outer taps alone weigh 0.0625^5 ≈ 1e-6, below which a driver may flush a half float to 0.
        const int speck = Width / 2 - 1;
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(
            filtered: true,
            KernelNamed(kernel),
            Wall
        );
        try
        {
            sample.Value = new(x: 0, y: 0, z: 0, w: 1);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 30);
            sample.Samples = (column, row) => new Vector4(new Vector3(column == speck ? 4000 : 0), 1);

            Continue(renderer, scene, frames: 1);

            float[] red = FilteredRed(renderer, filter);
            int row = Height / 2 * Width;
            red[row + speck + reach]
                .Should()
                .BePositive("the outer taps of every step together reach {0} pixels", reach);
            red[row + speck + reach + 1].Should().Be(0, "no chain of taps reaches past {0} pixels", reach);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Light 0 or 2 at every pixel, the same in a frame and the other in the next: noise without detail.</summary>
    private static Func<int, int, Vector4> Blink(Func<int> frame) =>
        (_, _) => new Vector4(new Vector3((frame() & 1) * 2), 1);

    [Fact]
    public void TheRetainedDeviationIsTheDeviationOfTheSlowHistorysSamples()
    {
        // Samples of 0 and 2 in turn have a deviation of 1. Each step goes from the fast history, which settles between
        // 0.857 and 1.143, so it is ±1.143, and the variance (1 − w)(variance + w step²) with the slow history's weight
        // w = 1 / 16 settles at (1 − w) step²: a deviation of 1.107.
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = Blink(() => frame);

            for (frame = 0; frame < 60; frame++)
                Continue(renderer, scene, frames: 1);

            RetainedDeviationOf(renderer, sample)[Center]
                .Should()
                .BeApproximately(1.107f, 0.03f, "the samples deviate by 1 from their mean");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFilteredDeviationIsThatOfAMeanOfItsTaps()
    {
        // Every pixel holds the same light. The feedback step carries the deviation by the shares and keeps the rest,
        // so a uniform deviation stays as it is. A later step takes the deviation of a weighted mean of independent
        // values with the weights w, √(Σ w²) of one value: (6 / 16)² for the plain 3 × 3 B-spline, so 0.375 in each
        // step, and more where a stop keeps weight with the pixel. At the center, every tap of every step lies inside the
        // image. The rule of the feedback step in every step would keep the deviation at 1.
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = Blink(() => frame);

            for (frame = 0; frame < 40; frame++)
                Continue(renderer, scene, frames: 1);

            float before = RetainedDeviationOf(renderer, sample)[Center];
            float after = renderer.ReadImage(filter.Filtered)[4 * Center + 3];
            after
                .Should()
                .BeInRange(
                    MathF.Pow(0.375f, 5) * before,
                    MathF.Pow(0.5f, 5) * before,
                    "five means of 9 taps divide the deviation that the feedback step keeps"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void LightThatCrossesAGentleCreaseKeepsItsTotal()
    {
        // Each wall turns by 3°, so the normal stop lets half the weight through, and a tap's distance from the other
        // wall's plane differs from the other's distance from its own: only a stop that takes both keeps the shares the
        // same both ways. The bright, noisy column on the right wall's edge sends light across the seam.
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(
            Crease(MathF.PI / 60)
        );
        try
        {
            sample.Samples = (column, row) =>
                new Vector4(new Vector3(column == Width / 2 ? ((row + frame) & 1) * 32 : 0), 1);

            for (frame = 0; frame < 30; frame++)
                Continue(renderer, scene, frames: 1);

            float[] history = renderer.ReadImage(sample.Signal.Slow(0));
            float light = Enumerable.Range(0, Width * Height).Sum(pixel => history[4 * pixel]);
            // All of this light moves, and each of the five later steps stores it in half floats, which may round toward
            // zero by up to 2^−10 of a value: 5 × 2^−10 ≈ 0.0049 in all.
            FilteredRed(renderer, filter)
                .Sum()
                .Should()
                .BeApproximately(light, 0.006f * light, "the later steps only move light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AYoungHistoryTakesNoNoiseFromAnotherSurface()
    {
        // In the first frame, the near box on the left holds 0.5 and the far wall 1, each without noise. The young
        // estimate looks only at the pixel's own surface, so it finds no noise at the depth edge either.
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(
            Wall,
            (new(-10, 0, -10), 0, new(20, 40, 1))
        );
        try
        {
            sample.Samples = (column, _) => new Vector4(new Vector3(column < Width / 2 ? 0.5f : 1), 1);

            renderer.Render(scene, 1f);

            float[] filtered = renderer.ReadImage(filter.Filtered);
            for (int pixel = 0; pixel < Width * Height; pixel++)
                filtered[4 * pixel + 3].Should().Be(0, "pixel {0} saw no noise on its surface", pixel);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnEdgeOfLightKeepsItsShapeBesideNoise()
    {
        // The left half holds 0 without noise, and the right half 4 with a little noise, 3.9 and 4.1 in turn. Their
        // difference of 4 is about 40 deviations of the difference of two pixels across the edge, so the luminance stop
        // lets almost no light across. In the first frames, the young estimate finds the edge's own difference as noise on
        // the one wall and lets light across, which the history forgets by 15 / 16 in each frame.
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = (column, row) =>
                new Vector4(new Vector3(column < Width / 2 ? 0 : 3.9f + 0.2f * ((column + row + frame) & 1)), 1);

            for (frame = 0; frame < 200; frame++)
                Continue(renderer, scene, frames: 1);

            float[] red = FilteredRed(renderer, filter);
            for (int row = 0; row < Height; row++)
                red[row * Width + Width / 2 - 1]
                    .Should()
                    .BeLessThan(0.01f, "the dark pixel ({0}, {1}) beside the edge saw no noise", Width / 2 - 1, row);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AResizedRendererFiltersAtItsNewSize()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            screenTrace: ScreenTrace.Default,
            warmUpFrames: 0
        );
        try
        {
            MeshId cube = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            RenderSnapshot scene = Gpu.Snapshot(new(0.3f, 1f, 0.4f));
            scene.Instances.Add(
                Gpu.Instance(
                    mesh: cube,
                    material: MaterialId.None,
                    position: new(0, 0, -10),
                    scale: new(60, 40, 1),
                    chainDepth: TestChains.CamerasFrame,
                    entity: 1
                )
            );
            renderer.Render(scene, 1f);

            renderer.Resize(Width / 2, Height / 2);
            Continue(renderer, scene, frames: 2);

            Extent2D extent = renderer.ScreenTraceImages.Filter!.Filtered.Extent;
            (extent.Width, extent.Height).Should().Be(((uint)Width / 2, (uint)Height / 2));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }
}
