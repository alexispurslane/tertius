using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Key and fill on the GPU (docs/plans/light-pairs.md, "Key and fill"): a scene with no more lamps than key lights
/// renders as with every light pair but for negligible losses, a lamp that is not key keeps its diffuse light and its
/// shadow, and no step shows at the edges of the screen tiles. The key lights of a corner are its lamps of the greatest
/// loss, wherever they stand in the light list, and a surface that no corner lies on takes every lamp in full. The
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

    /// <summary>The square: its center on the plane 3 m ahead of the camera, in m, and half its side, in m.</summary>
    private readonly record struct Square(float X, float Y, float HalfSide)
    {
        /// <summary>The square 3 m wide in the middle of the view.</summary>
        public static Square Wide => new(X: 0, Y: 0, HalfSide: 1.5f);
    }

    /// <summary>
    /// A dark snapshot with <paramref name="square"/> of <paramref name="desc"/> and <paramref name="bulbs"/>. With
    /// <paramref name="shadows"/>, every bulb casts shadows, and a box 0.3 m wide stands halfway between the bulbs'
    /// plane and the square, so each bulb throws its own shadow of the box on the square.
    /// </summary>
    private static RenderSnapshot Scene(
        VulkanRenderer renderer,
        MaterialDesc desc,
        (float X, float Y, float Power)[] bulbs,
        bool shadows,
        Square square
    )
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        snapshot.Instances.Add(
            Gpu.Instance(
                mesh: renderer.CreateMesh(Primitives.Plane(square.HalfSide)),
                material: renderer.CreateMaterial(desc),
                position: new(square.X, square.Y, -3),
                scale: Vector3.One,
                rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                radius: 2.2f,
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
        Radiance(lightPairs: lightPairs, desc: desc, bulbs: Bulbs[..bulbs], square: Square.Wide, shadows: shadows);

    /// <summary>
    /// The red channel of the HDR image of <paramref name="square"/> under <paramref name="lightPairs"/> and
    /// <paramref name="bulbs"/>, row by row from the top.
    /// </summary>
    private static float[] Radiance(
        LightPairSetting lightPairs,
        MaterialDesc desc,
        (float X, float Y, float Power)[] bulbs,
        Square square,
        bool shadows = false
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: Width,
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
                square: square
            );
            renderer.Render(scene, 1f);
            float[] image = renderer.ReadHdr();
            return [.. Enumerable.Range(0, Width * Height).Select(pixel => image[pixel * 4])];
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

    [Fact]
    public void ASceneWithNoMoreLampsThanKeyLightsKeepsTheShineOfItsHighlights()
    {
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: 3);
        float[] matte = Radiance(lightPairs: default, desc: Glossy with { Reflectance = 0 }, bulbs: 3);

        float[] split = Radiance(lightPairs: new(new KeyAndFill(KeyLights: 3)), desc: Glossy, bulbs: 3);

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
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: bulbs, square: Square.Wide);
        float[] matte = Radiance(
            lightPairs: default,
            desc: Glossy with { Reflectance = 0 },
            bulbs: bulbs,
            square: Square.Wide
        );

        float[] split = Radiance(
            lightPairs: new(new KeyAndFill(KeyLights: 2)),
            desc: Glossy,
            bulbs: bulbs,
            square: Square.Wide
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
        float[] every = Radiance(lightPairs: default, desc: Glossy, bulbs: bulbs, square: small);
        float[] matte = Radiance(
            lightPairs: default,
            desc: Glossy with { Reflectance = 0 },
            bulbs: bulbs,
            square: small
        );
        float[] unlit = Radiance(lightPairs: default, desc: Glossy, bulbs: [], square: small);

        float[] split = Radiance(
            lightPairs: new(new KeyAndFill(KeyLights: 2)),
            desc: Glossy,
            bulbs: bulbs,
            square: small
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
                .BeApproximately(every[pixel], 1e-4f * every[pixel], "pixel {0} has no corner to take keys from", pixel);
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
