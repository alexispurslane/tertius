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
public sealed class DiffuseFilterRenderTests
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
        TemporalSignal signal = renderer.CreateSignal(parameters: Parameters, name: "test", parts: 1);
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
    public void ASampleWithoutNoiseKeepsNoDeviationAfterAStep()
    {
        // After the step, the fast history's blend stops a few half-float steps below the sample, up to four where the
        // store rounds toward zero, which the spec allows: rounding, not noise. Just above a power of two, a half-float
        // step is the largest share of the light, 2^−10.
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Value = new(x: 0.2f, y: 0.2f, z: 0.2f, w: 1);
            renderer.Render(scene, 1f);
            Continue(renderer, scene, frames: 10);
            sample.Value = new(x: 0.2505f, y: 0.2505f, z: 0.2505f, w: 1);

            // The deviation of the step falls by √(3 / 4) a frame, and below the least half float after about 100 frames.
            Continue(renderer, scene, frames: 150);

            float[] fast = renderer.ReadImage(sample.Signal.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1));
            for (int pixel = 0; pixel < Width * Height; pixel++)
                fast[4 * pixel + 3].Should().Be(0, "pixel {0} saw one constant sample for 150 frames", pixel);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
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
    public void TheFastHistoryKeepsTheDeviationOfItsSamples()
    {
        // Samples of 0 and 2 in turn have a deviation of 1. The fast history settles between 0.857 and 1.143, so each
        // step is ±1.143, and the weighted variance (1 − w)(variance + w step²) at w = 1 / 4 settles at 0.98.
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, _, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = Blink(() => frame);

            for (frame = 0; frame < 40; frame++)
                Continue(renderer, scene, frames: 1);

            float[] fast = renderer.ReadImage(sample.Signal.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1));
            fast[4 * (Height / 2 * Width + Width / 2) + 3]
                .Should()
                .BeApproximately(0.99f, 0.1f, "the samples deviate by 1");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheFilteredDeviationIsThatOfAMeanOfItsTaps()
    {
        // Every pixel holds the same light, so every stop is 1 and each step takes the plain kernel. A weighted mean of
        // independent values with the weights w has the deviation √(Σ w²) of one value: (6 / 16)² for the 3 × 3
        // B-spline, so a deviation of 0.375 in each step and 0.0028 after six. Taps near the border lose weight, which
        // keeps their deviation higher.
        int frame = 0;
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = Blink(() => frame);

            for (frame = 0; frame < 40; frame++)
                Continue(renderer, scene, frames: 1);

            int center = Height / 2 * Width + Width / 2;
            float before = renderer.ReadImage(sample.Signal.Fast(part: 0, parity: (int)(renderer.DrawnFrames & 1) ^ 1))[
                4 * center + 3
            ];
            float after = renderer.ReadImage(filter.Filtered)[4 * center + 3];
            after.Should().BeInRange(0.0005f * before, 0.01f * before, "six means of 9 taps divide the deviation");
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

    [Theory]
    [InlineData(0.3f)]
    [InlineData(7.77f)]
    [InlineData(123.456f)]
    public void AYoungHistoryOfOneLuminanceFindsNoNoiseWhateverItsValue(float value)
    {
        // A wall that holds one value without noise has a variance of exactly 0. The estimate must find 0 in float
        // arithmetic as well: a mean square minus a squared mean cancels exactly only where the driver's rounding happens
        // to make the two equal. Apple's does for 0.5 and 1 but not for 7.77, and NVIDIA's not for 1 (ticket t8-kpoe).
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(Wall);
        try
        {
            sample.Samples = (_, _) => new Vector4(new Vector3(value), 1);

            renderer.Render(scene, 1f);

            float[] filtered = renderer.ReadImage(filter.Filtered);
            for (int pixel = 0; pixel < Width * Height; pixel++)
                filtered[4 * pixel + 3].Should().Be(0, "pixel {0} saw no noise on a wall of one value", pixel);
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
