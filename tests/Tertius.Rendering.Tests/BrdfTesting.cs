using System.Numerics;
using Tertius.Testing;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Helpers for the GPU tests of the BRDF (docs/plans/pbr-brdf.md, "Tests"): the geometry of what a pixel sees, the
/// lobe's sky share as the shaders compute it, and the displayed color of a radiance.
/// </summary>
internal static class BrdfTesting
{
    /// <summary>The linear value of an sRGB byte, as the GPU decodes it.</summary>
    public static double Decoded(byte srgb)
    {
        double c = srgb / 255.0;
        return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    /// <summary>The integer pixel that holds <paramref name="pixel"/>, as <see cref="Capture.Color(Vector2)"/> reads it.</summary>
    public static Vector2 Holding(Vector2 pixel) => new(MathF.Floor(pixel.X), MathF.Floor(pixel.Y));

    /// <summary>
    /// The unit direction from the eye through the center of the pixel that holds <paramref name="pixel"/>, for the camera
    /// of <see cref="Gpu.Snapshot"/>: identity rotation, looking down −Z, with <see cref="Gpu.Fov"/>.
    /// </summary>
    public static Double3 Ray(Vector2 pixel, int width, int height)
    {
        double tanY = Math.Tan(Gpu.Fov / 2);
        double tanX = tanY * width / height;
        double ndcX = (Math.Floor(pixel.X) + 0.5) / width * 2 - 1;
        double ndcY = 1 - (Math.Floor(pixel.Y) + 0.5) / height * 2;
        return new Double3(ndcX * tanX, ndcY * tanY, -1).Normalized();
    }

    /// <summary>Where a ray from the eye meets the plane through <paramref name="point"/> with <paramref name="normal"/>.</summary>
    public static Double3 OnPlane(Double3 ray, Double3 point, Double3 normal) =>
        ray * (Double3.Dot(normal, point) / Double3.Dot(normal, ray));

    /// <summary>Where a ray from the eye first meets a sphere. The ray must hit it.</summary>
    public static Double3 OnSphere(Double3 ray, Double3 center, double radius)
    {
        double along = Double3.Dot(ray, center);
        double inside = along * along - center.LengthSquared + radius * radius;
        inside.Should().BePositive("the ray must hit the sphere");
        return ray * (along - Math.Sqrt(inside));
    }

    /// <summary>The mirror direction of the view about the normal: GLSL's reflect(−V, N).</summary>
    public static Double3 Mirror(Double3 normal, Double3 toView) => 2 * Double3.Dot(normal, toView) * normal - toView;

    /// <summary>The normal's sky share s_n for the up +Y, as hemisphere.glsl computes it.</summary>
    public static double NormalSkyShare(Double3 normal) => Math.Clamp((1 + normal.Y) / 2, 0, 1);

    /// <summary>The lobe's sky share s_l for the up +Y, as hemisphere.glsl computes it from the C# lobe and table.</summary>
    public static double LobeSkyShare(Double3 normal, Double3 toView, double roughness)
    {
        var lobe = new GgxLobe(roughness);
        Double3 dominant = lobe.DominantDirection(normal, Mirror(normal, toView));
        return BrdfTables.Shared.SampleLobeSky(cosine: dominant.Y, roughness: lobe.Roughness);
    }

    /// <summary>The largest difference between two colors in any channel.</summary>
    public static float LargestChannelDifference(Vector3 a, Vector3 b)
    {
        var difference = Vector3.Abs(a - b);
        return MathF.Max(difference.X, MathF.Max(difference.Y, difference.Z));
    }

    /// <summary>
    /// A black material of reflectance 0 that emits <paramref name="radiance"/>, so it shows that and no light. It draws
    /// as <paramref name="alpha"/> says, opaque by default. Its roughness is the unorm code 128 / 255, just above the
    /// screen tracer's MaxRoughness of 0.5, so that it traces no reflection ray. A roughness of 0.5 lies halfway between the
    /// codes 127 and 128 of the G-buffer's RGBA8 roughness, where Vulkan lets a driver store either: NVIDIA's stores 127,
    /// which puts the emitter inside the hand-over band (ticket t8-ol7x).
    /// </summary>
    public static MaterialId Glow(VulkanRenderer renderer, Vector3 radiance, AlphaMode alpha = default) =>
        renderer.CreateMaterial(
            new(
                BaseColor: new(x: 0, y: 0, z: 0, w: 1),
                Roughness: 128 / 255f,
                Reflectance: 0,
                Emissive: radiance,
                Alpha: alpha
            )
        );

    /// <summary>
    /// The colors that <paramref name="radiances"/> show on screen: each is the emission of a black square that no light
    /// reaches, so it goes through the same HDR target and tone curve as a lit surface (as LitPuffRenderTests compares
    /// lit puffs with glow puffs). The squares stand in a row 3 m ahead.
    /// </summary>
    public static Vector3[] Displayed(VulkanRenderer renderer, IReadOnlyList<Vector3> radiances, string name)
    {
        const float spacing = 0.5f;
        (int width, int height) = renderer.OutputSize;
        MeshId square = renderer.CreateMesh(Primitives.Plane(0.2f));
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitZ, starIrradiance: 0);
        snapshot.Ambient.Intensity = 0;
        var centers = new Vector3[radiances.Count];
        for (int i = 0; i < radiances.Count; i++)
        {
            centers[i] = new((i - (radiances.Count - 1) / 2f) * spacing, 0, -3);
            snapshot.Instances.Add(
                Gpu.Instance(
                    mesh: square,
                    material: Glow(renderer, radiances[i]),
                    position: centers[i],
                    scale: Vector3.One,
                    rotation: Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
                    radius: 0.3f,
                    chainDepth: TestChains.CamerasFrame
                )
            );
        }

        Capture image = Gpu.RenderAndCapture(renderer: renderer, snapshot: snapshot, name: name);
        return [.. centers.Select(c => image.Color(Gpu.Project(point: c, width: width, height: height)))];
    }
}
