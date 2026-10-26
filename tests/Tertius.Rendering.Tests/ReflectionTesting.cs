using System.Numerics;

namespace Tertius.Rendering.Tests;

/// <summary>
/// Helpers for the GPU tests of the screen-space reflections (docs/plans/screen-space-reflections.md, "GPU tests of the
/// reflections"): scenes over a floor 1.5 m below the eye, reads of the HDR image, and the C# copy of what the trace does
/// at a floor pixel. The camera of <see cref="Gpu.Snapshot"/> sits at the origin and looks down −Z with +Y up.
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

    /// <summary>
    /// The setting of most tests: the renderer's default with the full-resolution trace, which the C# copy of the trace
    /// describes pixel by pixel.
    /// </summary>
    public static ConeTracedReflections Reflections =>
        ConeTracedReflections.Default with
        {
            Resolution = TraceResolution.Full,
        };

    /// <summary>The renderer's default, traced at <paramref name="resolution"/>.</summary>
    public static ConeTracedReflections ReflectionsAt(TraceResolution resolution) =>
        ConeTracedReflections.Default with
        {
            Resolution = resolution,
        };

    /// <summary>A white metal floor of <paramref name="roughness"/>: a mirror at 0, which the BRDF floors to 0.045.</summary>
    public static MaterialDesc Mirror(float roughness = 0) =>
        new(BaseColor: Vector4.One, Roughness: roughness, Metallic: 1);

    /// <summary>
    /// A normal map of one texel (128, 128, 255), the flat normal of an 8-bit map. The floors take it, so that the C# copy
    /// starts from the normal that the G-buffer holds, <see cref="FlatMapNormal"/>, whatever the renderer's default.
    /// </summary>
    public static TextureId FlatNormalMap(VulkanRenderer renderer) =>
        renderer.CreateTexture(new(Width: 1, Height: 1, Rgba: [128, 128, 255, 255], Srgb: false));

    /// <summary>A validating headless renderer of 640 × 360 at a fixed exposure of 0 stops, with <paramref name="reflections"/>.</summary>
    public static VulkanRenderer Headless(ReflectionSetting reflections, bool impostors = false) =>
        Gpu.Headless(impostors: impostors, reflections: reflections);

    /// <summary>No sun and no ambient light: only emission lights the scene, so a metal floor shows only what the trace adds.</summary>
    public static RenderSnapshot Dark()
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
        snapshot.Ambient.Intensity = 0;
        return snapshot;
    }

    /// <summary>No sun, and an ambient light of <paramref name="gray"/> from the sky and the ground alike.</summary>
    public static RenderSnapshot Gray(float gray)
    {
        RenderSnapshot snapshot = Gpu.Snapshot(Vector3.UnitY, sunIntensity: 0);
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
            radius: 43
        );

    /// <summary>A box of <paramref name="size"/> in m centered on <paramref name="center"/>.</summary>
    public static RenderInstance Block(MeshId box, MaterialId material, Vector3 center, Vector3 size) =>
        Gpu.Instance(mesh: box, material: material, position: center, scale: size, radius: size.Length() / 2);

    /// <summary>A black material of reflectance 0 that emits <paramref name="radiance"/>, so it shows that and no light.</summary>
    public static MaterialId Glow(VulkanRenderer renderer, Vector3 radiance) => BrdfTesting.Glow(renderer, radiance);

    /// <summary>Renders the scene that <paramref name="scene"/> builds once at 640 × 360 and 0 stops, with <paramref name="reflections"/>.</summary>
    /// <returns>The frame's HDR image, and its camera.</returns>
    public static (float[] Hdr, CameraView Camera) RenderHdr(
        ReflectionSetting reflections,
        Func<VulkanRenderer, RenderSnapshot> scene
    ) => RenderHdr(reflections: reflections, scene: scene, view: new());

    /// <summary>Renders the scene that <paramref name="scene"/> builds once in <paramref name="view"/>, with <paramref name="reflections"/>.</summary>
    /// <returns>The frame's HDR image, and its camera.</returns>
    public static (float[] Hdr, CameraView Camera) RenderHdr(
        ReflectionSetting reflections,
        Func<VulkanRenderer, RenderSnapshot> scene,
        TestView view
    )
    {
        VulkanRenderer renderer = Gpu.Headless(
            width: view.Width,
            height: view.Height,
            exposure: new FixedExposure(view.ExposureStops),
            reflections: reflections
        );
        try
        {
            renderer.Render(scene(renderer), 1f);
            return (renderer.ReadHdr(), renderer.LastScene.Camera);
        }
        finally
        {
            Gpu.DisposeAndAssertClean(renderer);
        }
    }

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

    /// <summary>W at a floor pixel: the compensated specular albedo of the floor's <paramref name="material"/> toward the eye, about its shading normal.</summary>
    public static Vector3 SpecularWeight(MaterialDesc material, CameraView camera, (int X, int Y) pixel)
    {
        Vector3 toView = -Vector3.Normalize(camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f)));
        var sample = new MaterialSample(
            BaseColor: new(material.BaseColor.X, material.BaseColor.Y, material.BaseColor.Z),
            Metallic: material.Metallic,
            Roughness: material.Roughness,
            Reflectance: material.Reflectance
        );
        return sample
            .CompensatedSpecularAlbedo(normal: FlatMapNormal, toView: toView, tables: BrdfTables.Shared)
            .ToVector3();
    }

    /// <summary>The C# depth pyramid of the last frame: the inverse depth of each pixel from the distance target.</summary>
    public static DepthPyramid PyramidOf(VulkanRenderer renderer)
    {
        CameraView camera = renderer.LastScene.Camera;
        float[] distance = renderer.ReadDistance();
        float[] inverseDepths = new float[distance.Length];
        for (int y = 0; y < camera.Height; y++)
        for (int x = 0; x < camera.Width; x++)
        {
            int pixel = y * camera.Width + x;
            float ray = camera.ViewRayThrough(new(x + 0.5f, y + 0.5f)).Length();
            inverseDepths[pixel] = distance[pixel] > 0 ? ray / distance[pixel] : 0;
        }

        return DepthPyramid.FromInverseDepths(inverseDepths, camera.Width, camera.Height);
    }

    /// <summary>
    /// The C# trace of the reflected ray of a floor pixel, as reflection_trace.comp starts it: from the surface point at
    /// the pixel's distance, along the specular dominant direction of <paramref name="roughness"/> about the shading
    /// <paramref name="normal"/>. The geometric normal is +Y.
    /// </summary>
    /// <returns>The trace, and the surface point of the pixel, camera-relative.</returns>
    public static (RayTrace Trace, Vector3 Point) TraceFromFloor(
        CameraView camera,
        float[] distance,
        DepthPyramid pyramid,
        (int X, int Y) pixel,
        Vector3 normal,
        float roughness,
        int maxSteps
    )
    {
        Vector3 viewRay = camera.ViewRayThrough(new(pixel.X + 0.5f, pixel.Y + 0.5f));
        var toSurface = Vector3.Normalize(viewRay);
        float distanceToEye = distance[pixel.Y * camera.Width + pixel.X];
        Vector3 point = toSurface * distanceToEye;
        var direction = new GgxLobe(roughness)
            .DominantDirection(normal: normal, mirror: Vector3.Reflect(toSurface, normal))
            .ToVector3();
        var ray = ScreenRay.FromSurface(
            point: point,
            viewDepth: distanceToEye / viewRay.Length(),
            geometricNormal: Vector3.UnitY,
            direction: direction,
            camera: camera
        );
        return (ray.Trace(pyramid, maxSteps), point);
    }

    /// <summary>
    /// The width in pixels over which <paramref name="values"/> rises from 10 % to 90 % of its value at
    /// <paramref name="plateau"/>, scanning from <paramref name="outside"/> toward it, with linear interpolation between
    /// pixels: the width of a blurred edge.
    /// </summary>
    public static float RiseWidth(IReadOnlyList<float> values, int outside, int plateau)
    {
        float peak = values[plateau];
        int direction = Math.Sign(plateau - outside);
        float Crossing(float share)
        {
            float level = share * peak;
            for (int x = outside + direction; x != plateau + direction; x += direction)
                if (values[x] >= level)
                {
                    float before = values[x - direction];
                    return x - direction + direction * (level - before) / (values[x] - before);
                }

            throw new InvalidOperationException($"The values never reach {share:P0} of {peak}.");
        }

        return MathF.Abs(Crossing(0.9f) - Crossing(0.1f));
    }

    /// <summary>The green channel of row <paramref name="y"/> of an RGBA image, indexed by x: the light of a white reflection.</summary>
    public static float[] Row(float[] rgba, int y, int width = Width)
    {
        float[] row = new float[width];
        for (int x = 0; x < width; x++)
            row[x] = rgba[4 * (y * width + x) + 1];
        return row;
    }
}

/// <summary>The screen of a test's renderer, in pixels, and its fixed exposure in stops.</summary>
internal sealed record TestView(
    int Width = ReflectionTesting.Width,
    int Height = ReflectionTesting.Height,
    float ExposureStops = 0
);
