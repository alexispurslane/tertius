using System.Numerics;
using static Tertius.Rendering.Tests.BrdfTesting;

namespace Tertius.Rendering.Tests;

/// <summary>
/// The shading normal of a material on the GPU: a material without a normal map shades with its interpolated vertex
/// normal, and a normal map decodes as glTF's xyz × 2 − 1. Each test finds the sun's highlight on a smooth white metal
/// square that fills a 10° view. At the roughness floor the highlight peaks where the half vector meets the shading
/// normal, so the center of the highlight shows that normal to a quarter of a milliradian.
/// </summary>
[Collection(GpuCollection.Name)]
public sealed class ShadingNormalTests
{
    private const int Width = 640,
        Height = 360;

    /// <summary>The vertical field of view: one pixel spans 0.49 mrad, so the highlight moves 4 px per mrad of tilt.</summary>
    private const float NarrowFov = 10 * MathF.PI / 180;

    /// <summary>The square turned to face the camera: its normal +Y becomes +Z, and its tangent stays +X.</summary>
    private static readonly Quaternion Facing = Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2);

    /// <summary>
    /// The pixel of the highlight of the sun along <paramref name="toSun"/> on a surface of shading normal
    /// <paramref name="normal"/>: the eye sees the sun's mirror image where the half vector is the normal.
    /// </summary>
    private static Vector2 MirrorPixel(Double3 normal, Double3 toSun) =>
        Gpu.Project(point: (-Mirror(normal, toSun)).ToVector3(), width: Width, height: Height, fov: NarrowFov);

    /// <summary>
    /// The center of the highlight in <paramref name="image"/>: the mean of the pixel centers at least half as bright as
    /// the brightest, weighted by luma. The 8-bit pixels near the peak tie, so the brightest one alone can miss it by a
    /// pixel.
    /// </summary>
    private static Vector2 HighlightCenter(Capture image)
    {
        float most = 0;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
            most = MathF.Max(most, image.Luma(x, y));
        most.Should().BePositive("the sun's mirror image must lie in view");

        var sum = Vector2.Zero;
        float weight = 0;
        for (int y = 0; y < image.Height; y++)
        for (int x = 0; x < image.Width; x++)
        {
            float luma = image.Luma(x, y);
            if (luma < most / 2)
                continue;
            sum += luma * new Vector2(x + 0.5f, y + 0.5f);
            weight += luma;
        }

        return sum / weight;
    }

    /// <summary>
    /// The center of the highlight on a smooth white metal square 3 m ahead that faces the camera and fills the view, with
    /// <paramref name="normalMap"/>, under a sun along <paramref name="toSun"/> and no ambient light.
    /// </summary>
    private static Vector2 HighlightOnAMirror(Vector3 toSun, TextureData? normalMap, string name)
    {
        // A dim sun keeps the peak below 1 before the tone curve, which would turn the pixels around it white too.
        var white = new MaterialSample(BaseColor: new(1, 1, 1), Metallic: 1, Roughness: 0, Reflectance: 0.5);
        double peak = white
            .Reflected(normal: Double3.UnitZ, toView: Double3.UnitZ, toLight: Double3.UnitZ, tables: BrdfTables.Shared)
            .X;
        RenderSnapshot snapshot = Gpu.Snapshot(toSun, sunIntensity: (float)(0.5 / peak));
        snapshot.Ambient.Intensity = 0;
        snapshot.Camera.VerticalFov = NarrowFov;

        VulkanRenderer renderer = Gpu.Headless(width: Width, height: Height);
        try
        {
            MeshId square = renderer.CreateMesh(Primitives.Plane(1f));
            TextureId map = normalMap is { } data ? renderer.CreateTexture(data) : TextureId.None;
            MaterialId mirror = renderer.CreateMaterial(
                new(BaseColor: Vector4.One, Roughness: 0, Metallic: 1, NormalMap: map)
            );
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: square,
                    material: mirror,
                    position: new(0, 0, -3),
                    scale: Vector3.One,
                    rotation: Facing,
                    radius: 1.5f
                )
            );
            return HighlightCenter(Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name));
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

    [Fact]
    public void ASurfaceWithoutANormalMapShadesWithExactlyItsVertexNormal()
    {
        // The sun stands off the view axis, so the highlight lies away from the image's center lines.
        Double3 toSun = new Double3(0.03, -0.02, 1).Normalized();

        Vector2 highlight = HighlightOnAMirror(toSun: toSun.ToVector3(), normalMap: null, name: "normal-unmapped");

        Vector2 expected = MirrorPixel(normal: Double3.UnitZ, toSun: toSun);
        Vector2
            .Distance(highlight, expected)
            .Should()
            .BeLessThanOrEqualTo(
                1f,
                "the vertex normal +Z puts the sun's mirror image at {0}, not {1}; a tilt of 1 mrad moves it 4 px",
                expected,
                highlight
            );
    }

    [Fact]
    public void ANormalMapDecodesItsTexelAsTwiceTheValueMinusOne()
    {
        // (132, 126, 255) leans the normal by 35 mrad along the tangent and −12 mrad along the bitangent. A decode that
        // took 128 to 0, (t − 128) / 127, would lean it 4 mrad less along the tangent, 15 px away.
        byte[] texel = [132, 126, 255, 255];
        Double3 tangentSpace = new Double3(texel[0], texel[1], texel[2]) / 255 * 2 - new Double3(1, 1, 1);
        Vertex corner = Primitives.Plane(1f).Vertices[0];
        Double3 normal = Vector3.Transform(corner.Normal, Facing);
        Double3 tangent = Vector3.Transform(corner.Tangent.AsVector3(), Facing);
        Double3 bitangent = Double3.Cross(normal, tangent) * corner.Tangent.W;
        Double3 mapped = (tangent * tangentSpace.X + bitangent * tangentSpace.Y + normal * tangentSpace.Z).Normalized();

        Vector2 highlight = HighlightOnAMirror(
            toSun: Vector3.UnitZ,
            normalMap: new(Width: 1, Height: 1, Rgba: texel, Srgb: false),
            name: "normal-mapped"
        );

        Vector2 expected = MirrorPixel(normal: mapped, toSun: Double3.UnitZ);
        Vector2
            .Distance(highlight, expected)
            .Should()
            .BeLessThanOrEqualTo(1f, "the decoded normal {0} puts the sun's mirror image at {1}", mapped, expected);
    }
}
