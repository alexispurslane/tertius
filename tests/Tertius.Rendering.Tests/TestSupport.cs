using System.Numerics;
using StbImageSharp;
using StbImageWriteSharp;
using ColorComponents = StbImageSharp.ColorComponents;

namespace Tertius.Rendering.Tests;

/// <summary>GPU tests run one at a time: they share one physical device and the captures folder.</summary>
[CollectionDefinition(Name)]
public sealed class GpuCollection
{
    public const string Name = "GPU";
}

/// <summary>
/// Tests that change an environment variable of the test process run alone, after the parallel tests: the variable is
/// shared by every test that runs beside them.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironmentCollection
{
    public const string Name = "Process environment";
}

/// <summary>Helpers for GPU tests: renderer creation, captures, projection of world points to pixels.</summary>
internal static class Gpu
{
    public const float Fov = MathF.PI / 3f;

    private static readonly Lazy<string> RepoRoot = new(() =>
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Tertius.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Cannot find the repository root.");
    });

    /// <summary>The folder that holds Tertius.slnx.</summary>
    public static string RepositoryRoot => RepoRoot.Value;

    /// <summary>
    /// A validating headless renderer. Impostors are off unless asked for, so that the geometry, lighting and shadow
    /// tests see every object as geometry; the impostor tests turn them on with a small atlas. Ambient occlusion is off
    /// unless asked for, because the zero case of the setting is off. The exposure is fixed, at 0 stops unless asked for,
    /// so that a pixel does not depend on the frames before it. Anti-aliasing is off unless asked for, so that the pixel
    /// tests keep their values (docs/plans/anti-aliasing.md, "Decisions"). Reflections are off unless asked for, because
    /// the zero case of the setting is off (docs/plans/screen-space-reflections.md, "Existing tests"). The environment map
    /// is off unless asked for, so the hemisphere lights the tests. Local shadows are off, as the impostors are
    /// (docs/plans/local-light-shadows.md, "Decisions"). One far light has a cascade set unless asked for, the renderer's
    /// default.
    /// </summary>
    public static VulkanRenderer Headless(
        int width = 640,
        int height = 360,
        int framesInFlight = 2,
        bool impostors = false,
        int impostorTiles = 16,
        AmbientOcclusionSetting ambientOcclusion = default,
        ExposureSetting exposure = default,
        AntiAliasing antiAliasing = AntiAliasing.None,
        ReflectionSetting reflections = default,
        EnvironmentMapSetting? environmentMap = null,
        int cascadedLights = 1
    ) =>
        VulkanRenderer.CreateHeadless(
            new()
            {
                Validation = true,
                Width = width,
                Height = height,
                FramesInFlight = framesInFlight,
                Impostors = impostors,
                ImpostorTiles = impostorTiles,
                AmbientOcclusion = ambientOcclusion,
                Exposure = exposure,
                Bloom = new NoBloom(),
                AntiAliasing = antiAliasing,
                LocalShadows = new NoLocalShadows(),
                Reflections = reflections,
                EnvironmentMap = environmentMap ?? new NoEnvironmentMap(),
                CascadedLights = cascadedLights,
            }
        );

    public static string CapturePath(string name) => Path.Combine(RepoRoot.Value, "captures", name + ".png");

    /// <summary>Renders one frame and captures it into memory. <paramref name="name"/> names the capture in a failure.</summary>
    public static Capture RenderAndCapture(
        VulkanRenderer renderer,
        RenderSnapshot snapshot,
        string name,
        float alpha = 1f
    )
    {
        var sink = new MemorySink();
        renderer.CaptureNextFrame(sink);
        renderer.Render(snapshot, alpha);
        sink.Takes.Should().Be(1, "the capture of {0} must reach its sink before Render returns", name);
        return sink.Image!;
    }

    /// <summary>Disposes the renderer and fails with the messages if validation reported anything.</summary>
    public static void DisposeAndAssertClean(VulkanRenderer renderer)
    {
        renderer.Dispose();
        AssertClean(renderer);
    }

    public static void AssertClean(VulkanRenderer renderer) =>
        renderer
            .ValidationMessages.Should()
            .BeEmpty("the Vulkan validation layers and the renderer's own checks must stay silent");

    /// <summary>The pixel of a camera-relative point for a camera with identity rotation (looking down −Z).</summary>
    public static Vector2 Project(Vector3 point, int width, int height, float fov = Fov)
    {
        float tanY = MathF.Tan(fov / 2);
        float tanX = tanY * width / height;
        float depth = -point.Z;
        float ndcX = point.X / depth / tanX;
        float ndcY = point.Y / depth / tanY;
        return new((ndcX * 0.5f + 0.5f) * width, (0.5f - ndcY * 0.5f) * height);
    }

    /// <summary>The pixel of a camera-relative point for a camera with any rotation and vertical field of view.</summary>
    public static Vector2 Project(Vector3 point, Quaternion cameraRotation, int width, int height, float fov = Fov) =>
        Project(
            point: Vector3.Transform(point, Quaternion.Conjugate(cameraRotation)),
            width: width,
            height: height,
            fov: fov
        );

    /// <summary>A camera turned down by <paramref name="pitch"/> radians from looking along −Z.</summary>
    public static Quaternion PitchDown(float pitch) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, -pitch);

    /// <summary>View depth (distance along the view axis) of a camera-relative point.</summary>
    public static float ViewDepth(Vector3 point, Quaternion cameraRotation) =>
        Vector3.Dot(point, Vector3.Transform(-Vector3.UnitZ, cameraRotation));

    /// <summary>
    /// A still, visible instance: <see cref="RenderInstance.Create"/> with <see cref="RenderFlags.Visible"/> always
    /// added. Its hemisphere ambient light has up along +Y: the test scenes stand on level ground in the camera's frame.
    /// Tests that need an instance that is not visible call <see cref="RenderInstance.Create"/> themselves.
    /// </summary>
    public static RenderInstance Instance(
        MeshId mesh,
        MaterialId material,
        Vector3 position,
        Vector3 scale,
        Quaternion? rotation = null,
        RenderFlags flags = RenderFlags.None,
        float radius = 0,
        uint entity = 0,
        ushort part = 0
    ) =>
        RenderInstance.Create(
            mesh: mesh,
            material: material,
            position: position,
            rotation: rotation ?? Quaternion.Identity,
            scale: scale,
            boundingRadius: radius,
            hemisphere: HemisphereUp.Along(Vector3.UnitY),
            flags: flags | RenderFlags.Visible,
            entityIndex: entity,
            part: part
        );

    /// <summary>How far the test star of <see cref="Snapshot"/> stands from the camera, in m: about 1 AU.</summary>
    public const double StarDistance = 1.5e11;

    /// <summary>The angular radius of the test star, in rad: about that of the Sun seen from Earth.</summary>
    public const float StarAngularRadius = 0.00467f;

    /// <summary>The color of the test star, which scales its power per channel.</summary>
    public static readonly Vector3 StarColor = new(1f, 0.96f, 0.9f);

    /// <summary>
    /// A snapshot with the camera at the origin, the test ambient light, and one light: the test star, a point light
    /// <see cref="StarDistance"/> along <paramref name="starDirection"/> that casts shadows, of the color
    /// <see cref="StarColor"/>, whose power gives the irradiance <paramref name="starIrradiance"/> per unit of color at the
    /// camera (<see cref="Star(RenderSnapshot)"/>).
    /// </summary>
    public static RenderSnapshot Snapshot(Vector3 starDirection, float starIrradiance = 3f)
    {
        var snapshot = new RenderSnapshot
        {
            Camera = new()
            {
                Rotation = Quaternion.Identity,
                PreviousRotation = Quaternion.Identity,
                VerticalFov = Fov,
            },
            Ambient = new()
            {
                SkyColor = new(0.09f, 0.11f, 0.15f),
                GroundColor = new(0.05f, 0.045f, 0.04f),
                Intensity = 1f,
            },
        };
        snapshot.Lights.Add(
            StarLight(
                direction: starDirection,
                irradiance: starIrradiance,
                angularRadius: StarAngularRadius,
                color: StarColor
            )
        );
        return snapshot;
    }

    /// <summary>
    /// A star <see cref="StarDistance"/> from the camera along <paramref name="direction"/>, that casts shadows: the power
    /// that gives the irradiance <paramref name="irradiance"/> per unit of <paramref name="color"/> at the camera,
    /// 4π d² E, and the radius whose sphere covers <paramref name="angularRadius"/> there, d sin α.
    /// </summary>
    public static PointRenderLight StarLight(Vector3 direction, float irradiance, float angularRadius, Vector3 color)
    {
        Vector3 position = Vector3.Normalize(direction) * (float)StarDistance;
        return new(
            EntityIndex: 0,
            Position: position,
            PreviousPosition: position,
            Color: color,
            Power: (float)(4 * Math.PI * StarDistance * StarDistance * irradiance),
            Radius: (float)(StarDistance * Math.Sin(angularRadius)),
            CastsShadow: true
        );
    }

    /// <summary>
    /// A lamp of radius 0 at <paramref name="position"/> that casts no shadow, with the radiant intensity
    /// <paramref name="intensity"/>, in W/sr per unit of <paramref name="color"/>: its power is 4π I.
    /// </summary>
    public static PointRenderLight Lamp(Vector3 position, Vector3 color, float intensity) =>
        new(
            EntityIndex: 0,
            Position: position,
            PreviousPosition: position,
            Color: color,
            Power: (float)(4 * Math.PI * intensity),
            Radius: 0,
            CastsShadow: false
        );

    /// <summary>
    /// A spot light of radius 0 at <paramref name="position"/> along <paramref name="direction"/> that casts no shadow,
    /// with the radiant intensity <paramref name="intensity"/> inside its cone, in W/sr per unit of
    /// <paramref name="color"/>: its power is I · 2π (1 − cos θo).
    /// </summary>
    public static SpotRenderLight Spot(
        Vector3 position,
        Vector3 direction,
        Vector3 color,
        float intensity,
        float innerConeAngle,
        float outerConeAngle
    )
    {
        double sine = Math.Sin(outerConeAngle / 2.0);
        return new(
            EntityIndex: 0,
            Position: position,
            PreviousPosition: position,
            Direction: direction,
            PreviousDirection: direction,
            Color: color,
            Power: (float)(intensity * 4 * Math.PI * sine * sine),
            Radius: 0,
            CastsShadow: false,
            InnerConeAngle: innerConeAngle,
            OuterConeAngle: outerConeAngle
        );
    }

    /// <summary>The test star of a snapshot that <see cref="Snapshot"/> made: its first light.</summary>
    public static PointRenderLight Star(RenderSnapshot snapshot) =>
        snapshot.Lights[0].TryGetValue(out PointRenderLight star)
            ? star
            : throw new InvalidOperationException("The snapshot's first light is not the test star.");

    /// <summary>
    /// Changes the test star of <paramref name="snapshot"/>: its irradiance at the camera per unit of color, its angular
    /// radius, its color or its direction. Each one left out stays as it is.
    /// </summary>
    public static void Star(
        RenderSnapshot snapshot,
        float? irradiance = null,
        float? angularRadius = null,
        Vector3? color = null,
        Vector3? direction = null
    )
    {
        PointRenderLight star = Star(snapshot);
        snapshot.Lights[0] = StarLight(
            direction: direction ?? star.Position,
            irradiance: irradiance ?? StarIrradiance(snapshot),
            angularRadius: angularRadius ?? (float)Math.Asin(star.Radius / StarDistance),
            color: color ?? star.Color
        );
    }

    /// <summary>The irradiance of the test star at the camera per unit of its color, P / (4π d²).</summary>
    public static float StarIrradiance(RenderSnapshot snapshot) =>
        (float)(Star(snapshot).Power / (4 * Math.PI * StarDistance * StarDistance));

    /// <summary>The irradiance of the test star at the camera per channel: its color times <see cref="StarIrradiance"/>.</summary>
    public static Vector3 StarIrradianceRgb(RenderSnapshot snapshot) => Star(snapshot).Color * StarIrradiance(snapshot);

    /// <summary>The unit direction from the camera toward the test star.</summary>
    public static Vector3 StarDirection(RenderSnapshot snapshot) => Vector3.Normalize(Star(snapshot).Position);
}

/// <summary>
/// The pixels of one capture from an in-memory sink, RGBA8, top row first. The fourth byte of each pixel holds no
/// defined value.
/// </summary>
internal sealed class Capture(int width, int height, byte[] rgba)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public byte[] Rgba { get; } = rgba;

    public static Capture Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        var image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new(image.Width, image.Height, image.Data);
    }

    /// <summary>An image made in memory: <paramref name="rgba"/> holds 4 bytes per pixel, top row first.</summary>
    public static Capture FromRgba(int width, int height, byte[] rgba)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        return rgba.Length != width * height * 4
            ? throw new ArgumentException(
                $"A {width}×{height} image needs {width * height * 4} bytes, not {rgba.Length}.",
                nameof(rgba)
            )
            : new(width, height, rgba);
    }

    /// <summary>Writes the image as a PNG file, replacing any file at <paramref name="path"/>.</summary>
    public void Save(string path)
    {
        using FileStream stream = File.Create(path);
        new ImageWriter().WritePng(
            data: Rgba,
            width: Width,
            height: Height,
            components: StbImageWriteSharp.ColorComponents.RedGreenBlueAlpha,
            dest: stream
        );
    }

    /// <summary>Color in [0, 1] (display-encoded, as stored).</summary>
    public Vector3 Color(int x, int y)
    {
        x = Math.Clamp(x, 0, Width - 1);
        y = Math.Clamp(y, 0, Height - 1);
        int i = (y * Width + x) * 4;
        return new Vector3(Rgba[i], Rgba[i + 1], Rgba[i + 2]) / 255f;
    }

    public Vector3 Color(Vector2 pixel) => Color((int)pixel.X, (int)pixel.Y);

    /// <summary>Rec. 709 luma of the stored values.</summary>
    public float Luma(int x, int y)
    {
        Vector3 c = Color(x, y);
        return 0.2126f * c.X + 0.7152f * c.Y + 0.0722f * c.Z;
    }

    public float Luma(Vector2 pixel) => Luma((int)pixel.X, (int)pixel.Y);

    /// <summary>The mean luma of a square window around a pixel.</summary>
    public float MeanLuma(Vector2 center, int radius)
    {
        float sum = 0;
        int count = 0;
        for (int y = (int)center.Y - radius; y <= (int)center.Y + radius; y++)
        for (int x = (int)center.X - radius; x <= (int)center.X + radius; x++)
        {
            sum += Luma(x, y);
            count++;
        }

        return sum / count;
    }

    public float MaxLuma(Vector2 center, int radius)
    {
        float max = 0;
        for (int y = (int)center.Y - radius; y <= (int)center.Y + radius; y++)
        for (int x = (int)center.X - radius; x <= (int)center.X + radius; x++)
            max = MathF.Max(max, Luma(x, y));
        return max;
    }
}

/// <summary>
/// A capture sink that keeps a copy of the pixels it takes, how many times it took pixels, and on which thread it last
/// took them.
/// </summary>
internal sealed class MemorySink : ICaptureSink
{
    public int Takes { get; private set; }
    public int ThreadId { get; private set; }
    public Capture? Image { get; private set; }

    public void Take(in CapturedImage image)
    {
        Takes++;
        ThreadId = Environment.CurrentManagedThreadId;
        Image = new(image.Width, image.Height, image.Rgba.ToArray());
    }
}

/// <summary>A capture sink that hands each capture to every sink it holds, in order.</summary>
internal sealed class FanOutSink(params ICaptureSink[] sinks) : ICaptureSink
{
    public void Take(in CapturedImage image)
    {
        foreach (ICaptureSink sink in sinks)
            sink.Take(image);
    }
}

/// <summary>Measurements on captures.</summary>
internal static class Measure
{
    /// <summary>The longest run of pixels brighter than <paramref name="threshold"/> along a row or a column. Ignores stars.</summary>
    public static int LongestRun(Capture image, int line, bool horizontal, float threshold = 0.1f)
    {
        int length = horizontal ? image.Width : image.Height;
        int best = 0,
            run = 0;
        for (int i = 0; i < length; i++)
        {
            float luma = horizontal ? image.Luma(i, line) : image.Luma(line, i);
            run = luma > threshold ? run + 1 : 0;
            best = Math.Max(best, run);
        }

        return best;
    }

    /// <summary>Converts display-encoded sRGB to linear light.</summary>
    public static Vector3 ToLinear(Vector3 srgb)
    {
        static float Channel(float c) => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        return new(Channel(srgb.X), Channel(srgb.Y), Channel(srgb.Z));
    }
}

/// <summary>Procedural textures for tests.</summary>
internal static class TestTextures
{
    /// <summary>A checker of <paramref name="tiles"/> × <paramref name="tiles"/> squares in two colors.</summary>
    public static TextureData Checker(int size, int tiles, (byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        byte[] rgba = new byte[size * size * 4];
        int tile = size / tiles;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            (byte r, byte g, byte bl) = (x / tile + y / tile) % 2 == 0 ? a : b;
            int i = (y * size + x) * 4;
            rgba[i] = r;
            rgba[i + 1] = g;
            rgba[i + 2] = bl;
            rgba[i + 3] = 255;
        }

        return new(Width: size, Height: size, Rgba: rgba, Srgb: true);
    }

    /// <summary>
    /// A tangent-space normal map of raised tiles with beveled edges, aligned with <see cref="Checker"/>. Green is
    /// +bitangent, which points up the image (toward row 0).
    /// </summary>
    public static TextureData BeveledTiles(int size, int tiles, float bevel = 0.18f, float slope = 1.2f)
    {
        byte[] rgba = new byte[size * size * 4];
        int tile = size / tiles;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = (x % tile + 0.5f) / tile;
            float v = (y % tile + 0.5f) / tile;
            float nx = 0;
            if (u < bevel)
                nx = -slope;
            else if (u > 1 - bevel)
                nx = slope;
            // Near the top of a tile (small v) the bevel faces up the image: +bitangent.
            float ny = 0;
            if (v < bevel)
                ny = slope;
            else if (v > 1 - bevel)
                ny = -slope;
            var n = Vector3.Normalize(new(nx, ny, 1));
            int i = (y * size + x) * 4;
            rgba[i] = (byte)MathF.Round((n.X * 0.5f + 0.5f) * 255);
            rgba[i + 1] = (byte)MathF.Round((n.Y * 0.5f + 0.5f) * 255);
            rgba[i + 2] = (byte)MathF.Round((n.Z * 0.5f + 0.5f) * 255);
            rgba[i + 3] = 255;
        }

        return new(Width: size, Height: size, Rgba: rgba, Srgb: false);
    }
}

/// <summary>Scene batchers for the CPU tests.</summary>
internal static class Batchers
{
    /// <summary>A batcher with the exposure and the cascaded light budget of a renderer made with default options.</summary>
    public static SceneBatcher Default()
    {
        var options = new RendererOptions();
        return new(exposure: options.Exposure, cascadedLights: options.CascadedLights);
    }
}
