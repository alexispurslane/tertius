using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The local shadow atlas on the GPU (docs/plans/local-light-shadows.md, Part 6): near spot and point lights that cast
/// shadows shadow their casters onto every surface that the shading passes light, within the tile budget, from cached
/// tiles as from drawn ones. The exposure is fixed at 0 stops, no ambient light and no star light the scenes, so a
/// shadowed surface is black. The camera is at the origin and looks down −Z unless a test turns it.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class LocalShadowRenderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The largest share of a lit pixel's luma that a shadowed pixel nearby may show.</summary>
    private const float ShadowedShare = 0.15f;

    /// <summary>The radiant intensity of the test lamps, in W/sr: about 2 W/m² at 4 m, a mid gray on white.</summary>
    private const float LampIntensity = 30;

    private const uint LampEntity = 100;
    private const uint SecondLampEntity = 101;
    private const uint BoxEntity = 7;

    /// <summary>The spot light of <see cref="SpotOverABox"/>, 4 m above the floor, pointing down.</summary>
    private static readonly Vector3 SpotPlace = new(0, 2, -7);

    /// <summary>The 1 m box of <see cref="SpotOverABox"/>, 3 m below the spot light and 1 m above the floor.</summary>
    private static readonly Vector3 BoxPlace = new(0, -1, -7);

    /// <summary>A point of the floor in the box's shadow, 0.72 m from the spot light's axis, which the camera sees.</summary>
    private static readonly Vector3 UnderTheBox = new(0.4f, -2, -6.4f);

    /// <summary>A point of the floor outside the box's shadow, 1.6 m from the axis, inside the spot light's inner cone.</summary>
    private static readonly Vector3 BesideTheBox = new(1.5f, -2, -6.4f);

    private static VulkanRenderer Renderer(
        LocalShadowSetting? setting = null,
        EnvironmentMapSetting? environmentMap = null,
        UpscalingSetting upscaling = default
    ) =>
        Gpu.Headless(
            width: Width,
            height: Height,
            localShadows: setting ?? LocalShadowAtlas.Default,
            environmentMap: environmentMap,
            upscaling: upscaling
        );

    /// <summary>A snapshot with no light at all, and the camera turned by <paramref name="camera"/>.</summary>
    private static RenderSnapshot Dark(Quaternion? camera = null)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        snapshot.Camera.Rotation = camera ?? Quaternion.Identity;
        snapshot.Camera.PreviousRotation = snapshot.Camera.Rotation;
        return snapshot;
    }

    /// <summary>A white spot light of radius 0 that casts shadows, with an outer half-angle of 0.6 rad.</summary>
    private static SpotRenderLight ShadowingSpot(
        Vector3 position,
        Vector3 direction,
        float intensity = LampIntensity,
        uint entity = LampEntity
    ) =>
        Gpu.Spot(
            position: position,
            direction: direction,
            color: Vector3.One,
            intensity: intensity,
            innerConeAngle: 0.48f,
            outerConeAngle: 0.6f,
            chainDepth: TestChains.CamerasFrame
        ) with
        {
            CastsShadow = true,
            EntityIndex = entity,
        };

    /// <summary>A white point light of radius 0 that casts shadows.</summary>
    private static PointRenderLight ShadowingLamp(Vector3 position, uint entity = LampEntity) =>
        Gpu.Lamp(
            position: position,
            color: Vector3.One,
            intensity: LampIntensity,
            chainDepth: TestChains.CamerasFrame
        ) with
        {
            CastsShadow = true,
            EntityIndex = entity,
        };

    /// <summary>
    /// The rotation of a camera at the origin that looks at <paramref name="target"/>, with its up as near
    /// <paramref name="upHint"/> as the view allows.
    /// </summary>
    private static Quaternion LookAt(Vector3 target, Vector3 upHint)
    {
        var forward = Vector3.Normalize(target);
        var right = Vector3.Normalize(Vector3.Cross(forward, upHint));
        var up = Vector3.Cross(right, forward);
        // Row-vector form: the rows are the images of the camera's x, y and z axes, and the camera looks down its −Z.
        var axes = new Matrix4x4(
            m11: right.X,
            m12: right.Y,
            m13: right.Z,
            m14: 0,
            m21: up.X,
            m22: up.Y,
            m23: up.Z,
            m24: 0,
            m31: -forward.X,
            m32: -forward.Y,
            m33: -forward.Z,
            m34: 0,
            m41: 0,
            m42: 0,
            m43: 0,
            m44: 1
        );
        return Quaternion.CreateFromRotationMatrix(axes);
    }

    /// <summary>The mean luma of the 3 × 3 pixels around the pixel that sees <paramref name="point"/>.</summary>
    private static float LumaAt(Capture image, Vector3 point, Quaternion? camera = null) =>
        image.MeanLuma(
            Gpu.Project(point: point, cameraRotation: camera ?? Quaternion.Identity, width: Width, height: Height),
            radius: 1
        );

    /// <summary>
    /// A white floor at y = −2 under a spot light 4 m above it that points down, and a box of 1 m that casts shadows at
    /// <paramref name="box"/>, <see cref="BoxPlace"/> by default, or no box.
    /// </summary>
    private static RenderSnapshot SpotOverABox(Kit kit, Vector3? box = null, bool withBox = true)
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Instances.Add(kit.Floor(center: new(0, -2, -7), halfSize: 8));
        snapshot.Lights.Add(ShadowingSpot(position: SpotPlace, direction: -Vector3.UnitY));
        if (withBox)
            snapshot.Instances.Add(kit.Caster(center: box ?? BoxPlace, size: Vector3.One));
        return snapshot;
    }

    /// <summary>The red, green and blue bytes of every pixel of <paramref name="image"/>: the fourth byte holds no defined value.</summary>
    private static byte[] Rgb(Capture image) => [.. image.Rgba.Where((_, index) => index % 4 != 3)];

    [Fact]
    public void ASpotLightOverABoxShadowsTheFloorUnderIt()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);

            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: SpotOverABox(kit),
                name: "spot-over-a-box"
            );

            float lit = LumaAt(image, BesideTheBox);
            lit.Should().BeGreaterThan(0.2f, "the spot light lights the floor beside the box");
            LumaAt(image, UnderTheBox)
                .Should()
                .BeLessThan(ShadowedShare * lit, "the box hides the floor under it from the spot light");
            renderer.LastFrameStats.ShadowTilesInUse.Should().Be(1, "a spot light of 0.6 rad takes one tile");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TemporalTileFilteringKeepsBothTheLitFloorAndTheBoxShadowInEveryGroup()
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
            height: Height,
            localShadows: LocalShadowAtlas.Default,
            upscaling: new Fsr3Upscaling(new FixedRenderScale(2f / 3), 0),
            warmUpFrames: 0
        );
        try
        {
            var kit = Kit.Create(renderer);
            RenderSnapshot scene = SpotOverABox(kit);
            for (int draw = 0; draw < 4; draw++)
            {
                scene.Time = draw / 60.0;
                Capture image = Gpu.RenderAndCapture(
                    renderer: renderer,
                    snapshot: scene,
                    name: "temporal tile shadows"
                );

                float lit = LumaAt(image, BesideTheBox);
                lit.Should().BeGreaterThan(0.2f);
                LumaAt(image, UnderTheBox).Should().BeLessThan(ShadowedShare * lit);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void APointLightShadowsEachOfSixPillarsAroundIt()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // One view per axis: the camera stands beside the axis inside the room and looks at the wall where the
            // pillar on that axis throws its shadow, so each face of the light's cube is seen once.
            foreach (CubeFace face in Enum.GetValues<CubeFace>())
            {
                Vector3 axis = CubeFaces.Basis(face).Forward;
                Vector3 aside = face switch
                {
                    CubeFace.PositiveX or CubeFace.NegativeX => Vector3.UnitY,
                    CubeFace.PositiveY or CubeFace.NegativeY => Vector3.UnitZ,
                    CubeFace.PositiveZ or CubeFace.NegativeZ => Vector3.UnitX,
                };
                Vector3 lamp = -(axis * 2 + aside * 3);
                Vector3 shadow = lamp + axis * 4;
                Quaternion camera = LookAt(target: shadow, upHint: Vector3.Cross(axis, aside));
                RenderSnapshot snapshot = Dark(camera);
                snapshot.Lights.Add(ShadowingLamp(lamp));
                kit.AddRoomWithPillars(snapshot: snapshot, center: lamp);

                Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: $"pillars-{face}");

                float lit = LumaAt(image, shadow + aside * 1.5f, camera);
                lit.Should().BeGreaterThan(0.2f, "the lamp lights the wall of {0} beside the shadow", face);
                LumaAt(image, shadow, camera)
                    .Should()
                    .BeLessThan(ShadowedShare * lit, "the pillar on the axis of {0} shadows the wall behind it", face);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AShadowAcrossTheSeamOfTwoFacesHasNoGap()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // The diagonal between +X and +Z, where the two faces meet, and the rod across it.
            var diagonal = Vector3.Normalize(new(1, 0, 1));
            var across = Vector3.Normalize(new(1, 0, -1));
            Vector3 lamp = -(diagonal + Vector3.UnitY * 1.2f);
            Vector3 wall = lamp + diagonal * 4;
            Quaternion camera = LookAt(target: wall, upHint: Vector3.UnitY);
            RenderSnapshot snapshot = Dark(camera);
            snapshot.Lights.Add(ShadowingLamp(lamp));
            snapshot.Instances.Add(kit.Panel(center: wall, normal: -diagonal, halfSize: 3));
            snapshot.Instances.Add(
                kit.Caster(
                    center: lamp + diagonal * 1.5f,
                    size: new(0.15f, 0.15f, 2),
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitY, 3 * MathF.PI / 4)
                )
            );

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "seam");

            float lit = LumaAt(image, wall + Vector3.UnitY, camera);
            lit.Should().BeGreaterThan(0.2f, "the lamp lights the wall above the rod's shadow");
            // The rod's shadow crosses the seam at the wall's center, 0.4 m wide and 5 m long: every point along it is dark.
            for (int step = -20; step <= 20; step++)
            {
                Vector3 point = wall + across * (step * 0.05f);
                LumaAt(image, point, camera)
                    .Should()
                    .BeLessThan(
                        ShadowedShare * lit,
                        "the rod shadows the wall {0} m along it from the seam",
                        step * 0.05f
                    );
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheShadowsEdgeIsSoftOverTheFilter(bool temporal)
    {
        const int tileSize = 128;
        VulkanRenderer renderer = Renderer(
            setting: new LocalShadowAtlas(Tiles: 8, TileSize: tileSize),
            upscaling: temporal ? new Fsr3Upscaling(new FixedRenderScale(1), 0) : new NoUpscaling()
        );
        try
        {
            var kit = Kit.Create(renderer);
            // A slab whose edge lies in the plane x = 0 through the light, so its shadow's edge on the floor 4 m below
            // the light is the line x = 0, on the light's axis.
            var spot = new Vector3(0, 3, -6);
            RenderSnapshot Scene(bool withSlab)
            {
                RenderSnapshot snapshot = Dark();
                snapshot.Instances.Add(kit.Floor(center: new(0, -1, -6), halfSize: 6));
                snapshot.Lights.Add(ShadowingSpot(position: spot, direction: -Vector3.UnitY));
                if (withSlab)
                    snapshot.Instances.Add(kit.Caster(center: new(-1.5f, 1, -6), size: new(3, 0.2f, 3)));
                return snapshot;
            }

            // The green channel of the HDR image, linear light, with and without the slab.
            float Share(float[] shadowed, float[] lit, Vector3 point)
            {
                Vector2 pixel = Gpu.Project(point: point, width: Width, height: Height);
                int i = ((int)pixel.Y * Width + (int)pixel.X) * 4 + 1;
                return shadowed[i] / lit[i];
            }

            renderer.Render(Scene(withSlab: false), 1f);
            float[] unshadowed = renderer.ReadHdr();
            renderer.Render(Scene(withSlab: true), 1f);
            float[] shadowed = renderer.ReadHdr();

            // The share of the light that reaches the floor at x, from 0.6 m inside the shadow to 0.6 m outside it.
            float[] xs = [.. Enumerable.Range(-120, 241).Select(step => step * 0.005f)];
            float[] shares = [.. xs.Select(x => Share(shadowed: shadowed, lit: unshadowed, point: new(x, -1, -6)))];
            shares[0].Should().BeLessThan(0.02f, "the slab hides the floor 0.6 m inside its shadow");
            shares[^1].Should().BeGreaterThan(0.98f, "the floor 0.6 m outside the shadow sees the whole light");
            shares
                .Should()
                .Contain(
                    share => share > 0.05f && share < 0.95f,
                    "the filter must keep partially lit receivers between the fully shadowed and fully lit floor"
                );
            if (!temporal)
            {
                float start = xs[Array.FindIndex(shares, share => share >= 0.05f)];
                float end = xs[Array.FindIndex(shares, share => share >= 0.95f)];
                float texel = 2 * MathF.Tan(0.6f) * tileSize / (tileSize - 2 * ShadowTiles.MarginTexels) / tileSize * 4;
                ((end - start) / texel)
                    .Should()
                    .BeInRange(2.5f, 5.5f, "the 4 × 4 bilinear taps one texel apart blur the edge over 4 texels");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(LocalShadowReads.NineGathers)]
    [InlineData(LocalShadowReads.SixteenCornerMeans)]
    public void TheShadowsEdgeRampsInProportionToTheReceiversPlaceWithNoSubTexelSteps(LocalShadowReads reads)
    {
        // The filter weighs the 5 × 5 texels from the tap's first texel by (1 − f, 1, 1, 1, f) along each axis, with f
        // the place of the lookup within its texel. Across a straight edge of a caster, the lit share of a pixel is then
        // (lit texels + f) / 4, a straight line of the pixel's place in the tile. A filter that rounds f to the device's
        // sub-texel precision (at least 4 bits in Vulkan) gives a staircase of steps of 1/4 of that precision instead.
        // Both ways to read the texels apply f themselves, so both hold the line on every device.
        VulkanRenderer renderer = Renderer(new LocalShadowAtlas(Tiles: 8, TileSize: 64) { Reads = reads });
        try
        {
            var kit = Kit.Create(renderer);
            // The camera looks straight down at the floor 0.3 m below it, so a row of pixels crosses the floor at even
            // steps of about 1 mm, and a tile texel of about 9 cm spans about 90 of them. The spot light points straight
            // down from 4 m above the floor, so the floor's place in the tile is a linear function of its place too. The
            // slab's edge lies in the plane x = 0 through the light.
            Quaternion down = LookAt(-Vector3.UnitY, -Vector3.UnitZ);
            RenderSnapshot Scene(bool withSlab)
            {
                RenderSnapshot snapshot = Dark(down);
                snapshot.Instances.Add(kit.Floor(center: new(0, -0.3f, 0), halfSize: 3));
                snapshot.Lights.Add(ShadowingSpot(position: new(0, 3.7f, 0), direction: -Vector3.UnitY));
                if (withSlab)
                    snapshot.Instances.Add(kit.Caster(center: new(-1.5f, 1.7f, 0), size: new(3, 0.2f, 3)));
                return snapshot;
            }

            renderer.Render(Scene(withSlab: false), 1f);
            float[] lit = renderer.ReadHdr();
            renderer.Render(Scene(withSlab: true), 1f);
            float[] shadowed = renderer.ReadHdr();

            // The lit share of each pixel of the middle row, from the green channel of the HDR image, where the pixel sees
            // less than a tenth of the light: there the half floats of both images hold the share to about 1e-4. The
            // pixel counts from the middle of the row, which keeps the fit's sums small.
            int row = Height / 2;
            List<(double Pixel, double Share)> ramp = [];
            for (int x = 0; x < Width; x++)
            {
                int i = (row * Width + x) * 4 + 1;
                double share = shadowed[i] / (double)lit[i];
                if (share is > 0.005 and < 0.1)
                    ramp.Add((Pixel: x - Width / 2, Share: share));
            }

            ramp.Count.Should().BeGreaterThan(20, "the darkest tenth of the edge spans a few cm, tens of pixels");
            double[] fit = QuadraticFit(ramp);
            double worst = ramp.Max(point =>
                Math.Abs(point.Share - (fit[0] + fit[1] * point.Pixel + fit[2] * point.Pixel * point.Pixel))
            );
            worst
                .Should()
                .BeLessThan(
                    1.5e-4,
                    "the share follows the place of the pixel; a step of 1/256 texel would leave up to 5e-4 off the line"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0f, LocalShadowReads.NineGathers)]
    [InlineData(90f, LocalShadowReads.NineGathers)]
    [InlineData(0f, LocalShadowReads.SixteenCornerMeans)]
    [InlineData(90f, LocalShadowReads.SixteenCornerMeans)]
    public void TheShadowsEdgeRampsInOneStraightLineAllTheWayToFullyLit(float turnDegrees, LocalShadowReads reads)
    {
        // The lit share of a pixel across a straight edge is (lit texels + f) / 4 from fully shadowed to fully lit, one
        // straight line of the pixel's place. Each of the 5 texels of the filter along an axis has its own weight, so a
        // read whose texels took each other's weights bends the line where the edge crosses those texels. The scene
        // turns by 0° and 90° about the vertical, so that the edge crosses each axis of the tile.
        VulkanRenderer renderer = Renderer(new LocalShadowAtlas(Tiles: 8, TileSize: 64) { Reads = reads });
        try
        {
            double[] shares = SharesAcrossTurnedEdge(renderer, turnDegrees);

            // The pixels that are neither fully shadowed nor fully lit, counted from the middle of the row.
            List<(double Pixel, double Share)> ramp =
            [
                .. shares
                    .Select((share, x) => (Pixel: (double)(x - Width / 2), Share: share))
                    .Where(point => point.Share is > 0.005 and < 0.995),
            ];
            ramp.Count.Should().BeGreaterThan(200, "the edge spans the 4 texels of the filter, about 90 pixels each");
            double[] fit = QuadraticFit(ramp);
            double worst = ramp.Max(point =>
                Math.Abs(point.Share - (fit[0] + fit[1] * point.Pixel + fit[2] * point.Pixel * point.Pixel))
            );
            worst
                .Should()
                .BeLessThan(
                    3e-3,
                    "the half floats of both images hold a share near 1 to about 1e-3, and the share follows the place"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(0f, LocalShadowReads.NineGathers)]
    [InlineData(90f, LocalShadowReads.NineGathers)]
    [InlineData(0f, LocalShadowReads.SixteenCornerMeans)]
    [InlineData(90f, LocalShadowReads.SixteenCornerMeans)]
    public void TheHalfLitMiddleOfTheShadowsEdgeLiesUnderTheCastersEdge(float turnDegrees, LocalShadowReads reads)
    {
        // The filter weighs the texels around the lookup symmetrically, so the floor straight under the caster's edge sees
        // half the light. A filter that read its texels one texel off would move the whole edge by a texel, about 9 cm
        // here and about 90 pixels. The scene turns by 0° and 90° about the vertical, so that the edge crosses each axis
        // of the tile.
        VulkanRenderer renderer = Renderer(new LocalShadowAtlas(Tiles: 8, TileSize: 64) { Reads = reads });
        try
        {
            double[] shares = SharesAcrossTurnedEdge(renderer, turnDegrees);

            int half = Enumerable.Range(0, Width).MinBy(x => Math.Abs(shares[x] - 0.5));

            (half - Width / 2)
                .Should()
                .BeInRange(
                    -5,
                    5,
                    "the caster's edge lies under the middle of the view, and a texel spans about 90 pixels"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Theory]
    [InlineData(LocalShadowReads.NineGathers, false)]
    [InlineData(LocalShadowReads.SixteenCornerMeans, true)]
    public void ARendererMakesItsShadersWithItsAtlasReads(LocalShadowReads reads, bool cornerMeans)
    {
        // Both ways to read a tile give the same image, so only the constant of the shaders tells which one they run.
        VulkanRenderer renderer = Renderer(new LocalShadowAtlas(Tiles: 8, TileSize: 64) { Reads = reads });
        try
        {
            renderer.ShadingConstants.TileCornerMeans.Should().Be(cornerMeans);
            renderer
                .FaceConstants.TileCornerMeans.Should()
                .Be(cornerMeans, "an environment face reads a tile the same way");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithoutLocalShadowsTheShadersKeepTheirDefaultReads()
    {
        VulkanRenderer renderer = Renderer(new NoLocalShadows());
        try
        {
            renderer.ShadingConstants.TileCornerMeans.Should().BeFalse("no shader reads a tile without an atlas");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void BothWaysToReadATileGiveTheSameShadow()
    {
        // The nine gathers and the sixteen corner means weigh the same 5 × 5 texels by the same weights, so a device shows
        // the same image whichever of them its driver takes. Only the order of the additions differs, which a half float
        // of the HDR image holds to one or two of its steps, about 0.1 % each.
        static float[] Image(LocalShadowReads reads)
        {
            VulkanRenderer renderer = Renderer(new LocalShadowAtlas(Tiles: 8, TileSize: 64) { Reads = reads });
            try
            {
                renderer.Render(SpotOverABox(Kit.Create(renderer)), 1f);
                return renderer.ReadHdr();
            }
            finally
            {
                Gpu.DisposeAndAssertClean(renderer);
            }
        }

        float[] gathers = Image(LocalShadowReads.NineGathers);
        float[] means = Image(LocalShadowReads.SixteenCornerMeans);

        gathers.Max().Should().BeGreaterThan(0.2f, "the spot light lights the floor beside the box");
        double worst = gathers.Zip(means, (a, b) => Math.Abs(a - b) / Math.Max(Math.Max(a, b), 1e-3)).Max();
        worst.Should().BeLessThan(3e-3, "both filters compute the same weighted sum of the same comparisons");
    }

    /// <summary>
    /// The lit share of each pixel of the middle row of the view, from the green channel of two HDR images, with and
    /// without a slab. The scene of <see cref="TheShadowsEdgeRampsInProportionToTheReceiversPlaceWithNoSubTexelSteps"/>,
    /// turned by <paramref name="turnDegrees"/> about the vertical: the camera looks straight down at the floor 0.3 m
    /// below it, under a spot light 4 m above the floor that points straight down, and the slab's edge lies in a
    /// vertical plane through the light, under the middle of the view and across its rows.
    /// </summary>
    private static double[] SharesAcrossTurnedEdge(VulkanRenderer renderer, float turnDegrees)
    {
        var kit = Kit.Create(renderer);
        var turn = Quaternion.CreateFromAxisAngle(Vector3.UnitY, turnDegrees * MathF.PI / 180);
        Quaternion down = LookAt(-Vector3.UnitY, Vector3.Transform(-Vector3.UnitZ, turn));
        RenderSnapshot Scene(bool withSlab)
        {
            RenderSnapshot snapshot = Dark(down);
            snapshot.Instances.Add(kit.Floor(center: new(0, -0.3f, 0), halfSize: 3));
            snapshot.Lights.Add(ShadowingSpot(position: new(0, 3.7f, 0), direction: -Vector3.UnitY));
            if (withSlab)
            {
                snapshot.Instances.Add(
                    kit.Caster(
                        center: Vector3.Transform(new Vector3(-1.5f, 1.7f, 0), turn),
                        size: new(3, 0.2f, 3),
                        rotation: turn
                    )
                );
            }

            return snapshot;
        }

        renderer.Render(Scene(withSlab: false), 1f);
        float[] lit = renderer.ReadHdr();
        renderer.Render(Scene(withSlab: true), 1f);
        float[] shadowed = renderer.ReadHdr();
        int row = Height / 2;
        return
        [
            .. Enumerable
                .Range(0, Width)
                .Select(x => shadowed[(row * Width + x) * 4 + 1] / (double)lit[(row * Width + x) * 4 + 1]),
        ];
    }

    [Fact]
    public void AFloorNearerASpotLightThanItsTilesNearPlaneIsLitAsWithoutAShadow()
    {
        // A lookup nearer the light than the tile's near plane, 5 cm for a light of radius 0, gets the depth 1, nearest
        // the light, and so does the floor that the tile pass draws there, which the depth clamp holds at the near plane.
        // Every comparison passes, and the floor 3 cm under the spot light is fully lit.
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // The camera looks straight down at the floor 0.3 m below it, which casts shadows. The spot light of 2 mW/sr
            // hangs 3 cm above the floor, which it lights with about 2 W/m², as the test lamps light it from 4 m.
            Quaternion down = LookAt(-Vector3.UnitY, -Vector3.UnitZ);
            RenderSnapshot Scene(bool castsShadow)
            {
                RenderSnapshot snapshot = Dark(down);
                snapshot.Instances.Add(
                    kit.Floor(center: new(0, -0.3f, 0), halfSize: 3, flags: RenderFlags.CastsShadow)
                );
                snapshot.Lights.Add(
                    ShadowingSpot(position: new(0, -0.27f, 0), direction: -Vector3.UnitY, intensity: 0.002f) with
                    {
                        CastsShadow = castsShadow,
                    }
                );
                return snapshot;
            }

            renderer.Render(Scene(castsShadow: false), 1f);
            float[] unshadowed = renderer.ReadHdr();
            renderer.Render(Scene(castsShadow: true), 1f);
            float[] shadowed = renderer.ReadHdr();

            renderer.LastFrameStats.ShadowTilesInUse.Should().Be(1, "the spot light casts shadows from one tile");
            int middle = (Height / 2 * Width + Width / 2) * 4 + 1;
            unshadowed[middle].Should().BeGreaterThan(0.1f, "the spot light lights the floor straight under it");
            shadowed[middle]
                .Should()
                .BeApproximately(
                    unshadowed[middle],
                    unshadowed[middle] * 1e-3f,
                    "the floor under the light is fully lit"
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The coefficients a, b and c of the least-squares fit a + b x + c x² to the points, by the normal equations.</summary>
    private static double[] QuadraticFit(List<(double Pixel, double Share)> points)
    {
        double[] sums = new double[5];
        double[] right = new double[3];
        foreach ((double x, double y) in points)
        {
            for (int k = 0; k < 5; k++)
                sums[k] += Math.Pow(x, k);
            for (int k = 0; k < 3; k++)
                right[k] += y * Math.Pow(x, k);
        }

        double[,] m = new double[3, 4];
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
                m[r, c] = sums[r + c];
            m[r, 3] = right[r];
        }

        for (int p = 0; p < 3; p++)
        {
            for (int r = p + 1; r < 3; r++)
            {
                double factor = m[r, p] / m[p, p];
                for (int c = p; c < 4; c++)
                    m[r, c] -= factor * m[p, c];
            }
        }

        double[] result = new double[3];
        for (int r = 2; r >= 0; r--)
        {
            double value = m[r, 3];
            for (int c = r + 1; c < 3; c++)
                value -= m[r, c] * result[c];
            result[r] = value / m[r, r];
        }

        return result;
    }

    /// <summary>
    /// A floor that casts shadows does not shadow itself where a spot light <paramref name="distance"/> m from it lights
    /// it. Seen from a tilted spot light, the floor slopes along the tile's y axis, one axis of the texel grid. With the
    /// whole scene turned by 45 degrees about the spot light's axis, the slope runs diagonally across the grid, where
    /// the slope-scaled bias covers the least and the normal offset of a texel is needed too. The offset is a texel of
    /// the tile at the receiver's distance, so a far lamp, whose texels are wide, needs as many texels as a near one.
    /// The lamp's intensity grows with the square of its distance, so that the floor stays as bright.
    /// </summary>
    [Theory]
    [InlineData(0f, 4f, 0f)]
    [InlineData(30f, 4f, 0f)]
    [InlineData(60f, 4f, 0f)]
    [InlineData(60f, 4f, 45f)]
    [InlineData(60f, 12f, 45f)]
    public void ALitFloorUnderASpotLightHasNoAcne(float angleDegrees, float distance, float turnDegrees)
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            float angle = angleDegrees * MathF.PI / 180;
            var toSpot = new Vector3(MathF.Sin(angle), MathF.Cos(angle), 0);
            var turn = Quaternion.CreateFromAxisAngle(toSpot, turnDegrees * MathF.PI / 180);
            var center = Vector3.Transform(new(0, -1.5f, -6), turn);
            Vector3 spot = center + distance * toSpot;
            Quaternion camera = LookAt(target: center, upHint: Vector3.Transform(Vector3.UnitY, turn));
            RenderSnapshot Scene(bool floorCasts)
            {
                RenderSnapshot snapshot = Dark(camera);
                snapshot.Lights.Add(
                    ShadowingSpot(
                        position: spot,
                        direction: Vector3.Normalize(center - spot),
                        intensity: LampIntensity * distance * distance / 16
                    )
                );
                snapshot.Instances.Add(
                    kit.Panel(
                        center: center,
                        normal: Vector3.Transform(Vector3.UnitY, turn),
                        halfSize: 8,
                        flags: floorCasts ? RenderFlags.CastsShadow : RenderFlags.None
                    )
                );
                return snapshot;
            }

            Capture reference = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene(floorCasts: false),
                name: "acne-reference"
            );
            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: Scene(floorCasts: true), name: "acne");

            // In the reference the floor casts nothing, so its lit pixels cannot have acne; with self-shadowing they must
            // come out the same.
            (int lit, int differing) = CountChangedLitPixels(reference: reference, image: image);
            lit.Should().BeGreaterThan(Width * Height / 20, "the spot light lights a large patch of the floor");
            differing.Should().BeLessThanOrEqualTo(lit / 1000, "a lit floor that casts shadows must not shadow itself");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The pixels whose luma in <paramref name="reference"/> is at least 0.1, and how many of them differ in
    /// <paramref name="image"/> by more than 2.5 levels of 255.
    /// </summary>
    private static (int Lit, int Differing) CountChangedLitPixels(Capture reference, Capture image)
    {
        int lit = 0,
            differing = 0;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            float expected = reference.Luma(x, y);
            if (expected < 0.1f)
                continue;
            lit++;
            if (MathF.Abs(image.Luma(x, y) - expected) > 2.5f / 255f)
                differing++;
        }

        return (lit, differing);
    }

    /// <summary>
    /// The upper-bound partner of <see cref="ALitFloorUnderASpotLightHasNoAcne"/>: a thin caster resting on the floor still
    /// shadows the floor one texel behind its base. Each case puts the caster's face that the light sees steep to it, where
    /// the slope-scaled bias is largest: a wall 4 cm thick at 30 degrees, a plate 5 cm thick at 60. A bias of 4 instead of
    /// 2.5, or a normal offset of 3 texels instead of 1, lights that floor.
    /// </summary>
    [Theory]
    [InlineData(30f, 0.5f, 0.04f)]
    [InlineData(60f, 0.05f, 0.5f)]
    public void AShadowMeetsTheBaseOfAThinCasterOnTheFloor(float angleDegrees, float height, float depth)
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            float angle = angleDegrees * MathF.PI / 180;
            // The camera looks steeply down at the floor 2 m below it, so a pixel there spans about one tile texel.
            var center = new Vector3(0, -2, -1);
            Vector3 spot = center + 4 * new Vector3(0, MathF.Cos(angle), -MathF.Sin(angle));
            Quaternion camera = LookAt(target: center, upHint: Vector3.UnitY);
            RenderSnapshot Scene(bool withCaster)
            {
                RenderSnapshot snapshot = Dark(camera);
                snapshot.Lights.Add(ShadowingSpot(position: spot, direction: Vector3.Normalize(center - spot)));
                snapshot.Instances.Add(kit.Floor(center: center, halfSize: 8));
                if (withCaster)
                    snapshot.Instances.Add(
                        kit.Caster(center: center + new Vector3(0, height / 2, 0), size: new(1, height, depth))
                    );
                return snapshot;
            }

            renderer.Render(Scene(withCaster: false), 1f);
            float[] lit = renderer.ReadHdr();
            renderer.Render(Scene(withCaster: true), 1f);
            float[] shadowed = renderer.ReadHdr();

            // The light comes from −Z, so the shadow falls toward the camera, at +Z. A texel of the default tile, 4 m from
            // the light, as TheShadowsEdgeIsSoftOverTheFilter computes it.
            float texel = 2 * MathF.Tan(0.6f) / (LocalShadowAtlas.Default.TileSize - 2 * ShadowTiles.MarginTexels) * 4;
            Vector2 pixel = Gpu.Project(
                point: center + new Vector3(0, 0, depth / 2 + texel),
                cameraRotation: camera,
                width: Width,
                height: Height
            );
            int green = ((int)pixel.Y * Width + (int)pixel.X) * 4 + 1;
            lit[green].Should().BeGreaterThan(0, "the spot light lights the floor there without the caster");
            (shadowed[green] / lit[green])
                .Should()
                .BeLessThan(0.05f, "the caster hides the floor one texel behind its base from the spot light");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A sheet that only casts hovers 0.3 texels of the tile above the floor, straight under a spot light 4 m above it.
    /// The tiles take no constant depth bias, and the normal offset scales with sin(angle to the light), which is 0 on
    /// a floor that faces the light, so the sheet shadows the floor below it however close it hovers. Half a texel of
    /// constant bias, or a texel of offset toward the light, would light that floor.
    /// </summary>
    [Fact]
    public void AFloorJustBelowAThinCasterStraightUnderASpotLightIsInItsShadow()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            var center = new Vector3(0, -2, -1);
            Vector3 spot = center + new Vector3(0, 4, 0);
            Quaternion camera = LookAt(target: center, upHint: -Vector3.UnitZ);
            // A texel of the default tile, 4 m from the light, as TheShadowsEdgeIsSoftOverTheFilter computes it.
            float texel = 2 * MathF.Tan(0.6f) / (LocalShadowAtlas.Default.TileSize - 2 * ShadowTiles.MarginTexels) * 4;
            RenderSnapshot Scene(bool withSheet)
            {
                RenderSnapshot snapshot = Dark(camera);
                snapshot.Lights.Add(ShadowingSpot(position: spot, direction: -Vector3.UnitY));
                snapshot.Instances.Add(kit.Floor(center: center, halfSize: 8));
                if (withSheet)
                    snapshot.Instances.Add(
                        kit.Floor(center: center + new Vector3(0, 0.3f * texel, 0), halfSize: 0.5f) with
                        {
                            Flags = RenderFlags.CastsShadow,
                        }
                    );
                return snapshot;
            }

            renderer.Render(Scene(withSheet: false), 1f);
            float[] lit = renderer.ReadHdr();
            renderer.Render(Scene(withSheet: true), 1f);
            float[] shadowed = renderer.ReadHdr();

            foreach (Vector3 offset in (Vector3[])[new(0, 0, 0), new(0.2f, 0, 0), new(-0.15f, 0, 0.15f)])
            {
                Vector2 pixel = Gpu.Project(
                    point: center + offset,
                    cameraRotation: camera,
                    width: Width,
                    height: Height
                );
                int green = ((int)pixel.Y * Width + (int)pixel.X) * 4 + 1;
                lit[green].Should().BeGreaterThan(0, "the spot light lights the floor there without the sheet");
                (shadowed[green] / lit[green])
                    .Should()
                    .BeLessThan(0.05f, "the sheet hides the spot light from the floor 0.3 texels below it");
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ALightPastTheBudgetLightsWithoutAShadow()
    {
        VulkanRenderer renderer = Renderer(LocalShadowAtlas.Default with { Tiles = 1 });
        try
        {
            var kit = Kit.Create(renderer);
            // Two spot lights over two boxes, 5 m apart; the brighter one at the eye takes the one tile.
            var shift = new Vector3(2.5f, 0, 0);
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(kit.Floor(center: new(0, -2, -7), halfSize: 8));
            snapshot.Lights.Add(ShadowingSpot(position: SpotPlace - shift, direction: -Vector3.UnitY));
            snapshot.Lights.Add(
                ShadowingSpot(
                    position: SpotPlace + shift,
                    direction: -Vector3.UnitY,
                    intensity: 20,
                    entity: SecondLampEntity
                )
            );
            snapshot.Instances.Add(kit.Caster(center: BoxPlace - shift, size: Vector3.One, entity: BoxEntity));
            snapshot.Instances.Add(kit.Caster(center: BoxPlace + shift, size: Vector3.One, entity: BoxEntity + 1));

            Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "past-the-budget");

            var mirror = new Vector3(-1, 1, 1);
            LumaAt(image, mirror * UnderTheBox - shift)
                .Should()
                .BeLessThan(
                    ShadowedShare * LumaAt(image, mirror * BesideTheBox - shift),
                    "the brighter light has the tile"
                );
            LumaAt(image, UnderTheBox + shift)
                .Should()
                .BeGreaterThanOrEqualTo(
                    LumaAt(image, BesideTheBox + shift),
                    "the dimmer light has no tile, so it lights the floor under its box, nearer its axis, more brightly"
                );
            renderer.LastFrameStats.ShadowTilesInUse.Should().Be(1);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACachedTileShowsWhatADrawnTileShows()
    {
        VulkanRenderer renderer = Renderer();
        VulkanRenderer fresh = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            _ = Gpu.RenderAndCapture(renderer: renderer, snapshot: SpotOverABox(kit), name: "drawn-tile");
            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(1, "the first frame draws the new tile");

            Capture cached = Gpu.RenderAndCapture(renderer: renderer, snapshot: SpotOverABox(kit), name: "cached-tile");
            RenderStats stats = renderer.LastFrameStats;
            Capture drawn = Gpu.RenderAndCapture(
                renderer: fresh,
                snapshot: SpotOverABox(Kit.Create(fresh)),
                name: "fresh-tile"
            );

            stats.ShadowTilesDrawn.Should().Be(0, "neither the light nor the box moved");
            stats.ShadowTilesInUse.Should().Be(1);
            LumaAt(cached, UnderTheBox)
                .Should()
                .BeLessThan(ShadowedShare * LumaAt(cached, BesideTheBox), "the cached tile holds the box");
            Rgb(cached).Should().Equal(Rgb(drawn), "a cached tile holds what the frame that drew it drew");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(fresh);
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ACasterThatMovesMovesItsShadowInTheSameFrame()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            _ = Gpu.RenderAndCapture(renderer: renderer, snapshot: SpotOverABox(kit), name: "before-the-move");

            // The box moves 1 m toward −X: its shadow now spans x = −2.4 to −0.57 m on the floor.
            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: SpotOverABox(kit, box: BoxPlace - Vector3.UnitX),
                name: "after-the-move"
            );

            float lit = LumaAt(image, BesideTheBox);
            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(1, "the box moved far more than a quarter of a texel");
            LumaAt(image, new(-1.4f, -2, -6.4f))
                .Should()
                .BeLessThan(ShadowedShare * lit, "the shadow follows the box in the frame it moved");
            LumaAt(image, UnderTheBox)
                .Should()
                .BeGreaterThanOrEqualTo(lit, "the floor where the shadow was is lit again");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void GlassAndALitPuffTakeTheTileShadows()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // A pane and a puff side by side under the box, in its shadow, with no floor behind them.
            var pane = new Vector3(-0.25f, -1.8f, -6.6f);
            var puff = new Vector3(0.25f, -1.8f, -6.6f);
            MaterialId glass = renderer.CreateMaterial(
                new(
                    BaseColor: new(x: 0.8f, y: 0.8f, z: 0.8f, w: 0.8f),
                    Roughness: 0.5f,
                    Reflectance: 0,
                    Alpha: new GlassAlpha()
                )
            );
            RenderSnapshot Scene(bool withBox) =>
                GlassAndPuffUnderASpot(kit: kit, glass: glass, pane: pane, puff: puff, withBox: withBox);

            Capture lit = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene(withBox: false),
                name: "glass-puff-lit"
            );
            Capture shadowed = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Scene(withBox: true),
                name: "glass-puff-shadowed"
            );

            foreach (
                (string name, Vector3 point) in (ReadOnlySpan<(string Name, Vector3 Point)>)
                    [("pane", pane), ("puff", puff)]
            )
            {
                LumaAt(lit, point).Should().BeGreaterThan(0.1f, "the spot light lights the {0}", name);
                LumaAt(shadowed, point)
                    .Should()
                    .BeLessThan(ShadowedShare * LumaAt(lit, point), "the box shadows the {0}", name);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A pane of <paramref name="glass"/> and a lit puff under the spot light, in the shadow of the box when there is one.</summary>
    private static RenderSnapshot GlassAndPuffUnderASpot(
        Kit kit,
        MaterialId glass,
        Vector3 pane,
        Vector3 puff,
        bool withBox
    )
    {
        RenderSnapshot snapshot = Dark();
        snapshot.Lights.Add(ShadowingSpot(position: SpotPlace, direction: -Vector3.UnitY, intensity: 100));
        if (withBox)
            snapshot.Instances.Add(kit.Caster(center: BoxPlace, size: Vector3.One));
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: kit.Plane,
                material: glass,
                position: pane,
                scale: new(0.12f, 1, 0.12f),
                chainDepth: TestChains.CamerasFrame
            )
        );
        snapshot.PuffLooks.Add(
            new(
                Life: 2,
                StartRadius: 0.1f,
                EndRadius: 0.1f,
                TemperatureRatio: 0.5f,
                Shading: PuffShading.Lit(new(0.8f))
            )
        );
        snapshot.Puffs.Add(
            new()
            {
                Position = puff,
                PreviousPosition = puff,
                Age = 0,
                Look = 0,
            }
        );
        return snapshot;
    }

    [Fact]
    public void AnEnvironmentFaceShowsALampsShadow()
    {
        const int faceSize = 64;
        VulkanRenderer renderer = Renderer(
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = faceSize,
                FacesPerFrame = CubeFaces.Count,
            }
        );
        try
        {
            var kit = Kit.Create(renderer);
            // The camera, at the origin, stands 2 m above the floor; the face that looks down sees the box's shadow.
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(kit.Floor(center: new(1.5f, -2, 0), halfSize: 4));
            snapshot.Lights.Add(ShadowingSpot(position: new(1.5f, 1, 0), direction: -Vector3.UnitY));
            snapshot.Instances.Add(kit.Caster(center: new(1.5f, -1, 0), size: new(0.6f)));

            EnvironmentScenes.Still(renderer: renderer, snapshot: snapshot, time: 0);
            Half[] face = EnvironmentScenes.Face(renderer, CubeFace.NegativeY);

            float Luminance(Vector3 point)
            {
                (CubeFace located, float s, float t) = CubeFaces.Locate(point);
                located.Should().Be(CubeFace.NegativeY);
                Vector4 texel = EnvironmentScenes.Texel(
                    face: face,
                    size: faceSize,
                    x: (int)(s * faceSize),
                    y: (int)(t * faceSize)
                );
                return 0.2126f * texel.X + 0.7152f * texel.Y + 0.0722f * texel.Z;
            }

            float lit = Luminance(new(1.5f, -2, 1.2f));
            lit.Should().BeGreaterThan(0, "the spot light lights the floor beside the box");
            Luminance(new(1.5f, -2, 0)).Should().BeLessThan(ShadowedShare * lit, "the face samples the lamp's tile");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The lens of <see cref="FarBoxUnderASpot"/>: 15°.</summary>
    private const float FarBoxLens = MathF.PI / 12;

    /// <summary>The floor point under the box of <see cref="FarBoxUnderASpot"/>: 20 m away, 30° below the horizon.</summary>
    private static readonly Vector3 FarBoxFloor = new(0, -10, -17.32f);

    /// <summary>The camera of <see cref="FarBoxUnderASpot"/>, which looks at <see cref="FarBoxFloor"/>.</summary>
    private static Quaternion FarBoxCamera => LookAt(FarBoxFloor, Vector3.UnitY);

    /// <summary>
    /// The image of a box of 0.15 m 0.2 m over a floor at <see cref="FarBoxFloor"/>, under a spot light 0.5 m over the
    /// floor, through <see cref="FarBoxLens"/>, with a local shadow budget of one tile and all six faces of the map drawn
    /// in the frame. With <paramref name="withTheLightBehind"/>, a second spot light stands 20 m behind the camera with a
    /// reach of 0.95 of that, so only face +Z keeps it and no surface lies within its reach. Both are under the cutoff
    /// irradiance at the eye, the light behind brighter: 0.9 of it against 0.6.
    /// </summary>
    private static Capture FarBoxUnderASpot(bool withTheLightBehind)
    {
        double cutoff = LightUnits.CutoffIrradiance(0);
        Vector3 spotPlace = FarBoxFloor + new Vector3(0, 0.5f, 0);
        Vector3 behindPlace = -20 * Vector3.Normalize(FarBoxFloor);
        VulkanRenderer renderer = Renderer(
            setting: LocalShadowAtlas.Default with
            {
                Tiles = 1,
            },
            environmentMap: SceneEnvironmentMap.Default with
            {
                FaceSize = EnvironmentMapSetting.MinFaceSize,
                FacesPerFrame = CubeFaces.Count,
            }
        );
        try
        {
            var kit = Kit.Create(renderer);
            RenderSnapshot snapshot = Dark(FarBoxCamera);
            snapshot.Camera.VerticalFov = FarBoxLens;
            snapshot.Instances.Add(kit.Floor(center: FarBoxFloor, halfSize: 4));
            snapshot.Instances.Add(kit.Caster(center: FarBoxFloor + new Vector3(0, 0.2f, 0), size: new(0.15f)));
            snapshot.Lights.Add(
                ShadowingSpot(
                    position: spotPlace,
                    direction: -Vector3.UnitY,
                    intensity: (float)(0.6 * cutoff * spotPlace.LengthSquared())
                )
            );
            if (withTheLightBehind)
                snapshot.Lights.Add(
                    ShadowingSpot(
                        position: behindPlace,
                        direction: Vector3.Normalize(behindPlace),
                        intensity: (float)(0.9 * cutoff * behindPlace.LengthSquared()),
                        entity: SecondLampEntity
                    )
                );
            return Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: snapshot,
                name: withTheLightBehind ? "face-only-light" : "no-face-only-light"
            );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The mean luma of the 3 × 3 pixels around the pixel of <see cref="FarBoxUnderASpot"/> that sees <paramref name="point"/>.</summary>
    private static float FarBoxLumaAt(Capture image, Vector3 point) =>
        image.MeanLuma(
            Gpu.Project(point: point, cameraRotation: FarBoxCamera, width: Width, height: Height, fov: FarBoxLens),
            radius: 1
        );

    [Fact]
    public void ALightThatOnlyAFaceKeepsLeavesTheMainViewsImageBitForBit()
    {
        Capture withIt = FarBoxUnderASpot(withTheLightBehind: true);
        Capture withoutIt = FarBoxUnderASpot(withTheLightBehind: false);

        float lit = FarBoxLumaAt(withIt, FarBoxFloor + new Vector3(0.22f, 0, 0));
        lit.Should().BeGreaterThan(0.05f, "the main view's spot light lights the floor beside the box");
        FarBoxLumaAt(withIt, FarBoxFloor + new Vector3(0.03f, 0, 0))
            .Should()
            .BeLessThan(ShadowedShare * lit, "the main view's spot light has the one tile");
        Rgb(withIt).Should().Equal(Rgb(withoutIt), "the light that only a face keeps adds nothing to the main view");
    }

    [Fact]
    public void WithLocalShadowsOffNoNearLightIsShadowedAndNoPassIsTimed()
    {
        VulkanRenderer renderer = Renderer(new NoLocalShadows());
        try
        {
            var kit = Kit.Create(renderer);

            Capture image = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: SpotOverABox(kit),
                name: "local-shadows-off"
            );

            LumaAt(image, UnderTheBox)
                .Should()
                .BeGreaterThanOrEqualTo(
                    LumaAt(image, BesideTheBox),
                    "without tiles the spot light lights the floor under the box"
                );
            renderer.LastFrameStats.ShadowTilesInUse.Should().Be(0);
            renderer.Graph.Passes.Select(pass => pass.Name).Should().NotContain("local shadows");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void WithLocalShadowsOffReadingATilesBoundsIsRefused()
    {
        VulkanRenderer renderer = Renderer(new NoLocalShadows());
        try
        {
            Action read = () => renderer.ReadShadowTileBounds(0);

            read.Should().Throw<InvalidOperationException>("without local shadows there is no atlas and so no bounds");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EveryAtlasLayerReadsBackAsItsLightsTile()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // A point light with a box along each face's axis, then a spot light with a box along its axis, each box 0.25 m
            // farther than the one before, so each layer holds its own depth. The point light, first in the list, takes
            // layers 0 to 5 in face order, and the spot light layer 6.
            var lamp = new Vector3(0, 0, -10);
            var spot = new Vector3(5, 0, -10);
            RenderSnapshot snapshot = Dark();
            snapshot.Lights.Add(ShadowingLamp(lamp));
            snapshot.Lights.Add(ShadowingSpot(position: spot, direction: Vector3.UnitX, entity: SecondLampEntity));
            Vector3[] axes = [.. Enum.GetValues<CubeFace>().Select(face => CubeFaces.Basis(face).Forward)];
            for (int i = 0; i < axes.Length; i++)
                snapshot.Instances.Add(
                    kit.Caster(center: lamp + axes[i] * BoxDistance(i), size: new(0.4f), entity: BoxEntity + (uint)i)
                );
            snapshot.Instances.Add(
                kit.Caster(
                    center: spot + Vector3.UnitX * BoxDistance(CubeFaces.Count),
                    size: new(0.4f),
                    entity: BoxEntity + CubeFaces.Count
                )
            );

            renderer.Render(snapshot, 1f);

            int size = LocalShadowAtlas.Default.TileSize;
            for (int layer = 0; layer <= CubeFaces.Count; layer++)
            {
                float[] tile = renderer.ReadShadowTile(layer);
                // Reversed-Z with an infinite far plane: depth = near / distance, with the near plane at 5 cm.
                float boxFront = DepthRanges.NearPlane / (BoxDistance(layer) - 0.2f);
                tile[size / 2 * size + size / 2]
                    .Should()
                    .BeApproximately(
                        boxFront,
                        boxFront * 0.01f,
                        "layer {0} sees its box's front {1} m away",
                        layer,
                        BoxDistance(layer) - 0.2f
                    );
                tile[0].Should().Be(0, "the corner of layer {0} sees no caster", layer);
            }
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }

        // The distance of the center of the box in layer i from its light, in m.
        static float BoxDistance(int i) => 2f + 0.25f * i;
    }

    [Fact]
    public void ATilesBoundsHoldTheNearestAndFarthestDepthOfEachWindowOfSixTexels()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            // A spot light tilted off the vertical, so that the floor's depth changes across the tile.
            RenderSnapshot snapshot = Dark();
            snapshot.Instances.Add(kit.Floor(center: new(0, -2, -7), halfSize: 8));
            snapshot.Instances.Add(kit.Caster(center: BoxPlace, size: Vector3.One));
            snapshot.Lights.Add(ShadowingSpot(position: SpotPlace, direction: Vector3.Normalize(new(0.5f, -1, 0))));

            renderer.Render(snapshot, 1f);

            renderer
                .ReadShadowTile(0)
                .Distinct()
                .Count()
                .Should()
                .BeGreaterThan(1, "the tile sees the floor and the box");
            BoundsMismatches(renderer, layer: 0)
                .Should()
                .Be(0, "the tile pass derives the bounds from the tile it drew");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheBoundsOfARedrawnTileFollowItsCasterInTheSameFrame()
    {
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            renderer.Render(SpotOverABox(kit), 1f);

            renderer.Render(SpotOverABox(kit, box: BoxPlace - Vector3.UnitX), 1f);

            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(1, "the box moved");
            BoundsMismatches(renderer, layer: 0)
                .Should()
                .Be(0, "the bounds follow the tile in the frame that redrew it");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void TheBoundsOfATileRedrawnAloneLandInThatTilesOwnLayer()
    {
        // Two spot lights over a box each take layers 0 and 1. Each later frame moves one box, so one of the two frames
        // redraws only the tile of layer 1, the first and only tile of its bounds dispatch.
        VulkanRenderer renderer = Renderer();
        try
        {
            var kit = Kit.Create(renderer);
            var side = new Vector3(3, 0, 0);
            RenderSnapshot TwoSpots(Vector3 leftBox, Vector3 rightBox)
            {
                RenderSnapshot snapshot = Dark();
                snapshot.Instances.Add(kit.Floor(center: new(0, -2, -7), halfSize: 8));
                snapshot.Lights.Add(ShadowingSpot(position: SpotPlace - side, direction: -Vector3.UnitY, entity: 1));
                snapshot.Lights.Add(ShadowingSpot(position: SpotPlace + side, direction: -Vector3.UnitY, entity: 2));
                snapshot.Instances.Add(kit.Caster(center: leftBox, size: Vector3.One));
                snapshot.Instances.Add(kit.Caster(center: rightBox, size: Vector3.One));
                return snapshot;
            }

            renderer.Render(TwoSpots(leftBox: BoxPlace - side, rightBox: BoxPlace + side), 1f);
            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(2);

            Vector3 moved = new(0, 0, 0.5f);
            renderer.Render(TwoSpots(leftBox: BoxPlace - side + moved, rightBox: BoxPlace + side), 1f);
            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(1, "only the left box moved");
            int[] afterTheLeft = [BoundsMismatches(renderer, layer: 0), BoundsMismatches(renderer, layer: 1)];

            renderer.Render(TwoSpots(leftBox: BoxPlace - side + moved, rightBox: BoxPlace + side + moved), 1f);
            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(1, "only the right box moved");
            int[] afterTheRight = [BoundsMismatches(renderer, layer: 0), BoundsMismatches(renderer, layer: 1)];

            afterTheLeft.Should().Equal([0, 0], "the bounds follow the left light's tile into its own layer");
            afterTheRight.Should().Equal([0, 0], "the bounds follow the right light's tile into its own layer");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void EveryTileOfAFrameThatDrawsMoreTilesThanOneBoundsDispatchHoldsGetsItsBounds()
    {
        // Six point lights draw 36 tiles, past the 32 of one dispatch. Each light sits inside a box, so every face of it
        // sees a wall.
        const int tileSize = 64;
        VulkanRenderer renderer = Renderer(new LocalShadowAtlas(Tiles: 64, TileSize: tileSize));
        try
        {
            var kit = Kit.Create(renderer);
            RenderSnapshot snapshot = Dark();
            for (int i = 0; i < 6; i++)
            {
                var lamp = new Vector3(-5 + 2 * i, 0, -8);
                snapshot.Lights.Add(ShadowingLamp(position: lamp, entity: (uint)(1 + i)));
                snapshot.Instances.Add(kit.Caster(center: lamp, size: Vector3.One));
            }

            renderer.Render(snapshot, 1f);

            renderer.LastFrameStats.ShadowTilesDrawn.Should().Be(36);
            int[] mismatches =
            [
                .. Enumerable.Range(0, 36).Select(layer => BoundsMismatches(renderer, layer, tileSize)),
            ];
            mismatches.Should().OnlyContain(count => count == 0, "each dispatch writes the bounds of its own tiles");
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// How many texels of the bounds of atlas layer <paramref name="layer"/> differ from the least and the greatest depth
    /// of the tile in their window: texel k of a bounds row covers the tile's texels 2k to 2k + 5 along each axis, clamped
    /// to the tile. Fails first when the tile holds no caster: an empty tile is cleared to 0, which bounds that were never
    /// written can match.
    /// </summary>
    private static int BoundsMismatches(VulkanRenderer renderer, int layer, int? tileSize = null)
    {
        int size = tileSize ?? LocalShadowAtlas.Default.TileSize,
            half = size / 2;
        float[] tile = renderer.ReadShadowTile(layer);
        float[] bounds = renderer.ReadShadowTileBounds(layer);
        tile.Should().Contain(depth => depth > 0, "the tile holds a caster");
        bounds.Length.Should().Be(half * half * 2, "a bounds texel holds the least and the greatest depth");
        int mismatches = 0;
        for (int ky = 0; ky < half; ky++)
        {
            for (int kx = 0; kx < half; kx++)
            {
                float least = float.MaxValue,
                    most = float.MinValue;
                for (int y = 2 * ky; y < 2 * ky + 6; y++)
                {
                    for (int x = 2 * kx; x < 2 * kx + 6; x++)
                    {
                        float depth = tile[Math.Min(y, size - 1) * size + Math.Min(x, size - 1)];
                        least = Math.Min(least, depth);
                        most = Math.Max(most, depth);
                    }
                }

                int texel = 2 * (ky * half + kx);
                if (bounds[texel] != least || bounds[texel + 1] != most)
                    mismatches++;
            }
        }

        return mismatches;
    }

    [Fact]
    public void AnAtlasOfMoreTilesThanTheDeviceHasLayersIsRefused()
    {
        using var device = VulkanDevice.Create(validation: true, surfaceExtensions: [], createSurface: null);
        using var allocator = new DeviceAllocator(device);
        using var layouts = new DescriptorLayouts(device: device, framesInFlight: 1, glowLevels: 0);
        int tiles = (int)device.Properties.Limits.MaxImageArrayLayers + 1;

        Action make = () =>
            new ShadowAtlas(
                device: device,
                allocator: allocator,
                layouts: layouts,
                setting: LocalShadowAtlas.Default with
                {
                    Tiles = tiles,
                }
            ).Dispose();

        make.Should().Throw<ArgumentOutOfRangeException>().WithMessage("*maxImageArrayLayers*");
        allocator.LiveAllocations.Should().Be(0, "the refusal comes before the atlas makes its image");
    }

    /// <summary>The meshes and materials of the tests' scenes.</summary>
    private sealed class Kit
    {
        public required MeshId Box { get; init; }
        public required MeshId Plane { get; init; }
        public required MaterialId White { get; init; }

        public static Kit Create(VulkanRenderer renderer) =>
            new()
            {
                Box = renderer.CreateMesh(Primitives.Box(new(0.5f))),
                Plane = renderer.CreateMesh(Primitives.Plane(1f)),
                White = renderer.CreateMaterial(
                    MaterialDesc.Color(new(x: 0.8f, y: 0.8f, z: 0.8f, w: 1), roughness: 0.9f)
                ),
            };

        /// <summary>A white square of half size <paramref name="halfSize"/> that faces up, at <paramref name="center"/>.</summary>
        public RenderInstance Floor(Vector3 center, float halfSize, RenderFlags flags = RenderFlags.None) =>
            Panel(center: center, normal: Vector3.UnitY, halfSize: halfSize, flags: flags);

        /// <summary>A white square of half size <paramref name="halfSize"/> whose front faces along <paramref name="normal"/>.</summary>
        public RenderInstance Panel(
            Vector3 center,
            Vector3 normal,
            float halfSize,
            RenderFlags flags = RenderFlags.None
        ) =>
            EnvironmentScenes.Facing(
                plane: Plane,
                material: White,
                center: center,
                normal: normal,
                halfSize: halfSize,
                flags: flags,
                chainDepth: TestChains.CamerasFrame
            );

        /// <summary>A white box of <paramref name="size"/> that casts shadows.</summary>
        public RenderInstance Caster(
            Vector3 center,
            Vector3 size,
            uint entity = BoxEntity,
            Quaternion? rotation = null
        ) =>
            Gpu.Instance(
                mesh: Box,
                material: White,
                position: center,
                scale: size,
                rotation: rotation,
                flags: RenderFlags.CastsShadow,
                entity: entity,
                chainDepth: TestChains.CamerasFrame
            );

        /// <summary>
        /// A closed room of six walls 4 m from <paramref name="center"/> that face it, and a pillar of 0.3 m that casts
        /// shadows 1.5 m from it along each axis.
        /// </summary>
        public void AddRoomWithPillars(RenderSnapshot snapshot, Vector3 center)
        {
            foreach (CubeFace face in Enum.GetValues<CubeFace>())
            {
                Vector3 axis = CubeFaces.Basis(face).Forward;
                snapshot.Instances.Add(Panel(center: center + axis * 4, normal: -axis, halfSize: 4));
                snapshot.Instances.Add(
                    Caster(center: center + axis * 1.5f, size: new(0.3f), entity: BoxEntity + (uint)face)
                );
            }
        }
    }
}
