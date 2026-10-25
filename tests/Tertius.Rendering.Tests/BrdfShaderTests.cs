using System.Numerics;
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

    /// <summary>102 of 255, which the specular target stores exactly.</summary>
    private const float Reflectance = 0.4f;

    private static readonly Vector3 SunDirection = Vector3.Normalize(new(0.5f, -0.2f, 0.85f));

    private static Vector3 CenterOf(int row, int column) => new((column - 1.5f) * 0.8f, (2.5f - row) * 0.6f, -5);

    /// <summary>The square's normal: it faces the camera, turned up by its row's tilt.</summary>
    private static Double3 NormalOf(int row)
    {
        double tilt = Rows[row].Tilt * Math.PI / 180;
        return new(0, Math.Sin(tilt), Math.Cos(tilt));
    }

    /// <summary>The sky and ground of the lit render: distinct colors, so a swapped share shows.</summary>
    private static readonly Vector3 Sky = new(0.2f, 0.3f, 0.6f);

    private static readonly Vector3 Ground = new(0.35f, 0.2f, 0.08f);

    private const float SunIntensity = 1.5f;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheShadersShadeEachMaterialAsTheCSharpBrdfDoes(bool transparent)
    {
        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            RenderFlags flags = transparent ? RenderFlags.Transparent : RenderFlags.None;
            MeshId square = renderer.CreateMesh(Primitives.Plane(0.25f));
            RenderSnapshot lit = Gpu.Snapshot(SunDirection, sunIntensity: SunIntensity);
            lit.Ambient = new()
            {
                SkyColor = Sky,
                GroundColor = Ground,
                Intensity = 1,
            };
            RenderSnapshot glowing = Gpu.Snapshot(SunDirection, sunIntensity: 0);
            glowing.Ambient.Intensity = 0;
            var pixels = new List<Vector2>();
            var expected = new List<Vector3>();
            for (int row = 0; row < Rows.Length; row++)
            for (int column = 0; column < Roughnesses.Length; column++)
            {
                Vector3 center = CenterOf(row, column);
                pixels.Add(Holding(Gpu.Project(point: center, width: Width, height: Height)));
                expected.Add(ExpectedLight(row: row, column: column, pixel: pixels[^1], transparent: transparent));
                var rotation = Quaternion.CreateFromAxisAngle(
                    Vector3.UnitX,
                    MathF.PI / 2 - Rows[row].Tilt * MathF.PI / 180
                );
                lit.Instances.Add(
                    Square(
                        mesh: square,
                        material: renderer.CreateMaterial(MaterialOf(row, column)),
                        center: center,
                        rotation: rotation,
                        flags: flags
                    )
                );
                MaterialId glow = renderer.CreateMaterial(
                    new(BaseColor: new(x: 0, y: 0, z: 0, w: 1), Roughness: 0.5f, Reflectance: 0, Emissive: expected[^1])
                );
                glowing.Instances.Add(
                    Square(mesh: square, material: glow, center: center, rotation: rotation, flags: flags)
                );
            }

            string name = transparent ? "brdf-shader-transparent" : "brdf-shader-opaque";
            Capture shaded = Gpu.RenderAndCapture(renderer: renderer, snapshot: lit, name: name);
            Capture glows = Gpu.RenderAndCapture(renderer: renderer, snapshot: glowing, name: name + "-glow");
            ShouldMatch(shaded: shaded, glows: glows, pixels: pixels, expected: expected);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>Requires each square's shaded color to match its emissive twin at its pixel.</summary>
    private static void ShouldMatch(Capture shaded, Capture glows, List<Vector2> pixels, List<Vector3> expected)
    {
        for (int i = 0; i < pixels.Count; i++)
        {
            var difference = Vector3.Abs(shaded.Color(pixels[i]) - glows.Color(pixels[i]));
            MathF
                .Max(difference.X, MathF.Max(difference.Y, difference.Z))
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
            BaseColor: new(ToVector(Rows[row].Metal ? MetalColor : NonMetalColor), 1),
            Roughness: Roughnesses[column],
            Metallic: Rows[row].Metal ? 1 : 0,
            Reflectance: Reflectance
        );

    /// <summary>
    /// The C# light of a square at the surface point that <paramref name="pixel"/>'s center sees: the sun and the
    /// hemisphere ambient light, with both sky shares from the geometry.
    /// </summary>
    private static Vector3 ExpectedLight(int row, int column, Vector2 pixel, bool transparent)
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
        var material = new MaterialSample(
            BaseColor: ToDouble(new(desc.BaseColor.X, desc.BaseColor.Y, desc.BaseColor.Z)),
            Metallic: desc.Metallic,
            Roughness: desc.Roughness,
            Reflectance: desc.Reflectance
        );
        Double3 sun =
            material.Reflected(
                normal: normal,
                toView: toView,
                toLight: ToDouble(SunDirection),
                tables: BrdfTables.Shared
            ) * ToDouble(new Vector3(1f, 0.96f, 0.9f) * SunIntensity);
        Double3 ambient = material.ReflectedHemisphere(
            normal: normal,
            toView: toView,
            sky: ToDouble(Sky),
            ground: ToDouble(Ground),
            normalSkyShare: normalSky,
            lobeSkyShare: lobeSky,
            tables: BrdfTables.Shared
        );
        return ToVector(sun + ambient);
    }

    private static RenderInstance Square(
        MeshId mesh,
        MaterialId material,
        Vector3 center,
        Quaternion rotation,
        RenderFlags flags
    ) =>
        Gpu.Instance(
            mesh: mesh,
            material: material,
            position: center,
            scale: Vector3.One,
            rotation: rotation,
            flags: flags,
            radius: 0.36f
        );
}
