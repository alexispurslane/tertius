using System.Numerics;
using Tertius.Testing;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The test that pins the shaders to the C# BRDF (docs/plans/pbr-brdf.md, "The GPU test that pins the shaders to the C#
/// copy"). A grid of squares faces the camera: one column per roughness, one row per kind and tilt. Each square's light
/// at the pixel of its center must show as an emissive square of the C# value shows.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class BrdfShaderTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>Two steps of an 8-bit channel: the rounding of both images, and of the stored normal.</summary>
    private const float Tolerance = 2f / 255;

    /// <summary>102 of 255, which the specular target stores exactly.</summary>
    private const float Reflectance = 0.4f;

    private const float SunIntensity = 1.5f;

    /// <summary>Values that the specular target stores exactly: 51, 102, 153 and 204 of 255.</summary>
    private static readonly float[] Roughnesses = [0.2f, 0.4f, 0.6f, 0.8f];

    /// <summary>The kind and the tilt of each row: the tilt turns the normal up from the view axis, in degrees.</summary>
    private static readonly (bool Metal, float Tilt)[] Rows =
    [
        (Metal: false, Tilt: 0),
        (Metal: false, Tilt: 45),
        (Metal: false, Tilt: -70),
        (Metal: true, Tilt: 0),
        (Metal: true, Tilt: 45),
        (Metal: true, Tilt: -70),
    ];

    /// <summary>Decoded sRGB bytes, so that the base color target stores them exactly.</summary>
    private static readonly Double3 NonMetalColor = new(Decoded(200), Decoded(60), Decoded(40));

    private static readonly Double3 MetalColor = new(Decoded(250), Decoded(210), Decoded(140));

    private static readonly Vector3 SunDirection = Vector3.Normalize(new(0.5f, -0.2f, 0.85f));

    /// <summary>The sky and ground of the lit render: distinct colors, so a swapped share shows.</summary>
    private static readonly Vector3 Sky = new(0.2f, 0.3f, 0.6f);

    private static readonly Vector3 Ground = new(0.35f, 0.2f, 0.08f);

    private static Vector3 CenterOf(int row, int column) => new((column - 1.5f) * 0.8f, (2.5f - row) * 0.6f, -5);

    /// <summary>The rotation that turns a plane, whose normal is +Y, to <see cref="NormalOf"/>.</summary>
    private static Quaternion RotationOf(int row) =>
        Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2 - Rows[row].Tilt * MathF.PI / 180);

    /// <summary>
    /// The snapshot of the lit render: the test's sun of <paramref name="sunAngularRadius"/> rad, and the ambient light of
    /// <see cref="Sky"/> and <see cref="Ground"/>.
    /// </summary>
    private static RenderSnapshot Lit(float sunAngularRadius)
    {
        RenderSnapshot lit = Gpu.Snapshot(SunDirection, starIrradiance: SunIntensity);
        Gpu.Star(snapshot: lit, angularRadius: sunAngularRadius);
        lit.Ambient = new()
        {
            SkyColor = Sky,
            GroundColor = Ground,
            Intensity = 1,
        };
        return lit;
    }

    /// <summary>The square's normal: it faces the camera, turned up by its row's tilt.</summary>
    private static Double3 NormalOf(int row)
    {
        double tilt = Rows[row].Tilt * Math.PI / 180;
        return new(0, Math.Sin(tilt), Math.Cos(tilt));
    }

    /// <summary>The Sun's size and a sun 0.2 rad wide, whose widened lobe changes every square.</summary>
    [Theory]
    [InlineData(false, Gpu.StarAngularRadius)]
    [InlineData(true, Gpu.StarAngularRadius)]
    [InlineData(false, 0.2f)]
    [InlineData(true, 0.2f)]
    public void TheShadersShadeEachMaterialAsTheCSharpBrdfDoes(bool transparent, float sunAngularRadius)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            AlphaMode alpha = transparent ? new GlassAlpha() : new OpaqueAlpha();
            MeshId square = renderer.CreateMesh(Primitives.Plane(0.25f));
            RenderSnapshot lit = Lit(sunAngularRadius);
            Double3 sunIrradiance = Gpu.StarIrradianceRgb(lit);
            RenderSnapshot glowing = Gpu.Snapshot(SunDirection, starIrradiance: 0);
            glowing.Ambient.Intensity = 0;
            var pixels = new List<Vector2>();
            var expected = new List<Vector3>();
            for (int row = 0; row < Rows.Length; row++)
            for (int column = 0; column < Roughnesses.Length; column++)
            {
                Vector3 center = CenterOf(row, column);
                pixels.Add(Holding(Gpu.Project(point: center, width: Width, height: Height)));
                expected.Add(
                    ExpectedLight(
                        row: row,
                        column: column,
                        pixel: pixels[^1],
                        transparent: transparent,
                        sunIrradiance: sunIrradiance,
                        sunSine: Math.Sin(sunAngularRadius)
                    )
                );
                Quaternion rotation = RotationOf(row);
                lit.Instances.Add(
                    Square(
                        mesh: square,
                        material: renderer.CreateMaterial(MaterialOf(row, column) with { Alpha = alpha }),
                        center: center,
                        rotation: rotation
                    )
                );
                MaterialId glow = Glow(renderer, expected[^1], alpha);
                glowing.Instances.Add(Square(mesh: square, material: glow, center: center, rotation: rotation));
            }

            string name = (transparent ? "brdf-shader-transparent-" : "brdf-shader-opaque-") + sunAngularRadius;
            Capture shaded = Gpu.RenderAndCapture(renderer: renderer, snapshot: lit, name: name);
            Capture glows = Gpu.RenderAndCapture(renderer: renderer, snapshot: glowing, name: name + "-glow");
            ShouldMatch(shaded: shaded, glows: glows, pixels: pixels, expected: expected);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void AGrazingViewReadsTheSchlickPartAtTheTexelOfItsViewCosine()
    {
        // μ = 2/31 is the center of the DFG table's third column. A read at the coordinate μ itself lands 0.014 lower
        // there, where S falls steeply with μ, so the shown color moves by several 8-bit steps.
        const double viewCosine = 2.0 / (BrdfTables.DfgSize - 1);
        const float roughness = 0.2f;
        (Vector2 pixel, Double3 toView) = WhereTheGroundsViewCosineIs(viewCosine);

        // A black ground of reflectance 0 has no diffuse color and an F0 of 0, so under a sky and a ground of one color
        // it shows the Schlick part S(μ, r) alone, whatever its sky shares.
        var black = new MaterialSample(BaseColor: Double3.Zero, Metallic: 0, Roughness: roughness, Reflectance: 0);
        var white = new Double3(1, 1, 1);
        Double3 schlickPart = black.ReflectedHemisphere(
            normal: Double3.UnitY,
            toView: toView,
            sky: white,
            ground: white,
            normalSkyShare: 0.5,
            lobeSkyShare: 0.5,
            occlusion: 1,
            bounce: Double3.Zero,
            tables: BrdfTables.Shared
        );

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId ground = renderer.CreateMesh(Primitives.Plane(50));
            MaterialId material = renderer.CreateMaterial(
                new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: roughness, Reflectance: 0)
            );
            RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
            snapshot.Ambient = new()
            {
                SkyColor = Vector3.One,
                GroundColor = Vector3.One,
                Intensity = 1,
            };
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: ground,
                    material: material,
                    position: new(0, -1, 0),
                    scale: Vector3.One,
                    radius: 71,
                    chainDepth: TestChains.CamerasFrame
                )
            );
            Vector3 shown = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: "brdf-grazing-schlick")
                .Color(pixel);
            Vector3 glow = Displayed(renderer, [schlickPart.ToVector3()], "brdf-grazing-schlick-glow")[0];

            LargestChannelDifference(shown, glow)
                .Should()
                .BeLessThanOrEqualTo(
                    Tolerance,
                    "the ground at μ = {0} must show the C# Schlick part {1}: shaded {2}, emissive {3}",
                    toView.Y,
                    schlickPart.X,
                    shown,
                    glow
                );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>
    /// The pixel of the center column, below the horizon, where the view of a ground with the normal +Y has the cosine
    /// nearest <paramref name="viewCosine"/>, and the unit direction from the ground there to the eye.
    /// </summary>
    private static (Vector2 Pixel, Double3 ToView) WhereTheGroundsViewCosineIs(double viewCosine)
    {
        (Vector2 Pixel, Double3 ToView) nearest = (Vector2.Zero, Double3.Zero);
        for (int y = Height / 2 + 1; y < Height; y++)
        {
            var pixel = new Vector2(Width / 2, y);
            Double3 toView = -Ray(pixel, Width, Height);
            if (Math.Abs(toView.Y - viewCosine) < Math.Abs(nearest.ToView.Y - viewCosine))
                nearest = (pixel, toView);
        }

        return nearest;
    }

    /// <summary>Requires each square's shaded color to match its emissive twin at its pixel.</summary>
    private static void ShouldMatch(Capture shaded, Capture glows, List<Vector2> pixels, List<Vector3> expected)
    {
        for (int i = 0; i < pixels.Count; i++)
        {
            LargestChannelDifference(shaded.Color(pixels[i]), glows.Color(pixels[i]))
                .Should()
                .BeLessThanOrEqualTo(
                    Tolerance,
                    "square {0} (row {1}, roughness {2}) must reflect the C# light {3}: shaded {4}, emissive {5}",
                    i,
                    i / Roughnesses.Length,
                    Roughnesses[i % Roughnesses.Length],
                    expected[i],
                    shaded.Color(pixels[i]),
                    glows.Color(pixels[i])
                );
        }
    }

    private static MaterialDesc MaterialOf(int row, int column) =>
        new(
            BaseColor: new((Rows[row].Metal ? MetalColor : NonMetalColor).ToVector3(), 1),
            Roughness: Roughnesses[column],
            Model: new StandardShading(Metallic: Rows[row].Metal ? 1 : 0),
            Reflectance: Reflectance
        );

    /// <summary>
    /// The C# light of a square at the surface point that <paramref name="pixel"/>'s center sees: the sun of
    /// <paramref name="sunIrradiance"/> whose angular radius has the sine <paramref name="sunSine"/>, and the hemisphere
    /// ambient light, with both sky shares from the geometry.
    /// </summary>
    private static Vector3 ExpectedLight(
        int row,
        int column,
        Vector2 pixel,
        bool transparent,
        Double3 sunIrradiance,
        double sunSine
    )
    {
        Double3 normal = NormalOf(row);
        Double3 toView = -Ray(pixel, Width, Height);
        double lobeSky = LobeSkyShare(normal: normal, toView: toView, roughness: Roughnesses[column]);
        double normalSky = NormalSkyShare(normal);
        // The specular target stores the lobe's sky share in 8 bits, and the emissive target the normal's in a half
        // float. The transparent pass keeps both in registers.
        if (!transparent)
        {
            lobeSky = Math.Round(lobeSky * 255) / 255;
            normalSky = (double)(Half)normalSky;
        }

        MaterialDesc desc = MaterialOf(row, column);
        MaterialSample material = TestMaterials.SampleOf(desc);
        Double3 sun =
            material.Reflected(
                normal: normal,
                toView: toView,
                toLight: SunDirection,
                tables: BrdfTables.Shared,
                lightSine: sunSine
            ) * sunIrradiance;
        Double3 ambient = material.ReflectedHemisphere(
            normal: normal,
            toView: toView,
            sky: Sky,
            ground: Ground,
            normalSkyShare: normalSky,
            lobeSkyShare: lobeSky,
            occlusion: 1,
            bounce: Double3.Zero,
            tables: BrdfTables.Shared
        );
        return (sun + ambient).ToVector3();
    }

    private static RenderInstance Square(MeshId mesh, MaterialId material, Vector3 center, Quaternion rotation) =>
        Gpu.Instance(
            mesh: mesh,
            material: material,
            position: center,
            scale: Vector3.One,
            rotation: rotation,
            radius: 0.36f,
            chainDepth: TestChains.CamerasFrame
        );
}
