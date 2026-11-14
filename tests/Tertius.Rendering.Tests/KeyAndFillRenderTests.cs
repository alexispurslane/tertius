using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Key and fill on the GPU (docs/plans/light-pairs.md, "Key and fill"): a scene with no more lamps than key lights
/// renders as with every light pair but for negligible losses, a lamp that is not key keeps its diffuse light and its
/// shadow, and no step shows at the edges of the screen tiles. The key lights of a corner are its lamps of the greatest
/// loss, wherever they stand in the light list, and a surface that no corner lies on takes every lamp in full. At a
/// corner, each lamp shines by the key share that the model gives its loss (<see cref="KeyAndFill.KeyShare"/>). The
/// exposure is fixed at 0 stops, so the HDR image holds plain radiance. A square 3 m wide stands 3 m ahead of the camera
/// and faces it, with no ambient light and bulbs 1 m in front of it.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class KeyAndFillRenderTests
{
    private const int Width = 320,
        Height = 180;

    /// <summary>The bulbs in front of the square: the place of each on the plane 1 m in front of it, in m, and its power in W.</summary>
    private static readonly (float X, float Y, float Power)[] Bulbs =
    [
        (-1.0f, 0.5f, 8),
        (-0.3f, -0.4f, 3),
        (0.4f, 0.6f, 5),
        (1.0f, -0.2f, 2),
        (0.0f, 0.0f, 1),
        (0.7f, 0.7f, 4),
    ];

    /// <summary>The first entity of the bulbs: each bulb has its own, so each has its own shadow maps.</summary>
    private const uint BulbEntity = 100;

    /// <summary>
    /// A square: its center on the plane 3 m ahead of the camera, in m, half its side, in m, how far it leans back from
    /// facing the camera, in degrees, and how far it stands behind that plane, in m.
    /// </summary>
    private readonly record struct Square(float X, float Y, float HalfSide, float LeanDegrees = 0, float Behind = 0)
    {
        /// <summary>The square 3 m wide in the middle of the view.</summary>
        public static Square Wide => new(X: 0, Y: 0, HalfSide: 1.5f);

        /// <summary>A square 8 m wide in the middle of the view, which fills the view.</summary>
        public static Square Wall => new(X: 0, Y: 0, HalfSide: 4);
    }

    /// <summary>
    /// A dark snapshot with <paramref name="squares"/> of <paramref name="desc"/> and <paramref name="bulbs"/>. With
    /// <paramref name="shadows"/>, every bulb casts shadows, and a box 0.3 m wide stands halfway between the bulbs'
    /// plane and the squares, so each bulb throws its own shadow of the box on them.
    /// </summary>
    private static RenderSnapshot Scene(
        VulkanRenderer renderer,
        MaterialDesc desc,
        (float X, float Y, float Power)[] bulbs,
        bool shadows,
        Square[] squares
    )
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        foreach (Square square in squares)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Plane(square.HalfSide)),
                    material: renderer.CreateMaterial(desc),
                    position: new(square.X, square.Y, -3 - square.Behind),
                    scale: Vector3.One,
                    rotation: Quaternion.CreateFromAxisAngle(
                        Vector3.UnitX,
                        MathF.PI / 2 - square.LeanDegrees * MathF.PI / 180
                    ),
                    radius: 1.5f * square.HalfSide,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        if (shadows)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: renderer.CreateMesh(Primitives.Box(new(0.15f))),
                    material: renderer.CreateMaterial(desc),
                    position: new(0.1f, 0.1f, -2.5f),
                    scale: Vector3.One,
                    flags: RenderFlags.CastsShadow,
                    radius: 0.3f,
                    entity: 7,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        for (int bulb = 0; bulb < bulbs.Length; bulb++)
            snapshot.Lights.Add(
                Gpu.Lamp(
                    position: new(bulbs[bulb].X, bulbs[bulb].Y, -2),
                    color: Vector3.One,
                    intensity: 0,
                    chainDepth: TestChains.CamerasFrame
                ) with
                {
                    Power = bulbs[bulb].Power,
                    CastsShadow = shadows,
                    EntityIndex = BulbEntity + (uint)bulb,
                }
            );
        return snapshot;
    }

    /// <summary>
    /// The red channel of the HDR image of the wide square under <paramref name="lightPairs"/> and the first
    /// <paramref name="bulbs"/> bulbs, row by row from the top.
    /// </summary>
    private static float[] Radiance(LightPairSetting lightPairs, MaterialDesc desc, int bulbs, bool shadows = false) =>
        Radiance(lightPairs: lightPairs, desc: desc, bulbs: Bulbs[..bulbs], squares: [Square.Wide], shadows: shadows);

    /// <summary>
    /// The red channel of the HDR image of <paramref name="squares"/> under <paramref name="lightPairs"/> and
    /// <paramref name="bulbs"/>, row by row from the top, <paramref name="width"/> pixels wide.
    /// </summary>
    private static float[] Radiance(
        LightPairSetting lightPairs,
        MaterialDesc desc,
        (float X, float Y, float Power)[] bulbs,
        Square[] squares,
        bool shadows = false,
        int width = Width
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: width,
            height: Height,
            localShadows: shadows ? LocalShadowAtlas.Default : default(LocalShadowSetting),
            lightPairs: lightPairs
        );
        try
        {
            RenderSnapshot scene = Scene(
                renderer: renderer,
                desc: desc,
                bulbs: bulbs,
                shadows: shadows,
                squares: squares
            );
            renderer.Render(scene, 1f);
            float[] image = renderer.ReadHdr();
            return [.. Enumerable.Range(0, width * Height).Select(pixel => image[pixel * 4])];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A white square with no shine: its light is its diffuse light alone.</summary>
    private static MaterialDesc Matte => new(BaseColor: Vector4.One, Roughness: 1, Reflectance: 0);

    /// <summary>A white square whose shine shows each bulb as a soft highlight.</summary>
    private static MaterialDesc Glossy => new(BaseColor: Vector4.One, Roughness: 0.4f, Reflectance: 0.5f);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void ASceneWithNoMoreLampsThanKeyLightsLosesOnlyNegligibleLight(int bulbs)
    {
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: bulbs);

        float[] split = Radiance(lightPairs: new(new KeyAndFill(KeyLights: 3)), desc: Glossy, bulbs: bulbs);

        every.Max().Should().BeGreaterThan(0.01f, "the bulbs light the square");
        // A lamp without a full key share loses under the negligible loss of its corner's whole light. Twice that, for
        // the difference between a pixel's light and its corners' light.
        float negligible = 2 * bulbs * (float)KeyAndFill.NegligibleLoss;
        for (int pixel = 0; pixel < every.Length; pixel++)
            split[pixel]
                .Should()
                .BeApproximately(
                    every[pixel],
                    1e-4f + negligible * every[pixel],
                    "pixel {0} has a key light to spare for every lamp",
                    pixel
                );
    }

    // At a roughness of 0.15 a highlight is narrower than a tile, so it lies between the corners: only the wide lobe of
    // a corner gives its bulb a loss there.
    [Theory]
    [InlineData(0.4f)]
    [InlineData(0.15f)]
    public void ASceneWithNoMoreLampsThanKeyLightsKeepsTheShineOfItsHighlights(float roughness)
    {
        MaterialDesc shiny = Glossy with { Roughness = roughness };
        float[] every = Radiance(lightPairs: default, desc: shiny, bulbs: 3);
        float[] matte = Radiance(lightPairs: default, desc: shiny with { Reflectance = 0 }, bulbs: 3);

        float[] split = Radiance(lightPairs: new(new KeyAndFill(KeyLights: 3)), desc: shiny, bulbs: 3);

        // The pixel where the shine adds the most light: the middle of the strongest highlight.
        int highlight = Enumerable.Range(0, every.Length).MaxBy(pixel => every[pixel] - matte[pixel]);
        (every[highlight] - matte[highlight]).Should().BeGreaterThan(0.05f * every[highlight], "the square shines");
        split[highlight].Should().BeApproximately(every[highlight], 1e-4f + 1e-3f * every[highlight]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    public void ALampThatIsNotKeyKeepsItsDiffuseLightAndItsShadow(int keyLights)
    {
        float[] every = Radiance(lightPairs: default, desc: Matte, bulbs: Bulbs.Length, shadows: true);
        float[] open = Radiance(lightPairs: default, desc: Matte, bulbs: Bulbs.Length);

        float[] split = Radiance(
            lightPairs: new(new KeyAndFill(keyLights)),
            desc: Matte,
            bulbs: Bulbs.Length,
            shadows: true
        );

        float brightest = every.Max();
        brightest.Should().BeGreaterThan(0.01f, "the bulbs light the square");
        Enumerable
            .Range(0, every.Length)
            .Count(pixel => every[pixel] < 0.9f * open[pixel])
            .Should()
            .BeGreaterThan(100, "the box's shadows darken a part of the square");
        // A point light's fill response is its diffuse light, and each bulb's shadow lookup is the pixel's own: the
        // image matches at every pixel, also along the edge of each bulb's shadow.
        for (int pixel = 0; pixel < every.Length; pixel++)
            split[pixel]
                .Should()
                .BeApproximately(
                    every[pixel],
                    0.01f * every[pixel] + 0.001f * brightest,
                    "pixel {0} keeps the diffuse light and the shadows of all six bulbs",
                    pixel
                );
    }

    [Fact]
    public void ASceneWithoutLampsRendersAsWithEveryLightPair()
    {
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: 0);

        float[] split = Radiance(lightPairs: new(new KeyAndFill(KeyLights: 3)), desc: Glossy, bulbs: 0);

        split.Should().Equal(every);
    }

    /// <summary>
    /// Twenty bulbs: eighteen weak ones along the square's border, and a strong one at each of the places
    /// <paramref name="first"/> and <paramref name="second"/> of the list, near the square's middle.
    /// </summary>
    private static (float X, float Y, float Power)[] BulbsWithTwoStrongOnes(int first, int second)
    {
        var bulbs = new (float X, float Y, float Power)[20];
        int weak = 0;
        for (int bulb = 0; bulb < bulbs.Length; bulb++)
            if (bulb == first)
                bulbs[bulb] = (-0.5f, 0.2f, 10);
            else if (bulb == second)
                bulbs[bulb] = (0.5f, -0.2f, 10);
            else
            {
                // Nine along the top border and nine along the bottom border.
                bulbs[bulb] = (-1.2f + 0.3f * (weak % 9), weak < 9 ? 1.3f : -1.3f, 0.05f);
                weak++;
            }

        return bulbs;
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(2, 18)]
    [InlineData(14, 15)]
    [InlineData(19, 3)]
    [InlineData(7, 11)]
    public void TheKeyLightsAreTheLampsOfTheGreatestLossWhereverTheyStandInTheLightList(int first, int second)
    {
        (float X, float Y, float Power)[] bulbs = BulbsWithTwoStrongOnes(first, second);
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: bulbs, squares: [Square.Wide]);
        float[] matte = Radiance(
            lightPairs: default,
            desc: Glossy with
            {
                Reflectance = 0,
            },
            bulbs: bulbs,
            squares: [Square.Wide]
        );

        float[] split = Radiance(
            lightPairs: new(new KeyAndFill(KeyLights: 2)),
            desc: Glossy,
            bulbs: bulbs,
            squares: [Square.Wide]
        );

        // The middle of each strong bulb's highlight: the pixel of the most shine in its half of the image.
        foreach (bool left in (bool[])[true, false])
        {
            int highlight = Enumerable
                .Range(0, every.Length)
                .Where(pixel => pixel % Width < Width / 2 == left)
                .MaxBy(pixel => every[pixel] - matte[pixel]);
            (every[highlight] - matte[highlight])
                .Should()
                .BeGreaterThan(0.2f * every[highlight], "a strong bulb shines on the square");
            // Only the weak bulbs lose their shine there, and it is far from their own highlights.
            split[highlight]
                .Should()
                .BeApproximately(every[highlight], 0.01f * every[highlight], "the strong bulb is a key light");
        }
    }

    [Fact]
    public void ASurfaceThatNoCornerLiesOnTakesEveryLampInFull()
    {
        // A square 10 pixels wide inside one screen tile, against the black sky, under six bulbs around it.
        var small = new Square(X: 0.15f, Y: 0.04f, HalfSide: 0.09f);
        (float X, float Y, float Power)[] bulbs =
        [
            .. Bulbs.Select(bulb => (small.X + 0.2f * bulb.X, small.Y + 0.2f * bulb.Y, bulb.Power)),
        ];
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: bulbs, squares: [small]);
        float[] matte = Radiance(
            lightPairs: default,
            desc: Glossy with
            {
                Reflectance = 0,
            },
            bulbs: bulbs,
            squares: [small]
        );
        float[] unlit = Radiance(lightPairs: default, desc: Glossy, bulbs: [], squares: [small]);

        float[] split = Radiance(
            lightPairs: new(new KeyAndFill(KeyLights: 2)),
            desc: Glossy,
            bulbs: bulbs,
            squares: [small]
        );

        // The bulbs light the square alone: the sky's stars shine without them too.
        int[] lit = [.. Enumerable.Range(0, every.Length).Where(pixel => every[pixel] > unlit[pixel])];
        lit.Length.Should().BeGreaterThan(50, "the small square shows");
        lit.Should()
            .OnlyContain(
                pixel => pixel % Width % KeyAndFill.TileSize != 0 && pixel / Width % KeyAndFill.TileSize != 0,
                "no corner of the tile grid lies on the square"
            );
        lit.Max(pixel => (every[pixel] - matte[pixel]) / every[pixel])
            .Should()
            .BeGreaterThan(0.2f, "the bulbs shine on the square");
        // Two key lights of six bulbs: a fill response for the other four would lose their shine.
        foreach (int pixel in lit)
            split[pixel]
                .Should()
                .BeApproximately(
                    every[pixel],
                    1e-4f * every[pixel],
                    "pixel {0} has no corner to take keys from",
                    pixel
                );
    }

    // A square 10 pixels wide inside one screen tile, in front of a wide wall that faces the camera. Every corner around
    // it lies on the wall. The square leans back by 60 degrees, or it faces the camera 0.6 m in front of the wall, more
    // than the size of a tile there.
    [Theory]
    [InlineData(60, 0.3f)]
    [InlineData(0, 0.6f)]
    public void ASurfaceThatIsNotTheOneOfTheCornersAroundItTakesEveryLampInFull(float leanDegrees, float inFront)
    {
        var small = new Square(X: 0.15f, Y: 0.04f, HalfSide: 0.09f, LeanDegrees: leanDegrees);
        Square wall = Square.Wide with { Behind = inFront };
        (float X, float Y, float Power)[] bulbs =
        [
            .. Bulbs.Select(bulb => (small.X + 0.2f * bulb.X, small.Y + 0.2f * bulb.Y, bulb.Power)),
        ];
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: bulbs, squares: [wall, small]);
        float[] wallAlone = Radiance(lightPairs: default, desc: Glossy, bulbs: bulbs, squares: [wall]);

        float[] split = Radiance(
            lightPairs: new(new KeyAndFill(KeyLights: 2)),
            desc: Glossy,
            bulbs: bulbs,
            squares: [wall, small]
        );

        // No bulb casts a shadow, so the small square changes only the pixels that it covers.
        int[] covered =
        [
            .. Enumerable
                .Range(0, every.Length)
                .Where(pixel => Math.Abs(every[pixel] - wallAlone[pixel]) > 0.02f * wallAlone[pixel]),
        ];
        covered.Length.Should().BeGreaterThan(20, "the small square shows in front of the wall");
        covered
            .Should()
            .OnlyContain(
                pixel => pixel % Width % KeyAndFill.TileSize != 0 && pixel / Width % KeyAndFill.TileSize != 0,
                "no corner of the tile grid lies on the small square"
            );
        // The wall's corners chose their key lights for the wall, whose highlights lie elsewhere.
        foreach (int pixel in covered)
            split[pixel]
                .Should()
                .BeApproximately(every[pixel], 1e-4f * every[pixel], "pixel {0} has no corner on its surface", pixel);
    }

    /// <summary>
    /// The width of the images of the tests that need a last tile narrower than the others. The height gives a last
    /// tile row lower than the others too.
    /// </summary>
    private const int UnevenWidth = Width + 8;

    /// <summary>
    /// Sixteen bulbs in front of the wall: the six of <see cref="Bulbs"/> at the places <see cref="SpreadPlaces"/> of
    /// the list, and bulbs too weak to show at the other places. So the six stand in the last groups of lights of the
    /// corner pass, along both axes of its copies.
    /// </summary>
    private static (float X, float Y, float Power)[] SpreadBulbs()
    {
        var bulbs = new (float X, float Y, float Power)[16];
        Array.Fill(bulbs, (0f, 0f, 1e-7f));
        for (int bulb = 0; bulb < Bulbs.Length; bulb++)
            bulbs[SpreadPlaces[bulb]] = Bulbs[bulb];
        return bulbs;
    }

    // The bulb toward the right and bottom edges stands at place 15, in the last copy of the corner grid along both axes.
    private static readonly int[] SpreadPlaces = [3, 13, 7, 15, 11, 12];

    /// <summary>
    /// Makes sure that each corner of the tile grid of an image <paramref name="width"/> by <paramref name="height"/>
    /// pixels shades each of the six bulbs by the key share that the model gives the bulb's loss there, under each count
    /// of <paramref name="keyLightCounts"/>. <paramref name="render"/> gives the red channel of the wall's image under a
    /// setting, a material and a list of bulbs.
    /// </summary>
    private static void EachCornerShouldShadeEachBulbByItsKeyShare(
        Func<LightPairSetting, MaterialDesc, (float X, float Y, float Power)[], float[]> render,
        int width,
        int height,
        params int[] keyLightCounts
    )
    {
        // Rough enough that a corner's lobe, which is at least as wide as a tile, is the wall's own lobe. So the loss
        // of a bulb at a corner is its shine at the corner's pixel: a point light's fill is its diffuse light.
        MaterialDesc rough = Glossy with
        {
            Roughness = 0.6f,
        };
        float[][] full = [.. Bulbs.Select(bulb => render(default, rough, [bulb]))];
        float[][] diffuse = [.. Bulbs.Select(bulb => render(default, rough with { Reflectance = 0 }, [bulb]))];

        foreach (int keyLights in keyLightCounts)
        {
            var setting = new KeyAndFill(keyLights);
            float[] split = render(new(setting), rough, SpreadBulbs());

            int corners = 0;
            double lost = 0,
                shone = 0;
            for (int y = 0; y < height; y += KeyAndFill.TileSize)
            for (int x = 0; x < width; x += KeyAndFill.TileSize)
            {
                int pixel = y * width + x;
                double whole = full.Sum(image => (double)image[pixel]);
                // A far part of a wall that leans lies out of every bulb's reach.
                if (whole <= 0)
                    continue;
                corners++;
                double[] shine = [.. full.Select((image, bulb) => (double)image[pixel] - diffuse[bulb][pixel])];
                double threshold = setting.Threshold(losses: shine, whole: whole);
                double expected = diffuse
                    .Select((image, bulb) => image[pixel] + shine[bulb] * KeyAndFill.KeyShare(shine[bulb], threshold))
                    .Sum();
                // A key share has 8 bits.
                split[pixel]
                    .Should()
                    .BeApproximately(
                        (float)expected,
                        (float)(0.005 * whole),
                        "the corner at ({0}, {1}) shades each bulb by its key share under {2} key lights",
                        x,
                        y,
                        keyLights
                    );
                lost += whole - expected;
                shone += shine.Sum();
            }

            corners.Should().BeGreaterThan(40, "the bulbs light the wall at many corners");
            lost.Should().BeGreaterThan(0.1 * shone, "the bulbs past the key lights lose their shine");
        }
    }

    [Fact]
    public void AtACornerOfTheTileGridEachLampShinesByTheKeyShareOfItsLoss() =>
        EachCornerShouldShadeEachBulbByItsKeyShare(
            render: (lightPairs, desc, bulbs) =>
                Radiance(lightPairs: lightPairs, desc: desc, bulbs: bulbs, squares: [Square.Wall], width: UnevenWidth),
            width: UnevenWidth,
            height: Height,
            keyLightCounts: [1, 2, 3]
        );

    /// <summary>The height of the window of <see cref="RadianceOfAWindowAtHalfItsRenderSize"/>: twice a whole count of tiles.</summary>
    private const int WindowHeight = 192;

    /// <summary>
    /// The red channel of the wall's HDR image from a window's renderer of <see cref="Width"/> by
    /// <see cref="WindowHeight"/> pixels whose render size fell to half along each axis, row by row from the top:
    /// only the pixels of that render size, in the first part of the renderer's images.
    /// </summary>
    private static float[] RadianceOfAWindowAtHalfItsRenderSize(
        LightPairSetting lightPairs,
        MaterialDesc desc,
        (float X, float Y, float Power)[] bulbs
    )
    {
        VulkanRenderer renderer = WindowRenderer(lightPairs);
        try
        {
            RenderSnapshot scene = Scene(
                renderer: renderer,
                desc: desc,
                bulbs: bulbs,
                shadows: false,
                squares: [Square.Wall]
            );
            RenderUntilTheRenderScaleIsAHalf(renderer, scene);

            CameraView camera = renderer.LastScene.Camera;
            (camera.Width, camera.Height).Should().Be((Width / 2, WindowHeight / 2), "the render size fell to half");
            float[] image = renderer.ReadHdr();
            return
            [
                .. Enumerable
                    .Range(0, WindowHeight / 2)
                    .SelectMany(y => Enumerable.Range(0, Width / 2).Select(x => image[(y * Width + x) * 4])),
            ];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// A window's renderer of <see cref="Width"/> by <see cref="WindowHeight"/> pixels with FSR 3.1, so that its render
    /// size can fall below the window's.
    /// </summary>
    private static VulkanRenderer WindowRenderer(LightPairSetting lightPairs) =>
        VulkanRenderer.CreateOffscreen(
            options: new()
            {
                Width = Width,
                Height = WindowHeight,
                FramesInFlight = 1,
                Validation = true,
                Impostors = false,
                ScreenTrace = new NoScreenTrace(),
                EnvironmentMap = new NoEnvironmentMap(),
                LocalShadows = new NoLocalShadows(),
                Subsurface = new NoSubsurface(),
                Exposure = new FixedExposure(0),
                Bloom = new NoBloom(),
                Upscaling = new Fsr3Upscaling(FrameTimeTarget.Default, 0),
                WarmUpFrames = 0,
                LightPairs = lightPairs,
            },
            createOutput: (device, allocator, extent) =>
                new HeadlessTarget(device: device, allocator: allocator, extent: extent, bgra: false),
            headless: false
        );

    /// <summary>
    /// Renders <paramref name="scene"/> until the render scale is at its least value, a half: each frame reports a
    /// G-buffer far over the frame's budget.
    /// </summary>
    private static void RenderUntilTheRenderScaleIsAHalf(VulkanRenderer renderer, RenderSnapshot scene)
    {
        renderer.Render(scene, 1f);
        for (int frame = 0; frame < 12; frame++)
        {
            renderer.ScaleController!.Measure(
                new(
                    renderer.DrawnFrames - 1,
                    new PassTiming[]
                    {
                        new("G-buffer", 100) { Stage = FrameGraph.Stage.GBuffer },
                        new("exposure", 0) { Stage = FrameGraph.Stage.Exposure },
                        new("FSR 3.1", 1) { Stage = FrameGraph.Stage.Upscale },
                        new("bloom", 1) { Stage = FrameGraph.Stage.Bloom },
                        new("tone map", 100) { Stage = FrameGraph.Stage.ToneMap },
                    },
                    202
                )
            );
            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1f);
        }
    }

    [Fact]
    public void ARenderSizeBelowTheLargestKeepsEachCornersKeyShares() =>
        EachCornerShouldShadeEachBulbByItsKeyShare(
            render: RadianceOfAWindowAtHalfItsRenderSize,
            width: Width / 2,
            height: WindowHeight / 2,
            keyLightCounts: [1]
        );

    [Fact]
    public void AfterAResizeKeyAndFillRendersAsARendererMadeAtTheNewSize()
    {
        var setting = new LightPairSetting(new KeyAndFill(KeyLights: 2));
        float[] made = Radiance(lightPairs: setting, desc: Glossy, bulbs: Bulbs.Length);

        VulkanRenderer renderer = Gpu.Headless(width: Width / 2, height: Height / 2, lightPairs: setting);
        try
        {
            RenderSnapshot scene = Scene(
                renderer: renderer,
                desc: Glossy,
                bulbs: Bulbs,
                shadows: false,
                squares: [Square.Wide]
            );
            renderer.Render(scene, 1f);
            renderer.Resize(Width, Height);
            // The next frame applies the new size.
            renderer.Render(scene, 1f);
            float[] image = renderer.ReadHdr();

            image.Length.Should().Be(Width * Height * 4, "the renderer draws at the new size");
            for (int pixel = 0; pixel < made.Length; pixel++)
                image[pixel * 4]
                    .Should()
                    .BeApproximately(made[pixel], 1e-4f * made[pixel], "pixel {0} has its tile's record", pixel);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void OnAWallThatLeansEachCornerStillShinesByTheKeyShareOfItsLoss() =>
        // The wall's normal is not the camera's axis, so the corners count only if their own normal reaches the pixels.
        EachCornerShouldShadeEachBulbByItsKeyShare(
            render: (lightPairs, desc, bulbs) =>
                Radiance(
                    lightPairs: lightPairs,
                    desc: desc,
                    bulbs: bulbs,
                    squares: [Square.Wall with { LeanDegrees = 30 }]
                ),
            width: Width,
            height: Height,
            keyLightCounts: [2]
        );

    [Fact]
    public void AKeyLampsShineStaysBehindItsShadow()
    {
        // One bulb straight ahead: its highlight lies in the middle of the square, and the box's shadow covers a part
        // of it. The corners outside the shadow give the bulb a key share to the pixels inside it.
        (float X, float Y, float Power)[] bulb = [(0, 0, 4)];
        float[] open = Radiance(lightPairs: default, desc: Glossy, bulbs: bulb, squares: [Square.Wide]);
        float[] matte = Radiance(
            lightPairs: default,
            desc: Glossy with
            {
                Reflectance = 0,
            },
            bulbs: bulb,
            squares: [Square.Wide]
        );
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: bulb, squares: [Square.Wide], shadows: true);

        float[] split = Radiance(
            lightPairs: new(new KeyAndFill(KeyLights: 1)),
            desc: Glossy,
            bulbs: bulb,
            squares: [Square.Wide],
            shadows: true
        );

        int[] hidden =
        [
            .. Enumerable
                .Range(0, every.Length)
                .Where(pixel => open[pixel] - matte[pixel] > 0.2f * open[pixel] && every[pixel] < 0.02f * open[pixel]),
        ];
        hidden.Length.Should().BeGreaterThan(20, "the box's shadow covers a part of the bulb's highlight");
        foreach (int pixel in hidden)
            split[pixel].Should().BeLessThan(0.02f * open[pixel], "pixel {0} lies in the bulb's shadow", pixel);
        // Along the shadow's edge the bulb is hidden in part, and so is its shine: key and fill adds no light there.
        int[] edge =
        [
            .. Enumerable.Range(0, every.Length).Where(pixel => IsBetween(every[pixel] / open[pixel], 0.1f, 0.9f)),
        ];
        edge.Length.Should().BeGreaterThan(20, "the shadow has an edge");
        float brightest = every.Max();
        foreach (int pixel in edge)
            split[pixel]
                .Should()
                .BeLessThanOrEqualTo(
                    1.01f * every[pixel] + 0.001f * brightest,
                    "pixel {0} lies on the edge of the bulb's shadow",
                    pixel
                );
    }

    private static bool IsBetween(float value, float least, float most) => value > least && value < most;

    [Fact]
    public void ALampInShadowAtACornerTakesNoKeySlotThere()
    {
        // A strong bulb straight ahead, whose shadow of the box covers the highlight of a weak bulb beside it. With one
        // key light, the weak bulb is the key light where the strong one is in shadow.
        (float X, float Y, float Power) strong = (0, 0, 20),
            weak = (0.4f, 0.4f, 1);
        static float[] Shadowed(MaterialDesc desc, params (float X, float Y, float Power)[] bulbs) =>
            Radiance(lightPairs: default, desc: desc, bulbs: bulbs, squares: [Square.Wide], shadows: true);
        float[] strongOpen = Radiance(lightPairs: default, desc: Glossy, bulbs: [strong], squares: [Square.Wide]);
        float[] strongAlone = Shadowed(Glossy, strong);
        float[] weakAlone = Shadowed(Glossy, weak);
        float[] weakMatte = Shadowed(Glossy with { Reflectance = 0 }, weak);
        float[] every = Shadowed(Glossy, strong, weak);

        float[] split = Radiance(
            lightPairs: new(new KeyAndFill(KeyLights: 1)),
            desc: Glossy,
            bulbs: [strong, weak],
            squares: [Square.Wide],
            shadows: true
        );

        // The corners of the tile grid where the strong bulb is hidden and the weak one shines.
        int[] corners =
        [
            .. Enumerable
                .Range(0, every.Length)
                .Where(pixel => pixel % Width % KeyAndFill.TileSize == 0 && pixel / Width % KeyAndFill.TileSize == 0)
                .Where(pixel => strongAlone[pixel] < 0.01f * strongOpen[pixel])
                .Where(pixel => weakAlone[pixel] - weakMatte[pixel] > 0.05f * every[pixel]),
        ];
        corners.Should().NotBeEmpty("the strong bulb's shadow covers a corner in the weak bulb's highlight");
        foreach (int pixel in corners)
            split[pixel]
                .Should()
                .BeApproximately(
                    every[pixel],
                    0.005f * every[pixel],
                    "the weak bulb is the key light at pixel {0}",
                    pixel
                );
    }

    /// <summary>A tube 1 m long along x, 1 m in front of the wide square at (x, y) of its plane, of <paramref name="power"/> W.</summary>
    private static TubeRenderLight Tube(float x, float y, float power) =>
        new(
            EntityIndex: Gpu.LampEntity,
            EntityGeneration: 0,
            Position: new(x, y, -2),
            PreviousPosition: new(x, y, -2),
            Axis: Vector3.UnitX,
            PreviousAxis: Vector3.UnitX,
            Color: Vector3.One,
            Power: power,
            Radius: 0.05f,
            Length: 1,
            CastsShadow: false,
            ChainDepth: TestChains.CamerasFrame
        );

    /// <summary>
    /// A panel 10 cm by 8 cm that faces the wide square, 1 m in front of it at (x, y) of its plane, of
    /// <paramref name="power"/> W. The square lies more than 8 half diagonals away, where a point light's diffuse light
    /// is the panel's to within 1 %.
    /// </summary>
    private static RectRenderLight Panel(float x, float y, float power)
    {
        // Turned half a turn about Y, the panel's front faces the square.
        var facing = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        return new(
            EntityIndex: Gpu.LampEntity,
            EntityGeneration: 0,
            Position: new(x, y, -2),
            PreviousPosition: new(x, y, -2),
            Rotation: facing,
            PreviousRotation: facing,
            Color: Vector3.One,
            Power: power,
            Width: 0.1f,
            Height: 0.08f,
            CastsShadow: false,
            ChainDepth: TestChains.CamerasFrame
        );
    }

    /// <summary>
    /// The red channel of the HDR image of the wide square of <paramref name="desc"/> under
    /// <paramref name="lightPairs"/> and the lights that <paramref name="addLights"/> adds, row by row from the top.
    /// </summary>
    private static float[] Radiance(LightPairSetting lightPairs, MaterialDesc desc, Action<RenderSnapshot> addLights)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height, lightPairs: lightPairs);
        try
        {
            RenderSnapshot scene = Scene(
                renderer: renderer,
                desc: desc,
                bulbs: [],
                shadows: false,
                squares: [Square.Wide]
            );
            addLights(scene);
            renderer.Render(scene, 1f);
            float[] image = renderer.ReadHdr();
            return [.. Enumerable.Range(0, Width * Height).Select(pixel => image[pixel * 4])];
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void KeyLightsOfEveryShapeKeepTheirShine()
    {
        // The tube and the panel come before the bulb in the light list, and the direct pass meets the bulb first. The
        // three stand close together, so their highlights overlap and one tile's list holds all three.
        Action<RenderSnapshot>[] lights =
        [
            scene => scene.Lights.Add(Tube(x: 0, y: 0.2f, power: 9)),
            scene => scene.Lights.Add(Panel(x: -0.2f, y: -0.15f, power: 3)),
            scene =>
                scene.Lights.Add(
                    Gpu.Lamp(
                        position: new(0.2f, -0.15f, -2),
                        color: Vector3.One,
                        intensity: 0,
                        chainDepth: TestChains.CamerasFrame
                    ) with
                    {
                        Power = 12,
                    }
                ),
        ];
        void All(RenderSnapshot scene)
        {
            foreach (Action<RenderSnapshot> add in lights)
                add(scene);
        }
        float[] every = Radiance(lightPairs: default, desc: Glossy, addLights: All);

        float[] split = Radiance(lightPairs: new(new KeyAndFill(KeyLights: 3)), desc: Glossy, addLights: All);

        foreach (Action<RenderSnapshot> light in lights)
        {
            // The middle of the light's highlight: the pixel where its shine alone adds the most light.
            float[] alone = Radiance(lightPairs: default, desc: Glossy, addLights: light);
            float[] matte = Radiance(lightPairs: default, desc: Glossy with { Reflectance = 0 }, addLights: light);
            int highlight = Enumerable.Range(0, every.Length).MaxBy(pixel => alone[pixel] - matte[pixel]);
            (alone[highlight] - matte[highlight])
                .Should()
                .BeGreaterThan(0.02f * every[highlight], "each light shines on the square");
            // The two other lights lose under the negligible loss there.
            split[highlight]
                .Should()
                .BeApproximately(
                    every[highlight],
                    0.01f * every[highlight],
                    "each light is a key light at its highlight"
                );
        }
    }

    [Fact]
    public void ATubeOrAPanelThatIsNotKeyKeepsItsDiffuseLight()
    {
        // Two tubes and two panels over a square with no shine, with one key light: at least three are fill.
        static void Lights(RenderSnapshot scene)
        {
            scene.Lights.Add(Tube(x: 0, y: 0.7f, power: 9));
            scene.Lights.Add(Tube(x: 0, y: -0.7f, power: 6));
            scene.Lights.Add(Panel(x: -0.7f, y: 0, power: 5));
            scene.Lights.Add(Panel(x: 0.7f, y: 0, power: 3));
        }
        float[] every = Radiance(lightPairs: default, desc: Matte, addLights: Lights);

        float[] split = Radiance(lightPairs: new(new KeyAndFill(KeyLights: 1)), desc: Matte, addLights: Lights);

        float brightest = every.Max();
        brightest.Should().BeGreaterThan(0.01f, "the lights light the square");
        // A tube's fill response is its diffuse light, and a panel's is that of a point light this far away.
        for (int pixel = 0; pixel < every.Length; pixel++)
            split[pixel]
                .Should()
                .BeApproximately(
                    every[pixel],
                    0.02f * every[pixel] + 0.001f * brightest,
                    "pixel {0} keeps the diffuse light of all four lights",
                    pixel
                );
    }

    [Fact]
    public void AHairSurfaceTakesEveryLampInFull()
    {
        // The fill response is a surface's diffuse light, which a fiber does not have.
        MaterialDesc hair = Glossy with
        {
            Model = new HairShading(StrandDirection.PlusV),
        };
        float[] every = Radiance(lightPairs: default, desc: hair, bulbs: Bulbs.Length);

        float[] split = Radiance(lightPairs: new(new KeyAndFill(KeyLights: 1)), desc: hair, bulbs: Bulbs.Length);

        every.Max().Should().BeGreaterThan(0.01f, "the bulbs light the hair");
        for (int pixel = 0; pixel < every.Length; pixel++)
            split[pixel].Should().BeApproximately(every[pixel], 1e-4f * every[pixel], "pixel {0} is a fiber", pixel);
    }

    [Fact]
    public void NoStepShowsAtTheEdgesOfTheScreenTiles()
    {
        // Two key lights of six bulbs on a glossy square: the key shares differ from corner to corner.
        float[] split = Radiance(lightPairs: new(new KeyAndFill(KeyLights: 2)), desc: Glossy, bulbs: Bulbs.Length);

        // The mean change from a pixel to its right neighbor, across a tile edge and inside a tile. A step at the tile
        // edges adds its height to the first mean alone.
        double acrossEdges = 0,
            inside = 0;
        int edgePairs = 0,
            insidePairs = 0;
        for (int y = 0; y < Height; y++)
        for (int x = 0; x + 1 < Width; x++)
        {
            double change = Math.Abs(split[y * Width + x + 1] - split[y * Width + x]);
            if ((x + 1) % KeyAndFill.TileSize == 0)
            {
                acrossEdges += change;
                edgePairs++;
            }
            else
            {
                inside += change;
                insidePairs++;
            }
        }

        (inside / insidePairs).Should().BeGreaterThan(0, "the bulbs' light changes across the square");
        (acrossEdges / edgePairs)
            .Should()
            .BeLessThan(1.25 * inside / insidePairs, "the light changes across a tile edge as it does inside a tile");
    }
}
