using System.Numerics;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The acceptance criteria of the bounce light on the GPU (docs/plans/screen-space-illumination.md, "GPU tests of the
/// acceptance criteria"): a lit surface lights the surfaces near it with its own color, near and through the far rays
/// beyond the radius, a lit surface off the screen gives no light, no direct light gives no bounce, and in a furnace the
/// open and the blocked shares sum to the light of an open surface. At 640 × 360 under
/// <see cref="VisibilityBitmaskLight.Default"/>, unless a test says otherwise.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BounceLightRenderTests(ITestOutputHelper output)
{
    /// <summary>The top face of the floor under the raised box, 0.1 m below the eye.</summary>
    private const float RaisedBoxFloor = -0.1f;

    /// <summary>The raised box's bottom face, 0.3 m above the floor and 0.2 m above the eye, which sees it from below.</summary>
    private const float Underside = 0.2f;

    /// <summary>Toward the sun: above the open side of the corner, so it lights the floor and the back wall.</summary>
    private static readonly Vector3 SunDirection = new(0.4f, 0.8f, 0.45f);

    [Fact]
    public void ALitRedWallTintsTheFloorBesideItRed()
    {
        // The floor 0.1 to 0.3 m in front of the back wall, and 3.1 to 3.3 m in front of it.
        Vector2[] beside = FloorPixels(fromTheWall: [0.1f, 0.2f, 0.3f], view: CornerView);
        Vector2[] away = FloorPixels(fromTheWall: [3.1f, 3.2f, 3.3f], view: CornerView);
        float[] on = RedWallHdr(VisibilityBitmaskLight.Default, CornerView);
        float[] off = RedWallHdr(new NoScreenSpaceLight(), CornerView);

        double besideOn = MeanRedShare(on, beside);
        double besideOff = MeanRedShare(off, beside);
        double awayOn = MeanRedShare(on, away);
        output.WriteLine(
            $"red share beside the wall {besideOn:0.0000} (off {besideOff:0.0000}), 3 m away {awayOn:0.0000}"
        );
        besideOn.Should().BeGreaterThanOrEqualTo(besideOff + 0.02, "the lit red wall bounces its color onto the floor");
        besideOn
            .Should()
            .BeGreaterThanOrEqualTo(awayOn + 0.02, "the floor 3 m away lies beyond the radius of the wall");
    }

    [Fact]
    public void AWallBeyondTheRadiusStillLightsTheFloorThroughTheFarRays()
    {
        // The floor 2.9 to 3.1 m in front of the red wall, six times the radius away from it, seen by a camera turned
        // down 15°, so that the screen shows the wall up to 2.8 m above the floor.
        Quaternion view = Gpu.PitchDown(MathF.PI / 12);
        Vector2[] measured = FloorPixels(fromTheWall: [2.9f, 3f, 3.1f], view: view);
        float[] on = RedWallHdr(VisibilityBitmaskLight.Default with { RadiusMeters = 0.5f }, view);
        float[] off = RedWallHdr(new NoScreenSpaceLight(), view);

        double redOn = MeanRedShare(on, measured);
        double redOff = MeanRedShare(off, measured);
        output.WriteLine($"red share 3 m from the wall {redOn:0.0000} (off {redOff:0.0000})");
        // The screen shows the wall over about a fifth of the floor's cosine-weighted hemisphere, and the wall's red
        // light is about a third of the floor's own: about 0.01 to 0.015 more red share. With no far ray, the near range
        // holds nothing there, and the share is the same as with the setting off.
        redOn
            .Should()
            .BeGreaterThanOrEqualTo(
                redOff + 0.005,
                "the far rays from the floor meet the lit red wall beyond the radius and bring its light"
            );
    }

    [Fact]
    public void ALitWallOffTheScreenGivesTheFloorNoBounce()
    {
        (float[] withWall, Vector2[] measured) = WallOffTheLeftEdge(wall: true);
        (float[] open, _) = WallOffTheLeftEdge(wall: false);

        foreach (Vector2 pixel in measured)
        {
            int i = ((int)pixel.Y * Width + (int)pixel.X) * 4;
            output.WriteLine(
                $"{pixel}: bounce ({withWall[i]}, {withWall[i + 1]}, {withWall[i + 2]}) against ({open[i]}, "
                    + $"{open[i + 1]}, {open[i + 2]}), V {withWall[i + 3]:0.000} against {open[i + 3]:0.000} without the wall"
            );
            // Against the open floor, not 0: the floor's own steps leave a rounding of its own light, about 10⁻⁴ of it.
            (withWall[i], withWall[i + 1], withWall[i + 2])
                .Should()
                .Be((open[i], open[i + 1], open[i + 2]), "no pixel shows the wall, so no ray meets it, at {0}", pixel);
            withWall[i + 3]
                .Should()
                .BeApproximately(open[i + 3], 0.02f, "a ray that leaves the screen is open, at {0}", pixel);
        }
    }

    [Fact]
    public void ASunlitFloorBrightensTheUndersideOfABoxAboveIt()
    {
        float[] on = RaisedBoxHdr(VisibilityBitmaskLight.Default);
        float[] off = RaisedBoxHdr(new NoScreenSpaceLight());

        foreach (Vector2 pixel in UndersidePixels())
        {
            Vector3 lit = ReflectionTesting.At(on, ((int)pixel.X, (int)pixel.Y));
            Vector3 dark = ReflectionTesting.At(off, ((int)pixel.X, (int)pixel.Y));
            output.WriteLine($"{pixel}: on {lit}, off {dark}");
            dark.Should().Be(Vector3.Zero, "no direct light and no ambient light reach the underside at {0}", pixel);
            lit.X.Should()
                .BePositive("the sunlit floor 0.3 m below bounces its light onto the underside at {0}", pixel);
        }
    }

    [Fact]
    public void ASceneWithNoDirectLightGetsNoBounce()
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            renderer.Render(Corner(renderer, Matte(renderer), UnderWhiteAmbientLight(CornerView)), 1f);
            float[] estimate = renderer.ReadEstimate();

            for (int i = 0; i < estimate.Length; i += 4)
                (estimate[i], estimate[i + 1], estimate[i + 2])
                    .Should()
                    .Be((0f, 0f, 0f), "only the direct light bounces, and the corner has only the ambient light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void InAFurnaceACreaseGetsTheLightOfTheOpenFloor()
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            // The walls emit the ambient light's radiance and reflect nothing; the floor reflects and has no specular lobe.
            MaterialId floor = renderer.CreateMaterial(
                new(BaseColor: new(x: 1, y: 1, z: 1, w: 1), Roughness: 1, Reflectance: 0)
            );
            MaterialId glow = BrdfTesting.Glow(renderer, Vector3.One);
            RenderSnapshot furnace = UnderWhiteAmbientLight(CornerView);
            MeshId box = UnitBox(renderer);
            AddCornerSlabs(snapshot: furnace, box: box, floor: floor, backWall: glow, sideWall: glow);
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: furnace, name: "bounce-furnace");

            float atTheCorner = image.Luma(PixelOf(FloorAtTheCorner, CornerView));
            float open = image.Luma(PixelOf(OpenFloor, CornerView));
            output.WriteLine($"floor at the corner {atTheCorner * 255:0.0}, open floor {open * 255:0.0} of 255");
            atTheCorner
                .Should()
                .BeApproximately(open, 3f / 255, "each direction is open or blocked, and both bring the same light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The HDR image of the floor and the red back wall of the corner, without the side wall, under the sun and a dim
    /// white ambient light, seen through <paramref name="view"/> and rendered by a renderer of <paramref name="setting"/>.
    /// </summary>
    private static float[] RedWallHdr(ScreenSpaceLightSetting setting, Quaternion view)
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(SunDirection, sunIntensity: 3);
            snapshot.Ambient = new()
            {
                SkyColor = Vector3.One,
                GroundColor = Vector3.One,
                Intensity = 0.3f,
            };
            Look(snapshot, view);
            MeshId box = UnitBox(renderer);
            AddSlab(
                snapshot: snapshot,
                box: box,
                material: renderer.CreateMaterial(MaterialDesc.Color(new(x: 1, y: 1, z: 1, w: 1), roughness: 0.8f)),
                center: new(0, FloorHeight - 0.5f, BackWallZ + 5),
                size: new(20, 1, 20)
            );
            AddSlab(
                snapshot: snapshot,
                box: box,
                material: renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.9f, y: 0.05f, z: 0.05f, w: 1), roughness: 0.8f)
                ),
                center: new(0, FloorHeight + 5, BackWallZ - 0.5f),
                size: new(20, 20, 1)
            );
            renderer.Render(snapshot, 1f);
            return renderer.ReadHdr();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The estimate target of a sunlit white floor seen by a level camera, with a lit red wall just past the left edge of
    /// the screen when <paramref name="wall"/>, and the floor pixels 0.3 m inside that edge, 3.5 to 4.5 m ahead. The
    /// wall's face lies 2 cm outside the left side plane of the view, parallel to it, so no pixel shows it.
    /// </summary>
    private static (float[] Estimate, Vector2[] Measured) WallOffTheLeftEdge(bool wall)
    {
        float tanHalfX = MathF.Tan(Gpu.Fov / 2) * Width / Height;
        // The side plane's normal toward the inside of the view, and its direction away from the eye.
        Vector3 inward = Vector3.Normalize(new(1, 0, -tanHalfX));
        Vector3 along = Vector3.Normalize(new(-tanHalfX, 0, -1));
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            // The sun lights the floor and the wall's face, which faces the inside of the view.
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.5f, 0.8f, -0.3f), sunIntensity: 3);
            snapshot.Ambient = new()
            {
                SkyColor = Vector3.One,
                GroundColor = Vector3.One,
                Intensity = 0.3f,
            };
            MeshId box = UnitBox(renderer);
            AddSlab(
                snapshot: snapshot,
                box: box,
                material: renderer.CreateMaterial(MaterialDesc.Color(new(x: 1, y: 1, z: 1, w: 1), roughness: 0.8f)),
                center: new(0, FloorHeight - 0.5f, -10),
                size: new(40, 1, 40)
            );
            if (wall)
            {
                // From the eye to 8 m along the side plane, past the measured floor's 6.5 m.
                Vector3 face = along * 4 - inward * 0.02f;
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: renderer.CreateMaterial(
                            MaterialDesc.Color(new(x: 0.9f, y: 0.05f, z: 0.05f, w: 1), roughness: 0.8f)
                        ),
                        position: face - inward * 0.5f,
                        // The box's x turns onto the inward normal.
                        rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.Atan(tanHalfX)),
                        scale: new(1, 20, 8),
                        radius: 11
                    )
                );
            }

            renderer.Render(snapshot, 1f);
            Vector2[] measured =
            [
                .. new[] { 3.5f, 4f, 4.5f }.Select(depth =>
                    PixelOf(new(-tanHalfX * depth + 0.3f, FloorHeight, -depth), Quaternion.Identity)
                ),
            ];
            return (renderer.ReadEstimate(), measured);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The floor pixels at <paramref name="fromTheWall"/> m in front of the back wall, at x of −1, 0 and 1 m, seen through <paramref name="view"/>.</summary>
    private static Vector2[] FloorPixels(float[] fromTheWall, Quaternion view) =>
        [
            .. fromTheWall.SelectMany(distance =>
                new[] { -1f, 0f, 1f }.Select(x => PixelOf(new(x, FloorHeight, BackWallZ + distance), view))
            ),
        ];

    /// <summary>The mean over <paramref name="pixels"/> of R / (R + G + B) in the HDR image <paramref name="hdr"/>.</summary>
    private static double MeanRedShare(float[] hdr, Vector2[] pixels) =>
        pixels
            .Select(pixel => ReflectionTesting.At(hdr, ((int)pixel.X, (int)pixel.Y)))
            .Average(color => color.X / (double)(color.X + color.Y + color.Z));

    /// <summary>
    /// The HDR image of a box 0.6 m wide whose bottom face floats 0.3 m above a white floor, seen from the eye between
    /// them, under a low sun from the side and no ambient light, rendered by a renderer of <paramref name="setting"/>.
    /// </summary>
    private static float[] RaisedBoxHdr(ScreenSpaceLightSetting setting)
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting);
        try
        {
            // The sun stands 30° high to the right, so the box's shadow falls 0.5 m and more to its left.
            RenderSnapshot snapshot = Gpu.Snapshot(new(0.85f, 0.5f, 0.15f), sunIntensity: 3);
            snapshot.Ambient.Intensity = 0;
            MeshId box = UnitBox(renderer);
            MaterialId white = renderer.CreateMaterial(
                MaterialDesc.Color(new(x: 1, y: 1, z: 1, w: 1), roughness: 0.8f)
            );
            AddSlab(
                snapshot: snapshot,
                box: box,
                material: white,
                center: new(0, RaisedBoxFloor - 0.5f, -5),
                size: new(20, 1, 20)
            );
            AddSlab(
                snapshot: snapshot,
                box: box,
                material: white,
                center: new(0, Underside + 0.3f, -1.2f),
                size: new(0.6f, 0.6f, 0.8f)
            );
            renderer.Render(snapshot, 1f);
            return renderer.ReadHdr();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The pixels of the box's bottom face, 0.1 m and more inside its edges.</summary>
    private static IEnumerable<Vector2> UndersidePixels() =>
        new[] { -0.2f, 0f, 0.2f }.SelectMany(x =>
            new[] { -0.9f, -1.2f, -1.5f }.Select(z => PixelOf(new(x, Underside, z), Quaternion.Identity))
        );

    /// <summary>Adds the floor, the back wall and the side wall of the corner, each of its own material.</summary>
    private static void AddCornerSlabs(
        RenderSnapshot snapshot,
        MeshId box,
        MaterialId floor,
        MaterialId backWall,
        MaterialId sideWall
    )
    {
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: floor,
            center: new(0, FloorHeight - 0.5f, BackWallZ + 5),
            size: new(20, 1, 20)
        );
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: backWall,
            center: new(0, FloorHeight + 5, BackWallZ - 0.5f),
            size: new(20, 20, 1)
        );
        AddSlab(
            snapshot: snapshot,
            box: box,
            material: sideWall,
            center: new(SideWallX - 0.5f, FloorHeight + 5, BackWallZ + 5),
            size: new(1, 20, 20)
        );
    }
}
