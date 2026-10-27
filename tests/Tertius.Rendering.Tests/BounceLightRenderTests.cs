using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The acceptance criteria of the bounce light on the GPU (docs/plans/screen-space-illumination.md, "GPU tests of the
/// acceptance criteria"): a lit surface lights the surfaces within the radius with its own color, a lit surface beyond
/// the radius or off the screen gives no light, no direct light gives no bounce, and in a furnace the
/// open and the blocked shares sum to the light of an open surface. A still scene has the same bounce in every frame, a
/// resized renderer bounces light at its new size, and the direct and the ambient passes keep a pixel within the
/// surfaces' share of the HDR image. At 640 × 360 under <see cref="VisibilityBitmaskLight.Default"/>, unless a test says
/// otherwise.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BounceLightRenderTests(ITestOutputHelper output)
{
    /// <summary>The top face of the floor under the raised box, 0.1 m below the eye.</summary>
    private const float RaisedBoxFloor = -0.1f;

    /// <summary>The raised box's bottom face, 0.3 m above the floor and 0.2 m above the eye, which sees it from below.</summary>
    private const float Underside = 0.2f;

    /// <summary>The view depth of the convex edge's front face, in m.</summary>
    private const float FrontFaceDepth = 5;

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
    public void AWallBeyondTheRadiusGivesTheFloorNoBounceAndLeavesItOpen()
    {
        // The floor 2.9 to 3.1 m in front of the red wall, six times the radius away from it, seen by a camera turned
        // down 15°, so that the screen shows the wall up to 2.8 m above the floor. Beyond the radius, an open sector
        // takes the ambient light (decision 40).
        Quaternion view = Gpu.PitchDown(MathF.PI / 12);
        Vector2[] measured = FloorPixels(fromTheWall: [2.9f, 3f, 3.1f], view: view);
        VulkanRenderer renderer = Gpu.Headless(
            screenSpaceLight: VisibilityBitmaskLight.Default with
            {
                RadiusMeters = 0.5f,
            }
        );
        try
        {
            renderer.Render(RedWall(renderer, view), 1f);
            float[] estimate = renderer.ReadEstimate();

            foreach (Vector2 pixel in measured)
            {
                int i = ((int)pixel.Y * Width + (int)pixel.X) * 4;
                var bounce = new Vector3(estimate[i], estimate[i + 1], estimate[i + 2]);
                output.WriteLine($"{pixel}: bounce {bounce}, V {estimate[i + 3]:0.0000}");
                bounce
                    .Should()
                    .Be(Vector3.Zero, "the lit wall lies beyond the radius, and the floor bounces nothing on itself");
                estimate[i + 3]
                    .Should()
                    .BeGreaterThanOrEqualTo(0.999f, "every sector that no near step blocks is open, at {0}", pixel);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALitWallOffTheScreenGivesTheFloorNoBounce()
    {
        (float[] estimate, float[] direct, Vector2[] measured) = WallOffTheLeftEdge();

        foreach (Vector2 pixel in measured)
        {
            int i = ((int)pixel.Y * Width + (int)pixel.X) * 4;
            var bounce = new Vector3(estimate[i], estimate[i + 1], estimate[i + 2]);
            var floorLight = new Vector3(direct[i], direct[i + 1], direct[i + 2]);
            output.WriteLine(
                $"{pixel}: bounce {bounce}, the floor's direct light {floorLight}, V {estimate[i + 3]:0.000}"
            );
            estimate[i + 3]
                .Should()
                .BeGreaterThanOrEqualTo(0.98f, "a ray that leaves the screen is open, at {0}", pixel);
            bounce
                .Should()
                .Be(
                    Vector3.Zero,
                    "no pixel shows the wall, a ray that leaves the screen brings no light, and the floor bounces nothing on itself, at {0}",
                    pixel
                );
        }
    }

    [Fact]
    public void AFlatFloorAloneBlocksNothingAndBouncesNothingOnItself()
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            // The term that the sector pass leaves, before the blur averages it.
            renderer.Graph.Skipped.UnionWith(["occlusion denoise", "occlusion upsample"]);
            renderer.Render(SunlitFloor(renderer), 1f);
            (float[] term, int halfWidth, _) = renderer.ReadOcclusionTerm();
            float[] depths = renderer.ReadOcclusionViewDepth().Depths;

            int[] floor = [.. Enumerable.Range(0, depths.Length).Where(i => depths[i] < (float)HorizonSearch.SkyDepth)];
            int[] blocking = [.. floor.Where(i => BlocksASector(term, i))];
            int[] bouncing = [.. floor.Where(i => term[i * 4] != 0 || term[i * 4 + 1] != 0 || term[i * 4 + 2] != 0)];
            output.WriteLine(
                $"of {floor.Length} floor texels, {blocking.Length} block sectors and {bouncing.Length} bounce light"
                    + (
                        blocking.Length > 0
                            ? $"; the first at ({blocking[0] % halfWidth}, {blocking[0] / halfWidth})"
                            : ""
                    )
            );

            floor.Length.Should().BeGreaterThan(20_000, "the floor fills the lower half of the 320 × 180 texels");
            blocking.Should().BeEmpty("a flat floor's own steps block nothing above it (decisions 12 and 39)");
            bouncing.Should().BeEmpty("a flat floor bounces none of its own light on itself");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AFaceBesideAGrazingConvexEdgeBlocksNothingAndGetsNoBounce()
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            // The term that the sector pass leaves, before the blur averages it.
            renderer.Graph.Skipped.UnionWith(["occlusion denoise", "occlusion upsample"]);
            renderer.Render(SunlitConvexEdge(renderer), 1f);
            (float[] term, int halfWidth, _) = renderer.ReadOcclusionTerm();
            float[] depths = renderer.ReadOcclusionViewDepth().Depths;

            // The side face lies 6 cm and more behind the front face at every texel that shows it.
            int[] front =
            [
                .. Enumerable.Range(0, depths.Length).Where(i => Math.Abs(depths[i] - FrontFaceDepth) < 0.01f),
            ];
            int[] blocking = [.. front.Where(i => BlocksASector(term, i))];
            int[] bouncing = [.. front.Where(i => term[i * 4] != 0 || term[i * 4 + 1] != 0 || term[i * 4 + 2] != 0)];
            output.WriteLine(
                $"of {front.Length} texels of the front face, {blocking.Length} block sectors and {bouncing.Length} get a bounce"
                    + (
                        blocking.Length > 0
                            ? $"; the first at ({blocking[0] % halfWidth}, {blocking[0] / halfWidth})"
                            : ""
                    )
            );

            front
                .Length.Should()
                .BeGreaterThan(8_000, "the front face spans 47 of the 320 texel columns, in every row");
            blocking.Should().BeEmpty("nothing in view rises above the front face's plane (decision 39)");
            bouncing.Should().BeEmpty("the lit side face lies behind the front face's plane");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AWallSquareToTheViewBlocksAsMuchWhicheverWayTheCameraFaces()
    {
        // Turned 63°, the wall's normal is no longer a view axis, so the rounding of its decoded normal moves each
        // step's surface point off the wall's one view depth by about 10⁻⁷ of it. Without the depths' precision, such a
        // step would stand at its texel's own point, off its slice.
        double straight = BlockedShareBeforeASquareWall(turn: 0);
        double turned = BlockedShareBeforeASquareWall(turn: 1.1f);
        output.WriteLine($"blocked share: {straight:0.000} with the camera along −Z, {turned:0.000} turned 63°");

        straight.Should().BeGreaterThan(1_000, "the wall blocks the floor before it");
        turned
            .Should()
            .BeApproximately(
                straight,
                straight / 10_000,
                "the same scene in view space blocks the same sectors: the depths' precision keeps every step on its slice"
            );
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

    [Fact]
    public void AStillSceneHasTheSameBounceInEveryFrame()
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            RenderSnapshot redWall = RedWall(renderer, CornerView);
            renderer.Render(redWall, 1f);
            float[] first = renderer.ReadHdr();
            for (int i = 0; i < 4; i++)
                renderer.Render(redWall, 1f);
            float[] fifth = renderer.ReadHdr();

            MeanRedShare(first, FloorPixels(fromTheWall: [0.1f, 0.2f, 0.3f], view: CornerView))
                .Should()
                .BeGreaterThan(
                    MeanRedShare(first, FloorPixels(fromTheWall: [3.1f, 3.2f, 3.3f], view: CornerView)),
                    "the red wall bounces its light onto the floor beside it"
                );
            fifth.Should().Equal(first, "nothing of the estimate carries over from one frame to the next");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AResizedRendererStillBouncesLightAtItsNewSize()
    {
        const int newWidth = 800,
            newHeight = 450;
        // The floor beside the wall from x = −3.5 m to 3.5 m, which at the new size reaches past the old 640 pixels.
        float[] fromTheWall = [0.1f, 0.2f, 0.3f];
        float[] across = [-3.5f, 0f, 3.5f];
        Vector2[] before = FloorPixels(
            fromTheWall: fromTheWall,
            across: across,
            view: CornerView,
            size: (Width, Height)
        );
        Vector2[] after = FloorPixels(
            fromTheWall: fromTheWall,
            across: across,
            view: CornerView,
            size: (newWidth, newHeight)
        );
        Vector2[] away = FloorPixels(fromTheWall: [3.1f, 3.2f, 3.3f], view: CornerView);
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            RenderSnapshot redWall = RedWall(renderer, CornerView);
            renderer.Render(redWall, 1f);
            float[] first = renderer.ReadHdr();
            renderer.Resize(newWidth, newHeight);
            renderer.Render(redWall, 1f);
            float[] resized = renderer.ReadHdr();

            MeanRedShare(first, before)
                .Should()
                .BeGreaterThanOrEqualTo(MeanRedShare(first, away) + 0.02, "the red wall tints the floor beside it");
            after.Should().Contain(pixel => pixel.X >= Width, "the measured pixels reach past the old width");
            for (int i = 0; i < before.Length; i++)
            {
                double share = MeanRedShare(first, [before[i]]);
                double resizedShare = MeanRedShare(resized, [after[i]], newWidth);
                output.WriteLine($"{before[i]} → {after[i]}: red share {share:0.0000} → {resizedShare:0.0000}");
                resizedShare
                    .Should()
                    .BeApproximately(
                        share,
                        0.01,
                        "the occlusion images and their sets follow the new size, at {0}",
                        after[i]
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The lit red wall in the corner, and a sphere whose normal map tilts its shading normals away from its geometric
    /// normals, so that the two normals of the normals target differ.
    /// </summary>
    private static RenderSnapshot RedWallWithANormalMappedSphere(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = RedWall(renderer, CornerView);
        byte[] tiltedTexel = [128, 200, 220, 255];
        MaterialId tilted = renderer.CreateMaterial(
            MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.8f) with
            {
                NormalMap = renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: tiltedTexel, Srgb: false)),
            }
        );
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Sphere(1)),
                material: tilted,
                position: new(0.8f, FloorHeight + 0.6f, BackWallZ + 1.5f),
                scale: new(0.6f),
                radius: 0.6f
            )
        );
        return snapshot;
    }

    [Fact]
    public void TheDepthPassCopiesTheDirectLightAndTheGeometricNormalOfEachTexelsRepresentativePixel()
    {
        // The near steps read the copies in place of the full-resolution images, so the copies must hold the very values
        // of pixel 2t (decision 25).
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            renderer.Render(RedWallWithANormalMappedSphere(renderer), 1f);
            float[] direct = renderer.ReadDirect();
            float[] normals = renderer.ReadNormals();
            (float[] directCopy, int width, int height) = renderer.ReadOcclusionDirect();
            float[] normalCopy = renderer.ReadOcclusionGeometricNormal().Rg;

            float[] directAtTexels = new float[3 * width * height];
            float[] normalAtTexels = new float[2 * width * height];
            int mappedTexels = 0;
            float[] directCopyRgb = new float[3 * width * height];
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                int texel = y * width + x;
                int pixel = 2 * y * Width + 2 * x;
                for (int c = 0; c < 3; c++)
                {
                    directAtTexels[3 * texel + c] = direct[4 * pixel + c];
                    directCopyRgb[3 * texel + c] = directCopy[4 * texel + c];
                }
                normalAtTexels[2 * texel] = normals[4 * pixel + 2];
                normalAtTexels[2 * texel + 1] = normals[4 * pixel + 3];
                if (Math.Abs(normals[4 * pixel] - normals[4 * pixel + 2]) > 0.02f)
                    mappedTexels++;
            }

            directAtTexels
                .Count(value => value > 0)
                .Should()
                .BeGreaterThan(1000, "the sun lights the wall and the floor");
            mappedTexels.Should().BeGreaterThan(100, "the sphere's shading normals differ from its geometric normals");
            directCopyRgb.Should().Equal(directAtTexels, "a half float stores back as itself on every driver");
            // The spec lets a driver round a float to SNORM16 other than to the nearest value, so the stored k / 32767
            // may come back one step off (ARCHITECTURE.md, "Portable Vulkan").
            const float snormStep = 1f / 32767;
            normalCopy.Should().Equal(normalAtTexels, (copy, pixel) => Math.Abs(copy - pixel) <= snormStep * 1.01f);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ThePassesKeepAPixelWithinTheSurfacesShare()
    {
        const float cap = ShaderInput.MaxRadiance;
        // A lamp 1 m above the floor and 8 m ahead, whose mirror image the eye sees on the floor at 4.8 m.
        var lamp = new Vector3(0, FloorHeight + 1, -8);
        Vector2 highlight = PixelOf(new(0, FloorHeight, -4.8f), Quaternion.Identity);
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
            // Bright, but below the cap on its own: the cap binds only on the sum of the two passes.
            snapshot.Ambient = new()
            {
                SkyColor = Vector3.One,
                GroundColor = Vector3.One,
                Intensity = 20_000,
            };
            snapshot.Lights.Add(
                new PointRenderLight(
                    Position: lamp,
                    Color: Vector3.One,
                    Intensity: 1e6f,
                    Range: 50,
                    ChainDepth: TestChains.CamerasFrame
                )
            );
            AddSlab(
                snapshot: snapshot,
                box: UnitBox(renderer),
                material: renderer.CreateMaterial(
                    new(BaseColor: new(x: 1, y: 1, z: 1, w: 1), Roughness: 0.2f, Metallic: 1)
                ),
                center: new(0, FloorHeight - 0.5f, -10),
                size: new(40, 1, 40)
            );
            renderer.Render(snapshot, 1f);
            float[] direct = renderer.ReadDirect();
            float[] hdr = renderer.ReadHdr();

            (int X, int Y) pixel = ((int)highlight.X, (int)highlight.Y);
            output.WriteLine(
                $"{highlight}: direct {ReflectionTesting.At(direct, pixel)}, HDR {ReflectionTesting.At(hdr, pixel)}"
            );
            ReflectionTesting
                .At(direct, pixel)
                .Should()
                .Be(new Vector3(cap), "the lamp's mirror highlight passes the cap in the direct image");
            ReflectionTesting
                .At(hdr, pixel)
                .Should()
                .Be(new Vector3(cap), "the ambient pass caps the sum of the direct and the ambient light");
            hdr.Should().OnlyContain(value => value <= cap, "every pixel keeps the surfaces' share of the HDR image");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The HDR image of <see cref="RedWall"/> seen through <paramref name="view"/>, rendered by a renderer of
    /// <paramref name="setting"/>.
    /// </summary>
    private static float[] RedWallHdr(ScreenSpaceLightSetting setting, Quaternion view)
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: setting);
        try
        {
            renderer.Render(RedWall(renderer, view), 1f);
            return renderer.ReadHdr();
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The floor and the red back wall of the corner, without the side wall, under the sun and a dim white ambient light,
    /// seen through <paramref name="view"/>.
    /// </summary>
    private static RenderSnapshot RedWall(VulkanRenderer renderer, Quaternion view)
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
        return snapshot;
    }

    /// <summary>
    /// The estimate target and the direct image of a sunlit white floor seen by a level camera, with a lit red wall
    /// just past the left edge of the screen, and the floor pixels 0.3 m inside that edge, 3.5 to 4.5 m ahead. The
    /// wall's face lies 2 cm outside the left side plane of the view, parallel to it, so no pixel shows it.
    /// </summary>
    private static (float[] Estimate, float[] Direct, Vector2[] Measured) WallOffTheLeftEdge()
    {
        float tanHalfX = MathF.Tan(Gpu.Fov / 2) * Width / Height;
        // The side plane's normal toward the inside of the view, and its direction away from the eye.
        var inward = Vector3.Normalize(new(1, 0, -tanHalfX));
        var along = Vector3.Normalize(new(-tanHalfX, 0, -1));
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            RenderSnapshot snapshot = SunlitFloor(renderer);
            // From the eye to 8 m along the side plane, past the measured floor's 6.5 m.
            Vector3 face = along * 4 - inward * 0.02f;
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: UnitBox(renderer),
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

            renderer.Render(snapshot, 1f);
            Vector2[] measured =
            [
                .. new[] { 3.5f, 4f, 4.5f }.Select(depth =>
                    PixelOf(new(-tanHalfX * depth + 0.3f, FloorHeight, -depth), Quaternion.Identity)
                ),
            ];
            return (renderer.ReadEstimate(), renderer.ReadDirect(), measured);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A white floor 40 m square, its top 1.5 m below the level camera's eye, under a sun high ahead to the right and a
    /// dim white ambient light.
    /// </summary>
    private static RenderSnapshot SunlitFloor(VulkanRenderer renderer)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(new(0.5f, 0.8f, -0.3f), sunIntensity: 3);
        snapshot.Ambient = new()
        {
            SkyColor = Vector3.One,
            GroundColor = Vector3.One,
            Intensity = 0.3f,
        };
        AddSlab(
            snapshot: snapshot,
            box: UnitBox(renderer),
            material: renderer.CreateMaterial(MaterialDesc.Color(new(x: 1, y: 1, z: 1, w: 1), roughness: 0.8f)),
            center: new(0, FloorHeight - 0.5f, -10),
            size: new(40, 1, 40)
        );
        return snapshot;
    }

    /// <summary>The floor pixels at <paramref name="fromTheWall"/> m in front of the back wall, at x of −1, 0 and 1 m, seen through <paramref name="view"/>.</summary>
    private static Vector2[] FloorPixels(float[] fromTheWall, Quaternion view) =>
        FloorPixels(fromTheWall: fromTheWall, across: [-1f, 0f, 1f], view: view, size: (Width, Height));

    /// <summary>
    /// The floor pixels at <paramref name="fromTheWall"/> m in front of the back wall, at <paramref name="across"/> m
    /// along x, seen through <paramref name="view"/> in an image of <paramref name="size"/>.
    /// </summary>
    private static Vector2[] FloorPixels(
        float[] fromTheWall,
        float[] across,
        Quaternion view,
        (int Width, int Height) size
    ) =>
        [
            .. fromTheWall.SelectMany(distance =>
                across.Select(x => PixelOf(new(x, FloorHeight, BackWallZ + distance), view, size))
            ),
        ];

    /// <summary>The mean over <paramref name="pixels"/> of R / (R + G + B) in the HDR image <paramref name="hdr"/>, <paramref name="width"/> pixels per row.</summary>
    private static double MeanRedShare(float[] hdr, Vector2[] pixels, int width = Width) =>
        pixels
            .Select(pixel => ReflectionTesting.At(hdr, ((int)pixel.X, (int)pixel.Y), width))
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

    /// <summary>
    /// A white box under the sun, seen by the level camera: its front face 5 m ahead from x = 0.5 m to 2 m, and its side
    /// face at x = 0.5 m from 5 m to 6 m, which the eye sees about 6° off its view rays. The box reaches past the image
    /// up and down.
    /// </summary>
    private static RenderSnapshot SunlitConvexEdge(VulkanRenderer renderer)
    {
        // Toward the sun: up, ahead and to the left, so it lights both faces.
        RenderSnapshot snapshot = Gpu.Snapshot(new(-0.5f, 0.6f, 0.6f), sunIntensity: 3);
        AddSlab(
            snapshot: snapshot,
            box: UnitBox(renderer),
            material: renderer.CreateMaterial(MaterialDesc.Color(new(x: 1, y: 1, z: 1, w: 1), roughness: 0.8f)),
            center: new(1.25f, 0, -FrontFaceDepth - 0.5f),
            size: new(1.5f, 40, 1)
        );
        return snapshot;
    }

    /// <summary>
    /// The sum over the texels of term A of the blocked share, 1 − V, before the blur, when the camera and a scene turn
    /// together by <paramref name="turn"/> rad about the vertical: the floor 1.5 m below the eye, and a wall square to
    /// the view 3 m ahead.
    /// </summary>
    private static double BlockedShareBeforeASquareWall(float turn)
    {
        VulkanRenderer renderer = Gpu.Headless(screenSpaceLight: VisibilityBitmaskLight.Default);
        try
        {
            renderer.Graph.Skipped.UnionWith(["occlusion denoise", "occlusion upsample"]);
            var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, turn);
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.Transform(new(0.5f, 0.8f, 0.3f), rotation), sunIntensity: 3);
            Look(snapshot, rotation);
            MeshId box = UnitBox(renderer);
            MaterialId matte = Matte(renderer);
            foreach (
                (Vector3 center, Vector3 size) in (ReadOnlySpan<(Vector3 Center, Vector3 Size)>)
                    [
                        (Center: new(0, FloorHeight - 0.5f, -10), Size: new(40, 1, 40)),
                        (Center: new(0, 5, -3.5f), Size: new(40, 20, 1)),
                    ]
            )
                snapshot.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: matte,
                        position: Vector3.Transform(center, rotation),
                        scale: size,
                        rotation: rotation,
                        radius: size.Length() / 2
                    )
                );
            renderer.Render(snapshot, 1f);
            float[] term = renderer.ReadOcclusionTerm().Rgba;
            double blocked = 0;
            for (int i = 3; i < term.Length; i += 4)
                blocked += 1 - term[i];
            return blocked;
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// Whether texel <paramref name="index"/> of the sector pass's <paramref name="term"/> blocks a sector: V below 1 by
    /// more than the half floats' step. One sector of one slice of 3 takes about 1/96 of the share.
    /// </summary>
    private static bool BlocksASector(float[] term, int index) => term[index * 4 + 3] < 1 - 1e-3f;

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
