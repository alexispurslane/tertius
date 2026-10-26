using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;
using static Tertius.Rendering.Tests.OcclusionScenes;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The test that pins the lighting pass's use of the occlusion to the C# copy (docs/plans/ambient-occlusion.md, "GPU
/// tests of the acceptance criteria"): the corner under a sky and a ground of distinct colors, where each of four pixels
/// must show the C# ambient light at the occlusion that the occlusion target holds there, as an emissive square of
/// that light on its line of sight shows.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class AmbientOcclusionShaderTests
{
    /// <summary>Two steps of an 8-bit channel: the rounding of both images, and of the stored normal.</summary>
    private const float Tolerance = 2f / 255;

    /// <summary>102 of 255, which the specular target stores exactly.</summary>
    private const float Reflectance = 0.4f;

    /// <summary>The sky and the ground: distinct colors, so a swapped share or a swapped term shows.</summary>
    private static readonly Vector3 Sky = new(0.2f, 0.3f, 0.6f);

    private static readonly Vector3 Ground = new(0.35f, 0.2f, 0.08f);

    /// <summary>The four surface points and their normals: the floor and the back wall, each at the corner and open.</summary>
    private static readonly (Vector3 Point, Double3 Normal)[] Points =
    [
        (Point: FloorAtTheCorner, Normal: Double3.UnitY),
        (Point: OpenFloor, Normal: Double3.UnitY),
        (Point: new(SideWallX + 0.1f, FloorHeight + 0.1f, BackWallZ), Normal: Double3.UnitZ),
        (Point: new(0.8f, FloorHeight + 1.2f, BackWallZ), Normal: Double3.UnitZ),
    ];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheLightingPassDimsTheAmbientLightOfEachPixelAsTheCSharpCopyDoes(bool smoothMetal)
    {
        MaterialDesc desc = MaterialOf(smoothMetal);
        VulkanRenderer renderer = Gpu.Headless(ambientOcclusion: GroundTruthOcclusion.Default);
        try
        {
            RenderSnapshot lit = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
            lit.Ambient = new()
            {
                SkyColor = Sky,
                GroundColor = Ground,
                Intensity = 1,
            };
            Capture shaded = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Corner(renderer, renderer.CreateMaterial(desc), lit),
                name: $"occlusion-shader-{(smoothMetal ? "metal" : "rough")}"
            );
            float[] occlusion = renderer.ReadOcclusion();

            Vector2[] pixels = [.. Points.Select(point => PixelOf(point.Point, CornerView))];
            Vector3[] expected =
            [
                .. Points.Select(
                    (point, i) =>
                        ExpectedLight(
                            desc: desc,
                            normal: point.Normal,
                            pixel: pixels[i],
                            occlusion: OcclusionAt(occlusion, pixels[i])
                        )
                ),
            ];
            Capture glows = Gpu.RenderAndCapture(
                renderer: renderer,
                snapshot: Glows(renderer, pixels, expected),
                name: $"occlusion-shader-{(smoothMetal ? "metal" : "rough")}-glow"
            );

            for (int i = 0; i < pixels.Length; i++)
                LargestChannelDifference(shaded.Color(pixels[i]), glows.Color(pixels[i]))
                    .Should()
                    .BeLessThanOrEqualTo(
                        Tolerance,
                        "point {0} at V = {1} must show the C# light {2}: shaded {3}, emissive {4}",
                        i,
                        OcclusionAt(occlusion, pixels[i]),
                        expected[i],
                        shaded.Color(pixels[i]),
                        glows.Color(pixels[i])
                    );
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    /// <summary>A smooth metal or a rough non-metal, in decoded sRGB bytes and roughnesses that the G-buffer stores exactly.</summary>
    private static MaterialDesc MaterialOf(bool smoothMetal) =>
        new(
            BaseColor: smoothMetal
                ? new(x: (float)Decoded(250), y: (float)Decoded(210), z: (float)Decoded(140), w: 1)
                : new(x: (float)Decoded(200), y: (float)Decoded(60), z: (float)Decoded(40), w: 1),
            Roughness: smoothMetal ? 0.2f : 0.8f,
            Model: new StandardShading(Metallic: smoothMetal ? 1 : 0),
            Reflectance: Reflectance
        );

    /// <summary>The unit direction from the eye through the center of <paramref name="pixel"/>, for the corner's camera.</summary>
    private static Double3 CornerRay(Vector2 pixel) =>
        Vector3.Transform(Ray(pixel, Width, Height).ToVector3(), CornerView);

    /// <summary>
    /// The C# ambient light of the surface that <paramref name="pixel"/>'s center sees, with the sky shares as the
    /// G-buffer stores them and the occlusion that the occlusion target holds.
    /// </summary>
    private static Vector3 ExpectedLight(MaterialDesc desc, Double3 normal, Vector2 pixel, double occlusion)
    {
        Double3 toView = -CornerRay(pixel);
        // The specular target stores the lobe's sky share in 8 bits, and the emissive target the normal's in a half float.
        double lobeSky =
            Math.Round(LobeSkyShare(normal: normal, toView: toView, roughness: desc.Roughness) * 255) / 255;
        double normalSky = (double)(Half)NormalSkyShare(normal);
        MaterialSample material = TestMaterials.SampleOf(desc);
        return material
            .ReflectedHemisphere(
                normal: normal,
                toView: toView,
                sky: Sky,
                ground: Ground,
                normalSkyShare: normalSky,
                lobeSkyShare: lobeSky,
                occlusion: occlusion,
                tables: BrdfTables.Shared
            )
            .ToVector3();
    }

    /// <summary>A small black square that emits each radiance, 1 m from the eye on its pixel's line of sight, facing the eye.</summary>
    private static RenderSnapshot Glows(VulkanRenderer renderer, Vector2[] pixels, Vector3[] radiances)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        Look(snapshot, CornerView);
        MeshId square = renderer.CreateMesh(Primitives.Plane(0.01f));
        Quaternion facingTheEye = CornerView * Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);
        for (int i = 0; i < pixels.Length; i++)
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: square,
                    material: Glow(renderer, radiances[i]),
                    position: CornerRay(pixels[i]).ToVector3(),
                    scale: Vector3.One,
                    rotation: facingTheEye,
                    radius: 0.02f
                )
            );
        return snapshot;
    }
}
