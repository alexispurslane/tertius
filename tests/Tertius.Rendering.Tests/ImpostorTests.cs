using System.Numerics;
using StbImageWriteSharp;

namespace Tertius.Rendering.Tests;

/// <summary>Impostors on the GPU: switching with hysteresis, capture accuracy, recapture, budget, eviction.</summary>
[Collection(GpuCollection.Name)]
public sealed class ImpostorTests(ITestOutputHelper output)
{
    private static readonly Vector3 Sun = Vector3.Normalize(new(0.5f, 0.6f, 0.62f));

    /// <summary>A unit box (half size 0.5, bounding radius √0.75) turned so three faces show.</summary>
    private static readonly Quaternion BoxTurn = Quaternion.CreateFromYawPitchRoll(0.6f, 0.35f, 0.1f);

    /// <summary>The distance at which a sphere of <paramref name="radius"/> has projected height <paramref name="height"/>.</summary>
    private static float DistanceForHeight(float height, float radius, int outputHeight, float fov = Gpu.Fov)
    {
        float tangent = height * MathF.Tan(fov / 2) / outputHeight;
        return radius * MathF.Sqrt(1 + tangent * tangent) / tangent;
    }

    private sealed class Assets
    {
        public MeshId Box;
        public MeshId Sphere;
        public MaterialId Checker;
        public MaterialId Red;
        public MaterialId Ground;

        public static Assets Create(VulkanRenderer renderer)
        {
            TextureId checker = renderer.CreateTexture(TestTextures.Checker(256, 4, (230, 180, 60), (60, 90, 170)));
            TextureId tiles = renderer.CreateTexture(TestTextures.BeveledTiles(256, 4));
            return new()
            {
                Box = renderer.CreateMesh(Primitives.Box(new(0.5f))),
                Sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
                Checker = renderer.CreateMaterial(new(Vector4.One, SpecularIntensity: 0.6f, Gloss: 0.6f,
                    Albedo: checker, Normal: tiles, SpecularGloss: TextureId.None)),
                Red = renderer.CreateMaterial(MaterialDesc.Color(new(0.75f, 0.08f, 0.06f, 1), gloss: 0.8f) with
                {
                    SpecularIntensity = 1f
                }),
                Ground = renderer.CreateMaterial(MaterialDesc.Color(new(0.5f, 0.5f, 0.5f, 1), gloss: 0.2f))
            };
        }
    }

    [Fact]
    public void ObjectBecomesAnImpostorBelow200PixelsAndReturnsAbove300WithHysteresis()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            var key = new ImpostorKey(7, 0, assets.Sphere.Value, MaterialStore.Slot(assets.Red));
            // Heights in pixels and the expected result: geometry above 300, impostor below 200, and between them
            // whatever the object was before.
            (float Height, bool Impostor)[] steps =
            [
                (250, false), (210, false), (190, true), (250, true), (295, true), (310, false), (250, false),
                (205, false), (199, true)
            ];
            int frame = 0;
            foreach ((float h, bool expected) in steps)
            {
                float distance = DistanceForHeight(h, 1f, height);
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new(0, 0, -distance), Vector3.One,
                    entity: 7));
                Capture image = Gpu.RenderAndCapture(renderer, snapshot, $"impostor-hysteresis-{frame++}-{h:0}px");
                RenderStats stats = renderer.LastFrameStats;
                renderer.Impostors!.TryGetStatus(key, out ImpostorStatus status).Should().BeTrue();
                status.ProjectedHeight.Should().BeApproximately(h, 0.5f);
                status.Impostor.Should().Be(expected, "at {0} px", h);
                stats.Impostors.Should().Be(expected ? 1 : 0);
                stats.VisibleInstances.Should().Be(1);
                // Drawn either way: the center pixel is the lit red sphere.
                Vector3 center = image.Color(width / 2, height / 2);
                center.X.Should().BeGreaterThan(0.3f, "the sphere is lit at {0} px", h).And
                    .BeGreaterThan(2 * center.Y, "it is red at {0} px: {1}", h, center);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TwoPartsOfOneEntityWithTheSameMeshAndMaterialAreSeparateImpostors()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            // One entity whose two render parts share the sphere mesh and the red material, side by side at 120 px.
            float distance = DistanceForHeight(120f, 1f, height);

            RenderSnapshot Scene(RenderFlags flags, bool objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                if (objects)
                {
                    snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new(-1.6f, 0, -distance),
                        Vector3.One, flags: flags, entity: 4, part: 0));
                    snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new(1.6f, 0.4f, -distance),
                        Vector3.One, flags: flags, entity: 4, part: 1));
                }

                return snapshot;
            }

            (Capture background, Capture geometry, Capture impostor) =
                GeometryAndImpostor(renderer, Scene, "impostor-two-parts");
            RenderStats stats = renderer.LastFrameStats;
            stats.Impostors.Should().Be(2, "each part is an impostor of its own");
            stats.ImpostorsWaiting.Should().Be(0);
            stats.ImpostorTilesInUse.Should().Be(2);
            ImpostorManager manager = renderer.Impostors!;

            int Tile(ushort part) =>
                manager.TryGetStatus(new(4, part, assets.Sphere.Value, MaterialStore.Slot(assets.Red)),
                    out ImpostorStatus status)
                    ? status.Tile
                    : -1;

            Tile(0).Should().BeGreaterThanOrEqualTo(0);
            Tile(1).Should().BeGreaterThanOrEqualTo(0).And.NotBe(Tile(0), "each part has its own capture");

            (float mean, float overlap, int pixels) = Compare(background, geometry, impostor);
            output.WriteLine($"silhouette {pixels} px, IoU {overlap:0.000}, mean |Δ| {mean * 255:0.00} / 255");
            overlap.Should().BeGreaterThan(0.97f, "both parts are drawn where their geometry is");
            (mean * 255).Should().BeLessThan(6f, "both impostors look like their geometry, in 1/255 steps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACapturedImpostorLooksLikeTheGeometry()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            float radius = MathF.Sqrt(0.75f) * 1.5f;
            Vector3 direction = Vector3.Normalize(new(0.12f, -0.05f, -1f));

            RenderSnapshot Scene(float distance)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                snapshot.Instances.Add(Gpu.Instance(assets.Box, assets.Checker, direction * distance, new(1.5f),
                    BoxTurn, entity: 3));
                return snapshot;
            }

            float near = DistanceForHeight(210, radius, height);
            float far = DistanceForHeight(195, radius, height);
            Capture background = Gpu.RenderAndCapture(renderer, Gpu.Snapshot(Sun), "impostor-accuracy-background");
            Capture geometry = Gpu.RenderAndCapture(renderer, Scene(near), "impostor-accuracy-geometry");
            renderer.LastFrameStats.Impostors.Should().Be(0);
            // Below 200 px: captured and drawn as an impostor in the same frame.
            Gpu.RenderAndCapture(renderer, Scene(far), "impostor-accuracy-195px");
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(1);
            renderer.LastFrameStats.Impostors.Should().Be(1);
            // Back at 210 px it stays an impostor (hysteresis). It moved along its view ray, so only the perspective
            // changed: r / d grew by 0.0145, times h = 210 that is above 2, so it is captured again.
            renderer.Render(Scene(near), 1f);
            renderer.LastFrameStats.Impostors.Should().Be(1);
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(1);
            Capture impostor = Gpu.RenderAndCapture(renderer, Scene(near), "impostor-accuracy-impostor");
            renderer.LastFrameStats.Impostors.Should().Be(1);
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(0);
            SaveTile(renderer, 0, "impostor-accuracy-tile");

            (float meanDifference, float overlap, int pixels) = Compare(background, geometry, impostor);
            output.WriteLine(
                $"silhouette {pixels} px, overlap (IoU) {overlap:0.000}, mean |Δ| inside {meanDifference * 255:0.00} / 255");
            pixels.Should().BeGreaterThan(20_000, "the object covers a large part of the frame");
            overlap.Should().BeGreaterThan(0.97f, "the silhouettes match");
            // Documented threshold: within the silhouette the impostor differs from the geometry by less than 6/255
            // per channel on average (display-encoded). The differences come from resampling the 512 px capture to
            // ~210 px through the mip chain.
            (meanDifference * 255).Should().BeLessThan(6f, "the impostor looks like the geometry, in 1/255 steps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TurningTheViewByMoreThanTwoOverHRecapturesAndLessDoesNot()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            float radius = MathF.Sqrt(0.75f) * 1.5f, h = 100f;
            float distance = DistanceForHeight(h, radius, height);
            float limit = 2f / h;

            RenderSnapshot Orbit(float angle, Quaternion? camera = null)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                Quaternion rotation = camera ?? Quaternion.Identity;
                snapshot.Camera.Rotation = rotation;
                snapshot.Camera.PreviousRotation = rotation;
                // The camera goes around the object at a constant distance: in camera-relative terms the object moves.
                Vector3 position = Vector3.Transform(new(0, 0, -distance),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
                snapshot.Instances.Add(
                    Gpu.Instance(assets.Box, assets.Checker, position, new(1.5f), BoxTurn, entity: 9));
                return snapshot;
            }

            int Captures(RenderSnapshot snapshot, string? name = null)
            {
                if (name != null)
                    Gpu.RenderAndCapture(renderer, snapshot, name);
                else
                    renderer.Render(snapshot, 1f);
                renderer.LastFrameStats.Impostors.Should().Be(1, "the box stays an impostor");
                return renderer.LastFrameStats.ImpostorCaptures;
            }

            Captures(Orbit(0f), "impostor-recapture-0").Should().Be(1);
            Captures(Orbit(0.5f * limit)).Should().Be(0);
            Captures(Orbit(0.95f * limit)).Should().Be(0);
            Captures(Orbit(1.05f * limit)).Should().Be(1);
            // Turning the camera in place changes nothing about the object's view.
            Captures(Orbit(1.05f * limit, Quaternion.CreateFromYawPitchRoll(0.1f, 0.08f, 0.2f))).Should().Be(0);
            Captures(Orbit(2f * limit)).Should().Be(0);
            Captures(Orbit(2.2f * limit)).Should().Be(1);
            // A large orbit: the capture follows, and the impostor shows the box from the new side.
            Captures(Orbit(0.6f), "impostor-recapture-orbit").Should().Be(1);
            SaveTile(renderer, 0, "impostor-recapture-orbit-tile");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheCaptureBudgetIsRespectedWithFiftyCandidates()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true, impostorTiles: 64);
        try
        {
            var assets = Assets.Create(renderer);
            MaterialId[] colors =
            [
                renderer.CreateMaterial(MaterialDesc.Color(new(0.8f, 0.2f, 0.2f, 1), gloss: 0.7f)),
                renderer.CreateMaterial(MaterialDesc.Color(new(0.2f, 0.7f, 0.3f, 1), gloss: 0.3f)),
                renderer.CreateMaterial(MaterialDesc.Color(new(0.2f, 0.3f, 0.8f, 1), gloss: 0.5f)),
                assets.Checker
            ];
            // 50 objects in a 10 × 5 grid, every one between 25 and 150 px tall.
            RenderSnapshot snapshot = Gpu.Snapshot(Sun);
            for (uint i = 0; i < 50; i++)
            {
                float h = 25f + 2.5f * i;
                bool box = i % 2 == 0;
                float radius = box ? MathF.Sqrt(0.75f) : 1f;
                float distance = DistanceForHeight(h, radius, height);
                var direction =
                    Quaternion.CreateFromYawPitchRoll((i % 10 - 4.5f) * 0.12f, ((int)(i / 10) - 2) * 0.16f, 0);
                snapshot.Instances.Add(Gpu.Instance(box ? assets.Box : assets.Sphere, colors[i % 4],
                    Vector3.Transform(new(0, 0, -distance), direction), Vector3.One, BoxTurn, entity: 1000 + i));
            }

            Capture? first = null;
            int captured = 0, frames = 0;
            while (captured < 50)
            {
                Capture image = Gpu.RenderAndCapture(renderer, snapshot, $"impostor-budget-frame-{frames}");
                first ??= image;
                frames++;
                RenderStats stats = renderer.LastFrameStats;
                stats.ImpostorCaptures.Should().Be(Math.Min(4, 50 - captured), "the budget is four captures per frame");
                captured += stats.ImpostorCaptures;
                stats.Impostors.Should().Be(captured);
                stats.ImpostorsWaiting.Should().Be(50 - captured);
                stats.VisibleInstances.Should().Be(50);
                stats.ImpostorTilesInUse.Should().Be(captured);
            }

            frames.Should().Be(13);
            Capture last = Gpu.RenderAndCapture(renderer, snapshot, "impostor-budget-all");
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(0);
            renderer.LastFrameStats.Impostors.Should().Be(50);

            // The first frame had 4 impostors and 46 objects as geometry; the last has 50 impostors. They look the same.
            double sum = 0;
            int pixels = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector3 a = first!.Color(x, y), b = last.Color(x, y);
                if (MaxAbs(a) < 4f / 255f && MaxAbs(b) < 4f / 255f)
                    continue;
                Vector3 d = Vector3.Abs(a - b);
                sum += (d.X + d.Y + d.Z) / 3f;
                pixels++;
            }

            float mean = (float)(sum / pixels);
            output.WriteLine($"{pixels} lit pixels, mean |Δ| {mean * 255:0.00} / 255");
            (mean * 255).Should().BeLessThan(6f, "geometry and impostors look the same, in 1/255 steps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheAtlasEvictsTheLeastRecentlyUsedTileOfAKeyThatIsGoneOrBackToGeometry()
    {
        const int width = 640, height = 360;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true, impostorTiles: 4);
        try
        {
            var assets = Assets.Create(renderer);
            Vector4[] tints =
            [
                new(0.9f, 0.1f, 0.1f, 1), new(0.1f, 0.9f, 0.1f, 1), new(0.1f, 0.1f, 0.9f, 1), new(0.9f, 0.9f, 0.1f, 1),
                new(0.1f, 0.9f, 0.9f, 1), new(0.9f, 0.1f, 0.9f, 1), new(0.9f, 0.9f, 0.9f, 1)
            ];
            MaterialId[] materials =
                [.. tints.Select(t => renderer.CreateMaterial(MaterialDesc.Color(t, gloss: 0.3f)))];
            float far = DistanceForHeight(80f, 1f, height), close = DistanceForHeight(320f, 1f, height);

            RenderSnapshot Scene(params (int Object, float Distance)[] objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                foreach ((int index, float distance) in objects)
                {
                    var position = new Vector3((index - 3f) * 0.12f * distance, 0, -distance);
                    snapshot.Instances.Add(Gpu.Instance(assets.Sphere, materials[index], position, Vector3.One,
                        entity: (uint)index));
                }

                return snapshot;
            }

            ImpostorManager manager = renderer.Impostors!;

            int Tile(int index) =>
                manager.TryGetStatus(new((uint)index, 0, assets.Sphere.Value, MaterialStore.Slot(materials[index])),
                    out ImpostorStatus s)
                    ? s.Tile
                    : -1;

            Vector3 TileColor(int tile)
            {
                (float[] rgba, _) = renderer.ReadImpostorTile(tile);
                Vector3 sum = Vector3.Zero;
                float coverage = 0;
                for (int i = 0; i < rgba.Length; i += 4)
                {
                    sum += new Vector3(rgba[i], rgba[i + 1], rgba[i + 2]);
                    coverage += rgba[i + 3];
                }

                return Vector3.Normalize(sum / coverage);
            }

            void AssertTileShows(int tile, int index)
            {
                Vector3 color = TileColor(tile),
                    expected = Vector3.Normalize(new(tints[index].X, tints[index].Y, tints[index].Z));
                Vector3.Dot(color, expected).Should().BeGreaterThan(0.95f, "tile {0} shows {1}, and object {2} is {3}",
                    tile, color, index, expected);
            }

            renderer.Render(Scene((0, far), (1, far), (2, far), (3, far)), 1f);
            renderer.LastFrameStats.ImpostorTilesInUse.Should().Be(4);
            int[] tiles = [Tile(0), Tile(1), Tile(2), Tile(3)];
            for (int i = 0; i < 4; i++)
                AssertTileShows(tiles[i], i);
            // Object 0 leaves, then object 1; object 4 arrives and takes the least recently used tile: object 0's.
            renderer.Render(Scene((1, far), (2, far), (3, far)), 1f);
            Gpu.RenderAndCapture(renderer, Scene((2, far), (3, far), (4, far)), "impostor-eviction-1");
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(1);
            Tile(4).Should().Be(tiles[0]);
            Tile(0).Should().Be(-1);
            AssertTileShows(tiles[0], 4);
            SaveTile(renderer, tiles[0], "impostor-eviction-tile-reused");
            // Object 5 takes object 1's tile, the only one left whose key is gone.
            renderer.Render(Scene((2, far), (3, far), (4, far), (5, far)), 1f);
            Tile(5).Should().Be(tiles[1]);
            AssertTileShows(tiles[1], 5);
            // Object 6 finds every tile in use by a drawn impostor: it waits, drawn as geometry.
            Gpu.RenderAndCapture(renderer, Scene((2, far), (3, far), (4, far), (5, far), (6, far)),
                "impostor-eviction-waiting");
            RenderStats stats = renderer.LastFrameStats;
            (stats.Impostors, stats.ImpostorsWaiting, stats.ImpostorCaptures, stats.VisibleInstances).Should()
                .Be((4, 1, 0, 5));
            // Object 2 comes close and returns to geometry: object 6 takes its tile.
            Gpu.RenderAndCapture(renderer, Scene((2, close), (3, far), (4, far), (5, far), (6, far)),
                "impostor-eviction-2");
            Tile(6).Should().Be(tiles[2]);
            AssertTileShows(tiles[2], 6);
            stats = renderer.LastFrameStats;
            (stats.Impostors, stats.ImpostorsWaiting, stats.ImpostorCaptures, stats.ImpostorTilesInUse).Should()
                .Be((4, 0, 1, 4));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Renders a scene with every instance flagged <see cref="RenderFlags.NoImpostor"/>, then without the flag until
    /// the impostors are captured, and returns the empty background, the geometry render and the impostor render.
    /// </summary>
    private static (Capture Background, Capture Geometry, Capture Impostor) GeometryAndImpostor(VulkanRenderer renderer,
        Func<RenderFlags, bool, RenderSnapshot> scene, string name)
    {
        Capture background = Gpu.RenderAndCapture(renderer, scene(RenderFlags.None, false), name + "-background");
        Capture geometry = Gpu.RenderAndCapture(renderer, scene(RenderFlags.NoImpostor, true), name + "-geometry");
        renderer.LastFrameStats.Impostors.Should().Be(0, "NoImpostor keeps every object geometry");
        for (int i = 0; i < 4; i++)
            renderer.Render(scene(RenderFlags.None, true), 1f);
        Capture impostor = Gpu.RenderAndCapture(renderer, scene(RenderFlags.None, true), name + "-impostor");
        renderer.LastFrameStats.ImpostorCaptures.Should().Be(0, "every impostor is captured by now");
        return (background, geometry, impostor);
    }

    [Fact]
    public void ImpostorsWriteTheirSurfaceDepthSoTheyIntersectGeometryLikeTheObject()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f, 10f));
            Quaternion pitch = Gpu.PitchDown(0.25f);
            float distance = DistanceForHeight(150f, 1f, height);

            // A sphere half sunk into the ground and a turned box pushed into it, both impostors; the ground is geometry.
            RenderSnapshot Scene(RenderFlags flags, bool objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                snapshot.Camera.Rotation = pitch;
                snapshot.Camera.PreviousRotation = pitch;
                snapshot.Instances.Add(Gpu.Instance(plane, assets.Ground, new(0, -1.2f, -20f), new(40f, 1f, 40f),
                    flags: RenderFlags.CastsShadow));
                if (objects)
                {
                    snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new(-0.9f, -1.2f, -distance),
                        Vector3.One, flags: flags | RenderFlags.CastsShadow, entity: 1));
                    snapshot.Instances.Add(Gpu.Instance(assets.Box, assets.Checker, new(1.3f, -1.2f, -distance),
                        new(1.1f),
                        Quaternion.CreateFromYawPitchRoll(0.5f, 0.6f, 0.3f), flags | RenderFlags.CastsShadow,
                        entity: 2));
                }

                return snapshot;
            }

            (Capture background, Capture geometry, Capture impostor) =
                GeometryAndImpostor(renderer, Scene, "impostor-ground");
            renderer.LastFrameStats.Impostors.Should().Be(2);
            (float mean, float overlap, int pixels) = Compare(background, geometry, impostor);
            output.WriteLine($"silhouette {pixels} px, IoU {overlap:0.000}, mean |Δ| {mean * 255:0.00} / 255");
            // A flat billboard would cut the objects off with a straight line where they meet the ground; the
            // captured depth gives the same curved intersection as the geometry.
            overlap.Should().BeGreaterThan(0.97f, "the impostors meet the ground where the geometry does");
            (mean * 255).Should().BeLessThan(8f, "the impostors look like the geometry, in 1/255 steps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnImpostorAcrossTheTwoKilometreBoundaryAndOneInTheMiddleRangeLookLikeTheirGeometry()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);

            // A 400 m sphere centered at 2 km (drawn in the middle and near ranges) and a 1 km box at 50 km.
            RenderSnapshot Scene(RenderFlags flags, bool objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                if (objects)
                {
                    snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new(-250f, 0, -2_000f), new(400f),
                        flags: flags, entity: 1));
                    snapshot.Instances.Add(Gpu.Instance(assets.Box, assets.Checker, new(20_000f, 3_000f, -50_000f),
                        new(1_000f), BoxTurn, flags, entity: 2));
                }

                return snapshot;
            }

            (Capture background, Capture geometry, Capture impostor) =
                GeometryAndImpostor(renderer, Scene, "impostor-ranges");
            renderer.LastFrameStats.Impostors.Should().Be(2);
            renderer.LastScene.Impostors[DepthRanges.Near].Count.Should()
                .Be(1, "the sphere is drawn in the near range");
            renderer.LastScene.Impostors[DepthRanges.Middle].Count.Should()
                .Be(2, "the sphere and the box are drawn in the middle range");
            (float mean, float overlap, int pixels) = Compare(background, geometry, impostor);
            output.WriteLine($"silhouette {pixels} px, IoU {overlap:0.000}, mean |Δ| {mean * 255:0.00} / 255");
            overlap.Should().BeGreaterThan(0.97f, "the silhouettes match");
            (mean * 255).Should().BeLessThan(6f, "the impostors look like the geometry, in 1/255 steps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheReferenceSceneWithImpostorsLooksLikeTheGeometry()
    {
        const int width = 640, height = 360;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true);
        try
        {
            var scene = TestScene.Create(renderer);

            RenderSnapshot Snapshot(RenderFlags flags)
            {
                RenderSnapshot snapshot = scene.Snapshot();
                // Distinct entities, as the snapshot builder would give them.
                for (int i = 0; i < snapshot.Instances.Count; i++)
                {
                    RenderInstance instance = snapshot.Instances[i];
                    instance.EntityIndex = (uint)i;
                    instance.Flags |= flags;
                    snapshot.Instances[i] = instance;
                }

                return snapshot;
            }

            Capture geometry =
                Gpu.RenderAndCapture(renderer, Snapshot(RenderFlags.NoImpostor), "impostor-scene-geometry");
            for (int i = 0; i < 4; i++)
                renderer.Render(Snapshot(RenderFlags.None), 1f);
            Capture impostors = Gpu.RenderAndCapture(renderer, Snapshot(RenderFlags.None), "impostor-scene");
            RenderStats stats = renderer.LastFrameStats;
            output.WriteLine($"{stats}");
            // The boxes, the opaque spheres and the station; not the ground (too large), the glass, planet or star.
            stats.Impostors.Should().Be(6);
            stats.ImpostorCaptures.Should().Be(0);

            double sum = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector3 d = Vector3.Abs(geometry.Color(x, y) - impostors.Color(x, y));
                sum += (d.X + d.Y + d.Z) / 3f;
            }

            float mean = (float)(sum / (width * height));
            output.WriteLine($"mean |Δ| over the frame {mean * 255:0.00} / 255");
            // Captures bake the sun, the ambient light and the point and spot lights, but not the shadow maps: the
            // shadows other objects cast onto the impostors are missing, which is most of the difference.
            (mean * 255).Should().BeLessThan(2f, "the frame with impostors looks like the geometry, in 1/255 steps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void SteadyStateFramesWithImpostorsDoNotAllocate()
    {
        VulkanRenderer renderer = Gpu.Headless(320, 180, impostors: true);
        try
        {
            var scene = TestScene.Create(renderer);
            RenderSnapshot snapshot = scene.Snapshot();
            for (int i = 0; i < snapshot.Instances.Count; i++)
            {
                RenderInstance instance = snapshot.Instances[i];
                instance.EntityIndex = (uint)i;
                snapshot.Instances[i] = instance;
            }

            for (int i = 0; i < 5; i++)
                renderer.Render(snapshot, 0.5f);
            renderer.LastFrameStats.Impostors.Should().BePositive();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                renderer.Render(snapshot, i / 20f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            allocated.Should().Be(0, "a steady state allocates nothing");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RecapturesEveryFrameAreSynchronizedForAnyNumberOfFramesInFlight(int framesInFlight)
    {
        const int width = 320, height = 180;
        VulkanRenderer renderer = Gpu.Headless(width, height, framesInFlight, impostors: true, impostorTiles: 4);
        try
        {
            var assets = Assets.Create(renderer);
            // Six objects for four tiles, orbiting fast: every frame recaptures, evicts and reuses tiles that earlier
            // frames in flight are still sampling.
            int captures = 0;
            for (int frame = 0; frame < 24; frame++)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                for (uint i = 0; i < 6; i++)
                {
                    if ((frame + i) % 4 == 0)
                        continue;
                    float angle = frame * 0.05f + i;
                    var position = new Vector3(MathF.Sin(i * 1.1f) * 4f, MathF.Cos(i * 0.7f) * 2f, -14f - i);
                    snapshot.Instances.Add(Gpu.Instance(i % 2 == 0 ? assets.Box : assets.Sphere, assets.Checker,
                        position, Vector3.One,
                        Quaternion.CreateFromYawPitchRoll(angle, angle * 0.5f, 0), entity: i));
                }

                if (frame == 23)
                    Gpu.RenderAndCapture(renderer, snapshot, $"impostor-in-flight-{framesInFlight}");
                else
                    renderer.Render(snapshot, 1f);
                captures += renderer.LastFrameStats.ImpostorCaptures;
                renderer.LastFrameStats.ImpostorCaptures.Should()
                    .BeInRange(1, 4, "every frame recaptures within the budget");
            }

            captures.Should().BeGreaterThanOrEqualTo(24);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ImpostorOptionsAreCheckedAndImpostorsCanBeTurnedOff()
    {
        FluentActions
            .Invoking(() => VulkanRenderer.CreateHeadless(new() { Width = 64, Height = 64, ImpostorTiles = 0 }))
            .Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() =>
                VulkanRenderer.CreateHeadless(new()
                    { Validation = true, Width = 64, Height = 64, ImpostorTiles = 1_000_000 }))
            .Should().ThrowExactly<ArgumentOutOfRangeException>(
                "the atlas cannot have more layers than the device allows");
        new RendererOptions().ImpostorTiles.Should().Be(64);
        new RendererOptions().Impostors.Should().BeTrue();

        // Off: no manager, no atlas, and a small object stays geometry.
        VulkanRenderer renderer = Gpu.Headless(320, 180, impostors: false);
        try
        {
            var assets = Assets.Create(renderer);
            renderer.Impostors.Should().BeNull();
            RenderSnapshot snapshot = Gpu.Snapshot(Sun);
            snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new(0, 0, -40f), Vector3.One, entity: 1));
            renderer.Render(snapshot, 1f);
            (renderer.LastFrameStats.Impostors, renderer.LastFrameStats.ImpostorCaptures,
                renderer.LastFrameStats.NearCommands).Should().Be((0, 0, 1));
            FluentActions.Invoking(() => renderer.ReadImpostorTile(0)).Should()
                .ThrowExactly<InvalidOperationException>();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }

        // A one-tile atlas still uses an array view, as the shader expects.
        renderer = Gpu.Headless(320, 180, impostors: true, impostorTiles: 1);
        try
        {
            var assets = Assets.Create(renderer);
            RenderSnapshot snapshot = Gpu.Snapshot(Sun);
            snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new(0, 0, -40f), Vector3.One, entity: 1));
            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "impostor-one-tile");
            renderer.LastFrameStats.Impostors.Should().Be(1);
            Vector3 center = image.Color(160, 90);
            center.X.Should().BeGreaterThan(0.2f, "the impostor is lit").And
                .BeGreaterThan(2 * center.Y, "it is red: {0}", center);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The mean absolute difference per channel of two renders inside the first one's silhouette, the intersection
    /// over union of the two silhouettes, and the silhouette's size. A pixel is in a silhouette when it differs from
    /// the empty background.
    /// </summary>
    private static (float MeanDifference, float Overlap, int Pixels) Compare(Capture background, Capture a, Capture b)
    {
        double sum = 0;
        int inA = 0, inB = 0, both = 0;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
        {
            Vector3 bg = background.Color(x, y);
            Vector3 ca = a.Color(x, y), cb = b.Color(x, y);
            bool maskA = MaxAbs(ca - bg) > 4f / 255f;
            bool maskB = MaxAbs(cb - bg) > 4f / 255f;
            inA += maskA ? 1 : 0;
            inB += maskB ? 1 : 0;
            both += maskA && maskB ? 1 : 0;
            if (maskA)
            {
                Vector3 d = Vector3.Abs(ca - cb);
                sum += (d.X + d.Y + d.Z) / 3f;
            }
        }

        return ((float)(sum / Math.Max(inA, 1)), both / (float)Math.Max(inA + inB - both, 1), inA);
    }

    private static float MaxAbs(Vector3 v) => MathF.Max(MathF.Abs(v.X), MathF.Max(MathF.Abs(v.Y), MathF.Abs(v.Z)));

    /// <summary>Saves an atlas tile as a PNG: the premultiplied color tone-mapped over a dark checker, and the depth offset beside it.</summary>
    internal static void SaveTile(VulkanRenderer renderer, int tile, string name)
    {
        (float[] rgba, float[] depth) = renderer.ReadImpostorTile(tile);
        const int size = ImpostorAtlas.TileSize;
        byte[] pixels = new byte[size * 2 * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = y * size + x;
            float alpha = rgba[i * 4 + 3];
            float checker = ((x / 32 + y / 32) & 1) == 0 ? 0.08f : 0.16f;
            int o = (y * size * 2 + x) * 4;
            for (int c = 0; c < 3; c++)
            {
                // Premultiplied over the checker, then x / (1 + x) and the sRGB curve.
                float linear = rgba[i * 4 + c] + checker * (1 - alpha);
                pixels[o + c] = ToByte(Srgb(linear / (1 + linear)));
            }

            pixels[o + 3] = 255;
            // Depth offset: red toward the eye, blue away, black where nothing was captured.
            float offset = alpha > 0 ? depth[i] / alpha : 0;
            int d = (y * size * 2 + size + x) * 4;
            pixels[d] = ToByte(alpha > 0 ? 0.5f + 0.5f * offset : 0);
            pixels[d + 1] = ToByte(alpha > 0 ? 0.25f : 0);
            pixels[d + 2] = ToByte(alpha > 0 ? 0.5f - 0.5f * offset : 0);
            pixels[d + 3] = 255;
        }

        string path = Gpu.CapturePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        new ImageWriter().WritePng(pixels, size * 2, size, ColorComponents.RedGreenBlueAlpha, stream);
    }

    private static float Srgb(float c) => c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);
}
