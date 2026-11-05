using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Helpers for the GPU tests of the screen tracer's reflections (docs/plans/temporal-renderer.md, section 6): scenes over
/// a floor 1.5 m below the eye, reads of the HDR image, and the C# depth pyramid of a frame. The camera of <see cref="Gpu.Snapshot"/> sits at the origin and looks down −Z with +Y up.
/// </summary>
/// <remarks>
/// The G-buffer stores the roughness and the reflectance in 8 bits, so the scenes use values that 8 bits hold exactly,
/// such as 26 / 255 for a roughness of about 0.1, and the C# BRDF reads the same values.
/// </remarks>
internal static class ReflectionTesting
{
    public const int Width = 640,
        Height = 360;

    /// <summary>The height of the floor's top, in m: 1.5 m below the eye.</summary>
    public const float FloorHeight = -1.5f;

    /// <summary>
    /// The shading normal that material.glsl decodes from <see cref="FlatNormalMap"/> on the floor, whose tangent is +X
    /// and bitangent −Z: an 8-bit map holds 0 only as 128 / 255 · 2 − 1 ≈ 0.0039, so the normal leans by about 0.3°.
    /// </summary>
    public static readonly Vector3 FlatMapNormal = Vector3.Normalize(
        new(x: 128 / 255f * 2 - 1, y: 1, z: -(128 / 255f * 2 - 1))
    );

    /// <summary>A white metal floor of <paramref name="roughness"/>: a mirror at 0, which the BRDF floors to 0.045.</summary>
    public static MaterialDesc Mirror(float roughness = 0) =>
        new(BaseColor: Vector4.One, Roughness: roughness, Model: new StandardShading(Metallic: 1));

    /// <summary>
    /// A normal map of one texel (128, 128, 255), the flat normal of an 8-bit map. The floors take it, so that the C# copy
    /// starts from the normal that the G-buffer holds, <see cref="FlatMapNormal"/>, whatever the renderer's default.
    /// </summary>
    public static TextureId FlatNormalMap(VulkanRenderer renderer) =>
        renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: [128, 128, 255, 255], Srgb: false));

    /// <summary>No sun and no ambient light: only emission lights the scene, so a metal floor shows only what the trace adds.</summary>
    public static RenderSnapshot Dark()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>No sun, and an ambient light of <paramref name="gray"/> from the sky and the ground alike.</summary>
    public static RenderSnapshot Gray(float gray)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, starIrradiance: 0);
        snapshot.Ambient = new()
        {
            SkyColor = new(gray),
            GroundColor = new(gray),
            Intensity = 1,
        };
        return snapshot;
    }

    /// <summary>
    /// The floor: a plane of 60 m by 60 m whose top lies at <see cref="FloorHeight"/>, from 10 m behind the eye to 50 m
    /// ahead, with <see cref="FlatNormalMap"/>, so that its shading normal is <see cref="FlatMapNormal"/>.
    /// </summary>
    public static RenderInstance Floor(VulkanRenderer renderer, MaterialDesc material) =>
        Gpu.Instance(
            mesh: renderer.CreateMesh(Primitives.Plane(1)),
            material: renderer.CreateMaterial(material with { NormalMap = FlatNormalMap(renderer) }),
            position: new(0, FloorHeight, -20),
            scale: new(30, 1, 30),
            radius: 43,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A box of <paramref name="size"/> in m centered on <paramref name="center"/>.</summary>
    public static RenderInstance Block(MeshId box, MaterialId material, Vector3 center, Vector3 size) =>
        Gpu.Instance(
            mesh: box,
            material: material,
            position: center,
            scale: size,
            radius: size.Length() / 2,
            chainDepth: TestChains.CamerasFrame
        );

    /// <summary>A black material of reflectance 0 that emits <paramref name="radiance"/>, so it shows that and no light.</summary>
    public static MaterialId Glow(VulkanRenderer renderer, Vector3 radiance) => BrdfTesting.Glow(renderer, radiance);

    /// <summary>The RGB of a pixel of an RGBA image read row by row from the top.</summary>
    public static Vector3 At(float[] rgba, (int X, int Y) pixel, int width = Width)
    {
        int index = 4 * (pixel.Y * width + pixel.X);
        return new(x: rgba[index], y: rgba[index + 1], z: rgba[index + 2]);
    }

    /// <summary>The pixel that holds a camera-relative point, for the camera of <see cref="Gpu.Snapshot"/>.</summary>
    public static (int X, int Y) PixelOf(Vector3 point, int width = Width, int height = Height)
    {
        Vector2 pixel = Gpu.Project(point: point, width: width, height: height);
        return ((int)MathF.Floor(pixel.X), (int)MathF.Floor(pixel.Y));
    }

    /// <summary>The floor pixel that shows the mirror image of <paramref name="point"/>, a point above the floor.</summary>
    public static (int X, int Y) MirrorPixel(Vector3 point, int width = Width, int height = Height) =>
        PixelOf(new(point.X, 2 * FloorHeight - point.Y, point.Z), width, height);

    /// <summary>The C# depth pyramid of the last frame, from its distance target and the geometric normals of its
    /// normals target.</summary>
    public static DepthPyramid PyramidOf(VulkanRenderer renderer)
    {
        float[] normals = renderer.ReadNormals();
        var geometricNormals = new Vector3[normals.Length / 4];
        for (int pixel = 0; pixel < geometricNormals.Length; pixel++)
            geometricNormals[pixel] = OctahedralNormal.Decode(new(normals[4 * pixel + 2], normals[4 * pixel + 3]));
        return DepthPyramid.Of(
            camera: renderer.LastScene.Camera,
            distances: renderer.ReadDistance(),
            geometricNormals: geometricNormals
        );
    }
}
