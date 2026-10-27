using System.Diagnostics;
using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

[Collection(GpuCollection.Name)]
public sealed class StressTests(ITestOutputHelper output)
{
    /// <summary>A 200 x 100 field of objects on a gentle wave, from 4 m to 400 m in front of the camera.</summary>
    private static RenderSnapshot Field(VulkanRenderer renderer, RenderFlags flags, Vector3? sun = null)
    {
        MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
        MeshId sphere = renderer.CreateMesh(Primitives.Sphere(0.5f, 16, 8));
        MaterialId[] materials =
        [
            renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.8f, y: 0.2f, z: 0.2f, w: 1), roughness: 0.26f)),
            renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.2f, y: 0.7f, z: 0.3f, w: 1), roughness: 0.55f)),
            renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.2f, y: 0.3f, z: 0.8f, w: 1), roughness: 0.38f)),
            renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.8f, y: 0.7f, z: 0.2f, w: 1), roughness: 0.18f)),
        ];
        RenderSnapshot snapshot = Gpu.Snapshot(sun ?? new Vector3(0.4f, 0.8f, 0.45f));
        const int columns = 200,
            rows = 100;
        for (int row = 0; row < rows; row++)
        for (int column = 0; column < columns; column++)
        {
            int i = row * columns + column;
            float x = (column - columns / 2) * 2.2f;
            float z = -4f - row * 4f;
            float y = -3f + MathF.Sin(column * 0.3f) * MathF.Cos(row * 0.2f);
            RenderInstance instance = Gpu.Instance(
                mesh: i % 3 == 0 ? sphere : box,
                material: materials[i % 4],
                position: new(x, y, z),
                scale: new(1.4f),
                rotation: Quaternion.CreateFromYawPitchRoll(i * 0.37f, i * 0.11f, 0),
                flags: flags,
                entity: (uint)i,
                chainDepth: TestChains.CamerasFrame
            );
            snapshot.Instances.Add(instance);
        }

        return snapshot;
    }

    [Fact]
    public void TwentyThousandInstancesDrawWithABoundedNumberOfIndirectDraws()
    {
        const int width = 1280,
            height = 720;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            RenderSnapshot snapshot = Field(renderer, RenderFlags.None);
            snapshot.Instances.Should().HaveCount(20_000);

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "twenty-thousand");
            RenderStats stats = renderer.LastFrameStats;
            output.WriteLine($"{stats}");
            stats.SubmittedInstances.Should().Be(20_000);
            stats.VisibleInstances.Should().BeGreaterThan(8_000);
            // All visible objects are in the near range: one command per mesh, one draw call.
            stats.IndirectCommands.Should().Be(2);
            stats.IndirectDrawCalls.Should().Be(1);

            // A large part of the lower half of the screen is covered by objects.
            int covered = 0,
                samples = 0;
            for (int y = height / 2 + 20; y < height; y += 4)
            for (int x = 0; x < width; x += 4)
            {
                samples++;
                if (image.Luma(x, y) > 0.08f)
                    covered++;
            }

            covered
                .Should()
                .BeGreaterThan(samples / 3, "the field covers much of the lower half ({0} samples)", samples);

            // Steady-state frame time, for information; correctness does not depend on it.
            LogFrameTime(renderer, snapshot, "validation");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TwentyThousandImpostorCandidatesFillTheAtlasAtTheBudgetAndThenCaptureNothing()
    {
        const int width = 1280,
            height = 720;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true, impostorTiles: 64);
        try
        {
            RenderSnapshot snapshot = Field(renderer, RenderFlags.None);
            Capture geometry = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "twenty-thousand-impostors-frame-0"
            );
            RenderStats first = renderer.LastFrameStats;
            output.WriteLine($"first frame: {first}");
            first.ImpostorCaptures.Should().Be(4);
            // Every visible object here is below 200 px.
            first.ImpostorsWaiting.Should().Be(first.VisibleInstances - 4);

            AssertFullBudgetWhileTilesAreFree(renderer, snapshot);

            Capture impostors = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "twenty-thousand-impostors"
            );
            RenderStats stats = renderer.LastFrameStats;
            output.WriteLine($"atlas full: {stats}");
            // 64 impostors, each visible every frame, so no tile can be evicted: the rest wait as geometry.
            (stats.Impostors, stats.ImpostorTilesInUse, stats.ImpostorCaptures)
                .Should()
                .Be((64, 64, 0));
            stats.ImpostorsWaiting.Should().Be(stats.VisibleInstances - 64);

            float mean = MeanColorDifference(geometry, impostors);
            output.WriteLine($"mean |Δ| against the first frame {mean * 255:0.000} / 255");
            (mean * 255)
                .Should()
                .BeLessThan(
                    0.5f,
                    "64 impostors among 20,000 objects change the frame by less than half a step of 1/255"
                );

            LogFrameTime(renderer, snapshot, "validation and impostors");

            // The CPU cost of the impostor decisions alone: batching the same snapshot with and without the manager.
            double without = BatchMilliseconds(snapshot: snapshot, width: width, height: height, withManager: false),
                with = BatchMilliseconds(snapshot: snapshot, width: width, height: height, withManager: true);
            output.WriteLine(
                $"CPU batching of 20,000 instances: {without:F2} ms without impostors, {with:F2} ms with (Debug build)"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TwentyThousandShadowCastersDrawOneIndirectDrawPerCascade()
    {
        const int width = 1280,
            height = 720;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height);
        try
        {
            // The sun is low in front and to the right, and the field stands on a ground plane, so the shadows show.
            RenderSnapshot snapshot = Field(renderer, RenderFlags.CastsShadow, new Vector3(0.6f, 0.45f, -0.5f));
            AddGround(renderer, snapshot);
            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "twenty-thousand-shadows"
            );
            RenderStats stats = renderer.LastFrameStats;
            output.WriteLine($"{stats}");
            stats.ShadowCasters.Should().Be(20_001);
            stats.ShadowCascades.Should().Be(4);
            stats
                .ShadowInstances.Should()
                .BeGreaterThanOrEqualTo(stats.ShadowCasters, "every caster is in at least one cascade");
            // Three meshes: at most three commands per cascade, and one multi-draw per cascade plus the near range.
            stats.ShadowCommands.Should().BeInRange(ShadowCascades.Count, 3 * ShadowCascades.Count);
            stats.IndirectDrawCalls.Should().Be(1 + ShadowCascades.Count);

            // Shadows make the field darker than the same field without shadow maps.
            SetFlags(snapshot, RenderFlags.Visible);

            Capture unshadowed = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: "twenty-thousand-without-shadow-maps"
            );

            LowerHalfLuma(image).Should().BeLessThan(0.85 * LowerHalfLuma(unshadowed), "shadows darken the field");

            SetFlags(snapshot, RenderFlags.Visible | RenderFlags.CastsShadow);

            LogFrameTime(renderer, snapshot, "validation, all casting shadows");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Renders 20 frames of <paramref name="snapshot"/> and writes the mean time of one, with <paramref name="setting"/>.</summary>
    private void LogFrameTime(VulkanRenderer renderer, RenderSnapshot snapshot, string setting)
    {
        var clock = Stopwatch.StartNew();
        const int frames = 20;
        for (int i = 0; i < frames; i++)
            renderer.Render(snapshot, i / (float)frames);
        output.WriteLine($"{clock.Elapsed.TotalMilliseconds / frames:F2} ms per frame with {setting}");
    }

    /// <summary>Renders frames 1 to 15 of the impostor field, and checks that each one captures the whole budget of 4.</summary>
    private static void AssertFullBudgetWhileTilesAreFree(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        for (int i = 1; i < 16; i++)
        {
            renderer.Render(snapshot, 1f);
            renderer
                .LastFrameStats.ImpostorCaptures.Should()
                .Be(4, "the budget is used in full while tiles are free (frame {0})", i);
        }
    }

    /// <summary>The mean absolute difference of two captures of one size, over every other pixel and the three channels.</summary>
    private static float MeanColorDifference(Capture first, Capture second)
    {
        int width = first.Width,
            height = first.Height;
        double sum = 0;
        for (int y = 0; y < height; y += 2)
        for (int x = 0; x < width; x += 2)
        {
            var d = Vector3.Abs(first.Color(x, y) - second.Color(x, y));
            sum += (d.X + d.Y + d.Z) / 3f;
        }

        return (float)(sum / (width / 2 * (height / 2)));
    }

    /// <summary>
    /// The mean time in milliseconds of one batch of <paramref name="snapshot"/> on the CPU, after 20 batches to warm up,
    /// with or without the impostor manager.
    /// </summary>
    private static double BatchMilliseconds(RenderSnapshot snapshot, int width, int height, bool withManager)
    {
        MeshInfo[] meshes =
        [
            new(FirstIndex: 0, IndexCount: 36, VertexOffset: 0, Radius: MathF.Sqrt(0.75f), Deform: null),
            new(FirstIndex: 36, IndexCount: 3000, VertexOffset: 24, Radius: 0.5f, Deform: null),
        ];
        SceneBatcher batcher = Batchers.Default();
        if (withManager)
            batcher.Processors.Add(new ImpostorManager(64));
        var scene = new PreparedScene();
        for (int i = 0; i < 20; i++)
            batcher.Prepare(
                snapshot: snapshot,
                alpha: 1f,
                width: width,
                height: height,
                meshes: meshes,
                materials: MaterialSlots.Of(8),
                environment: default,
                region: DeformRegion.None,
                scene: scene
            );
        var timer = Stopwatch.StartNew();
        for (int i = 0; i < 50; i++)
            batcher.Prepare(
                snapshot: snapshot,
                alpha: 1f,
                width: width,
                height: height,
                meshes: meshes,
                materials: MaterialSlots.Of(8),
                environment: default,
                region: DeformRegion.None,
                scene: scene
            );
        return timer.Elapsed.TotalMilliseconds / 50;
    }

    /// <summary>Adds a gray ground plane under the field, which casts shadows too.</summary>
    private static void AddGround(VulkanRenderer renderer, RenderSnapshot snapshot)
    {
        MeshId plane = renderer.CreateMesh(Primitives.Plane(1f));
        MaterialId ground = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), roughness: 0.65f)
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: plane,
                material: ground,
                position: new(0, -5f, -200f),
                scale: new(450f, 1f, 450f),
                flags: RenderFlags.CastsShadow,
                chainDepth: TestChains.CamerasFrame
            )
        );
    }

    /// <summary>Gives every instance of <paramref name="snapshot"/> the flags <paramref name="flags"/>.</summary>
    private static void SetFlags(RenderSnapshot snapshot, RenderFlags flags)
    {
        for (int i = 0; i < snapshot.Instances.Count; i++)
        {
            RenderInstance instance = snapshot.Instances[i];
            instance.Flags = flags;
            snapshot.Instances[i] = instance;
        }
    }

    /// <summary>The sum of the luma of every other pixel in the lower half of a capture, below its top 20 rows.</summary>
    private static double LowerHalfLuma(Capture capture)
    {
        int width = capture.Width,
            height = capture.Height;
        double sum = 0;
        for (int y = height / 2 + 20; y < height; y += 2)
        for (int x = 0; x < width; x += 2)
            sum += capture.Luma(x, y);
        return sum;
    }
}
