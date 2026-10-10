using System.Numerics;
using StbImageWriteSharp;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>Impostors on the GPU: switching with hysteresis, capture accuracy, recapture, budget, eviction.</summary>
[Collection(GpuCollection.Name)]
public sealed class ImpostorTests(ITestOutputHelper output)
{
    private static readonly Vector3 Sun = Vector3.Normalize(new Vector3(0.5f, 0.6f, 0.62f));

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
            return new Assets
            {
                Box = renderer.CreateMesh(Primitives.Box(new Vector3(0.5f))),
                Sphere = renderer.CreateMesh(Primitives.Sphere(1f, 48, 24)),
                Checker = renderer.CreateMaterial(new MaterialDesc(Vector4.One, SpecularIntensity: 0.6f, Gloss: 0.6f,
                    Albedo: checker, Normal: tiles, SpecularGloss: TextureId.None)),
                Red = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.75f, 0.08f, 0.06f, 1), gloss: 0.8f) with { SpecularIntensity = 1f }),
                Ground = renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.5f, 0.5f, 0.5f, 1), gloss: 0.2f)),
            };
        }
    }

    /// <summary>A unit box (half size 0.5, bounding radius √0.75) turned so three faces show.</summary>
    private static readonly Quaternion BoxTurn = Quaternion.CreateFromYawPitchRoll(0.6f, 0.35f, 0.1f);

    [Fact]
    public void ObjectBecomesAnImpostorBelow200PixelsAndReturnsAbove300WithHysteresis()
    {
        const int width = 960, height = 540;
        VulkanRenderer renderer = Gpu.Headless(width, height, impostors: true);
        try
        {
            var assets = Assets.Create(renderer);
            var key = new ImpostorKey(7, assets.Sphere.Value, (uint)assets.Red.Value);
            // Heights in pixels and the expected result: geometry above 300, impostor below 200, and between them
            // whatever the object was before.
            (float Height, bool Impostor)[] steps =
            [
                (250, false), (210, false), (190, true), (250, true), (295, true), (310, false), (250, false), (205, false), (199, true),
            ];
            int frame = 0;
            foreach ((float h, bool expected) in steps)
            {
                float distance = DistanceForHeight(h, 1f, height);
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new Vector3(0, 0, -distance), Vector3.One, entity: 7));
                Capture image = Gpu.RenderAndCapture(renderer, snapshot, $"impostor-hysteresis-{frame++}-{h:0}px");
                RenderStats stats = renderer.LastFrameStats;
                Assert.True(renderer.Impostors!.TryGetStatus(key, out ImpostorStatus status));
                Assert.Equal(h, status.ProjectedHeight, 0.5f);
                Assert.Equal(expected, status.Impostor);
                Assert.Equal(expected ? 1 : 0, stats.Impostors);
                Assert.Equal(1, stats.VisibleInstances);
                // Drawn either way: the center pixel is the lit red sphere.
                Vector3 center = image.Color(width / 2, height / 2);
                Assert.True(center.X > 0.3f && center.X > 2 * center.Y, $"{h} px: center {center}");
            }
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
            var direction = Vector3.Normalize(new Vector3(0.12f, -0.05f, -1f));
            RenderSnapshot Scene(float distance)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                snapshot.Instances.Add(Gpu.Instance(assets.Box, assets.Checker, direction * distance, new Vector3(1.5f), BoxTurn, entity: 3));
                return snapshot;
            }

            float near = DistanceForHeight(210, radius, height);
            float far = DistanceForHeight(195, radius, height);
            Capture background = Gpu.RenderAndCapture(renderer, Gpu.Snapshot(Sun), "impostor-accuracy-background");
            Capture geometry = Gpu.RenderAndCapture(renderer, Scene(near), "impostor-accuracy-geometry");
            Assert.Equal(0, renderer.LastFrameStats.Impostors);
            // Below 200 px: captured and drawn as an impostor in the same frame.
            Gpu.RenderAndCapture(renderer, Scene(far), "impostor-accuracy-195px");
            Assert.Equal(1, renderer.LastFrameStats.ImpostorCaptures);
            Assert.Equal(1, renderer.LastFrameStats.Impostors);
            // Back at 210 px it stays an impostor (hysteresis). It moved along its view ray, so only the perspective
            // changed: r / d grew by 0.0145, times h = 210 that is above 2, so it is captured again.
            renderer.Render(Scene(near), 1f);
            Assert.Equal(1, renderer.LastFrameStats.Impostors);
            Assert.Equal(1, renderer.LastFrameStats.ImpostorCaptures);
            Capture impostor = Gpu.RenderAndCapture(renderer, Scene(near), "impostor-accuracy-impostor");
            Assert.Equal(1, renderer.LastFrameStats.Impostors);
            Assert.Equal(0, renderer.LastFrameStats.ImpostorCaptures);
            SaveTile(renderer, 0, "impostor-accuracy-tile");

            (float meanDifference, float overlap, int pixels) = Compare(background, geometry, impostor);
            output.WriteLine($"silhouette {pixels} px, overlap (IoU) {overlap:0.000}, mean |Δ| inside {meanDifference * 255:0.00} / 255");
            Assert.True(pixels > 20_000, $"the object covers {pixels} px");
            Assert.True(overlap > 0.97f, $"silhouette IoU {overlap}");
            // Documented threshold: within the silhouette the impostor differs from the geometry by less than 6/255
            // per channel on average (display-encoded). The differences come from resampling the 512 px capture to
            // ~210 px through the mip chain, and from the orthographic capture of a perspective view.
            Assert.True(meanDifference < 6f / 255f, $"mean difference {meanDifference * 255} / 255");
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
                Vector3 position = Vector3.Transform(new Vector3(0, 0, -distance), Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
                snapshot.Instances.Add(Gpu.Instance(assets.Box, assets.Checker, position, new Vector3(1.5f), BoxTurn, entity: 9));
                return snapshot;
            }
            int Captures(RenderSnapshot snapshot, string? name = null)
            {
                if (name != null)
                    Gpu.RenderAndCapture(renderer, snapshot, name);
                else
                    renderer.Render(snapshot, 1f);
                Assert.Equal(1, renderer.LastFrameStats.Impostors);
                return renderer.LastFrameStats.ImpostorCaptures;
            }

            Assert.Equal(1, Captures(Orbit(0f), "impostor-recapture-0"));
            Assert.Equal(0, Captures(Orbit(0.5f * limit)));
            Assert.Equal(0, Captures(Orbit(0.95f * limit)));
            Assert.Equal(1, Captures(Orbit(1.05f * limit)));
            // Turning the camera in place changes nothing about the object's view.
            Assert.Equal(0, Captures(Orbit(1.05f * limit, Quaternion.CreateFromYawPitchRoll(0.1f, 0.08f, 0.2f))));
            Assert.Equal(0, Captures(Orbit(2f * limit)));
            Assert.Equal(1, Captures(Orbit(2.2f * limit)));
            // A large orbit: the capture follows, and the impostor shows the box from the new side.
            Assert.Equal(1, Captures(Orbit(0.6f), "impostor-recapture-orbit"));
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
                renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.8f, 0.2f, 0.2f, 1), gloss: 0.7f)),
                renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.2f, 0.7f, 0.3f, 1), gloss: 0.3f)),
                renderer.CreateMaterial(MaterialDesc.Color(new Vector4(0.2f, 0.3f, 0.8f, 1), gloss: 0.5f)),
                assets.Checker,
            ];
            // 50 objects in a 10 × 5 grid, every one between 25 and 150 px tall.
            RenderSnapshot snapshot = Gpu.Snapshot(Sun);
            for (uint i = 0; i < 50; i++)
            {
                float h = 25f + 2.5f * i;
                bool box = i % 2 == 0;
                float radius = box ? MathF.Sqrt(0.75f) : 1f;
                float distance = DistanceForHeight(h, radius, height);
                Quaternion direction = Quaternion.CreateFromYawPitchRoll((i % 10 - 4.5f) * 0.12f, ((int)(i / 10) - 2) * 0.16f, 0);
                snapshot.Instances.Add(Gpu.Instance(box ? assets.Box : assets.Sphere, colors[i % 4],
                    Vector3.Transform(new Vector3(0, 0, -distance), direction), Vector3.One, BoxTurn, entity: 1000 + i));
            }

            Capture? first = null;
            int captured = 0, frames = 0;
            while (captured < 50)
            {
                Capture image = Gpu.RenderAndCapture(renderer, snapshot, $"impostor-budget-frame-{frames}");
                first ??= image;
                frames++;
                RenderStats stats = renderer.LastFrameStats;
                Assert.Equal(Math.Min(4, 50 - captured), stats.ImpostorCaptures);
                captured += stats.ImpostorCaptures;
                Assert.Equal(captured, stats.Impostors);
                Assert.Equal(50 - captured, stats.ImpostorsWaiting);
                Assert.Equal(50, stats.VisibleInstances);
                Assert.Equal(captured, stats.ImpostorTilesInUse);
            }
            Assert.Equal(13, frames);
            Capture last = Gpu.RenderAndCapture(renderer, snapshot, "impostor-budget-all");
            Assert.Equal(0, renderer.LastFrameStats.ImpostorCaptures);
            Assert.Equal(50, renderer.LastFrameStats.Impostors);

            // The first frame had 4 impostors and 46 objects as geometry; the last has 50 impostors. They look the same.
            double sum = 0;
            int pixels = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 a = first!.Color(x, y), b = last.Color(x, y);
                    if (MaxAbs(a) < 4f / 255f && MaxAbs(b) < 4f / 255f)
                        continue;
                    Vector3 d = Vector3.Abs(a - b);
                    sum += (d.X + d.Y + d.Z) / 3f;
                    pixels++;
                }
            }
            float mean = (float)(sum / pixels);
            output.WriteLine($"{pixels} lit pixels, mean |Δ| {mean * 255:0.00} / 255");
            Assert.True(mean < 6f / 255f, $"geometry and impostors differ by {mean * 255} / 255 on average");
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
            Vector4[] tints = [new(0.9f, 0.1f, 0.1f, 1), new(0.1f, 0.9f, 0.1f, 1), new(0.1f, 0.1f, 0.9f, 1), new(0.9f, 0.9f, 0.1f, 1), new(0.1f, 0.9f, 0.9f, 1), new(0.9f, 0.1f, 0.9f, 1), new(0.9f, 0.9f, 0.9f, 1)];
            MaterialId[] materials = tints.Select(t => renderer.CreateMaterial(MaterialDesc.Color(t, gloss: 0.3f))).ToArray();
            float far = DistanceForHeight(80f, 1f, height), close = DistanceForHeight(320f, 1f, height);
            RenderSnapshot Scene(params (int Object, float Distance)[] objects)
            {
                RenderSnapshot snapshot = Gpu.Snapshot(Sun);
                foreach ((int index, float distance) in objects)
                {
                    var position = new Vector3((index - 3f) * 0.12f * distance, 0, -distance);
                    snapshot.Instances.Add(Gpu.Instance(assets.Sphere, materials[index], position, Vector3.One, entity: (uint)index));
                }
                return snapshot;
            }
            ImpostorManager manager = renderer.Impostors!;
            int Tile(int index) => manager.TryGetStatus(new ImpostorKey((uint)index, assets.Sphere.Value, (uint)materials[index].Value), out ImpostorStatus s) ? s.Tile : -1;
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
                Vector3 color = TileColor(tile), expected = Vector3.Normalize(new Vector3(tints[index].X, tints[index].Y, tints[index].Z));
                Assert.True(Vector3.Dot(color, expected) > 0.95f, $"tile {tile} shows {color}, expected object {index} ({expected})");
            }

            renderer.Render(Scene((0, far), (1, far), (2, far), (3, far)), 1f);
            Assert.Equal(4, renderer.LastFrameStats.ImpostorTilesInUse);
            int[] tiles = [Tile(0), Tile(1), Tile(2), Tile(3)];
            for (int i = 0; i < 4; i++)
                AssertTileShows(tiles[i], i);
            // Object 0 leaves, then object 1; object 4 arrives and takes the least recently used tile: object 0's.
            renderer.Render(Scene((1, far), (2, far), (3, far)), 1f);
            Gpu.RenderAndCapture(renderer, Scene((2, far), (3, far), (4, far)), "impostor-eviction-1");
            Assert.Equal(1, renderer.LastFrameStats.ImpostorCaptures);
            Assert.Equal(tiles[0], Tile(4));
            Assert.Equal(-1, Tile(0));
            AssertTileShows(tiles[0], 4);
            SaveTile(renderer, tiles[0], "impostor-eviction-tile-reused");
            // Object 5 takes object 1's tile, the only one left whose key is gone.
            renderer.Render(Scene((2, far), (3, far), (4, far), (5, far)), 1f);
            Assert.Equal(tiles[1], Tile(5));
            AssertTileShows(tiles[1], 5);
            // Object 6 finds every tile in use by a drawn impostor: it waits, drawn as geometry.
            Gpu.RenderAndCapture(renderer, Scene((2, far), (3, far), (4, far), (5, far), (6, far)), "impostor-eviction-waiting");
            RenderStats stats = renderer.LastFrameStats;
            Assert.Equal((4, 1, 0, 5), (stats.Impostors, stats.ImpostorsWaiting, stats.ImpostorCaptures, stats.VisibleInstances));
            // Object 2 comes close and returns to geometry: object 6 takes its tile.
            Gpu.RenderAndCapture(renderer, Scene((2, close), (3, far), (4, far), (5, far), (6, far)), "impostor-eviction-2");
            Assert.Equal(tiles[2], Tile(6));
            AssertTileShows(tiles[2], 6);
            stats = renderer.LastFrameStats;
            Assert.Equal((4, 0, 1, 4), (stats.Impostors, stats.ImpostorsWaiting, stats.ImpostorCaptures, stats.ImpostorTilesInUse));
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
        Assert.Equal(0, renderer.LastFrameStats.Impostors);
        for (int i = 0; i < 4; i++)
            renderer.Render(scene(RenderFlags.None, true), 1f);
        Capture impostor = Gpu.RenderAndCapture(renderer, scene(RenderFlags.None, true), name + "-impostor");
        Assert.Equal(0, renderer.LastFrameStats.ImpostorCaptures);
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
                snapshot.Instances.Add(Gpu.Instance(plane, assets.Ground, new Vector3(0, -1.2f, -20f), new Vector3(40f, 1f, 40f), flags: RenderFlags.CastsShadow));
                if (objects)
                {
                    snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new Vector3(-0.9f, -1.2f, -distance), Vector3.One, flags: flags | RenderFlags.CastsShadow, entity: 1));
                    snapshot.Instances.Add(Gpu.Instance(assets.Box, assets.Checker, new Vector3(1.3f, -1.2f, -distance), new Vector3(1.1f),
                        Quaternion.CreateFromYawPitchRoll(0.5f, 0.6f, 0.3f), flags | RenderFlags.CastsShadow, entity: 2));
                }
                return snapshot;
            }
            (Capture background, Capture geometry, Capture impostor) = GeometryAndImpostor(renderer, Scene, "impostor-ground");
            Assert.Equal(2, renderer.LastFrameStats.Impostors);
            (float mean, float overlap, int pixels) = Compare(background, geometry, impostor);
            output.WriteLine($"silhouette {pixels} px, IoU {overlap:0.000}, mean |Δ| {mean * 255:0.00} / 255");
            // A flat billboard would cut the objects off with a straight line where they meet the ground; the
            // captured depth gives the same curved intersection as the geometry.
            Assert.True(overlap > 0.97f, $"IoU {overlap}");
            Assert.True(mean < 8f / 255f, $"mean difference {mean * 255} / 255");
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
                    snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new Vector3(-250f, 0, -2_000f), new Vector3(400f), flags: flags, entity: 1));
                    snapshot.Instances.Add(Gpu.Instance(assets.Box, assets.Checker, new Vector3(20_000f, 3_000f, -50_000f), new Vector3(1_000f), BoxTurn, flags, entity: 2));
                }
                return snapshot;
            }
            (Capture background, Capture geometry, Capture impostor) = GeometryAndImpostor(renderer, Scene, "impostor-ranges");
            Assert.Equal(2, renderer.LastFrameStats.Impostors);
            Assert.True(renderer.LastScene.Impostors[DepthRanges.Near].Count == 1 && renderer.LastScene.Impostors[DepthRanges.Middle].Count == 2,
                "the sphere is drawn in the near and middle ranges, the box in the middle range");
            (float mean, float overlap, int pixels) = Compare(background, geometry, impostor);
            output.WriteLine($"silhouette {pixels} px, IoU {overlap:0.000}, mean |Δ| {mean * 255:0.00} / 255");
            Assert.True(overlap > 0.97f, $"IoU {overlap}");
            Assert.True(mean < 6f / 255f, $"mean difference {mean * 255} / 255");
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
            Capture geometry = Gpu.RenderAndCapture(renderer, Snapshot(RenderFlags.NoImpostor), "impostor-scene-geometry");
            for (int i = 0; i < 4; i++)
                renderer.Render(Snapshot(RenderFlags.None), 1f);
            Capture impostors = Gpu.RenderAndCapture(renderer, Snapshot(RenderFlags.None), "impostor-scene");
            RenderStats stats = renderer.LastFrameStats;
            output.WriteLine($"{stats}");
            // The boxes, the opaque spheres and the station; not the ground (too large), the glass, planet or star.
            Assert.Equal(6, stats.Impostors);
            Assert.Equal(0, stats.ImpostorCaptures);

            double sum = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Vector3 d = Vector3.Abs(geometry.Color(x, y) - impostors.Color(x, y));
                    sum += (d.X + d.Y + d.Z) / 3f;
                }
            }
            float mean = (float)(sum / (width * height));
            output.WriteLine($"mean |Δ| over the frame {mean * 255:0.00} / 255");
            // Captures bake the sun, the ambient light and the point and spot lights, but not the shadow maps: the
            // shadows other objects cast onto the impostors are missing, which is most of the difference.
            Assert.True(mean < 2f / 255f, $"mean difference {mean * 255} / 255");
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
            Assert.True(renderer.LastFrameStats.Impostors > 0);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 20; i++)
                renderer.Render(snapshot, i / 20f);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
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
                    snapshot.Instances.Add(Gpu.Instance(i % 2 == 0 ? assets.Box : assets.Sphere, assets.Checker, position, Vector3.One,
                        Quaternion.CreateFromYawPitchRoll(angle, angle * 0.5f, 0), entity: i));
                }
                if (frame == 23)
                    Gpu.RenderAndCapture(renderer, snapshot, $"impostor-in-flight-{framesInFlight}");
                else
                    renderer.Render(snapshot, 1f);
                captures += renderer.LastFrameStats.ImpostorCaptures;
                Assert.InRange(renderer.LastFrameStats.ImpostorCaptures, 1, 4);
            }
            Assert.True(captures >= 24, $"{captures} captures");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ImpostorOptionsAreCheckedAndImpostorsCanBeTurnedOff()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => VulkanRenderer.CreateHeadless(new RendererOptions { Width = 64, Height = 64, ImpostorTiles = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => VulkanRenderer.CreateHeadless(new RendererOptions { Validation = true, Width = 64, Height = 64, ImpostorTiles = 1_000_000 }));
        Assert.Equal(64, new RendererOptions().ImpostorTiles);
        Assert.True(new RendererOptions().Impostors);

        // Off: no manager, no atlas, and a small object stays geometry.
        VulkanRenderer renderer = Gpu.Headless(320, 180, impostors: false);
        try
        {
            var assets = Assets.Create(renderer);
            Assert.Null(renderer.Impostors);
            RenderSnapshot snapshot = Gpu.Snapshot(Sun);
            snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new Vector3(0, 0, -40f), Vector3.One, entity: 1));
            renderer.Render(snapshot, 1f);
            Assert.Equal((0, 0, 1), (renderer.LastFrameStats.Impostors, renderer.LastFrameStats.ImpostorCaptures, renderer.LastFrameStats.NearCommands));
            Assert.Throws<InvalidOperationException>(() => renderer.ReadImpostorTile(0));
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
            snapshot.Instances.Add(Gpu.Instance(assets.Sphere, assets.Red, new Vector3(0, 0, -40f), Vector3.One, entity: 1));
            Capture image = Gpu.RenderAndCapture(renderer, snapshot, "impostor-one-tile");
            Assert.Equal(1, renderer.LastFrameStats.Impostors);
            Vector3 center = image.Color(160, 90);
            Assert.True(center.X > 0.2f && center.X > 2 * center.Y, $"center {center}");
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
        {
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
        }
        return ((float)(sum / Math.Max(inA, 1)), both / (float)Math.Max(inA + inB - both, 1), inA);
    }

    private static float MaxAbs(Vector3 v) => MathF.Max(MathF.Abs(v.X), MathF.Max(MathF.Abs(v.Y), MathF.Abs(v.Z)));

    /// <summary>Saves an atlas tile as a PNG: the premultiplied color tone-mapped over a dark checker, and the depth offset beside it.</summary>
    internal static void SaveTile(VulkanRenderer renderer, int tile, string name)
    {
        (float[] rgba, float[] depth) = renderer.ReadImpostorTile(tile);
        const int size = ImpostorAtlas.TileSize;
        var pixels = new byte[size * 2 * size * 4];
        for (int y = 0; y < size; y++)
        {
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
        }
        string path = Gpu.CapturePath(name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using FileStream stream = File.Create(path);
        new ImageWriter().WritePng(pixels, size * 2, size, ColorComponents.RedGreenBlueAlpha, stream);
    }

    private static float Srgb(float c) => c <= 0.0031308f ? 12.92f * c : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;

    private static byte ToByte(float v) => (byte)Math.Clamp(MathF.Round(v * 255f), 0, 255);
}
