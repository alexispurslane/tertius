using System.Numerics;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The impostor decisions on the CPU, through the real batcher: hysteresis, the 2 / h recapture rule for every
/// error term, the capture budget and priorities, tile eviction, exclusions and allocation.
/// </summary>
public sealed class ImpostorManagerTests
{
    private const int Width = 1280, Height = 720;
    private static readonly MeshInfo[] Meshes = [new(0, 36, 0, MathF.Sqrt(0.75f)), new(36, 600, 24, 1f)];
    private const int Sphere = 1;

    private sealed class Harness
    {
        public readonly SceneBatcher Batcher = new();
        public readonly PreparedScene Scene = new();
        public readonly ImpostorManager Manager;

        public Harness(int tiles = 64)
        {
            Manager = new ImpostorManager(tiles);
            Batcher.Processors.Add(Manager);
        }

        public PreparedScene Run(RenderSnapshot snapshot)
        {
            Batcher.Prepare(snapshot, 1f, Width, Height, Meshes, 8, Scene);
            return Scene;
        }

        public ImpostorStatus Status(uint entity, int mesh = Sphere, uint material = 1)
        {
            Assert.True(Manager.TryGetStatus(new ImpostorKey(entity, mesh, material), out ImpostorStatus status), $"entity {entity} is unknown");
            return status;
        }
    }

    private static readonly Vector3 SunDirection = Vector3.Normalize(new Vector3(0.3f, 0.8f, 0.5f));

    private static RenderSnapshot Snapshot(Vector3? sun = null, float intensity = 3f)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(sun ?? SunDirection, intensity);
        return snapshot;
    }

    /// <summary>The distance at which a unit sphere has projected height <paramref name="height"/> on the 720 px output.</summary>
    private static float DistanceFor(float height, float radius = 1f)
    {
        float tangent = height * MathF.Tan(Gpu.Fov / 2) / Height;
        return radius * MathF.Sqrt(1 + tangent * tangent) / tangent;
    }

    private static RenderInstance Ball(uint entity, Vector3 position, Quaternion? rotation = null, RenderFlags flags = RenderFlags.None, Vector3? scale = null) =>
        Gpu.Instance(new MeshId(Sphere), new MaterialId(1), position, scale ?? Vector3.One, rotation, flags, entity: entity);

    [Fact]
    public void ProjectedHeightIsTheDiameterOfTheSpheresImageAtTheCenterOfTheScreen()
    {
        var camera = new CameraView { Height = Height, TanHalfY = MathF.Tan(Gpu.Fov / 2) };
        float distance = 12f, radius = 2f;
        // The tangent cone's half angle, projected: the image's top and bottom edges are at ±tan(asin(r / d)).
        float top = Gpu.Project(new Vector3(0, distance * MathF.Tan(MathF.Asin(radius / distance)), -distance), Width, Height).Y;
        float bottom = Gpu.Project(new Vector3(0, -distance * MathF.Tan(MathF.Asin(radius / distance)), -distance), Width, Height).Y;
        Assert.Equal(bottom - top, ImpostorManager.ProjectedHeight(distance, radius, camera), 0.01f);
        Assert.Equal(float.PositiveInfinity, ImpostorManager.ProjectedHeight(1.5f, 2f, camera));
        Assert.Equal(200f, ImpostorManager.ProjectedHeight(DistanceFor(200, 3f), 3f, camera), 0.01f);
    }

    [Fact]
    public void HysteresisSwitchesBelow200AndBackAbove300()
    {
        var harness = new Harness();
        (float Height, bool Impostor)[] steps = [(320, false), (250, false), (199, true), (250, true), (299, true), (301, false), (201, false), (150, true)];
        foreach ((float h, bool expected) in steps)
        {
            RenderSnapshot snapshot = Snapshot();
            snapshot.Instances.Add(Ball(1, new Vector3(0, 0, -DistanceFor(h))));
            PreparedScene scene = harness.Run(snapshot);
            Assert.Equal(expected, harness.Status(1).Impostor);
            // An impostor with a capture leaves the mesh batch; geometry stays in it.
            Assert.Equal(expected ? 0 : 1, scene.VisibleCount);
            Assert.Equal(expected ? 1 : 0, harness.Manager.Draws.Length);
        }
    }

    [Fact]
    public void NoImpostorCelestialTransparentAndNearlyEnclosingObjectsStayGeometry()
    {
        var harness = new Harness();
        RenderSnapshot snapshot = Snapshot();
        snapshot.Instances.Add(Ball(1, new Vector3(-30, 0, -200), flags: RenderFlags.NoImpostor));
        snapshot.Instances.Add(Ball(2, new Vector3(-10, 0, -200), flags: RenderFlags.Celestial));
        snapshot.Instances.Add(Ball(3, new Vector3(10, 0, -200), flags: RenderFlags.Transparent));
        snapshot.Instances.Add(Ball(4, new Vector3(30, 0, -200)));
        PreparedScene scene = harness.Run(snapshot);
        Assert.Equal(3, scene.VisibleCount);
        Assert.Equal(1, harness.Manager.Draws.Length);
        Assert.False(harness.Manager.TryGetStatus(new ImpostorKey(1, Sphere, 1), out _));
        Assert.False(harness.Manager.TryGetStatus(new ImpostorKey(2, Sphere, 1), out _));
        Assert.False(harness.Manager.TryGetStatus(new ImpostorKey(3, Sphere, 1), out _));
        Assert.True(harness.Status(4).Impostor);
    }

    [Fact]
    public void ASecondInstanceWithTheSameKeyInOneFrameStaysGeometry()
    {
        var harness = new Harness();
        RenderSnapshot snapshot = Snapshot();
        snapshot.Instances.Add(Ball(5, new Vector3(-5, 0, -100)));
        snapshot.Instances.Add(Ball(5, new Vector3(5, 0, -100)));
        PreparedScene scene = harness.Run(snapshot);
        Assert.Equal(1, harness.Manager.Draws.Length);
        Assert.Equal(1, scene.VisibleCount);
        Assert.Equal(1, harness.Manager.Captures.Length);
    }

    /// <summary>Captures an object at <paramref name="height"/> px and returns the harness and the object's distance.</summary>
    private static (Harness Harness, float Distance) Captured(float height, Vector3? sun = null)
    {
        var harness = new Harness();
        float distance = DistanceFor(height);
        RenderSnapshot snapshot = Snapshot(sun);
        snapshot.Instances.Add(Ball(1, new Vector3(0, 0, -distance)));
        harness.Run(snapshot);
        Assert.Equal(1, harness.Manager.Captures.Length);
        return (harness, distance);
    }

    private static int CapturesAfter(Harness harness, RenderInstance instance, Vector3? sun = null, float intensity = 3f, Quaternion? camera = null)
    {
        RenderSnapshot snapshot = Snapshot(sun, intensity);
        if (camera is Quaternion rotation)
        {
            snapshot.Camera.Rotation = rotation;
            snapshot.Camera.PreviousRotation = rotation;
        }
        snapshot.Instances.Add(instance);
        harness.Run(snapshot);
        Assert.Equal(1, harness.Manager.Draws.Length);
        return harness.Manager.Captures.Length;
    }

    [Theory]
    [InlineData(150f)]
    [InlineData(60f)]
    [InlineData(12f)]
    public void ChangingTheViewDirectionRecapturesAfterTwoOverH(float height)
    {
        // The camera orbits the object at a constant distance: the view direction turns by the angle, nothing else changes.
        (Harness harness, float distance) = Captured(height);
        float limit = 2f / height;
        Vector3 At(float angle) => Vector3.Transform(new Vector3(0, 0, -distance), Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
        Assert.Equal(0, CapturesAfter(harness, Ball(1, At(0.5f * limit))));
        Assert.Equal(0, CapturesAfter(harness, Ball(1, At(0.95f * limit))));
        Assert.Equal(1, CapturesAfter(harness, Ball(1, At(1.05f * limit))));
        // Measured from the new capture now.
        Assert.Equal(0, CapturesAfter(harness, Ball(1, At(1.95f * limit))));
        Assert.Equal(1, CapturesAfter(harness, Ball(1, At(2.1f * limit))));
    }

    [Fact]
    public void TheObjectsOwnRotationCountsButARollAboutTheViewAxisDoesNot()
    {
        // The sun is behind the camera, on the view axis, so a roll about that axis leaves it unchanged in object space too.
        float height = 80f;
        (Harness harness, float distance) = Captured(height, sun: Vector3.UnitZ);
        Vector3 position = new(0, 0, -distance);
        float limit = 2f / height;
        Quaternion Turn(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
        Assert.Equal(0, CapturesAfter(harness, Ball(1, position, Turn(0.9f * limit)), sun: Vector3.UnitZ));
        Assert.Equal(1, CapturesAfter(harness, Ball(1, position, Turn(1.1f * limit)), sun: Vector3.UnitZ));

        // A large roll about the view axis: no recapture, and the billboard turns with the object.
        Quaternion roll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f) * Turn(1.1f * limit);
        Assert.Equal(0, CapturesAfter(harness, Ball(1, position, roll), sun: Vector3.UnitZ));
        ImpostorDraw draw = harness.Manager.Draws[0];
        float angle = MathF.Atan2(draw.Right.Y, draw.Right.X);
        Assert.Equal(1.2f, angle, 0.02f);
    }

    [Fact]
    public void TurningTheCameraInPlaceDoesNotRecapture()
    {
        (Harness harness, float distance) = Captured(120f);
        Quaternion turned = Quaternion.CreateFromYawPitchRoll(0.2f, 0.15f, 0.3f);
        Assert.Equal(0, CapturesAfter(harness, Ball(1, new Vector3(0, 0, -distance)), camera: turned));
    }

    [Fact]
    public void TheSunDirectionInObjectSpaceRecapturesAfterTwoOverH()
    {
        float height = 100f;
        (Harness harness, float distance) = Captured(height);
        var position = new Vector3(0, 0, -distance);
        Vector3 Sun(float angle) => Vector3.Transform(SunDirection, Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(SunDirection, Vector3.UnitX)), angle));
        float limit = 2f / height;
        Assert.Equal(0, CapturesAfter(harness, Ball(1, position), sun: Sun(0.9f * limit)));
        Assert.Equal(1, CapturesAfter(harness, Ball(1, position), sun: Sun(1.1f * limit)));
    }

    [Fact]
    public void ChangingTheDistanceRecapturesWhenThePerspectiveChangesByAboutAPixel()
    {
        // The parallax term: |Δ(r / d)| · h > 2.
        float height = 150f;
        (Harness harness, float distance) = Captured(height);
        float parallax = 1f / distance;
        // Distances whose r / d differ from the capture's by 0.9 and 1.1 times 2 / h (h barely changes over this step).
        float Closer(float factor) => 1f / (parallax + factor * 2f / height);
        Assert.Equal(0, CapturesAfter(harness, Ball(1, new Vector3(0, 0, -Closer(0.9f)))));
        Assert.Equal(1, CapturesAfter(harness, Ball(1, new Vector3(0, 0, -Closer(1.1f)))));
    }

    [Fact]
    public void ALightingChangeRecapturesButASmallOneDoesNot()
    {
        float height = 100f;
        (Harness harness, float distance) = Captured(height);
        var position = new Vector3(0, 0, -distance);
        // The ambient total is small next to the sun (3 × white), so 0.5% of the sun is below 2 / h of the total.
        Assert.Equal(0, CapturesAfter(harness, Ball(1, position), intensity: 3f * 1.005f));
        Assert.Equal(1, CapturesAfter(harness, Ball(1, position), intensity: 3f * 1.5f));

        // An eclipse: a moon slides in front of the sun as seen from the object.
        RenderSnapshot snapshot = Snapshot(intensity: 3f * 1.5f);
        snapshot.Instances.Add(Ball(1, position));
        snapshot.Occluders.Add(new RenderOccluder { Center = position + SunDirection * 1_000f, Radius = 20f });
        harness.Run(snapshot);
        Assert.Equal(1, harness.Manager.Captures.Length);
    }

    [Fact]
    public void PointAndSpotLightsAreBakedSoChangingThemRecaptures()
    {
        float height = 100f;
        var harness = new Harness();
        float distance = DistanceFor(height);
        var position = new Vector3(0, 0, -distance);
        RenderSnapshot Scene(Vector3 lightOffset, float intensity = 5f, float range = 6f, bool spot = false, float spotTurn = 0f)
        {
            RenderSnapshot snapshot = Snapshot();
            snapshot.Instances.Add(Ball(1, position));
            Vector3 lightPosition = position + lightOffset;
            snapshot.Lights.Add(new RenderLight
            {
                Kind = spot ? LightKind.Spot : LightKind.Point,
                Position = lightPosition,
                Direction = Vector3.Transform(Vector3.Normalize(position - lightPosition), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, spotTurn)),
                Color = new Vector3(1f, 0.8f, 0.6f),
                Intensity = intensity,
                Range = range,
                InnerConeAngle = 0.3f,
                OuterConeAngle = 0.5f,
            });
            return snapshot;
        }
        int Captures(RenderSnapshot snapshot)
        {
            harness.Run(snapshot);
            return harness.Manager.Captures.Length;
        }

        Assert.Equal(1, Captures(Scene(new Vector3(2.5f, 1f, 0))));
        Assert.Equal(0, Captures(Scene(new Vector3(2.5f, 1f, 0))));
        // The light goes around the object at the same distance: same irradiance, other direction.
        Assert.Equal(1, Captures(Scene(new Vector3(-2.5f, 1f, 0))));
        // It brightens.
        Assert.Equal(1, Captures(Scene(new Vector3(-2.5f, 1f, 0), intensity: 8f)));
        // It moves out of reach: the object loses its light. Out there, it can move without effect.
        Assert.Equal(1, Captures(Scene(new Vector3(-9f, 1f, 0), intensity: 8f)));
        Assert.Equal(0, Captures(Scene(new Vector3(-12f, 3f, 0), intensity: 8f)));
        // A spot light aimed at the object, then turned away from it.
        Assert.Equal(1, Captures(Scene(new Vector3(0, 3f, 0), spot: true)));
        Assert.Equal(0, Captures(Scene(new Vector3(0, 3f, 0), spot: true)));
        Assert.Equal(1, Captures(Scene(new Vector3(0, 3f, 0), spot: true, spotTurn: 0.8f)));
    }

    [Fact]
    public void ANonUniformScaleChangeRecapturesButAUniformOneOnlyResizesTheBillboard()
    {
        float height = 100f;
        (Harness harness, float distance) = Captured(height);
        var position = new Vector3(0, 0, -distance);
        float extent = harness.Manager.Draws[0].Right.Length();
        // Uniform: the bounding radius grows with it, so r / d changes; keep r / d by moving away by the same factor.
        Assert.Equal(0, CapturesAfter(harness, Ball(1, position * 1.5f, scale: new Vector3(1.5f))));
        Assert.Equal(1.5f * extent, harness.Manager.Draws[0].Right.Length(), 0.001f);
        Assert.Equal(1, CapturesAfter(harness, Ball(1, position * 1.5f, scale: new Vector3(1.5f, 1.5f, 1.8f))));
    }

    [Fact]
    public void TheBudgetIsFourCapturesPerFrameInOrderOfPriority()
    {
        var harness = new Harness(tiles: 64);
        // 50 spheres between 40 and 189 px. All want to be impostors; the largest have the highest priority (π · h).
        RenderSnapshot snapshot = Snapshot();
        for (uint i = 0; i < 50; i++)
        {
            float h = 40f + 3f * i;
            float angle = (i % 10 - 4.5f) * 0.08f;
            float distance = DistanceFor(h);
            snapshot.Instances.Add(Ball(i, Vector3.Transform(new Vector3(0, 0, -distance), Quaternion.CreateFromYawPitchRoll(angle, ((int)(i / 10) - 2) * 0.06f, 0))));
        }

        int frames = 0, captured = 0;
        while (captured < 50)
        {
            PreparedScene scene = harness.Run(snapshot);
            frames++;
            int captures = harness.Manager.Captures.Length;
            Assert.Equal(Math.Min(4, 50 - captured), captures);
            captured += captures;
            Assert.Equal(captured, harness.Manager.Draws.Length);
            Assert.Equal(50 - captured, harness.Manager.Waiting);
            // Waiting objects still draw as geometry.
            Assert.Equal(50 - captured, scene.VisibleCount);
            Assert.Equal(captured, harness.Manager.TilesInUse);
        }
        Assert.Equal(13, frames);
        // The tallest were captured first.
        for (uint i = 0; i < 50; i++)
            Assert.Equal(1 + (int)(i / 4), (int)harness.Status(49 - i).CapturedFrame);
        // Steady: nothing changes, nothing is captured.
        harness.Run(snapshot);
        Assert.Equal(0, harness.Manager.Captures.Length);
    }

    [Fact]
    public void RecapturesWithLargerErrorGoFirst()
    {
        var harness = new Harness();
        float[] heights = [100f, 110f, 120f, 130f, 140f, 150f];
        RenderSnapshot Scene(float turn)
        {
            RenderSnapshot snapshot = Snapshot();
            for (int i = 0; i < heights.Length; i++)
            {
                var position = new Vector3((i - 2.5f) * 2f, 0, -DistanceFor(heights[i]));
                // Object i turns i + 1 times as far: its error × h grows with i.
                snapshot.Instances.Add(Ball((uint)i, position, Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn * (i + 1))));
            }
            return snapshot;
        }
        harness.Run(Scene(0));
        harness.Run(Scene(0));
        Assert.Equal(6, harness.Manager.Draws.Length);
        harness.Run(Scene(0.05f));
        Assert.Equal(4, harness.Manager.Captures.Length);
        Assert.Equal(new[] { 5, 4, 3, 2 }, harness.Manager.Captures.ToArray().Select(c => (int)harness.Manager.TileOwner(c.Tile)!.Value.EntityIndex).ToArray());
        harness.Run(Scene(0.05f));
        Assert.Equal(new[] { 1, 0 }, harness.Manager.Captures.ToArray().Select(c => (int)harness.Manager.TileOwner(c.Tile)!.Value.EntityIndex).ToArray());
    }

    [Fact]
    public void AStaleCaptureIsDrawnOnlyByAnImpostorThatWasDrawnLastFrame()
    {
        var harness = new Harness();
        float far = DistanceFor(150f), close = DistanceFor(400f);
        Vector3 At(float distance, float angle) =>
            Vector3.Transform(new Vector3(0, 0, -distance), Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle));
        // Four tall objects keep wanting first captures with a higher priority than any recapture below.
        RenderSnapshot Scene(Vector3 position, uint crowdStart)
        {
            RenderSnapshot snapshot = Snapshot();
            snapshot.Instances.Add(Ball(1, position));
            for (uint i = 0; i < 4; i++)
                snapshot.Instances.Add(Ball(crowdStart + i, new Vector3((i - 1.5f) * 1.5f, 3f, -DistanceFor(190f))));
            return snapshot;
        }

        RenderSnapshot first = Snapshot();
        first.Instances.Add(Ball(1, At(far, 0f)));
        harness.Run(first);
        Assert.Equal(0, harness.Status(1).Tile);
        // The object turns a little while it stays an impostor: the crowd takes the budget, so it keeps its stale
        // capture rather than flickering to geometry.
        harness.Run(Scene(At(far, 0f), 200));
        PreparedScene scene = harness.Run(Scene(At(far, 0.1f), 300));
        Assert.DoesNotContain(harness.Manager.Captures.ToArray(), c => c.Tile == 0);
        Assert.Contains(harness.Manager.Draws.ToArray(), d => d.Tile == 0);

        // It comes close (geometry), the camera goes around it, and it recedes: its old capture is far off, and it
        // was not drawn last frame, so it draws as geometry until its recapture.
        harness.Run(Scene(At(close, 0.6f), 400));
        Assert.False(harness.Status(1).Impostor);
        scene = harness.Run(Scene(At(far, 0.6f), 500));
        Assert.True(harness.Status(1).Impostor);
        Assert.DoesNotContain(harness.Manager.Captures.ToArray(), c => c.Tile == 0);
        Assert.DoesNotContain(harness.Manager.Draws.ToArray(), d => d.Tile == 0);
        Assert.Contains(scene.VisibleSpan.ToArray(), v => v.EntityIndex == 1);
        // With the budget free again it is recaptured and drawn in the same frame.
        RenderSnapshot alone = Snapshot();
        alone.Instances.Add(Ball(1, At(far, 0.6f)));
        harness.Run(alone);
        Assert.Equal(0, harness.Manager.Captures[0].Tile);
        Assert.Equal(0, harness.Manager.Draws[0].Tile);
    }

    [Fact]
    public void TilesAreEvictedLeastRecentlyUsedFirstAndOnlyFromKeysThatAreGoneOrGeometry()
    {
        var harness = new Harness(tiles: 4);
        float far = DistanceFor(100f), close = DistanceFor(400f);
        RenderSnapshot Scene(params (uint Entity, float Distance)[] objects)
        {
            RenderSnapshot snapshot = Snapshot();
            foreach ((uint entity, float distance) in objects)
                snapshot.Instances.Add(Ball(entity, new Vector3((entity - 4f) * 0.5f * (distance / 10f), 0, -distance)));
            return snapshot;
        }
        int Tile(uint entity) => harness.Status(entity).Tile;

        // Frame 1: A, B, C, D fill the atlas.
        harness.Run(Scene((0, far), (1, far), (2, far), (3, far)));
        Assert.Equal(4, harness.Manager.TilesInUse);
        int[] tiles = [Tile(0), Tile(1), Tile(2), Tile(3)];
        // Frame 2: A is gone; B, C, D are drawn, so A's tile is now the least recently used.
        harness.Run(Scene((1, far), (2, far), (3, far)));
        // Frame 3: B is gone too and E appears. A and B could both go; A was used longer ago.
        harness.Run(Scene((2, far), (3, far), (4, far)));
        Assert.Equal(tiles[0], Tile(4));
        Assert.Equal(-1, Tile(0));
        Assert.Equal(tiles[1], Tile(1));
        // Frame 4: F appears; B's is the only evictable tile.
        harness.Run(Scene((2, far), (3, far), (4, far), (5, far)));
        Assert.Equal(tiles[1], Tile(5));
        Assert.Equal(-1, Tile(1));
        // Frame 5: G appears, but every tile belongs to an impostor drawn this frame: G waits as geometry.
        PreparedScene scene = harness.Run(Scene((2, far), (3, far), (4, far), (5, far), (6, far)));
        Assert.Equal(0, harness.Manager.Captures.Length);
        Assert.Equal(1, harness.Manager.Waiting);
        Assert.Equal(1, scene.VisibleCount);
        Assert.Equal(-1, Tile(6));
        // Frame 6: C comes close and returns to geometry, which frees its tile for G.
        harness.Run(Scene((2, close), (3, far), (4, far), (5, far), (6, far)));
        Assert.False(harness.Status(2).Impostor);
        Assert.Equal(tiles[2], Tile(6));
        Assert.Equal(0, harness.Manager.Waiting);
        Assert.Equal(4, harness.Manager.TilesInUse);
        Assert.Equal(new ImpostorKey(6, Sphere, 1), harness.Manager.TileOwner(tiles[2]));
    }

    [Fact]
    public void KeysWithoutATileAreForgottenAfterTheirLifetime()
    {
        var harness = new Harness(tiles: 1);
        float far = DistanceFor(100f);
        RenderSnapshot both = Snapshot();
        both.Instances.Add(Ball(1, new Vector3(-3, 0, -far)));
        both.Instances.Add(Ball(2, new Vector3(3, 0, -far)));
        harness.Run(both);
        // One tile: entity 1 (first in order at equal priority) has it, entity 2 waits.
        Assert.Equal(0, harness.Status(1).Tile);
        Assert.Equal(-1, harness.Status(2).Tile);
        RenderSnapshot empty = Snapshot();
        for (int i = 0; i < ImpostorManager.StateLifetime; i++)
            harness.Run(empty);
        Assert.True(harness.Manager.TryGetStatus(new ImpostorKey(2, Sphere, 1), out _));
        harness.Run(empty);
        Assert.False(harness.Manager.TryGetStatus(new ImpostorKey(2, Sphere, 1), out _));
        // The key with a tile is kept, so its capture can be reused.
        Assert.True(harness.Manager.TryGetStatus(new ImpostorKey(1, Sphere, 1), out _));
        RenderSnapshot again = Snapshot();
        again.Instances.Add(Ball(1, new Vector3(-3, 0, -far)));
        harness.Run(again);
        Assert.Equal(0, harness.Manager.Captures.Length);
        Assert.Equal(1, harness.Manager.Draws.Length);
    }

    [Fact]
    public void BillboardsGoInTheRangeOfTheirCenterAndAnyNearerRangeTheyReach()
    {
        var harness = new Harness();
        RenderSnapshot snapshot = Snapshot();
        // Straddles 2 km with its center beyond it; one in the middle range; one in the near range.
        snapshot.Instances.Add(Gpu.Instance(new MeshId(Sphere), new MaterialId(1), new Vector3(0, 0, -2_050f), new Vector3(300f), entity: 1));
        snapshot.Instances.Add(Gpu.Instance(new MeshId(Sphere), new MaterialId(1), new Vector3(0, 0, -50_000f), new Vector3(300f), entity: 2));
        snapshot.Instances.Add(Ball(3, new Vector3(0, 0, -200f)));
        harness.Run(snapshot);
        ImpostorDraw[] draws = harness.Manager.Draws.ToArray();
        Assert.Equal(3, draws.Length);
        int Ranges(float z) => draws.Single(d => MathF.Abs(d.Center.Z - z) < 1f).Ranges;
        Assert.Equal((1 << DepthRanges.Middle) | (1 << DepthRanges.Near), Ranges(-2_050f));
        Assert.Equal(1 << DepthRanges.Middle, Ranges(-50_000f));
        Assert.Equal(1 << DepthRanges.Near, Ranges(-200f));
    }

    [Fact]
    public void CaptureProjectionMapsTheTangentConeToTheTileAndTheSphereDepthToReversedZ()
    {
        var center = new Vector3(3f, -2f, -40f);
        float radius = 4f, distance = center.Length();
        Vector3 toward = -center / distance;
        Vector3 right = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, toward));
        Vector3 up = Vector3.Cross(toward, right);
        float tan = radius / MathF.Sqrt(distance * distance - radius * radius);
        Matrix4x4 m = ImpostorPass.CaptureProjection(right, up, toward, distance, radius, tan);
        Vector4 Clip(Vector3 p) => Vector4.Transform(new Vector4(p, 1), m);

        // The center maps to the tile's center, at relative depth 1 (clip w).
        Vector4 c = Clip(center);
        Assert.Equal(0f, c.X / c.W, 1e-5f);
        Assert.Equal(0f, c.Y / c.W, 1e-5f);
        Assert.Equal(1f, c.W, 1e-5f);
        // A tangent point of the cone on the right maps to x = 1; one on top to y = −1 (Vulkan's top).
        float tangentDistance = MathF.Sqrt(distance * distance - radius * radius);
        Vector3 rightTangent = Vector3.Normalize(-toward * tangentDistance + right * radius) * tangentDistance;
        Assert.Equal(1f, Clip(rightTangent).X / Clip(rightTangent).W, 1e-4f);
        Vector3 topTangent = Vector3.Normalize(-toward * tangentDistance + up * radius) * tangentDistance;
        Assert.Equal(-1f, Clip(topTangent).Y / Clip(topTangent).W, 1e-4f);
        // The sphere's front and back are inside the reversed depth range, front nearer (greater).
        Vector4 front = Clip(center + toward * radius), back = Clip(center - toward * radius);
        Assert.InRange(front.Z / front.W, 0.99f, 1f);
        Assert.InRange(back.Z / back.W, 0f, 0.01f);
        Assert.Equal(1f - radius / distance, front.W, 1e-5f);
    }

    [Fact]
    public void EclipseMatchesTheShaderCases()
    {
        Vector3 sun = Vector3.UnitY;
        float sunRadius = 0.00467f;
        Assert.Equal(1f, Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, []));
        // A planet straight toward the sun hides it; one off to the side does not.
        Assert.Equal(0f, Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(0, 1e7f, 0, 6e6f)]), 1e-6f);
        Assert.Equal(1f, Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(1e7f, 0, 0, 6e6f)]));
        // A moon of the sun's angular size centered on it hides it all. Offset by one radius, the two discs overlap
        // in a lens of r² (2π/3 − √3/2), which leaves 1 − (2π/3 − √3/2) / π ≈ 0.609 of the sun.
        float moonDistance = 1e6f, moonRadius = moonDistance * MathF.Sin(sunRadius);
        Assert.Equal(0f, Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(0, moonDistance, 0, moonRadius)]), 1e-3f);
        float partial = Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(moonDistance * MathF.Tan(sunRadius), moonDistance, 0, moonRadius)]);
        Assert.Equal(1f - (2f * MathF.PI / 3f - MathF.Sqrt(3f) / 2f) / MathF.PI, partial, 0.005f);
    }

    [Fact]
    public void SteadyStateProcessingDoesNotAllocate()
    {
        var harness = new Harness(tiles: 16);
        RenderSnapshot snapshot = Snapshot();
        snapshot.Occluders.Add(new RenderOccluder { Center = new Vector3(0, 1e7f, 0), Radius = 1e6f });
        for (uint i = 0; i < 40; i++)
            snapshot.Instances.Add(Ball(i, new Vector3((i % 8 - 3.5f) * 4f, (i / 8 - 2f) * 4f, -DistanceFor(60f + i))));
        for (int i = 0; i < 20; i++)
            harness.Run(snapshot);
        // 16 impostors drawn with current captures, 24 waiting with no tile to take.
        Assert.Equal(16, harness.Manager.Draws.Length);
        Assert.Equal(24, harness.Manager.Waiting);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
            harness.Run(snapshot);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
