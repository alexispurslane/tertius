using System.Diagnostics;
using System.Numerics;
using Tertius.Testing;
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

    /// <summary>The setting of the tests of the floor beside a wall: dense slices and steps, so little noise is left.</summary>
    private static readonly VisibilityBitmaskLight Dense = VisibilityBitmaskLight.Default with
    {
        Slices = 16,
        StepsPerSide = 16,
    };

    /// <summary>The low wall's size, in m: 0.8 m long, 0.4 m tall and 0.1 m thick.</summary>
    private static readonly Vector3 LowWallSize = new(0.8f, 0.4f, 0.1f);

    /// <summary>The tall wall's center: its front face stands at <see cref="BackWallZ"/>, 10 m below its top's middle.</summary>
    private static readonly Vector3 TallWallCenter = new(0, FloorHeight + 10, BackWallZ - 0.5f);

    /// <summary>The tall wall's size, in m: 40 m wide and tall, 1 m thick.</summary>
    private static readonly Vector3 TallWallSize = new(40, 40, 1);

    [Fact]
    public void AnInnerCornerGetsLessAmbientLightThanTheOpenFloor()
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
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

    /// <summary>
    /// A slab 0.6 m square and 0.2 m thick 2 m ahead of the eye, as the box of
    /// <see cref="NoDarkHaloAppearsBesideANearSilhouette"/>, with a wall 0.1 m behind its back: the slab blocks part of
    /// the sky of the wall's points around it, so the wall beside the silhouette holds V below 1, and the slab's
    /// front face is open. The denoise weighs each neighbor by the edge toward that neighbor (XeGTAO's edges), so no blur
    /// takes the wall's estimate across the silhouette onto the face, on its left side or its right.
    /// </summary>
    [Fact]
    public void NoDarkHaloAppearsOnANearFaceInFrontOfAWallThatItDarkens()
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
                    center: new(0, 0, -2.1f),
                    size: new(2 * half, 2 * half, 0.2f)
                );
                AddSlab(snapshot: snapshot, box: box, material: matte, center: new(0, 0, -2.8f), size: new(40, 40, 1));
                return snapshot;
            },
            "occlusion-silhouette-before-a-wall"
        );

        Vector2 topLeft = Gpu.Project(point: new(-half, half, -2), width: Width, height: Height);
        Vector2 bottomRight = Gpu.Project(point: new(half, -half, -2), width: Width, height: Height);
        int middle = (int)((topLeft.Y + bottomRight.Y) / 2);
        int wall = LargestDifference(on, off, Block(centerX: (int)topLeft.X - 6, centerY: middle, halfSize: 2));
        // The face's pixels from 2 to 6 px inside its left and its right edge: within the blurs' reach of the wall.
        int face = LargestDifference(
            on,
            off,
            [
                .. Block(centerX: (int)topLeft.X + 4, centerY: middle, halfSize: 2),
                .. Block(centerX: (int)bottomRight.X - 5, centerY: middle, halfSize: 2),
            ]
        );
        output.WriteLine($"largest difference on the wall beside the silhouette {wall}, on the face {face} of 255");
        wall.Should().BeGreaterThan(4, "the test needs a wall that the box darkens beside the silhouette");
        face.Should().BeLessThanOrEqualTo(1, "the face is open, and the wall's estimate stays on its side of the edge");
    }

    [Theory]
    [InlineData(0.15)]
    [InlineData(0.3)]
    [InlineData(0.6)]
    public void AFloorBesideALowWallLosesTheShareThatTheWallHides(double distance)
    {
        // A wall 0.4 m tall, 0.8 m long and 0.1 m thick, whose every point lies within the radius of the measured floor.
        var wallCenter = new Vector3(0, FloorHeight + LowWallSize.Y / 2, BackWallZ - LowWallSize.Z / 2);
        (double measured, double rowDistance) = FloorBesideAWall(
            wallCenter: wallCenter,
            wallSize: LowWallSize,
            distance: distance
        );

        double expected = VisibilityBesideALowWall(
            floorPoint: new(0, FloorHeight, BackWallZ + rowDistance),
            wallCenter: wallCenter,
            radius: Dense.RadiusMeters
        );
        output.WriteLine($"{rowDistance:0.000} m from the wall: measured {measured:0.000}, expected {expected:0.000}");
        measured
            .Should()
            .BeApproximately(expected, 0.03, "the near sectors estimate the share that the wall and its slab hide");
    }

    /// <summary>
    /// The floor's V within <paramref name="tolerance"/> of the wall's share, 0.03 at 0.3 m and 0.6 m. At 0.15 m, 2
    /// half-resolution texels from the crease, the tolerance is 0.05, because the denoise's 6 blurs reach 6 texels and
    /// the crease cuts them on the wall's side: the blurs pull V toward the open floor's. The user accepted that spread for
    /// the default of 3 slices and 6 blurs (docs/plans/screen-space-illumination.md, decision 43, 2026-11-01). Measured
    /// pull at 0.172 m: +0.016 at 2 blurs, +0.025 at 4, +0.032 at 6. The tolerance keeps the 0.014 that 0.03 left above
    /// the pull of 2 blurs, for the slices' error, over the pull of 6: 0.032 + 0.014 = 0.046, rounded up to 0.05.
    /// </summary>
    [Theory]
    [InlineData(0.15, 0.05)]
    [InlineData(0.3, 0.03)]
    [InlineData(0.6, 0.03)]
    public void AFloorBesideATallWallLosesOnlyTheShareThatTheWallHidesWithinTheRadius(double distance, double tolerance)
    {
        (double measured, double rowDistance) = FloorBesideAWall(
            wallCenter: TallWallCenter,
            wallSize: TallWallSize,
            distance: distance
        );

        double expected = VisibilityBesideATallWall(distance: rowDistance, radius: Dense.RadiusMeters);
        output.WriteLine($"{rowDistance:0.000} m from the wall: measured {measured:0.000}, expected {expected:0.000}");
        measured
            .Should()
            .BeApproximately(
                expected,
                tolerance,
                "the near sectors find the wall within the radius, and beyond it an open sector takes the ambient light"
            );
    }

    [Theory]
    [InlineData(DistantSurface.InnerCornerInTheNearRange)]
    [InlineData(DistantSurface.CliffInTheMiddleRange)]
    [InlineData(DistantSurface.PlanetLimbInTheFarRange)]
    public void ASurfaceWhoseRadiusCoversLessThanOneTexelKeepsItsAmbientLight(DistantSurface surface)
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
                                radius: 6.4e6f,
                                chainDepth: TestChains.CamerasFrame
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

        // The near range holds no step, so the sectors pass writes the open estimate, but the denoise's sums round in
        // single floats, and a store into the half floats of term A and the estimate target may round toward zero, which
        // the Vulkan spec allows: one step below 1, 2⁻¹¹.
        LargestDifference(on, off, EveryPixel())
            .Should()
            .BeLessThanOrEqualTo(
                1,
                "with no near step, the texel takes the ambient light, to within a half float's step"
            );
    }

    /// <summary>
    /// Under the map, each direction around the floor at the corner counts once (docs/plans/screen-space-illumination.md,
    /// decision 41). The walls block most directions within the radius, and they have no direct light, so those
    /// directions bring nothing. The map, drawn from the camera, 5.5 m from the corner, shows the glowing ceiling along
    /// many of them, past the walls' tops and ends, and the map's irradiance without the screen-space light counts that
    /// light. The estimate takes it away. Were the map's light along the blocked directions not taken away, the walls'
    /// net bounce would be 0 and the floor would keep the map's irradiance, to within the rounding of one 8-bit step of
    /// <see cref="ASurfaceWhoseRadiusCoversLessThanOneTexelKeepsItsAmbientLight"/>. Four steps keep the test clear of it.
    /// </summary>
    [Fact]
    public void UnderTheEnvironmentMapAnInnerCornerLosesTheMapsLightAlongTheDirectionsThatItsWallsHide()
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
                4f / 255,
                "the walls hide from the floor the glowing ceiling that the map shows along their directions"
            );
    }

    [Fact]
    public void TheSunAndTheLampsLightACornerAsIfThereWereNoOcclusion()
    {
        static float[] DirectImage(ScreenSpaceLightSetting setting)
        {
            VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting);
            try
            {
                var probe = DirectLightProbe.On(renderer);
                RenderSnapshot snapshot = Gpu.Snapshot(new(0.4f, 0.8f, 0.45f), starIrradiance: 3);
                // A white ambient light too, which the occlusion dims, so that only the direct light stays the same.
                snapshot.Ambient = new()
                {
                    SkyColor = Vector3.One,
                    GroundColor = Vector3.One,
                    Intensity = 1,
                };
                snapshot.Lights.Add(
                    Gpu.Lamp(
                        position: new(-1.5f, -1, -4.5f),
                        color: Vector3.One,
                        intensity: 5,
                        chainDepth: TestChains.CamerasFrame
                    )
                );
                RenderSnapshot corner = Corner(renderer, Matte(renderer), snapshot);
                // Twice: in the second frame, the estimate target holds the occlusion of the first.
                renderer.Render(corner, 1f);
                renderer.Render(corner, 1f);
                return probe.Read();
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        float[] on = DirectImage(VisibilityBitmaskLight.Default);
        float[] off = DirectImage(new NoScreenSpaceLight());

        Vector2 corner = PixelOf(FloorAtTheCorner, CornerView);
        on[((int)corner.Y * Width + (int)corner.X) * 4]
            .Should()
            .BePositive("the lamp 0.5 m above the floor beside the corner lights it");
        on.Should().Equal(off, "the occlusion scales only the ambient light, which the direct image does not hold");
    }

    [Fact]
    public void AStillSceneHasTheSameOcclusionInEveryFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
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
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
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
        VulkanRenderer renderer = Gpu.Headless(impostors: true, screenSpaceLight: VisibilityBitmaskLight.Default);
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
            float[] geometry = ReadOcclusion(renderer);
            for (int i = 0; i < 5; i++)
                renderer.Render(Scene(RenderFlags.None), 1f);
            renderer.LastFrameStats.Impostors.Should().Be(1, "the box is below the height at which it becomes one");
            renderer.LastFrameStats.ImpostorCaptures.Should().Be(0, "the impostor is captured by now");
            float[] impostor = ReadOcclusion(renderer);

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
                entity: 1,
                chainDepth: TestChains.CamerasFrame
            )
        );
        return snapshot;
    }

    /// <summary>
    /// One capture of <paramref name="scene"/> under <see cref="VisibilityBitmaskLight.Default"/> and one with occlusion off,
    /// each by a renderer of its own.
    /// </summary>
    private static (Capture On, Capture Off) OnAndOff(
        Func<VulkanRenderer, RenderSnapshot> scene,
        string name,
        EnvironmentMapSetting? environmentMap = null
    )
    {
        Capture Render(ScreenSpaceLightSetting setting, string suffix)
        {
            VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting, environmentMap: environmentMap);
            try
            {
                return Gpu.RenderAndCapture(renderer: renderer, snapshot: scene(renderer), name: name + suffix);
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        return (Render(VisibilityBitmaskLight.Default, "-on"), Render(new NoScreenSpaceLight(), "-off"));
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
    /// The mean V of the floor's pixels in the middle 40 columns of the row that sees the floor
    /// <paramref name="distance"/> m in front of a wall of <paramref name="wallSize"/> around
    /// <paramref name="wallCenter"/>, under <see cref="Dense"/>, and the floor's distance from the wall in that row.
    /// </summary>
    private static (double Measured, double RowDistance) FloorBesideAWall(
        Vector3 wallCenter,
        Vector3 wallSize,
        double distance
    )
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: Dense);
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
            AddSlab(snapshot: snapshot, box: box, material: matte, center: wallCenter, size: wallSize);
            renderer.Render(snapshot, 1f);
            float[] occlusion = ReadOcclusion(renderer);

            // The camera only pitches, so every pixel of a row sees the floor at the same distance from the wall.
            int row = (int)PixelOf(new(0, FloorHeight, BackWallZ + (float)distance), CornerView).Y;
            double measured = Enumerable.Range(Width / 2 - 20, 40).Average(x => occlusion[row * Width + x]);
            return (measured, FloorDistanceFromTheWall(row));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// V at <paramref name="floorPoint"/> beside the low wall around <paramref name="wallCenter"/>, for rays of length
    /// <paramref name="radius"/> under the slab model: the cosine-weighted share of the hemisphere above the floor whose
    /// rays pass no point that lies within κ z behind the surface that the eye sees along that point's view ray. By the
    /// midpoint rule over the polar angle θ from the normal, the azimuth φ and the length along the ray.
    /// </summary>
    private static double VisibilityBesideALowWall(Double3 floorPoint, Vector3 wallCenter, double radius)
    {
        const int polarSteps = 90,
            azimuthSteps = 360,
            lengthSteps = 100;
        const double polarStep = Math.PI / 2 / polarSteps,
            azimuthStep = 2 * Math.PI / azimuthSteps;
        var forward = (Double3)Vector3.Transform(-Vector3.UnitZ, CornerView);
        Double3 wallLow = wallCenter - LowWallSize / 2;
        Double3 wallHigh = wallCenter + LowWallSize / 2;
        double visible = 0;
        for (int i = 0; i < polarSteps; i++)
        {
            double polar = (i + 0.5) * polarStep;
            double cosineWeight = Math.Cos(polar) * Math.Sin(polar);
            for (int j = 0; j < azimuthSteps; j++)
            {
                double azimuth = (j + 0.5) * azimuthStep;
                var direction = new Double3(
                    Math.Sin(polar) * Math.Cos(azimuth),
                    Math.Cos(polar),
                    Math.Sin(polar) * Math.Sin(azimuth)
                );
                bool blocked = Enumerable
                    .Range(0, lengthSteps)
                    .Any(k =>
                        InASlab(
                            point: floorPoint + direction * ((k + 0.5) * radius / lengthSteps),
                            forward: forward,
                            wallLow: wallLow,
                            wallHigh: wallHigh
                        )
                    );
                if (!blocked)
                    visible += cosineWeight;
            }
        }

        return visible * polarStep * azimuthStep / Math.PI;
    }

    /// <summary>
    /// Whether a point lies within κ z behind the surface that the eye sees along its view ray: the floor's top face or
    /// the low wall, a box from <paramref name="wallLow"/> to <paramref name="wallHigh"/>.
    /// </summary>
    private static bool InASlab(Double3 point, Double3 forward, Double3 wallLow, Double3 wallHigh)
    {
        Double3 toPoint = point.Normalized();
        double floorDistance = toPoint.Y < 0 ? FloorHeight / toPoint.Y : double.PositiveInfinity;
        double seen = Math.Min(floorDistance, BoxEntry(toPoint, wallLow, wallHigh) ?? double.PositiveInfinity);
        double pointDepth = Double3.Dot(point, forward);
        double seenDepth = seen * Double3.Dot(toPoint, forward);
        return seenDepth <= pointDepth && pointDepth <= seenDepth * (1 + CellBounds.ThicknessShare);
    }

    /// <summary>The distance along a unit ray from the eye at which it enters a box, or null when it misses.</summary>
    private static double? BoxEntry(Double3 direction, Double3 low, Double3 high)
    {
        double near = 0,
            far = double.PositiveInfinity;
        foreach (
            (double along, double from, double to) in new[]
            {
                (direction.X, low.X, high.X),
                (direction.Y, low.Y, high.Y),
                (direction.Z, low.Z, high.Z),
            }
        )
        {
            if (along == 0)
            {
                if (from > 0 || to < 0)
                    return null;
                continue;
            }
            double a = from / along,
                b = to / along;
            near = Math.Max(near, Math.Min(a, b));
            far = Math.Min(far, Math.Max(a, b));
        }

        return near <= far ? near : null;
    }

    /// <summary>
    /// V at a floor point <paramref name="distance"/> m in front of the tall wall: the cosine-weighted share of the
    /// hemisphere above the floor whose rays meet the wall within <paramref name="radius"/>. A ray that meets it only
    /// beyond the radius is open. By the midpoint rule over the polar angle θ from the normal and the azimuth φ.
    /// </summary>
    private static double VisibilityBesideATallWall(double distance, double radius)
    {
        const int polarSteps = 400,
            azimuthSteps = 800;
        const double polarStep = Math.PI / 2 / polarSteps,
            azimuthStep = 2 * Math.PI / azimuthSteps;
        var floorPoint = new Double3(0, FloorHeight, BackWallZ + distance);
        double blocked = 0;
        for (int i = 0; i < polarSteps; i++)
        {
            double polar = (i + 0.5) * polarStep;
            double cosineWeight = Math.Cos(polar) * Math.Sin(polar);
            for (int j = 0; j < azimuthSteps; j++)
            {
                double azimuth = (j + 0.5) * azimuthStep;
                var direction = new Double3(
                    Math.Sin(polar) * Math.Cos(azimuth),
                    Math.Cos(polar),
                    Math.Sin(polar) * Math.Sin(azimuth)
                );
                if (direction.Z >= 0)
                    continue;
                double along = -distance / direction.Z;
                Double3 meeting = floorPoint + direction * along;
                bool onTheWallWithinTheRadius =
                    along < radius
                    && Math.Abs(meeting.X) <= TallWallSize.X / 2
                    && meeting.Y <= TallWallCenter.Y + TallWallSize.Y / 2;
                if (onTheWallWithinTheRadius)
                    blocked += cosineWeight;
            }
        }

        return 1 - blocked * polarStep * azimuthStep / Math.PI;
    }
}
