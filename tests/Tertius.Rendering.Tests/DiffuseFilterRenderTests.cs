using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The diffuse filter (ticket ter-9371, <see cref="DiffuseFilter"/>): it spreads a bright sample over its neighbors on the
/// same surface and keeps its light, it lets no light cross a depth edge or a crease, it leaves a smooth image without
/// noise as it is, and a ray's hit reads its output. A test signal and its producer stand for the diffuse signal, and the
/// filter runs after their accumulation. The camera stands still, and each frame steps one tick.
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
    /// The renderer with the test signal, its producer, its accumulation and its filter after the direct pass, before the
    /// white boxes of <paramref name="boxes"/>, each a center, a rotation about +Y in radians and a scale.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, DiffuseFilter Filter, RenderSnapshot Scene) Filtering(
        params (Vector3 Center, float Turn, Vector3 Scale)[] boxes
    ) => Filtering(filtered: true, boxes);

    /// <summary>
    /// The renderer of <see cref="Filtering(ValueTuple{Vector3, float, Vector3}[])"/>, whose filter runs only when
    /// <paramref name="filtered"/>.
    /// </summary>
    private static (VulkanRenderer Renderer, SamplePass Sample, DiffuseFilter Filter, RenderSnapshot Scene) Filtering(
        bool filtered,
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
        DiffuseFilter filter = renderer.CreateDiffuseFilter(signal);
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
        float[] filtered = renderer.ReadImage(filter.Filtered);
        var red = new float[Width * Height];
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
                red[pixel]
                    .Should()
                    .BeApproximately(history[4 * pixel], 4.5e-3f, "pixel {0} saw no noise", pixel);
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
            sample.Samples = (column, row) => column < Width / 2 ? new Vector4(x: 0, y: 0, z: 0, w: 1) : flicker(column, row);

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

    [Fact]
    public void NoLightCrossesAShallowCrease()
    {
        // Two walls meet at x = 0, 20 m before the eye, each turned by 15° toward the eye: their normals differ by 30°,
        // and their seam lies between the middle columns.
        const float turn = MathF.PI / 12;
        var left = new Vector3(-MathF.Cos(turn), 0, MathF.Sin(turn));
        var right = new Vector3(MathF.Cos(turn), 0, MathF.Sin(turn));
        var seam = new Vector3(0, 0, -20);
        (VulkanRenderer renderer, SamplePass sample, DiffuseFilter filter, RenderSnapshot scene) = Filtering(
            (seam + 30 * left - 0.5f * new Vector3(MathF.Sin(turn), 0, MathF.Cos(turn)), turn, new(60, 200, 1)),
            (seam + 30 * right - 0.5f * new Vector3(-MathF.Sin(turn), 0, MathF.Cos(turn)), -turn, new(60, 200, 1))
        );
        try
        {
            int frame = 0;
            Func<int, int, Vector4> flicker = Flicker(() => frame);
            sample.Samples = (column, row) => column < Width / 2 ? new Vector4(x: 0, y: 0, z: 0, w: 1) : flicker(column, row);

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
}
