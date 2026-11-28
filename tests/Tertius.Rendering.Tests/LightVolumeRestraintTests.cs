using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The restraint of the directional light volume (issue #42): how much of each surface's bounce light the volume keeps.
/// The rule judges one surface and one light. Gray paint under gray light keeps 2% of its bounce. A color's colored part
/// keeps the weight of its saturation s, mix(0.02, 1, ramp(s)) with ramp(s) = min(1, (s / 0.78)³), and its gray level
/// keeps 2%. The paint's colored part takes the whole light, and gray paint keeps of each lamp's light the weight of that
/// lamp's saturation. Glow enters in full.
///
/// The camera looks along -Z from the origin. A white receiver panel 3 m ahead reads the 0.5 m grid. A reflector panel
/// 1.1 m ahead and below the view's center faces the receiver. A star up and ahead lights only the reflector's face toward
/// the receiver, so the receiver gets only the reflector's bounce light. A saturated red reflector under a white star
/// keeps its bounce in full, so its red at the receiver is the receiver's response to one unit of kept light. Each test
/// states its expected light in those units.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class LightVolumeRestraintTests
{
    private static readonly Vector3 StarAheadAndAbove = Vector3.Normalize(new(0, 1, -0.5f));

    private static readonly Vector3 White = Vector3.One;

    private static readonly Vector3 Orange = new(1, 0.4f, 0);

    private static readonly Vector4 Red = new(x: 1, y: 0, z: 0, w: 1);

    private readonly record struct Panel(Vector3 Center, Vector3 Size, Vector4 Color)
    {
        public Vector3 Emission { get; init; }

        public uint Entity { get; init; }
    }

    /// <summary>The reflector: 0.48 m wide and 0.98 m tall, inside the 0.25 m grid, in one 0.5 m column.</summary>
    private static Panel Reflector(Vector4 color) => new(new(0.75f, -1, -1.1f), new(0.48f, 0.98f, 0.02f), color);

    private static VulkanRenderer CreateRenderer() =>
        VulkanRenderer.CreateHeadless(
            new RendererOptions
            {
                Width = 64,
                Height = 64,
                WarmUpFrames = 0,
                Validation = true,
                Upscaling = new NoUpscaling(),
                Exposure = new FixedExposure(0),
                EnvironmentMap = new NoEnvironmentMap(),
                ScreenTrace = new NoScreenTrace(),
                LightVolume = true,
                Impostors = false,
            }
        );

    /// <summary>Gray paint of reflectance <paramref name="level"/>.</summary>
    private static Vector4 GrayPaint(float level) => new(x: level, y: level, z: level, w: 1);

    /// <summary>Each channel of <paramref name="light"/> lies within <paramref name="tolerance"/> of <paramref name="expected"/>.</summary>
    private static void ShouldBeNear(Vector3 light, Vector3 expected, float tolerance, string because)
    {
        light.X.Should().BeApproximately(expected.X, tolerance, because);
        light.Y.Should().BeApproximately(expected.Y, tolerance, because);
        light.Z.Should().BeApproximately(expected.Z, tolerance, because);
    }

    /// <summary>
    /// The receiver's scene-linear RGB at the center pixel. Each star of <paramref name="stars"/> stands in the same
    /// direction with its own color and gives irradiance 20 per unit of its color. <paramref name="lamp"/> is one more
    /// light of the scene.
    /// </summary>
    private static Vector3 ReceiverLight(
        IEnumerable<Panel> panels,
        IEnumerable<Vector3> stars,
        Vector3 camera = default,
        RenderLight? lamp = null
    )
    {
        VulkanRenderer renderer = CreateRenderer();
        try
        {
            MeshId box = renderer.CreateMesh(Primitives.Box(new(0.5f)));
            RenderSnapshot scene = Gpu.Snapshot(StarAheadAndAbove);
            scene.Ambient = default;
            scene.Lights.Clear();
            foreach (Vector3 color in stars)
                scene.Lights.Add(
                    Gpu.StarLight(
                        direction: StarAheadAndAbove,
                        irradiance: 20,
                        angularRadius: Gpu.StarAngularRadius,
                        color: color,
                        chainDepth: TestChains.Root
                    ) with
                    {
                        // No test here needs a shadow, and the volume takes only 71% of each shadow-casting light
                        // after the first (#50).
                        CastsShadow = false,
                    }
                );
            if (lamp is not null)
                scene.Lights.Add(lamp.Value);
            scene.Camera.Position = camera;
            scene.Camera.PreviousPosition = camera;
            uint entity = 1;
            Panel receiver = new(new(camera.X, camera.Y, -3), new(3, 3, 0.08f), Vector4.One);
            foreach (Panel panel in panels.Prepend(receiver))
            {
                MaterialId material = renderer.CreateMaterial(
                    new(BaseColor: panel.Color, Roughness: 1, Emissive: panel.Emission)
                );
                scene.Instances.Add(
                    Gpu.Instance(
                        mesh: box,
                        material: material,
                        position: panel.Center,
                        scale: panel.Size,
                        chainDepth: TestChains.CamerasFrame,
                        flags: RenderFlags.CastsShadow,
                        radius: 0.5f * panel.Size.Length(),
                        entity: panel.Entity != 0 ? panel.Entity : entity
                    )
                );
                entity++;
            }

            scene.Time += 1.0 / 60;
            renderer.Render(scene, 1f);
            float[] hdr = renderer.ReadHdr();
            int center = 4 * (32 * 64 + 32);
            return new(hdr[center], hdr[center + 1], hdr[center + 2]);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>The receiver's light from one reflector of <paramref name="paint"/> under one white star.</summary>
    private static Vector3 UnderAWhiteStar(Vector3 paint) => ReceiverLight(panels: [Reflector(new(paint, 1))], stars: [White]);

    /// <summary>The receiver's red from a saturated red reflector under a white star: one unit of kept light.</summary>
    private static float OneUnit()
    {
        float unit = UnderAWhiteStar(new(1, 0, 0)).X;
        unit.Should().BeGreaterThan(1e-3f, "a sunlit red reflector lights the receiver");
        return unit;
    }

    [Fact]
    public void GrayPaintUnderAWhiteLampKeepsTwoPercentOfItsBounce()
    {
        float unit = OneUnit();

        Vector3 gray = UnderAWhiteStar(White);

        ShouldBeNear(light: gray, expected: new Vector3(0.02f * unit), tolerance: 0.003f * unit, because: "gray light on gray paint keeps 2%");
    }

    [Fact]
    public void AFaintTintGivesADimWashOfItsOwnHue()
    {
        // Beige (0.7, 0.6, 0.5) has saturation 0.286, ramp 0.049 and weight 0.068. Its colored part (0.2, 0.1, 0) keeps
        // that weight: (0.0136, 0.0068, 0). Its gray level 0.5 keeps 2%: 0.01 in every channel.
        float unit = OneUnit();

        Vector3 beige = UnderAWhiteStar(new(0.7f, 0.6f, 0.5f));

        float gray = beige.Z;
        gray.Should().BeApproximately(0.010f * unit, 0.0015f * unit, "the gray level keeps 2%");
        (beige.X - gray).Should().BeApproximately(0.0136f * unit, 0.0014f * unit, "the red of the tint keeps its weight");
        (beige.Y - gray).Should().BeApproximately(0.0068f * unit, 0.0010f * unit, "the green of the tint keeps its weight");
    }

    [Fact]
    public void ASaturatedPaintUnderAWhiteLampKeepsItsColoredWashInFullAndTwoPercentOfItsGray()
    {
        // Red (0.9, 0.1, 0.1) has saturation 0.89, so its colored part (0.8, 0, 0) keeps weight 1. Its gray level 0.1
        // keeps 2%: 0.002.
        float unit = OneUnit();

        Vector3 red = UnderAWhiteStar(new(0.9f, 0.1f, 0.1f));

        (red.X - red.Z).Should().BeApproximately(0.8f * unit, 0.04f * unit, "the colored wash keeps its full light");
        red.Z.Should().BeApproximately(0.002f * unit, 0.0006f * unit, "the gray level keeps 2%");
    }

    [Fact]
    public void ASaturatedLampOnGrayPaintKeepsItsWashInFull()
    {
        // Gray paint 0.8 keeps of the orange star (1, 0.4, 0), saturation 1, its whole light: (0.8, 0.32, 0).
        float unit = OneUnit();

        Vector3 orange = ReceiverLight(panels: [Reflector(GrayPaint(0.8f))], stars: [Orange]);

        ShouldBeNear(light: orange, expected: new Vector3(0.8f, 0.32f, 0) * unit, tolerance: 0.04f * unit, because: "a saturated light keeps its full bounce");
    }

    [Fact]
    public void AWhiteLampBesideAnOrangeLampDoesNotRemoveTheOrangeWash()
    {
        // Each lamp is judged alone: gray paint 0.8 keeps the orange star in full, (0.8, 0.32, 0), and 2% of the white
        // one, 0.016. Judged as one light, the sum (2, 1.4, 1) has saturation 0.5 and would keep a quarter of the orange.
        float unit = OneUnit();
        Panel gray = Reflector(GrayPaint(0.8f));

        Vector3 both = ReceiverLight(panels: [gray], stars: [Orange, White]);

        ShouldBeNear(light: both, expected: new Vector3(0.816f, 0.336f, 0.016f) * unit, tolerance: 0.04f * unit, because: "each lamp is judged alone");
    }

    [Fact]
    public void AColoredSurfaceKeepsItsWashWhenItsCellMergesIntoACoarseCellWithAGraySurface()
    {
        // A red and a white slab share one 0.5 m column, x 2 to 2.5. With the camera at x 0.05 m, the 0.25 m grid ends at
        // x 2, so both slabs lie in the 0.5 m grid and share its cell faces. With the camera at x 0.2 m, the 0.25 m grid
        // reaches x 2.25 and holds the red slab alone. The red slab's wash must not depend on which grid holds it.
        Panel[] slabs =
        [
            new(new(2.12f, -1, -1.1f), new(0.23f, 0.98f, 0.02f), Red),
            new(new(2.38f, -1, -1.1f), new(0.23f, 0.98f, 0.02f), Vector4.One),
        ];

        Vector3 shared = ReceiverLight(panels: slabs, stars: [White], camera: new(0.05f, 0, 0));
        Vector3 alone = ReceiverLight(panels: slabs, stars: [White], camera: new(0.2f, 0, 0));

        float sharedRed = shared.X - shared.Y;
        float aloneRed = alone.X - alone.Y;
        aloneRed.Should().BeGreaterThan(1e-3f, "the sunlit red slab lights the receiver");
        sharedRed
            .Should()
            .BeInRange(
                aloneRed * 0.95f,
                aloneRed * 1.05f,
                "each surface is judged alone, before the cell face adds it to the white slab"
            );
    }

    [Fact]
    public void GlowEntersTheVolumeInFull()
    {
        // No star. A white glow and a red glow of the same red radiance light the receiver with the same red.
        Panel Glow(Vector3 radiance) => Reflector(Vector4.UnitW) with { Emission = radiance };

        float red = ReceiverLight(panels: [Glow(new(8, 0, 0))], stars: []).X;
        float white = ReceiverLight(panels: [Glow(new(8, 8, 8))], stars: []).X;

        red.Should().BeGreaterThan(1e-3f, "the glow lights the receiver");
        white.Should().BeApproximately(red, 0.05f * red, "glow is light the scene emits, which the volume keeps in full");
    }

    [Fact]
    public void ASurfaceWhoseEntityHoldsALightSendsNoGlow()
    {
        // The glowing panel is the face of a lamp, which already sends the lamp's power. The lamp here shines with
        // intensity 0, so the receiver can get only the panel's own glow.
        Panel glow = Reflector(Vector4.UnitW) with { Emission = new(8, 0, 0) };

        float free = ReceiverLight(panels: [glow], stars: []).X;
        float held = ReceiverLight(
                panels: [glow with { Entity = Gpu.LampEntity }],
                stars: [],
                lamp: Gpu.Lamp(position: glow.Center, color: White, intensity: 0, chainDepth: TestChains.CamerasFrame)
            )
            .X;

        free.Should().BeGreaterThan(1e-3f, "a glow that no light carries lights the receiver");
        held.Should().BeLessThan(0.01f * free, "the lamp's entity sends its power through the lamp alone");
    }
}
