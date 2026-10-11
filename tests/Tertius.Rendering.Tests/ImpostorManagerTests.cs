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
    private static readonly MaterialId BallMaterial = new(0);
    private static readonly MaterialId OtherMaterial = new(1);

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

        public ImpostorStatus Status(uint entity, ushort part = 0, MaterialId? material = null)
        {
            Manager.TryGetStatus(Key(entity, part, material), out ImpostorStatus status).Should().BeTrue("entity {0} part {1} must be known", entity, part);
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

    /// <summary>The key of a ball: its entity and part, the sphere mesh, and its material's slot.</summary>
    private static ImpostorKey Key(uint entity, ushort part = 0, MaterialId? material = null) =>
        new(entity, part, Sphere, MaterialStore.Slot(material ?? BallMaterial));

    private static RenderInstance Ball(uint entity, Vector3 position, Quaternion? rotation = null, RenderFlags flags = RenderFlags.None,
        Vector3? scale = null, ushort part = 0, MaterialId? material = null, HemisphereUp? hemisphere = null)
    {
        RenderInstance ball = Gpu.Instance(new MeshId(Sphere), material ?? BallMaterial, position, scale ?? Vector3.One, rotation, flags, entity: entity, part: part);
        ball.Hemisphere = hemisphere ?? ball.Hemisphere;
        return ball;
    }

    /// <summary>An invisible sphere of <paramref name="radius"/> that casts a far shadow: a moon, for an eclipse.</summary>
    private static RenderInstance Moon(Vector3 center, float radius) =>
        RenderInstance.Create(new MeshId(Sphere), BallMaterial, center, Quaternion.Identity, new Vector3(radius), 0, HemisphereUp.Even, RenderFlags.CastsShadow, 9_999, 0);

    [Fact]
    public void ProjectedHeightIsTheDiameterOfTheSpheresImageAtTheCenterOfTheScreen()
    {
        var camera = new CameraView { Height = Height, TanHalfY = MathF.Tan(Gpu.Fov / 2) };
        float distance = 12f, radius = 2f;
        // The tangent cone's half angle, projected: the image's top and bottom edges are at ±tan(asin(r / d)).
        float top = Gpu.Project(new Vector3(0, distance * MathF.Tan(MathF.Asin(radius / distance)), -distance), Width, Height).Y;
        float bottom = Gpu.Project(new Vector3(0, -distance * MathF.Tan(MathF.Asin(radius / distance)), -distance), Width, Height).Y;
        ImpostorManager.ProjectedHeight(distance, radius, camera).Should().BeApproximately(bottom - top, 0.01f);
        ImpostorManager.ProjectedHeight(1.5f, 2f, camera).Should().Be(float.PositiveInfinity, "the eye is inside the sphere");
        ImpostorManager.ProjectedHeight(DistanceFor(200, 3f), 3f, camera).Should().BeApproximately(200f, 0.01f);
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
            harness.Status(1).Impostor.Should().Be(expected, "at {0} px", h);
            // An impostor with a capture leaves the mesh batch; geometry stays in it.
            scene.VisibleCount.Should().Be(expected ? 0 : 1);
            harness.Manager.Draws.Length.Should().Be(expected ? 1 : 0);
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
        scene.VisibleCount.Should().Be(3);
        harness.Manager.Draws.Length.Should().Be(1);
        harness.Manager.TryGetStatus(Key(1), out _).Should().BeFalse("NoImpostor objects are never considered");
        harness.Manager.TryGetStatus(Key(2), out _).Should().BeFalse("Celestial objects are never impostors");
        harness.Manager.TryGetStatus(Key(3), out _).Should().BeFalse("Transparent objects are never impostors");
        harness.Status(4).Impostor.Should().BeTrue();
    }

    [Fact]
    public void ASecondInstanceWithTheSameKeyInOneFrameStaysGeometry()
    {
        // The same entity and part twice, as a faulty snapshot could have it.
        var harness = new Harness();
        RenderSnapshot snapshot = Snapshot();
        snapshot.Instances.Add(Ball(5, new Vector3(-5, 0, -100)));
        snapshot.Instances.Add(Ball(5, new Vector3(5, 0, -100)));
        PreparedScene scene = harness.Run(snapshot);
        harness.Manager.Draws.Length.Should().Be(1);
        scene.VisibleCount.Should().Be(1);
        harness.Manager.Captures.Length.Should().Be(1);
    }

    [Fact]
    public void TwoPartsOfOneEntitySharingAMeshAndAMaterialAreSeparateImpostors()
    {
        var harness = new Harness();
        float far = DistanceFor(120f), close = DistanceFor(400f);
        Vector3 left = Vector3.Normalize(new Vector3(-3, 0, -far)), right = Vector3.Normalize(new Vector3(3, 0, -far));
        RenderSnapshot Scene(float rightDistance, MaterialId? leftMaterial = null)
        {
            RenderSnapshot snapshot = Snapshot();
            snapshot.Instances.Add(Ball(1, left * far, part: 0, material: leftMaterial));
            snapshot.Instances.Add(Ball(1, right * rightDistance, part: 1));
            return snapshot;
        }

        // Both parts are below 200 px: each gets its own capture and tile, and both draw as impostors.
        PreparedScene scene = harness.Run(Scene(far));
        harness.Manager.Captures.Length.Should().Be(2);
        harness.Manager.Draws.Length.Should().Be(2);
        scene.VisibleCount.Should().Be(0);
        ImpostorStatus first = harness.Status(1, part: 0), second = harness.Status(1, part: 1);
        first.Impostor.Should().BeTrue();
        second.Impostor.Should().BeTrue();
        second.Tile.Should().NotBe(first.Tile, "each part has its own capture");

        // Part 1 comes close and returns to geometry. Part 0 stays an impostor and keeps its capture.
        scene = harness.Run(Scene(close));
        harness.Status(1, part: 0).Impostor.Should().BeTrue();
        harness.Status(1, part: 1).Impostor.Should().BeFalse();
        harness.Manager.Captures.Length.Should().Be(0);
        harness.Manager.Draws.ToArray().Should().ContainSingle().Which.Tile.Should().Be(first.Tile);
        scene.VisibleSpan.ToArray().Should().ContainSingle().Which.Part.Should().Be((ushort)1);

        // Part 1 goes back: it is an impostor again with its own capture, which is still current.
        scene = harness.Run(Scene(far));
        harness.Status(1, part: 1).Impostor.Should().BeTrue();
        harness.Manager.Captures.Length.Should().Be(0);
        harness.Manager.Draws.ToArray().Select(d => d.Tile).Should().BeEquivalentTo(new[] { first.Tile, second.Tile });
        scene.VisibleCount.Should().Be(0);

        // A part that changes its material is a new key: it never draws the old capture, and the other part is untouched.
        harness.Run(Scene(far, leftMaterial: OtherMaterial));
        harness.Manager.Captures.ToArray().Should().ContainSingle().Which.Material.Should().Be(MaterialStore.Slot(OtherMaterial));
        harness.Status(1, part: 0, material: OtherMaterial).Tile.Should().NotBe(first.Tile).And.NotBe(second.Tile);
        harness.Status(1, part: 1).Tile.Should().Be(second.Tile);
    }

    /// <summary>Captures an object at <paramref name="height"/> px and returns the harness and the object's distance.</summary>
    private static (Harness Harness, float Distance) Captured(float height, Vector3? sun = null, HemisphereUp? hemisphere = null)
    {
        var harness = new Harness();
        float distance = DistanceFor(height);
        RenderSnapshot snapshot = Snapshot(sun);
        snapshot.Instances.Add(Ball(1, new Vector3(0, 0, -distance), hemisphere: hemisphere));
        harness.Run(snapshot);
        harness.Manager.Captures.Length.Should().Be(1, "the first sight of an impostor is captured");
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
        harness.Manager.Draws.Length.Should().Be(1, "the object stays an impostor");
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
        CapturesAfter(harness, Ball(1, At(0.5f * limit))).Should().Be(0);
        CapturesAfter(harness, Ball(1, At(0.95f * limit))).Should().Be(0);
        CapturesAfter(harness, Ball(1, At(1.05f * limit))).Should().Be(1);
        // Measured from the new capture now.
        CapturesAfter(harness, Ball(1, At(1.95f * limit))).Should().Be(0);
        CapturesAfter(harness, Ball(1, At(2.1f * limit))).Should().Be(1);
    }

    [Fact]
    public void TheObjectsOwnRotationCountsButARollAboutTheViewAxisDoesNot()
    {
        // The sun is behind the camera, on the view axis, so a roll about that axis leaves it unchanged in object space too.
        // No way is up, so a roll leaves the ambient light unchanged as well: with an up, the sky would light another side.
        float height = 80f;
        (Harness harness, float distance) = Captured(height, sun: Vector3.UnitZ, hemisphere: HemisphereUp.Even);
        Vector3 position = new(0, 0, -distance);
        float limit = 2f / height;
        Quaternion Turn(float angle) => Quaternion.CreateFromAxisAngle(Vector3.UnitY, angle);
        CapturesAfter(harness, Ball(1, position, Turn(0.9f * limit), hemisphere: HemisphereUp.Even), sun: Vector3.UnitZ).Should().Be(0);
        CapturesAfter(harness, Ball(1, position, Turn(1.1f * limit), hemisphere: HemisphereUp.Even), sun: Vector3.UnitZ).Should().Be(1);

        // A large roll about the view axis: no recapture, and the billboard turns with the object.
        Quaternion roll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f) * Turn(1.1f * limit);
        CapturesAfter(harness, Ball(1, position, roll, hemisphere: HemisphereUp.Even), sun: Vector3.UnitZ).Should().Be(0);
        ImpostorDraw draw = harness.Manager.Draws[0];
        float angle = MathF.Atan2(draw.Right.Y, draw.Right.X);
        angle.Should().BeApproximately(1.2f, 0.02f);
    }

    [Fact]
    public void TurningTheCameraInPlaceDoesNotRecapture()
    {
        (Harness harness, float distance) = Captured(120f);
        Quaternion turned = Quaternion.CreateFromYawPitchRoll(0.2f, 0.15f, 0.3f);
        CapturesAfter(harness, Ball(1, new Vector3(0, 0, -distance)), camera: turned).Should().Be(0);
    }

    [Fact]
    public void TheSunDirectionInObjectSpaceRecapturesAfterTwoOverH()
    {
        float height = 100f;
        (Harness harness, float distance) = Captured(height);
        var position = new Vector3(0, 0, -distance);
        Vector3 Sun(float angle) => Vector3.Transform(SunDirection, Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(SunDirection, Vector3.UnitX)), angle));
        float limit = 2f / height;
        CapturesAfter(harness, Ball(1, position), sun: Sun(0.9f * limit)).Should().Be(0);
        CapturesAfter(harness, Ball(1, position), sun: Sun(1.1f * limit)).Should().Be(1);
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
        CapturesAfter(harness, Ball(1, new Vector3(0, 0, -Closer(0.9f)))).Should().Be(0);
        CapturesAfter(harness, Ball(1, new Vector3(0, 0, -Closer(1.1f)))).Should().Be(1);
    }

    [Fact]
    public void ALightingChangeRecapturesButASmallOneDoesNot()
    {
        float height = 100f;
        (Harness harness, float distance) = Captured(height);
        var position = new Vector3(0, 0, -distance);
        // The ambient total is small next to the sun (3 × white), so 0.5% of the sun is below 2 / h of the total.
        CapturesAfter(harness, Ball(1, position), intensity: 3f * 1.005f).Should().Be(0);
        CapturesAfter(harness, Ball(1, position), intensity: 3f * 1.5f).Should().Be(1);

        // An eclipse: a moon slides in front of the sun as seen from the object.
        RenderSnapshot snapshot = Snapshot(intensity: 3f * 1.5f);
        snapshot.Instances.Add(Ball(1, position));
        snapshot.Instances.Add(Moon(position + SunDirection * 50_000f, 1_000f));
        harness.Run(snapshot);
        harness.Manager.Captures.Length.Should().Be(1, "the eclipse changes the light that reaches the object");

        // Turning the sun off (a zero or negative intensity) is a lighting change too: the capture bakes no sunlight.
        RenderSnapshot clear = Snapshot(intensity: 3f * 1.5f);
        clear.Instances.Add(Ball(1, position));
        harness.Run(clear);
        harness.Manager.Captures.Length.Should().Be(1, "the eclipse is over");
        clear.Sun.Intensity = -1f;
        harness.Run(clear);
        harness.Manager.Captures.Length.Should().Be(1, "the sun went out");
        harness.Run(clear);
        harness.Manager.Captures.Length.Should().Be(0, "a sun that is off stays off");
    }

    [Fact]
    public void TheHemispheresUpIsBakedSoTurningItAgainstTheObjectRecapturesButASmallTurnDoesNot()
    {
        // The ball was captured with up along +Y. The sky and the ground differ by 0.14, against about 5.3 of light in all.
        (Harness harness, float distance) = Captured(100f);
        var position = new Vector3(0, 0, -distance);
        RenderInstance Tilted(float angle)
        {
            RenderInstance ball = Ball(1, position);
            ball.Hemisphere = HemisphereUp.Along(Vector3.Transform(Vector3.UnitY, Quaternion.CreateFromAxisAngle(Vector3.UnitZ, angle)));
            return ball;
        }

        CapturesAfter(harness, Tilted(0.05f)).Should().Be(0);
        CapturesAfter(harness, Tilted(MathF.PI / 2)).Should().Be(1);
    }

    [Fact]
    public void WithAnUpARollAboutTheViewAxisTurnsTheSkyLitSideSoItRecaptures()
    {
        // As in the test above, but up is +Y: the roll turns the up in object space by 1.2 rad. The sky and the ground
        // differ by 0.134 against 5.24 of light in all, so the change is 0.134 · 2 sin(0.6) / 5.24, times 80 px: about 2.3.
        (Harness harness, float distance) = Captured(80f, sun: Vector3.UnitZ);
        Quaternion roll = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 1.2f);

        CapturesAfter(harness, Ball(1, new Vector3(0, 0, -distance), roll), sun: Vector3.UnitZ).Should().Be(1);
    }

    [Fact]
    public void WhenTheSkyAndTheGroundHaveOneColorTheUpDoesNotMatter()
    {
        var harness = new Harness();
        var position = new Vector3(0, 0, -DistanceFor(100f));
        RenderSnapshot Scene(Vector3 up)
        {
            RenderSnapshot snapshot = Snapshot();
            snapshot.Ambient = new AmbientLight { SkyColor = new Vector3(0.1f), GroundColor = new Vector3(0.1f), Intensity = 1 };
            snapshot.Instances.Add(Ball(1, position, hemisphere: HemisphereUp.Along(up)));
            return snapshot;
        }
        harness.Run(Scene(Vector3.UnitY));
        harness.Manager.Captures.Length.Should().Be(1);

        harness.Run(Scene(Vector3.UnitX));

        harness.Manager.Captures.Length.Should().Be(0, "every side gets the same ambient light whichever way is up");
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

        Captures(Scene(new Vector3(2.5f, 1f, 0))).Should().Be(1);
        Captures(Scene(new Vector3(2.5f, 1f, 0))).Should().Be(0);
        // The light goes around the object at the same distance: same irradiance, other direction.
        Captures(Scene(new Vector3(-2.5f, 1f, 0))).Should().Be(1);
        // It brightens.
        Captures(Scene(new Vector3(-2.5f, 1f, 0), intensity: 8f)).Should().Be(1);
        // It moves out of reach: the object loses its light. Out there, it can move without effect.
        Captures(Scene(new Vector3(-9f, 1f, 0), intensity: 8f)).Should().Be(1);
        Captures(Scene(new Vector3(-12f, 3f, 0), intensity: 8f)).Should().Be(0);
        // A spot light aimed at the object, then turned away from it.
        Captures(Scene(new Vector3(0, 3f, 0), spot: true)).Should().Be(1);
        Captures(Scene(new Vector3(0, 3f, 0), spot: true)).Should().Be(0);
        Captures(Scene(new Vector3(0, 3f, 0), spot: true, spotTurn: 0.8f)).Should().Be(1);
    }

    [Fact]
    public void ANonUniformScaleChangeRecapturesButAUniformOneOnlyResizesTheBillboard()
    {
        float height = 100f;
        (Harness harness, float distance) = Captured(height);
        var position = new Vector3(0, 0, -distance);
        float extent = harness.Manager.Draws[0].Right.Length();
        // Uniform: the bounding radius grows with it, so r / d changes; keep r / d by moving away by the same factor.
        CapturesAfter(harness, Ball(1, position * 1.5f, scale: new Vector3(1.5f))).Should().Be(0);
        harness.Manager.Draws[0].Right.Length().Should().BeApproximately(1.5f * extent, 0.001f);
        CapturesAfter(harness, Ball(1, position * 1.5f, scale: new Vector3(1.5f, 1.5f, 1.8f))).Should().Be(1);
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
            captures.Should().Be(Math.Min(4, 50 - captured));
            captured += captures;
            harness.Manager.Draws.Length.Should().Be(captured);
            harness.Manager.Waiting.Should().Be(50 - captured);
            // Waiting objects still draw as geometry.
            scene.VisibleCount.Should().Be(50 - captured);
            harness.Manager.TilesInUse.Should().Be(captured);
        }
        frames.Should().Be(13);
        // The tallest were captured first.
        for (uint i = 0; i < 50; i++)
            harness.Status(49 - i).CapturedFrame.Should().Be(1 + i / 4, "entity {0} is the {1}th tallest", 49 - i, i + 1);
        // Steady: nothing changes, nothing is captured.
        harness.Run(snapshot);
        harness.Manager.Captures.Length.Should().Be(0);
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
        harness.Manager.Draws.Length.Should().Be(6);
        harness.Run(Scene(0.05f));
        harness.Manager.Captures.Length.Should().Be(4);
        uint[] CapturedEntities() => harness.Manager.Captures.ToArray().Select(c => harness.Manager.TileOwner(c.Tile)!.Value.EntityIndex).ToArray();
        CapturedEntities().Should().Equal(5u, 4u, 3u, 2u);
        harness.Run(Scene(0.05f));
        CapturedEntities().Should().Equal(1u, 0u);
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
        harness.Status(1).Tile.Should().Be(0);
        // The object turns a little while it stays an impostor: the crowd takes the budget, so it keeps its stale
        // capture rather than flickering to geometry.
        harness.Run(Scene(At(far, 0f), 200));
        PreparedScene scene = harness.Run(Scene(At(far, 0.1f), 300));
        harness.Manager.Captures.ToArray().Should().NotContain(c => c.Tile == 0, "the crowd's first captures take the budget");
        harness.Manager.Draws.ToArray().Should().Contain(d => d.Tile == 0, "a stale capture still draws while the impostor waits");

        // It comes close (geometry), the camera goes around it, and it recedes: its old capture is far off, and it
        // was not drawn last frame, so it draws as geometry until its recapture.
        harness.Run(Scene(At(close, 0.6f), 400));
        harness.Status(1).Impostor.Should().BeFalse();
        scene = harness.Run(Scene(At(far, 0.6f), 500));
        harness.Status(1).Impostor.Should().BeTrue();
        harness.Manager.Captures.ToArray().Should().NotContain(c => c.Tile == 0);
        harness.Manager.Draws.ToArray().Should().NotContain(d => d.Tile == 0, "a stale capture is not drawn after a spell as geometry");
        scene.VisibleSpan.ToArray().Should().Contain(v => v.EntityIndex == 1, "it draws as geometry meanwhile");
        // With the budget free again it is recaptured and drawn in the same frame.
        RenderSnapshot alone = Snapshot();
        alone.Instances.Add(Ball(1, At(far, 0.6f)));
        harness.Run(alone);
        harness.Manager.Captures[0].Tile.Should().Be(0);
        harness.Manager.Draws[0].Tile.Should().Be(0);
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
        harness.Manager.TilesInUse.Should().Be(4);
        int[] tiles = [Tile(0), Tile(1), Tile(2), Tile(3)];
        // Frame 2: A is gone; B, C, D are drawn, so A's tile is now the least recently used.
        harness.Run(Scene((1, far), (2, far), (3, far)));
        // Frame 3: B is gone too and E appears. A and B could both go; A was used longer ago.
        harness.Run(Scene((2, far), (3, far), (4, far)));
        Tile(4).Should().Be(tiles[0]);
        Tile(0).Should().Be(-1);
        Tile(1).Should().Be(tiles[1]);
        // Frame 4: F appears; B's is the only evictable tile.
        harness.Run(Scene((2, far), (3, far), (4, far), (5, far)));
        Tile(5).Should().Be(tiles[1]);
        Tile(1).Should().Be(-1);
        // Frame 5: G appears, but every tile belongs to an impostor drawn this frame: G waits as geometry.
        PreparedScene scene = harness.Run(Scene((2, far), (3, far), (4, far), (5, far), (6, far)));
        harness.Manager.Captures.Length.Should().Be(0);
        harness.Manager.Waiting.Should().Be(1);
        scene.VisibleCount.Should().Be(1);
        Tile(6).Should().Be(-1);
        // Frame 6: C comes close and returns to geometry, which frees its tile for G.
        harness.Run(Scene((2, close), (3, far), (4, far), (5, far), (6, far)));
        harness.Status(2).Impostor.Should().BeFalse();
        Tile(6).Should().Be(tiles[2]);
        harness.Manager.Waiting.Should().Be(0);
        harness.Manager.TilesInUse.Should().Be(4);
        harness.Manager.TileOwner(tiles[2]).Should().Be(Key(6));
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
        harness.Status(1).Tile.Should().Be(0);
        harness.Status(2).Tile.Should().Be(-1);
        RenderSnapshot empty = Snapshot();
        for (int i = 0; i < ImpostorManager.StateLifetime; i++)
            harness.Run(empty);
        harness.Manager.TryGetStatus(Key(2), out _).Should().BeTrue("the lifetime has not run out yet");
        harness.Run(empty);
        harness.Manager.TryGetStatus(Key(2), out _).Should().BeFalse("a key without a tile is forgotten after its lifetime");
        // The key with a tile is kept, so its capture can be reused.
        harness.Manager.TryGetStatus(Key(1), out _).Should().BeTrue("a key that holds a tile is kept");
        RenderSnapshot again = Snapshot();
        again.Instances.Add(Ball(1, new Vector3(-3, 0, -far)));
        harness.Run(again);
        harness.Manager.Captures.Length.Should().Be(0);
        harness.Manager.Draws.Length.Should().Be(1);
    }

    [Fact]
    public void BillboardsGoInTheRangeOfTheirCenterAndAnyNearerRangeTheyReach()
    {
        var harness = new Harness();
        RenderSnapshot snapshot = Snapshot();
        // Straddles 2 km with its center beyond it; one in the middle range; one in the near range.
        snapshot.Instances.Add(Ball(1, new Vector3(0, 0, -2_050f), scale: new Vector3(300f)));
        snapshot.Instances.Add(Ball(2, new Vector3(0, 0, -50_000f), scale: new Vector3(300f)));
        snapshot.Instances.Add(Ball(3, new Vector3(0, 0, -200f)));
        harness.Run(snapshot);
        ImpostorDraw[] draws = harness.Manager.Draws.ToArray();
        draws.Should().HaveCount(3);
        int Ranges(float z) => draws.Single(d => MathF.Abs(d.Center.Z - z) < 1f).Ranges;
        Ranges(-2_050f).Should().Be((1 << DepthRanges.Middle) | (1 << DepthRanges.Near));
        Ranges(-50_000f).Should().Be(1 << DepthRanges.Middle);
        Ranges(-200f).Should().Be(1 << DepthRanges.Near);
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
        (c.X / c.W).Should().BeApproximately(0f, 1e-5f);
        (c.Y / c.W).Should().BeApproximately(0f, 1e-5f);
        c.W.Should().BeApproximately(1f, 1e-5f);
        // A tangent point of the cone on the right maps to x = 1; one on top to y = −1 (Vulkan's top).
        float tangentDistance = MathF.Sqrt(distance * distance - radius * radius);
        Vector3 rightTangent = Vector3.Normalize(-toward * tangentDistance + right * radius) * tangentDistance;
        (Clip(rightTangent).X / Clip(rightTangent).W).Should().BeApproximately(1f, 1e-4f);
        Vector3 topTangent = Vector3.Normalize(-toward * tangentDistance + up * radius) * tangentDistance;
        (Clip(topTangent).Y / Clip(topTangent).W).Should().BeApproximately(-1f, 1e-4f);
        // The sphere's front and back are inside the reversed depth range, front nearer (greater).
        Vector4 front = Clip(center + toward * radius), back = Clip(center - toward * radius);
        (front.Z / front.W).Should().BeInRange(0.99f, 1f);
        (back.Z / back.W).Should().BeInRange(0f, 0.01f);
        front.W.Should().BeApproximately(1f - radius / distance, 1e-5f);
    }

    [Fact]
    public void EclipseMatchesTheShaderCases()
    {
        Vector3 sun = Vector3.UnitY;
        float sunRadius = 0.00467f;
        Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, []).Should().Be(1f);
        // A planet straight toward the sun hides it; one off to the side does not.
        Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(0, 1e7f, 0, 6e6f)]).Should().BeApproximately(0f, 1e-6f);
        Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(1e7f, 0, 0, 6e6f)]).Should().Be(1f);
        // A moon of the sun's angular size centered on it hides it all. Offset by one radius, the two discs overlap
        // in a lens of r² (2π/3 − √3/2), which leaves 1 − (2π/3 − √3/2) / π ≈ 0.609 of the sun.
        float moonDistance = 1e6f, moonRadius = moonDistance * MathF.Sin(sunRadius);
        Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(0, moonDistance, 0, moonRadius)]).Should().BeApproximately(0f, 1e-3f);
        float partial = Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(moonDistance * MathF.Tan(sunRadius), moonDistance, 0, moonRadius)]);
        partial.Should().BeApproximately(1f - (2f * MathF.PI / 3f - MathF.Sqrt(3f) / 2f) / MathF.PI, 0.005f);
    }

    [Fact]
    public void ASphereShadowsPointsOutsideItAndNeverAPointInsideIt()
    {
        Vector3 sun = Vector3.UnitY;
        const float sunRadius = 0.00467f;
        // The sphere's center lies 5 m toward the sun from a point inside it: its own caster's surface, or the ground of a
        // planet, which faces away from the sun at night anyway.
        Eclipse.SunVisibility(Vector3.Zero, sun, sunRadius, [new Vector4(0, 5, 0, 10)]).Should().Be(1f);
        Eclipse.SunVisibility(new Vector3(0, -20, 0), sun, sunRadius, [new Vector4(0, 0, 0, 10)]).Should().BeApproximately(0f, 1e-6f);
    }

    [Fact]
    public void AFarShadowCasterThatMovesIntoAPlanetsShadowIsRecaptured()
    {
        var harness = new Harness();
        // A ball of 100 m radius, about 3.1 km away and 40 px tall: an impostor, and a far-shadow caster itself.
        var position = new Vector3(0, 0, -DistanceFor(40f, 100f));
        RenderInstance ball = Ball(1, position, flags: RenderFlags.CastsShadow, scale: new Vector3(100f));
        RenderSnapshot lit = Snapshot();
        lit.Instances.Add(ball);
        harness.Run(lit);
        harness.Manager.Captures.Length.Should().Be(1, "the first sight of an impostor is captured");
        harness.Run(lit);
        harness.Manager.Captures.Length.Should().Be(0, "nothing changed");

        // A planet comes between the ball and the sun.
        RenderSnapshot dark = Snapshot();
        dark.Instances.Add(ball);
        dark.Instances.Add(RenderInstance.Create(new MeshId(Sphere), BallMaterial, position + SunDirection * 1e7f, Quaternion.Identity,
            new Vector3(6e6f), 0, HemisphereUp.Even, RenderFlags.CastsShadow, 9_998, 0));
        harness.Run(dark);

        harness.Manager.Captures.Length.Should().Be(1, "the planet's shadow changes the light that reaches the ball");
    }

    [Fact]
    public void SteadyStateProcessingDoesNotAllocate()
    {
        var harness = new Harness(tiles: 16);
        RenderSnapshot snapshot = Snapshot();
        snapshot.Instances.Add(Moon(new Vector3(0, 1e7f, 0), 1e6f));
        for (uint i = 0; i < 40; i++)
            snapshot.Instances.Add(Ball(i, new Vector3((i % 8 - 3.5f) * 4f, (i / 8 - 2f) * 4f, -DistanceFor(60f + i))));
        for (int i = 0; i < 20; i++)
            harness.Run(snapshot);
        // 16 impostors drawn with current captures, 24 waiting with no tile to take.
        harness.Manager.Draws.Length.Should().Be(16);
        harness.Manager.Waiting.Should().Be(24);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 50; i++)
            harness.Run(snapshot);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        allocated.Should().Be(0, "a steady state allocates nothing");
    }
}
