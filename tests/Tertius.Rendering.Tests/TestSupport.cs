using System.Numerics;
using StbImageSharp;
using StbImageWriteSharp;
using Tertius.Testing;
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
    /// A validating headless renderer. Impostors are off unless asked for, so that the geometry, lighting and shadow tests
    /// see every object as geometry; the impostor tests turn them on with a small atlas. The exposure is fixed, at 0 stops
    /// unless asked for, so that a pixel does not depend on the frames before it. Anti-aliasing is off unless asked for, so
    /// that the pixel tests keep their native values (docs/plans/temporal-renderer.md, section 4). The screen tracer is off unless
    /// asked for, because the zero case of the setting is off (docs/plans/temporal-renderer.md, section 8). The environment map
    /// is off unless asked for, so the hemisphere lights the tests. Local shadows are off unless asked for, as the
    /// impostors are (docs/plans/local-light-shadows.md, "Decisions"). One far light has a cascade set unless asked for,
    /// the renderer's default. The deformation is on unless asked for, the renderer's default. The skin scatter is off
    /// unless asked for, because the zero case of the setting is off (docs/plans/skin-and-hair.md, decision 29).
    /// </summary>
    public static VulkanRenderer Headless(
        int width = 640,
        int height = 360,
        int framesInFlight = 2,
        bool impostors = false,
        int impostorTiles = 16,
        ExposureSetting exposure = default,
        UpscalingSetting upscaling = default,
        EnvironmentMapSetting? environmentMap = null,
        int cascadedLights = 1,
        LocalShadowSetting localShadows = default,
        bool deformation = true,
        SubsurfaceSetting subsurface = default,
        bool capturesInFlight = false,
        ScreenTraceSetting screenTrace = default,
        int warmUpFrames = RendererOptions.DefaultWarmUpFrames,
        bool passTimestamps = true,
        LightPairSetting lightPairs = default
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
                Exposure = exposure,
                Bloom = new NoBloom(),
                Upscaling = upscaling,
                LocalShadows = localShadows,
                EnvironmentMap = environmentMap ?? new NoEnvironmentMap(),
                CascadedLights = cascadedLights,
                Deformation = deformation,
                Subsurface = subsurface,
                CapturesInFlight = capturesInFlight,
                ScreenTrace = screenTrace,
                WarmUpFrames = warmUpFrames,
                PassTimestamps = passTimestamps,
                LightPairs = lightPairs,
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

    /// <summary>
    /// The world side of a texel of the sun's first cascade for a view of <paramref name="width"/> by
    /// <paramref name="height"/> pixels at <see cref="Fov"/>: the texel that <see cref="ShadowCascades.Fit"/> gives the
    /// slice from the near plane to the first split.
    /// </summary>
    public static float FirstCascadeTexel(int width, int height)
    {
        float tanHalfY = MathF.Tan(Fov / 2);
        float radius = ShadowCascades
            .BoundingSphere(
                near: DepthRanges.NearPlane,
                far: ShadowCascades.Split(0),
                tanHalfX: tanHalfY * width / height,
                tanHalfY: tanHalfY
            )
            .Radius;
        return 2 * radius / (ShadowMap.DefaultSize - 2 * ShadowCascades.MarginTexels);
    }

    /// <summary>View depth (distance along the view axis) of a camera-relative point.</summary>
    public static float ViewDepth(Vector3 point, Quaternion cameraRotation) =>
        Vector3.Dot(point, Vector3.Transform(-Vector3.UnitZ, cameraRotation));

    /// <summary>
    /// A still, visible instance: <see cref="RenderInstance.Create"/> with <see cref="RenderFlags.Visible"/> always
    /// added. Its hemisphere ambient light has up along +Y: the test scenes stand on level ground in the camera's frame.
    /// <paramref name="chainDepth"/> is the depth of the instance's frame in the camera's chain, named by every test
    /// (<see cref="TestChains.CamerasFrame"/> for an instance in the camera's frame).
    /// Tests that need an instance that is not visible call <see cref="RenderInstance.Create"/> themselves.
    /// </summary>
    public static RenderInstance Instance(
        MeshId mesh,
        MaterialId material,
        Vector3 position,
        Vector3 scale,
        ushort chainDepth,
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
            chainDepth: chainDepth,
            flags: flags | RenderFlags.Visible,
            entityIndex: entity,
            part: part
        );

    /// <summary>How far the test star of <see cref="Snapshot"/> stands from the camera, in m: about 1 AU.</summary>
    public const double StarDistance = 1.5e11;

    /// <summary>The angular radius of the test star, in rad: about that of the Sun seen from Earth.</summary>
    public const float StarAngularRadius = 0.00467f;

    /// <summary>
    /// sin ϕ of the test star's angular radius ϕ: the lightSine of MaterialSample.Reflected at a point that the test
    /// star lights, which the GPU shades as a sphere light of that size.
    /// </summary>
    public static readonly double StarSine = Math.Sin(StarAngularRadius);

    /// <summary>
    /// The entity of the test star, which no test instance holds: a glow of an instance's entity bounces only when no
    /// light carries that entity (docs/plans/screen-space-illumination.md, "The bounce source").
    /// </summary>
    public const uint StarEntity = 1_000_000;

    /// <summary>
    /// The entity of the test lamps and spot lights, which no test instance holds, for the reason of
    /// <see cref="StarEntity"/>: a default instance near a test lamp glows as an instance of a real scene does.
    /// </summary>
    public const uint LampEntity = 1_000_001;

    /// <summary>The color of the test star, which scales its power per channel.</summary>
    public static readonly Vector3 StarColor = new(1f, 0.96f, 0.9f);

    /// <summary>
    /// A snapshot with the camera at the origin, the test ambient light, and one light: the test star, a point light
    /// <see cref="StarDistance"/> along <paramref name="starDirection"/> in the root (<see cref="TestChains.Root"/>, so
    /// every map of the chain that a test gives the snapshot keeps it) that casts shadows, of the color
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
                color: StarColor,
                chainDepth: TestChains.Root
            )
        );
        return snapshot;
    }

    /// <summary>
    /// A star <see cref="StarDistance"/> from the camera along <paramref name="direction"/>, that casts shadows: the power
    /// that gives the irradiance <paramref name="irradiance"/> per unit of <paramref name="color"/> at the camera,
    /// 4π d² E, and the radius whose sphere covers <paramref name="angularRadius"/> there, d sin α. Its carrier is at
    /// <paramref name="chainDepth"/>, which is <see cref="TestChains.Root"/> for a star of the root, as a real star is.
    /// </summary>
    public static PointRenderLight StarLight(
        Vector3 direction,
        float irradiance,
        float angularRadius,
        Vector3 color,
        ushort chainDepth
    )
    {
        Vector3 position = Vector3.Normalize(direction) * (float)StarDistance;
        return new(
            EntityIndex: StarEntity,
            EntityGeneration: 0,
            Position: position,
            PreviousPosition: position,
            Color: color,
            Power: (float)(4 * Math.PI * StarDistance * StarDistance * irradiance),
            Radius: (float)(StarDistance * Math.Sin(angularRadius)),
            CastsShadow: true,
            ChainDepth: chainDepth
        );
    }

    /// <summary>
    /// A lamp of radius 0 at <paramref name="position"/> that casts no shadow, with the radiant intensity
    /// <paramref name="intensity"/>, in W/sr per unit of <paramref name="color"/>: its power is 4π I. Its carrier is at
    /// <paramref name="chainDepth"/>.
    /// </summary>
    public static PointRenderLight Lamp(Vector3 position, Vector3 color, float intensity, ushort chainDepth) =>
        new(
            EntityIndex: LampEntity,
            EntityGeneration: 0,
            Position: position,
            PreviousPosition: position,
            Color: color,
            Power: (float)(4 * Math.PI * intensity),
            Radius: 0,
            CastsShadow: false,
            ChainDepth: chainDepth
        );

    /// <summary>
    /// A spot light of radius 0 at <paramref name="position"/> along <paramref name="direction"/> that casts no shadow,
    /// with the radiant intensity <paramref name="intensity"/> inside its cone, in W/sr per unit of
    /// <paramref name="color"/>: its power is I · 2π (1 − cos θo). Its carrier is at <paramref name="chainDepth"/>.
    /// </summary>
    public static SpotRenderLight Spot(
        Vector3 position,
        Vector3 direction,
        Vector3 color,
        float intensity,
        float innerConeAngle,
        float outerConeAngle,
        ushort chainDepth
    )
    {
        double sine = Math.Sin(outerConeAngle / 2.0);
        return new(
            EntityIndex: LampEntity,
            EntityGeneration: 0,
            Position: position,
            PreviousPosition: position,
            Direction: direction,
            PreviousDirection: direction,
            Color: color,
            Power: (float)(intensity * 4 * Math.PI * sine * sine),
            Radius: 0,
            CastsShadow: false,
            InnerConeAngle: innerConeAngle,
            OuterConeAngle: outerConeAngle,
            ChainDepth: chainDepth
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
            color: color ?? star.Color,
            chainDepth: star.ChainDepth
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
    /// <summary>
    /// The highest exposure, in stops, that the CPU tests prepare a frame for, unless a test names its own: the default
    /// setting's highest stops, where every light reaches farthest.
    /// </summary>
    public const float HighestStops = 10;

    /// <summary>A batcher with the cascaded light budget of a renderer made with default options.</summary>
    public static SceneBatcher Default() => WithCascadedLights(new RendererOptions().CascadedLights);

    /// <summary>
    /// A batcher with <paramref name="cascadedLights"/> cascade sets, whose boxes are those of the default shadow map on a
    /// device that filters D32 depth.
    /// </summary>
    public static SceneBatcher WithCascadedLights(int cascadedLights) =>
        new(cascadedLights: cascadedLights, shadowMapSize: ShadowMap.DefaultSize, depthRangeScale: 16f);

    /// <summary>
    /// The pending state of the first draw of a renderer, which continues no drawn frame: the frame of a CPU test that
    /// prepares one frame.
    /// </summary>
    public static FrameHistory.PendingFrame FirstFrame() =>
        new FrameHistory(StillRule.Window).Begin(
            step: FrameHistoryTesting.FilmStep,
            draw: 0,
            snapshot: new RenderSnapshot(),
            outputSize: new Silk.NET.Vulkan.Extent2D(1600, 900),
            renderSize: new Silk.NET.Vulkan.Extent2D(1600, 900)
        );
}

/// <summary>The material slots that the CPU tests hand the batcher, as a renderer's material store holds them.</summary>
internal static class MaterialSlots
{
    /// <summary>The cutout material of the CPU tests, with a cutoff of 0.5: slot 5 of <see cref="Of"/>.</summary>
    public static readonly MaterialId Cutout = new(4);

    /// <summary>The fringed material of the CPU tests, with a cutoff of 0.5: slot 6 of <see cref="Of"/>.</summary>
    public static readonly MaterialId Fringed = new(5);

    /// <summary>The glass material of the CPU tests: slot 7 of <see cref="Of"/>.</summary>
    public static readonly MaterialId Glass = new(6);

    /// <summary>The opaque hair material of the CPU tests, two-sided: slot 8 of <see cref="Of"/>.</summary>
    public static readonly MaterialId Hair = new(7);

    /// <summary>The fringed hair material of the CPU tests, two-sided, with a cutoff of 0.5: slot 9 of <see cref="Of"/>.</summary>
    public static readonly MaterialId FringedHair = new(8);

    /// <summary>The glass hair material of the CPU tests, two-sided: slot 10 of <see cref="Of"/>.</summary>
    public static readonly MaterialId GlassHair = new(9);

    /// <summary>
    /// The infos of <paramref name="count"/> slots, the default material's first: every slot opaque and one-sided but those
    /// of <see cref="Cutout"/>, <see cref="Fringed"/>, <see cref="Glass"/>, <see cref="Hair"/>, <see cref="FringedHair"/>
    /// and <see cref="GlassHair"/>, when there are such slots.
    /// </summary>
    public static MaterialInfo[] Of(int count = 8)
    {
        var slots = new MaterialInfo[count];
        Set(slots, Cutout, new(Alpha: new CutoutAlpha(Cutoff: 0.5f), TwoSided: false));
        Set(slots, Fringed, new(Alpha: new FringedAlpha(Cutoff: 0.5f), TwoSided: false));
        Set(slots, Glass, new(Alpha: new GlassAlpha(), TwoSided: false));
        Set(slots, Hair, new(Alpha: new OpaqueAlpha(), TwoSided: true));
        Set(slots, FringedHair, new(Alpha: new FringedAlpha(Cutoff: 0.5f), TwoSided: true));
        Set(slots, GlassHair, new(Alpha: new GlassAlpha(), TwoSided: true));
        return slots;
    }

    private static void Set(MaterialInfo[] slots, MaterialId material, MaterialInfo info)
    {
        uint slot = MaterialStore.Slot(material);
        if (slot < slots.Length)
            slots[slot] = info;
    }
}

/// <summary>Materials of the C# twins of the shaders.</summary>
internal static class TestMaterials
{
    /// <summary>
    /// The C# sample of <paramref name="desc"/> without maps: its base color and factors, with the metallic of a standard
    /// model and 0 for any other, as the material buffer holds it.
    /// </summary>
    public static MaterialSample SampleOf(MaterialDesc desc) =>
        new(
            BaseColor: new(desc.BaseColor.X, desc.BaseColor.Y, desc.BaseColor.Z),
            Metallic: desc.Model is StandardShading(var metallic) ? metallic : 0,
            Roughness: desc.Roughness,
            Reflectance: desc.Reflectance
        );
}
