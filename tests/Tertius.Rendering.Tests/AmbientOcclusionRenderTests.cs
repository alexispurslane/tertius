using System.Diagnostics;
using System.Numerics;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The acceptance criteria of ambient occlusion on the GPU (docs/plans/ambient-occlusion.md, "GPU tests of the
/// acceptance criteria"): creases darken, open surfaces and silhouettes do not, the method estimates the physical
/// visibility, and only the ambient light changes. Each scene has only the white ambient light unless a test says
/// otherwise, at 640 × 360, where a radius of 1 m covers one half-resolution texel at 156 m.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AmbientOcclusionRenderTests(ITestOutputHelper output)
{
    /// <summary>The surfaces that each depth range holds farther than the default radius covers in one texel.</summary>
    public enum DistantSurface
    {
        InnerCornerInTheNearRange,
        CliffInTheMiddleRange,
        PlanetLimbInTheFarRange,
    }

    [Fact]
    public void AnInnerCornerGetsLessAmbientLightThanTheOpenFloor()
    {
        VulkanRenderer renderer = Gpu.Headless(ambientOcclusion: GroundTruthOcclusion.Default);
        try
        {
            RenderSnapshot corner = Corner(renderer, Matte(renderer), UnderWhiteAmbientLight(CornerView));
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: corner, name: "occlusion-corner");

            float atTheCorner = image.Luma(PixelOf(FloorAtTheCorner, CornerView));
            float open = image.Luma(PixelOf(OpenFloor, CornerView));
            output.WriteLine($"floor at the corner {atTheCorner * 255:0}, open floor {open * 255:0} of 255");
            (open - atTheCorner)
                .Should()
                .BeGreaterThanOrEqualTo(20f / 255, "the two walls hide much of the hemisphere 0.1 m from both");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AnOpenFloorKeepsItsAmbientLight()
    {
        Quaternion view = Gpu.PitchDown(MathF.PI / 4);
        (Capture on, Capture off) = OnAndOff(
            renderer =>
            {
                RenderSnapshot snapshot = UnderWhiteAmbientLight(view);
                AddSlab(
                    snapshot: snapshot,
                    box: UnitBox(renderer),
                    material: Matte(renderer),
                    center: new(0, -3.5f, -3),
                    size: new(60, 1, 60)
                );
                return snapshot;
            },
            "occlusion-open-floor"
        );

        // The middle of the image sees the floor 4.2 m away, where 60 px span about 0.4 m.
        int largest = LargestDifference(on, off, Block(centerX: Width / 2, centerY: Height / 2, halfSize: 60));
        output.WriteLine($"largest difference on the open floor: {largest} of 255");
        largest.Should().BeLessThanOrEqualTo(2, "nothing lies within the radius above the open floor");
    }

    [Fact]
    public void NoDarkHaloAppearsBesideANearSilhouette()
    {
        const float half = 0.3f;
        (Capture on, Capture off) = OnAndOff(
            renderer =>
            {
                RenderSnapshot snapshot = UnderWhiteAmbientLight(Quaternion.Identity);
                MeshId box = UnitBox(renderer);
                MaterialId matte = Matte(renderer);
                AddSlab(
                    snapshot: snapshot,
                    box: box,
                    material: matte,
                    center: new(0, 0, -2 - half),
                    size: new(2 * half)
                );
                AddSlab(snapshot: snapshot, box: box, material: matte, center: new(0, 0, -12.5f), size: new(40, 40, 1));
                return snapshot;
            },
            "occlusion-silhouette"
        );

        Vector2 topLeft = Gpu.Project(point: new(-half, half, -2), width: Width, height: Height);
        Vector2 bottomRight = Gpu.Project(point: new(half, -half, -2), width: Width, height: Height);
        int largest = LargestDifference(on, off, NearTheEdge(topLeft: topLeft, bottomRight: bottomRight, band: 4));
        output.WriteLine($"largest difference within 4 px of the silhouette: {largest} of 255");
        largest
            .Should()
            .BeLessThanOrEqualTo(1, "the wall lies 10 m behind the box, beyond the radius, and each face is open");
    }

    [Theory]
    [InlineData(0.15)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    public void AFloorBesideAWallLosesTheShareOfItsHemisphereThatTheWallHidesWithinTheRadius(double distance)
    {
        var setting = new GroundTruthOcclusion(RadiusMeters: 1, Slices: 16, StepsPerSide: 16);
        VulkanRenderer renderer = Gpu.Headless(ambientOcclusion: setting);
        try
        {
            RenderSnapshot snapshot = UnderWhiteAmbientLight(CornerView);
            MeshId box = UnitBox(renderer);
            MaterialId matte = Matte(renderer);
            AddSlab(
                snapshot: snapshot,
                box: box,
                material: matte,
                center: new(0, FloorHeight - 0.5f, BackWallZ + 5),
                size: new(40, 1, 20)
            );
            AddSlab(
                snapshot: snapshot,
                box: box,
                material: matte,
                center: new(0, FloorHeight + 10, BackWallZ - 0.5f),
                size: new(40, 40, 1)
            );
            renderer.Render(snapshot, 1f);
            float[] occlusion = renderer.ReadOcclusion();

            // The camera only pitches, so every pixel of a row sees the floor at the same distance from the wall.
            int row = (int)PixelOf(new(0, FloorHeight, BackWallZ + (float)distance), CornerView).Y;
            double rowDistance = FloorDistanceFromTheWall(row);
            double measured = Enumerable.Range(Width / 2 - 20, 40).Average(x => occlusion[row * Width + x]);
            double expected = VisibilityBesideAWall(distance: rowDistance, radius: setting.RadiusMeters);
            output.WriteLine(
                $"{rowDistance:0.000} m from the wall: measured {measured:0.000}, expected {expected:0.000}"
            );
            measured
                .Should()
                .BeApproximately(expected, 0.03, "GTAO at the physical constants estimates the visibility integral");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(DistantSurface.InnerCornerInTheNearRange)]
    [InlineData(DistantSurface.CliffInTheMiddleRange)]
    [InlineData(DistantSurface.PlanetLimbInTheFarRange)]
    public void ASurfaceWhoseRadiusCoversLessThanOneTexelKeepsItsAmbientLightExactly(DistantSurface surface)
    {
        (Capture on, Capture off) = OnAndOff(
            renderer =>
            {
                RenderSnapshot snapshot = UnderWhiteAmbientLight(Quaternion.Identity);
                switch (surface)
                {
                    case DistantSurface.InnerCornerInTheNearRange:
                        AddDistantCorner(renderer: renderer, snapshot: snapshot, distance: 200);
                        break;
                    case DistantSurface.CliffInTheMiddleRange:
                        AddDistantCorner(renderer: renderer, snapshot: snapshot, distance: 50_000);
                        break;
                    case DistantSurface.PlanetLimbInTheFarRange:
                        // Its nearest point lies 3,600 km away, and its limb crosses both sides of the image.
                        snapshot.Instances.Add(
                            Gpu.Instance(
                                mesh: renderer.CreateMesh(Primitives.Sphere(1)),
                                material: Matte(renderer),
                                position: new(0, 0, -1e7f),
                                scale: new(6.4e6f),
                                flags: RenderFlags.Celestial,
                                radius: 6.4e6f
                            )
                        );
                        break;
                    default:
                        throw new UnreachableException();
                }

                return snapshot;
            },
            $"occlusion-distant-{surface}"
        );

        LargestDifference(on, off, EveryPixel())
            .Should()
            .Be(0, "below one texel the fade writes full visibility, and the lighting pass multiplies by exactly 1");
    }

    [Fact]
    public void UnderTheEnvironmentMapAnInnerCornerGetsLessAmbientLightThanWithoutOcclusion()
    {
        (Capture on, Capture off) = OnAndOff(
            renderer =>
            {
                // The map's light alone: no sun, no hemisphere, and a glowing ceiling high above the corner, which the
                // map holds above its horizon.
                RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
                snapshot.Ambient.Intensity = 0;
                AddSlab(
                    snapshot: snapshot,
                    box: UnitBox(renderer),
                    material: BrdfTesting.Glow(renderer, Vector3.One),
                    center: new(0, 40, 0),
                    size: new(400, 1, 400)
                );
                return Corner(renderer, Matte(renderer), snapshot);
            },
            "occlusion-corner-map",
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = 32,
            }
        );

        Vector2 pixel = PixelOf(FloorAtTheCorner, CornerView);
        float unoccluded = off.Luma(pixel);
        float occluded = on.Luma(pixel);
        output.WriteLine(
            $"floor at the corner under the map: {occluded * 255:0} with occlusion, {unoccluded * 255:0} without"
        );
        unoccluded.Should().BeGreaterThan(40f / 255, "the map's glowing ceiling lights the floor");
        (unoccluded - occluded)
            .Should()
            .BeGreaterThanOrEqualTo(
                20f / 255,
                "the occlusion scales the map's ambient terms as it scales the hemisphere's, and the walls hide "
                    + "much of the hemisphere 0.1 m from both"
            );
    }

    [Fact]
    public void TheSunAndTheLampsLightACornerAsIfThereWereNoOcclusion()
    {
        (Capture on, Capture off) = OnAndOff(
            renderer =>
            {
                RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.45f), starIrradiance: 3);
                snapshot.Ambient.Intensity = 0;
                snapshot.Lights.Add(Gpu.Lamp(position: new(-1.5f, -1, -4.5f), color: Vector3.One, intensity: 5));
                return Corner(renderer, Matte(renderer), snapshot);
            },
            "occlusion-sun-and-lamp"
        );

        LargestDifference(on, off, EveryPixel()).Should().Be(0, "the occlusion scales only the ambient light");
    }

    [Fact]
    public void AStillSceneHasTheSameOcclusionInEveryFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(ambientOcclusion: GroundTruthOcclusion.Default);
        try
        {
            RenderSnapshot corner = Corner(renderer, Matte(renderer), UnderWhiteAmbientLight(CornerView));
            Capture first = Gpu.RenderAndCapture(renderer: renderer, snapshot: corner, name: "occlusion-still-first");
            for (int i = 0; i < 3; i++)
                renderer.Render(corner, 1f);
            Capture fifth = Gpu.RenderAndCapture(renderer: renderer, snapshot: corner, name: "occlusion-still-fifth");

            LargestDifference(first, fifth, EveryPixel())
                .Should()
                .Be(0, "nothing carries over from one frame to the next");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AResizedRendererStillDarkensTheCornerAtItsNewSize()
    {
        const int newWidth = 800,
            newHeight = 450;
        VulkanRenderer renderer = Gpu.Headless(ambientOcclusion: GroundTruthOcclusion.Default);
        try
        {
            RenderSnapshot corner = Corner(renderer, Matte(renderer), UnderWhiteAmbientLight(CornerView));
            Capture before = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: corner,
                name: "occlusion-before-resize"
            );
            renderer.Resize(newWidth, newHeight);
            Capture after = Gpu.RenderAndCapture(renderer: renderer, snapshot: corner, name: "occlusion-after-resize");

            after.Width.Should().Be(newWidth);
            before
                .Luma(PixelOf(FloorAtTheCorner, CornerView))
                .Should()
                .BeLessThan(before.Luma(PixelOf(OpenFloor, CornerView)), "the corner darkens at the first size");
            after
                .Luma(PixelOf(FloorAtTheCorner, CornerView, size: (newWidth, newHeight)))
                .Should()
                .BeLessThan(
                    after.Luma(PixelOf(OpenFloor, CornerView, size: (newWidth, newHeight))),
                    "the occlusion images and their sets follow the new size"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheGroundBesideAnImpostorDarkensAsBesideItsGeometry()
    {
        Quaternion view = Gpu.PitchDown(0.25f);
        VulkanRenderer renderer = Gpu.Headless(impostors: true, ambientOcclusion: GroundTruthOcclusion.Default);
        try
        {
            MeshId box = UnitBox(renderer);
            MaterialId matte = Matte(renderer);
            RenderSnapshot Scene(RenderFlags flags) =>
                BoxOnTheFloor(box: box, material: matte, view: view, flags: flags);

            // The floor 0.1 m beside the front half of the left and the right face of the box's base, which the box
            // does not hide from the eye, and 0.1 m in front of its front face.
            Vector2[] beside =
            [
                .. new[] { -3.3f, -3.45f, -3.6f }.Select(z => PixelOf(new(-0.6f, FloorHeight, z), view)),
                .. new[] { -3.3f, -3.45f, -3.6f }.Select(z => PixelOf(new(0.6f, FloorHeight, z), view)),
                .. new[] { -0.3f, 0f, 0.3f }.Select(x => PixelOf(new(x, FloorHeight, -3.2f), view)),
            ];
            renderer.Render(Scene(RenderFlags.NoImpostor), 1f);
            renderer.LastFrameStats.Impostors.Should().Be(0, "NoImpostor keeps the box geometry");
            float[] geometry = renderer.ReadOcclusion();
            for (int i = 0; i < 5; i++)
                renderer.Render(Scene(RenderFlags.None), 1f);
            renderer.LastFrameStats.Impostors.Should().Be(1, "the box is below the height at which it becomes one");
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(0, "the impostor is captured by now");
            float[] impostor = renderer.ReadOcclusion();

            foreach (Vector2 pixel in beside)
            {
                float fromGeometry = OcclusionAt(geometry, pixel);
                float fromImpostor = OcclusionAt(impostor, pixel);
                output.WriteLine($"{pixel}: geometry {fromGeometry:0.000}, impostor {fromImpostor:0.000}");
                fromImpostor
                    .Should()
                    .BeApproximately(
                        fromGeometry,
                        0.05f,
                        "the impostor's distance has the box's real shape, at {0}",
                        pixel
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A 1 m box standing on the floor about 3.9 m away, where it is about 140 px tall, so an impostor unless
    /// <paramref name="flags"/> hold <see cref="RenderFlags.NoImpostor"/>. The floor is never one.
    /// </summary>
    private static RenderSnapshot BoxOnTheFloor(MeshId box, MaterialId material, Quaternion view, RenderFlags flags)
    {
        RenderSnapshot snapshot = UnderWhiteAmbientLight(view);
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: material,
            center: new(0, FloorHeight - 0.5f, -5),
            size: new(20, 1, 20),
            flags: RenderFlags.NoImpostor
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: box,
                material: material,
                position: new(0, FloorHeight + 0.5f, -3.8f),
                scale: Vector3.One,
                flags: flags,
                radius: MathF.Sqrt(0.75f),
                entity: 1
            )
        );
        return snapshot;
    }

    /// <summary>
    /// One capture of <paramref name="scene"/> under <see cref="GroundTruthOcclusion.Default"/> and one with occlusion off,
    /// each by a renderer of its own.
    /// </summary>
    private static (Capture On, Capture Off) OnAndOff(
        Func<VulkanRenderer, RenderSnapshot> scene,
        string name,
        EnvironmentMapSetting? environmentMap = null
    )
    {
        Capture Render(AmbientOcclusionSetting setting, string suffix)
        {
            VulkanRenderer renderer = Gpu.Headless(ambientOcclusion: setting, environmentMap: environmentMap);
            try
            {
                return Gpu.RenderAndCapture(renderer: renderer, snapshot: scene(renderer), name: name + suffix);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        return (Render(GroundTruthOcclusion.Default, "-on"), Render(new NoAmbientOcclusion(), "-off"));
    }

    /// <summary>
    /// An inner corner whose back wall stands <paramref name="distance"/> m ahead of the identity camera, with every
    /// surface at a view depth of at least 0.85 times that: a floor below, the back wall and a wall to the left.
    /// </summary>
    private static void AddDistantCorner(VulkanRenderer renderer, RenderSnapshot snapshot, float distance)
    {
        float scale = distance / 200;
        MeshId box = UnitBox(renderer);
        MaterialId matte = Matte(renderer);
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: matte,
            center: new Vector3(0, -40.5f, -185) * scale,
            size: new Vector3(240, 1, 30) * scale
        );
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: matte,
            center: new Vector3(0, 40, -200.5f) * scale,
            size: new Vector3(240, 160, 1) * scale
        );
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: matte,
            center: new Vector3(-60.5f, 40, -185) * scale,
            size: new Vector3(1, 160, 30) * scale
        );
    }

    /// <summary>The pixels of a square of 2 <paramref name="halfSize"/> + 1 pixels around a center.</summary>
    private static IEnumerable<(int X, int Y)> Block(int centerX, int centerY, int halfSize)
    {
        for (int y = centerY - halfSize; y <= centerY + halfSize; y++)
        for (int x = centerX - halfSize; x <= centerX + halfSize; x++)
            yield return (x, y);
    }

    /// <summary>The pixels whose centers lie within <paramref name="band"/> px of the edge of a rectangle, inside it or outside.</summary>
    private static IEnumerable<(int X, int Y)> NearTheEdge(Vector2 topLeft, Vector2 bottomRight, float band)
    {
        foreach ((int x, int y) in EveryPixel())
        {
            var center = new Vector2(x + 0.5f, y + 0.5f);
            float outsideX = MathF.Max(MathF.Max(topLeft.X - center.X, center.X - bottomRight.X), 0);
            float outsideY = MathF.Max(MathF.Max(topLeft.Y - center.Y, center.Y - bottomRight.Y), 0);
            bool inside = outsideX == 0 && outsideY == 0;
            float toTheEdge = inside
                ? MathF.Min(
                    MathF.Min(center.X - topLeft.X, bottomRight.X - center.X),
                    MathF.Min(center.Y - topLeft.Y, bottomRight.Y - center.Y)
                )
                : MathF.Sqrt(outsideX * outsideX + outsideY * outsideY);
            if (toTheEdge <= band)
                yield return (x, y);
        }
    }

    /// <summary>How far the floor that the pixels of <paramref name="row"/> see lies from the back wall, in m.</summary>
    private static double FloorDistanceFromTheWall(int row)
    {
        Double3 ray = BrdfTesting.Ray(new(Width / 2, row), Width, Height);
        var turned = (Double3)Vector3.Transform(ray.ToVector3(), CornerView);
        double along = FloorHeight / turned.Y;
        return along * turned.Z - BackWallZ;
    }

    /// <summary>
    /// V at a floor point <paramref name="distance"/> m from an infinite wall, for rays of length
    /// <paramref name="radius"/>: the cosine-weighted share of the hemisphere above the floor whose rays travel the radius
    /// without reaching the wall, by the midpoint rule over the polar angle θ from the normal and the azimuth φ from the
    /// direction toward the wall.
    /// </summary>
    private static double VisibilityBesideAWall(double distance, double radius)
    {
        const int polarSteps = 400,
            azimuthSteps = 800;
        const double polarStep = Math.PI / 2 / polarSteps,
            azimuthStep = 2 * Math.PI / azimuthSteps;
        double visible = 0;
        for (int i = 0; i < polarSteps; i++)
        {
            double polar = (i + 0.5) * polarStep;
            double cosineWeight = Math.Cos(polar) * Math.Sin(polar);
            for (int j = 0; j < azimuthSteps; j++)
            {
                double towardTheWall = Math.Sin(polar) * Math.Cos((j + 0.5) * azimuthStep);
                bool reachesTheWall = towardTheWall * radius > distance;
                if (!reachesTheWall)
                    visible += cosineWeight;
            }
        }

        return visible * polarStep * azimuthStep / Math.PI;
    }
}
