using System.Numerics;
using StbImageWriteSharp;
using Tertius.Testing;

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
            TextureId checker = renderer.CreateTexture(
                TestTextures.Checker(size: 256, tiles: 4, a: (230, 180, 60), b: (60, 90, 170))
            );
            TextureId tiles = renderer.CreateTexture(TestTextures.BeveledTiles(size: 256, tiles: 4));
            return new()
            {
                Box = renderer.CreateMesh(Primitives.Box(new(0.5f))),
                Sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
                Checker = renderer.CreateMaterial(
                    new(
                        BaseColor: Vector4.One,
                        Reflectance: 0.6f,
                        Roughness: 0.32f,
                        BaseColorMap: checker,
                        NormalMap: tiles,
                        MetallicRoughnessMap: TextureId.None
                    )
                ),
                Red = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.75f, y: 0.08f, z: 0.06f, w: 1), roughness: 0.22f) with
                    {
                        Reflectance = 1f,
                    }
                ),
                Ground = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), roughness: 0.65f)
                ),
            };
        }
    }

    [Fact]
    public void ObjectBecomesAnImpostorBelow200PixelsAndReturnsAbove300WithHysteresis()
    {
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            var key = new ImpostorKey(
                EntityIndex: 7,
                Part: 0,
                Mesh: assets.Sphere.Value,
                Material: MaterialStore.Slot(assets.Red)
            );
            // Heights in pixels and the expected result: geometry above 300, impostor below 200, and between them
            // whatever the object was before.
            (float Height, bool Impostor)[] steps =
            [
                (250, false),
                (210, false),
                (190, true),
                (250, true),
                (295, true),
                (310, false),
                (250, false),
                (205, false),
                (199, true),
            ];
            int frame = 0;
            foreach ((float h, bool expected) in steps)
            {
                float distance = DistanceForHeight(height: h, radius: 1f, outputHeight: height);
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: assets.Sphere,
                        material: assets.Red,
                        position: new(0, 0, -distance),
                        scale: Vector3.One,
                        entity: 7
                    )
                );
                Capture image = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: snapshot,
                    name: $"impostor-hysteresis-{frame++}-{h:0}px"
                );
                RenderStats stats = renderer.LastFrameStats;
                ImpostorStatus status = renderer.Impostors!.Status(key)!.Value;
                status.ProjectedHeight.Should().BeApproximately(h, 0.5f);
                status.Impostor.Should().Be(expected, "at {0} px", h);
                stats.Impostors.Should().Be(expected ? 1 : 0);
                stats.VisibleInstances.Should().Be(1);
                // Drawn either way: the center pixel is the lit red sphere.
                Vector3 center = image.Color(width / 2, height / 2);
                center
                    .X.Should()
                    .BeGreaterThan(0.3f, "the sphere is lit at {0} px", h)
                    .And.BeGreaterThan(2 * center.Y, "it is red at {0} px: {1}", h, center);
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
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            // One entity whose two render parts share the sphere mesh and the red material, side by side at 120 px.
            float distance = DistanceForHeight(height: 120f, radius: 1f, outputHeight: height);

            RenderSnapshot Scene(RenderFlags flags, bool objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                if (objects)
                {
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: assets.Sphere,
                            material: assets.Red,
                            position: new(-1.6f, 0, -distance),
                            scale: Vector3.One,
                            flags: flags,
                            entity: 4,
                            part: 0
                        )
                    );
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: assets.Sphere,
                            material: assets.Red,
                            position: new(1.6f, 0.4f, -distance),
                            scale: Vector3.One,
                            flags: flags,
                            entity: 4,
                            part: 1
                        )
                    );
                }

                return snapshot;
            }

            (Capture background, Capture geometry, Capture impostor) = GeometryAndImpostor(
                renderer,
                Scene,
                "impostor-two-parts"
            );
            RenderStats stats = renderer.LastFrameStats;
            stats.Impostors.Should().Be(2, "each part is an impostor of its own");
            stats.ImpostorsWaiting.Should().Be(0);
            stats.ImpostorTilesInUse.Should().Be(2);
            ImpostorManager manager = renderer.Impostors!;

            int Tile(ushort part) =>
                manager
                    .Status(
                        new(
                            EntityIndex: 4,
                            Part: part,
                            Mesh: assets.Sphere.Value,
                            Material: MaterialStore.Slot(assets.Red)
                        )
                    )
                    ?.Tile
                ?? -1;

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
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            float radius = MathF.Sqrt(0.75f) * 1.5f;
            var direction = Vector3.Normalize(new(0.12f, -0.05f, -1f));

            RenderSnapshot Scene(float distance)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: assets.Box,
                        material: assets.Checker,
                        position: direction * distance,
                        scale: new(1.5f),
                        rotation: BoxTurn,
                        entity: 3
                    )
                );
                return snapshot;
            }

            float near = DistanceForHeight(height: 210, radius: radius, outputHeight: height);
            float far = DistanceForHeight(height: 195, radius: radius, outputHeight: height);
            Capture background = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Gpu.Snapshot(Sun),
                name: "impostor-accuracy-background"
            );
            Capture geometry = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene(near),
                name: "impostor-accuracy-geometry"
            );
            renderer.LastFrameStats.Impostors.Should().Be(0);
            // Below 200 px: captured and drawn as an impostor in the same frame.
            Gpu.RenderAndCapture(renderer: renderer, snapshot: Scene(far), name: "impostor-accuracy-195px");
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(1);
            renderer.LastFrameStats.Impostors.Should().Be(1);
            // Back at 210 px it stays an impostor (hysteresis). It moved along its view ray, so only the perspective
            // changed: r / d grew by 0.0145, times h = 210 that is above 2, so it is captured again.
            renderer.Render(Scene(near), 1f);
            renderer.LastFrameStats.Impostors.Should().Be(1);
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(1);
            Capture impostor = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene(near),
                name: "impostor-accuracy-impostor"
            );
            renderer.LastFrameStats.Impostors.Should().Be(1);
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(0);
            SaveTile(renderer, 0, "impostor-accuracy-tile");

            (float meanDifference, float overlap, int pixels) = Compare(background, geometry, impostor);
            output.WriteLine(
                $"silhouette {pixels} px, overlap (IoU) {overlap:0.000}, mean |Δ| inside {meanDifference * 255:0.00} / 255"
            );
            pixels.Should().BeGreaterThan(20_000, "the object covers a large part of the frame");
            overlap.Should().BeGreaterThan(0.97f, "the silhouettes match");
            // Documented threshold: within the silhouette the impostor differs from the geometry by less than 6/255
            // per channel on average (display-encoded). The differences come from resampling the 512 px capture to
            // ~210 px through the mip chain.
            (meanDifference * 255)
                .Should()
                .BeLessThan(6f, "the impostor looks like the geometry, in 1/255 steps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TurningTheViewByMoreThanTwoOverHRecapturesAndLessDoesNot()
    {
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            float radius = MathF.Sqrt(0.75f) * 1.5f,
                h = 100f;
            float distance = DistanceForHeight(height: h, radius: radius, outputHeight: height);
            float limit = 2f / h;

            RenderSnapshot Orbit(float angle, Quaternion? camera = null)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                Quaternion rotation = camera ?? Quaternion.Identity;
                snapshot.Camera.Rotation = rotation;
                snapshot.Camera.PreviousRotation = rotation;
                // The camera goes around the object at a constant distance: in camera-relative terms the object moves.
                var position = Vector3.Transform(
                    new(0, 0, -distance),
                    Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle)
                );
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: assets.Box,
                        material: assets.Checker,
                        position: position,
                        scale: new(1.5f),
                        rotation: BoxTurn,
                        entity: 9
                    )
                );
                return snapshot;
            }

            int Captures(RenderSnapshot snapshot, string? name = null)
            {
                if (name != null)
                    Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
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
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true, impostorTiles: 64);
        try
        {
            var assets = Assets.Create(renderer);
            MaterialId[] colors =
            [
                renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.8f, y: 0.2f, z: 0.2f, w: 1), roughness: 0.26f)),
                renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.2f, y: 0.7f, z: 0.3f, w: 1), roughness: 0.55f)),
                renderer.CreateMaterial(MaterialDesc.Color(new(x: 0.2f, y: 0.3f, z: 0.8f, w: 1), roughness: 0.38f)),
                assets.Checker,
            ];
            // 50 objects in a 10 × 5 grid, every one between 25 and 150 px tall.
            RenderSnapshot snapshot = Gpu.Snapshot(Sun);
            for (uint i = 0; i < 50; i++)
            {
                float h = 25f + 2.5f * i;
                bool box = i % 2 == 0;
                float radius = box ? MathF.Sqrt(0.75f) : 1f;
                float distance = DistanceForHeight(height: h, radius: radius, outputHeight: height);
                var direction = Quaternion.CreateFromYawPitchRoll(
                    (i % 10 - 4.5f) * 0.12f,
                    ((int)(i / 10) - 2) * 0.16f,
                    0
                );
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: box ? assets.Box : assets.Sphere,
                        material: colors[i % 4],
                        position: Vector3.Transform(new(0, 0, -distance), direction),
                        scale: Vector3.One,
                        rotation: BoxTurn,
                        entity: 1000 + i
                    )
                );
            }

            Capture? first = null;
            int captured = 0,
                frames = 0;
            while (captured < 50)
            {
                Capture image = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: snapshot,
                    name: $"impostor-budget-frame-{frames}"
                );
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
            Capture last = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "impostor-budget-all");
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(0);
            renderer.LastFrameStats.Impostors.Should().Be(50);

            // The first frame had 4 impostors and 46 objects as geometry; the last has 50 impostors. They look the same.
            double sum = 0;
            int pixels = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                Vector3 a = first!.Color(x, y),
                    b = last.Color(x, y);
                if (MaxAbs(a) < 4f / 255f && MaxAbs(b) < 4f / 255f)
                    continue;
                var d = Vector3.Abs(a - b);
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
        const int width = 640,
            height = 360;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true, impostorTiles: 4);
        try
        {
            var assets = Assets.Create(renderer);
            Vector4[] tints =
            [
                new(x: 0.9f, y: 0.1f, z: 0.1f, w: 1),
                new(x: 0.1f, y: 0.9f, z: 0.1f, w: 1),
                new(x: 0.1f, y: 0.1f, z: 0.9f, w: 1),
                new(x: 0.9f, y: 0.9f, z: 0.1f, w: 1),
                new(x: 0.1f, y: 0.9f, z: 0.9f, w: 1),
                new(x: 0.9f, y: 0.1f, z: 0.9f, w: 1),
                new(x: 0.9f, y: 0.9f, z: 0.9f, w: 1),
            ];
            MaterialId[] materials =
            [
                .. tints.Select(t => renderer.CreateMaterial(MaterialDesc.Color(t, roughness: 0.55f))),
            ];
            float far = DistanceForHeight(height: 80f, radius: 1f, outputHeight: height),
                close = DistanceForHeight(height: 320f, radius: 1f, outputHeight: height);

            RenderSnapshot Scene(params (int Object, float Distance)[] objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                foreach ((int index, float distance) in objects)
                {
                    var position = new Vector3((index - 3f) * 0.12f * distance, 0, -distance);
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: assets.Sphere,
                            material: materials[index],
                            position: position,
                            scale: Vector3.One,
                            entity: (uint)index
                        )
                    );
                }

                return snapshot;
            }

            ImpostorManager manager = renderer.Impostors!;

            int Tile(int index) =>
                manager
                    .Status(
                        new(
                            EntityIndex: (uint)index,
                            Part: 0,
                            Mesh: assets.Sphere.Value,
                            Material: MaterialStore.Slot(materials[index])
                        )
                    )
                    ?.Tile
                ?? -1;

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
                Vector3
                    .Dot(color, expected)
                    .Should()
                    .BeGreaterThan(0.95f, "tile {0} shows {1}, and object {2} is {3}", tile, color, index, expected);
            }

            renderer.Render(Scene((0, far), (1, far), (2, far), (3, far)), 1f);
            renderer.LastFrameStats.ImpostorTilesInUse.Should().Be(4);
            int[] tiles = [Tile(0), Tile(1), Tile(2), Tile(3)];
            for (int i = 0; i < 4; i++)
                AssertTileShows(tiles[i], i);
            // Object 0 leaves, then object 1; object 4 arrives and takes the least recently used tile: object 0's.
            renderer.Render(Scene((1, far), (2, far), (3, far)), 1f);
            Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene((2, far), (3, far), (4, far)),
                name: "impostor-eviction-1"
            );
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
            Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene((2, far), (3, far), (4, far), (5, far), (6, far)),
                name: "impostor-eviction-waiting"
            );
            RenderStats stats = renderer.LastFrameStats;
            (stats.Impostors, stats.ImpostorsWaiting, stats.ImpostorCaptures, stats.VisibleInstances)
                .Should()
                .Be((4, 1, 0, 5));
            // Object 2 comes close and returns to geometry: object 6 takes its tile.
            Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene((2, close), (3, far), (4, far), (5, far), (6, far)),
                name: "impostor-eviction-2"
            );
            Tile(6).Should().Be(tiles[2]);
            AssertTileShows(tiles[2], 6);
            stats = renderer.LastFrameStats;
            (stats.Impostors, stats.ImpostorsWaiting, stats.ImpostorCaptures, stats.ImpostorTilesInUse)
                .Should()
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
    private static (Capture Background, Capture Geometry, Capture Impostor) GeometryAndImpostor(
        VulkanRenderer renderer,
        Func<RenderFlags, bool, RenderSnapshot> scene,
        string name
    )
    {
        Capture background = Gpu.RenderAndCapture(
            renderer: renderer,
            snapshot: scene(RenderFlags.None, false),
            name: name + "-background"
        );
        Capture geometry = Gpu.RenderAndCapture(
            renderer: renderer,
            snapshot: scene(RenderFlags.NoImpostor, true),
            name: name + "-geometry"
        );
        renderer.LastFrameStats.Impostors.Should().Be(0, "NoImpostor keeps every object geometry");
        for (int i = 0; i < 4; i++)
            renderer.Render(scene(RenderFlags.None, true), 1f);
        Capture impostor = Gpu.RenderAndCapture(
            renderer: renderer,
            snapshot: scene(RenderFlags.None, true),
            name: name + "-impostor"
        );
        renderer.LastFrameStats.ImpostorCaptures.Should().Be(0, "every impostor is captured by now");
        return (background, geometry, impostor);
    }

    [Fact]
    public void ImpostorsWriteTheirSurfaceDepthSoTheyIntersectGeometryLikeTheObject()
    {
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            MeshId plane = renderer.CreateMesh(Primitives.Plane(1f, 10f));
            Quaternion pitch = Gpu.PitchDown(0.25f);
            float distance = DistanceForHeight(height: 150f, radius: 1f, outputHeight: height);

            // A sphere half sunk into the ground and a turned box pushed into it, both impostors; the ground is geometry.
            RenderSnapshot Scene(RenderFlags flags, bool objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                snapshot.Camera.Rotation = pitch;
                snapshot.Camera.PreviousRotation = pitch;
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: plane,
                        material: assets.Ground,
                        position: new(0, -1.2f, -20f),
                        scale: new(40f, 1f, 40f),
                        flags: RenderFlags.CastsShadow
                    )
                );
                if (objects)
                {
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: assets.Sphere,
                            material: assets.Red,
                            position: new(-0.9f, -1.2f, -distance),
                            scale: Vector3.One,
                            flags: flags | RenderFlags.CastsShadow,
                            entity: 1
                        )
                    );
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: assets.Box,
                            material: assets.Checker,
                            position: new(1.3f, -1.2f, -distance),
                            scale: new(1.1f),
                            rotation: Quaternion.CreateFromYawPitchRoll(0.5f, 0.6f, 0.3f),
                            flags: flags | RenderFlags.CastsShadow,
                            entity: 2
                        )
                    );
                }

                return snapshot;
            }

            (Capture background, Capture geometry, Capture impostor) = GeometryAndImpostor(
                renderer,
                Scene,
                "impostor-ground"
            );
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
        const int width = 960,
            height = 540;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);

            // A 400 m sphere centered at 2 km (drawn in the middle and near ranges) and a 1 km box at 50 km.
            RenderSnapshot Scene(RenderFlags flags, bool objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                if (objects)
                {
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: assets.Sphere,
                            material: assets.Red,
                            position: new(-250f, 0, -2_000f),
                            scale: new(400f),
                            flags: flags,
                            entity: 1
                        )
                    );
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: assets.Box,
                            material: assets.Checker,
                            position: new(20_000f, 3_000f, -50_000f),
                            scale: new(1_000f),
                            rotation: BoxTurn,
                            flags: flags,
                            entity: 2
                        )
                    );
                }

                return snapshot;
            }

            (Capture background, Capture geometry, Capture impostor) = GeometryAndImpostor(
                renderer,
                Scene,
                "impostor-ranges"
            );
            renderer.LastFrameStats.Impostors.Should().Be(2);
            renderer
                .LastScene.Impostors[DepthRanges.Near]
                .Count.Should()
                .Be(1, "the sphere is drawn in the near range");
            renderer
                .LastScene.Impostors[DepthRanges.Middle]
                .Count.Should()
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
        const int width = 640,
            height = 360;
        VulkanRenderer renderer = Gpu.Headless(width: width, height: height, impostors: true);
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

            Capture geometry = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Snapshot(RenderFlags.NoImpostor),
                name: "impostor-scene-geometry"
            );
            for (int i = 0; i < 4; i++)
                renderer.Render(Snapshot(RenderFlags.None), 1f);
            Capture impostors = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Snapshot(RenderFlags.None),
                name: "impostor-scene"
            );
            RenderStats stats = renderer.LastFrameStats;
            output.WriteLine($"{stats}");
            // The boxes, the opaque spheres and the station; not the ground (too large), the glass, planet or star.
            stats.Impostors.Should().Be(6);
            stats.ImpostorCaptures.Should().Be(0);

            double sum = 0;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var d = Vector3.Abs(geometry.Color(x, y) - impostors.Color(x, y));
                sum += (d.X + d.Y + d.Z) / 3f;
            }

            float mean = (float)(sum / (width * height));
            output.WriteLine($"mean |Δ| over the frame {mean * 255:0.00} / 255");
            // Captures bake the sun, the ambient light and the point and spot lights, but not the shadow maps: the
            // shadows other objects cast onto the impostors are missing, which is most of the difference.
            (mean * 255)
                .Should()
                .BeLessThan(2f, "the frame with impostors looks like the geometry, in 1/255 steps");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void SteadyStateFramesWithImpostorsDoNotAllocate()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180, impostors: true);
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
            long allocated = Allocations.Fewest(() =>
            {
                for (int i = 0; i < 20; i++)
                    renderer.Render(snapshot, i / 20f);
            });
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
        const int width = 320,
            height = 180;
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            framesInFlight: framesInFlight,
            impostors: true,
            impostorTiles: 4
        );
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
                    snapshot.Instances.Add(
                        Gpu.Instance(
                            mesh: i % 2 == 0 ? assets.Box : assets.Sphere,
                            material: assets.Checker,
                            position: position,
                            scale: Vector3.One,
                            rotation: Quaternion.CreateFromYawPitchRoll(angle, angle * 0.5f, 0),
                            entity: i
                        )
                    );
                }

                if (frame == 23)
                    Gpu.RenderAndCapture(
                        renderer: renderer,
                        snapshot: snapshot,
                        name: $"impostor-in-flight-{framesInFlight}"
                    );
                else
                    renderer.Render(snapshot, 1f);
                captures += renderer.LastFrameStats.ImpostorCaptures;
                renderer
                    .LastFrameStats.ImpostorCaptures.Should()
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
    public void AnImpostorAtlasOfNoTilesOrOfMoreLayersThanTheDeviceAllowsIsRefused()
    {
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Width = 64,
                        Height = 64,
                        ImpostorTiles = 0,
                        AntiAliasing = AntiAliasing.None,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions
            .Invoking(() =>
                VulkanRenderer.CreateHeadless(
                    new()
                    {
                        Validation = true,
                        Width = 64,
                        Height = 64,
                        ImpostorTiles = 1_000_000,
                        AntiAliasing = AntiAliasing.None,
                    }
                )
            )
            .Should()
            .ThrowExactly<ArgumentOutOfRangeException>("the atlas cannot have more layers than the device allows");
    }

    [Fact]
    public void ImpostorsAreOnByDefaultWithAnAtlasOf64Tiles()
    {
        new RendererOptions().ImpostorTiles.Should().Be(64);
        new RendererOptions().Impostors.Should().BeTrue();
    }

    [Fact]
    public void WithImpostorsOffTheRendererHasNoManagerAndNoAtlasAndASmallObjectStaysGeometry()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180, impostors: false);
        try
        {
            var assets = Assets.Create(renderer);
            renderer.Impostors.Should().BeNull();
            RenderSnapshot snapshot = Gpu.Snapshot(Sun);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: assets.Sphere,
                    material: assets.Red,
                    position: new(0, 0, -40f),
                    scale: Vector3.One,
                    entity: 1
                )
            );
            renderer.Render(snapshot, 1f);
            (
                renderer.LastFrameStats.Impostors,
                renderer.LastFrameStats.ImpostorCaptures,
                renderer.LastFrameStats.NearCommands
            )
                .Should()
                .Be((0, 0, 1));
            FluentActions
                .Invoking(() => renderer.ReadImpostorTile(0))
                .Should()
                .ThrowExactly<InvalidOperationException>();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AOneTileAtlasStillDrawsALitImpostorBecauseItUsesAnArrayViewAsTheShaderExpects()
    {
        VulkanRenderer renderer = Gpu.Headless(width: 320, height: 180, impostors: true, impostorTiles: 1);
        try
        {
            var assets = Assets.Create(renderer);
            RenderSnapshot snapshot = Gpu.Snapshot(Sun);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: assets.Sphere,
                    material: assets.Red,
                    position: new(0, 0, -40f),
                    scale: Vector3.One,
                    entity: 1
                )
            );
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "impostor-one-tile");
            renderer.LastFrameStats.Impostors.Should().Be(1);
            Vector3 center = image.Color(160, 90);
            center
                .X.Should()
                .BeGreaterThan(0.2f, "the impostor is lit")
                .And.BeGreaterThan(2 * center.Y, "it is red: {0}", center);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The atlas tile of a gray ball of radius 1 m in the sun, 12 m from the eye, whose center lies on the view's right
    /// side plane, so that its inner half shows, under an environment map that draws all six faces in the frame, after one
    /// frame. With <paramref name="withTheLamp"/>, a lamp stands 2 cm off the ball's surface, 30° from the plane's outward
    /// normal toward the eye, with a reach of 0.5 m: its reach sphere lies 0.38 m outside the plane, so only a face of the
    /// map keeps it, and it lights a part of the ball's off-screen half that the eye sees.
    /// </summary>
    private static (float[] Tile, int Lights) BallAtTheRightEdge(bool withTheLamp)
    {
        const int width = 640,
            height = 360;
        // The right side plane passes through the eye: its outward normal, and its direction in the plane y = 0.
        float tanHalfX = MathF.Tan(Gpu.Fov / 2) * width / height;
        var outward = Vector3.Normalize(new(1, 0, tanHalfX));
        var along = Vector3.Normalize(new(tanHalfX, 0, -1));
        Vector3 center = 12 * along;
        Vector3 toward = MathF.Cos(MathF.PI / 6) * outward - MathF.Sin(MathF.PI / 6) * along;
        const float reach = 0.5f;
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: height,
            impostors: true,
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = EnvironmentMapSetting.MinFaceSize,
                FacesPerFrame = CubeFaces.Count,
            }
        );
        try
        {
            MeshId sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24));
            MaterialId gray = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 0.5f, y: 0.5f, z: 0.5f, w: 1), roughness: 0.9f) with
                {
                    Reflectance = 0f,
                }
            );
            RenderSnapshot snapshot = Gpu.Snapshot(new(0, 0.3f, 1), starIrradiance: 1f);
            snapshot.Ambient.Intensity = 0f;
            snapshot.Instances.Add(
                Gpu.Instance(mesh: sphere, material: gray, position: center, scale: Vector3.One, entity: 1)
            );
            if (withTheLamp)
                snapshot.Lights.Add(
                    Gpu.Lamp(
                        position: center + 1.02f * toward,
                        color: Vector3.One,
                        intensity: (float)(reach * reach * LightUnits.CutoffIrradiance(new FixedExposure(0))),
                        chainDepth: TestChains.CamerasFrame
                    )
                );
            renderer.Render(snapshot, 1f);
            renderer.LastFrameStats.Impostors.Should().Be(1, "a ball 12 m away is about 50 px high");
            ImpostorStatus status = renderer
                .Impostors!.Status(
                    new(EntityIndex: 1, Part: 0, Mesh: sphere.Value, Material: MaterialStore.Slot(gray))
                )!
                .Value;
            return (renderer.ReadImpostorTile(status.Tile).Rgba, renderer.LastFrameStats.Lights);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnImpostorCaptureTakesNoDirectLightFromALampThatOnlyAFaceOfTheMapKeeps()
    {
        (float[] withIt, int lights) = BallAtTheRightEdge(withTheLamp: true);
        (float[] withoutIt, _) = BallAtTheRightEdge(withTheLamp: false);

        lights.Should().Be(2, "a face keeps the lamp, so the frame's one list holds the sun and the lamp");
        withoutIt.Max().Should().BePositive("the tile holds the sunlit ball");
        // The lamp lights the ball in the map's faces too, which the capture takes as a little more ambient light:
        // about 1e-5 of radiance. Its direct light would give up to 0.04.
        withIt
            .Zip(withoutIt, (a, b) => MathF.Abs(a - b))
            .Max()
            .Should()
            .BeLessThan(1e-3f, "the capture takes only the lights whose reach touches the main view");
    }

    /// <summary>
    /// The mean absolute difference per channel of two renders inside the first one's silhouette, the intersection
    /// over union of the two silhouettes, and the silhouette's size. A pixel is in a silhouette when it differs from
    /// the empty background.
    /// </summary>
    private static (float MeanDifference, float Overlap, int Pixels) Compare(Capture background, Capture a, Capture b)
    {
        double sum = 0;
        int inA = 0,
            inB = 0,
            both = 0;
        for (int y = 0; y < a.Height; y++)
        for (int x = 0; x < a.Width; x++)
        {
            Vector3 bg = background.Color(x, y);
            Vector3 ca = a.Color(x, y),
                cb = b.Color(x, y);
            bool maskA = MaxAbs(ca - bg) > 4f / 255f;
            bool maskB = MaxAbs(cb - bg) > 4f / 255f;
            inA += maskA ? 1 : 0;
            inB += maskB ? 1 : 0;
            both += maskA && maskB ? 1 : 0;
            if (maskA)
            {
                var d = Vector3.Abs(ca - cb);
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
        new ImageWriter().WritePng(
            data: pixels,
            width: size * 2,
            height: size,
            components: ColorComponents.RedGreenBlueAlpha,
            dest: stream
        );
    }

    private static float Srgb(float c) => c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);
}
