using System.Numerics;
using StbImageSharp;
using Tertius.Rendering;

namespace Tertius.Rendering.Tests;

/// <summary>GPU tests run one at a time: they share one physical device and the captures folder.</summary>
[CollectionDefinition(Name)]
public sealed class GpuCollection
{
    public const string Name = "GPU";
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

    /// <summary>
    /// A validating headless renderer. Impostors are off unless asked for, so that the geometry, lighting and shadow
    /// tests see every object as geometry; the impostor tests turn them on with a small atlas.
    /// </summary>
    public static VulkanRenderer Headless(int width = 640, int height = 360, int framesInFlight = 2, bool impostors = false, int impostorTiles = 16) =>
        VulkanRenderer.CreateHeadless(new RendererOptions
        {
            Validation = true,
            Width = width,
            Height = height,
            FramesInFlight = framesInFlight,
            Impostors = impostors,
            ImpostorTiles = impostorTiles,
        });

    public static string CapturePath(string name) => Path.Combine(RepoRoot.Value, "captures", name + ".png");

    /// <summary>Renders one frame, captures it to captures/<paramref name="name"/>.png, and loads the PNG back.</summary>
    public static Capture RenderAndCapture(VulkanRenderer renderer, RenderSnapshot snapshot, string name, float alpha = 1f)
    {
        string path = CapturePath(name);
        if (File.Exists(path))
            File.Delete(path);
        renderer.CaptureNextFrame(path);
        renderer.Render(snapshot, alpha);
        Assert.True(File.Exists(path), $"{path} was not written.");
        return Capture.Load(path);
    }

    /// <summary>Disposes the renderer and fails with the messages if validation reported anything.</summary>
    public static void DisposeAndAssertClean(VulkanRenderer renderer)
    {
        renderer.Dispose();
        AssertClean(renderer);
    }

    public static void AssertClean(VulkanRenderer renderer)
    {
        IReadOnlyList<string> messages = renderer.ValidationMessages;
        Assert.True(messages.Count == 0, "Validation messages:\n" + string.Join("\n", messages));
    }

    /// <summary>The pixel of a camera-relative point for a camera with identity rotation (looking down −Z).</summary>
    public static Vector2 Project(Vector3 point, int width, int height, float fov = Fov)
    {
        float tanY = MathF.Tan(fov / 2);
        float tanX = tanY * width / height;
        float depth = -point.Z;
        float ndcX = point.X / depth / tanX;
        float ndcY = point.Y / depth / tanY;
        return new Vector2((ndcX * 0.5f + 0.5f) * width, (0.5f - ndcY * 0.5f) * height);
    }

    /// <summary>The pixel of a camera-relative point for a camera with any rotation and vertical field of view.</summary>
    public static Vector2 Project(Vector3 point, Quaternion cameraRotation, int width, int height, float fov = Fov) =>
        Project(Vector3.Transform(point, Quaternion.Conjugate(cameraRotation)), width, height, fov);

    /// <summary>A camera turned down by <paramref name="pitch"/> radians from looking along −Z.</summary>
    public static Quaternion PitchDown(float pitch) => Quaternion.CreateFromAxisAngle(Vector3.UnitX, -pitch);

    /// <summary>View depth (distance along the view axis) of a camera-relative point.</summary>
    public static float ViewDepth(Vector3 point, Quaternion cameraRotation) =>
        Vector3.Dot(point, Vector3.Transform(-Vector3.UnitZ, cameraRotation));

    public static RenderInstance Instance(MeshId mesh, MaterialId material, Vector3 position, Vector3 scale,
        Quaternion? rotation = null, RenderFlags flags = RenderFlags.None, float radius = 0, uint entity = 0)
    {
        Quaternion r = rotation ?? Quaternion.Identity;
        return new RenderInstance
        {
            EntityIndex = entity,
            Mesh = mesh,
            Material = material,
            Flags = flags | RenderFlags.Visible,
            Position = position,
            PreviousPosition = position,
            Rotation = r,
            PreviousRotation = r,
            Scale = scale,
            BoundingRadius = radius,
        };
    }

    public static RenderSnapshot Snapshot(Vector3 sunDirection, float sunIntensity = 3f)
    {
        var snapshot = new RenderSnapshot
        {
            Camera = new RenderCamera { Rotation = Quaternion.Identity, PreviousRotation = Quaternion.Identity, VerticalFov = Fov },
            Sun = new RenderSun { Direction = Vector3.Normalize(sunDirection), Color = new Vector3(1f, 0.96f, 0.9f), Intensity = sunIntensity, AngularRadius = 0.00467f },
            Ambient = new AmbientLight { SkyColor = new Vector3(0.09f, 0.11f, 0.15f), GroundColor = new Vector3(0.05f, 0.045f, 0.04f), Intensity = 1f },
        };
        return snapshot;
    }
}

/// <summary>A PNG loaded back from disk, RGBA8, top row first.</summary>
internal sealed class Capture
{
    private Capture(int width, int height, byte[] rgba)
    {
        Width = width;
        Height = height;
        Rgba = rgba;
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }

    public static Capture Load(string path)
    {
        using FileStream stream = File.OpenRead(path);
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);
        return new Capture(image.Width, image.Height, image.Data);
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
        {
            for (int x = (int)center.X - radius; x <= (int)center.X + radius; x++)
            {
                sum += Luma(x, y);
                count++;
            }
        }
        return sum / count;
    }

    public float MaxLuma(Vector2 center, int radius)
    {
        float max = 0;
        for (int y = (int)center.Y - radius; y <= (int)center.Y + radius; y++)
        {
            for (int x = (int)center.X - radius; x <= (int)center.X + radius; x++)
                max = MathF.Max(max, Luma(x, y));
        }
        return max;
    }
}

/// <summary>Measurements on captures.</summary>
internal static class Measure
{
    /// <summary>The longest run of pixels brighter than <paramref name="threshold"/> along a row or a column. Ignores stars.</summary>
    public static int LongestRun(Capture image, int line, bool horizontal, float threshold = 0.1f)
    {
        int length = horizontal ? image.Width : image.Height;
        int best = 0, run = 0;
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
        return new Vector3(Channel(srgb.X), Channel(srgb.Y), Channel(srgb.Z));
    }
}

/// <summary>Procedural textures for tests.</summary>
internal static class TestTextures
{
    /// <summary>A checker of <paramref name="tiles"/> × <paramref name="tiles"/> squares in two colors.</summary>
    public static TextureData Checker(int size, int tiles, (byte R, byte G, byte B) a, (byte R, byte G, byte B) b)
    {
        var rgba = new byte[size * size * 4];
        int tile = size / tiles;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                (byte r, byte g, byte bl) = ((x / tile) + (y / tile)) % 2 == 0 ? a : b;
                int i = (y * size + x) * 4;
                rgba[i] = r;
                rgba[i + 1] = g;
                rgba[i + 2] = bl;
                rgba[i + 3] = 255;
            }
        }
        return new TextureData(size, size, rgba, Srgb: true);
    }

    /// <summary>
    /// A tangent-space normal map of raised tiles with beveled edges, aligned with <see cref="Checker"/>. Green is
    /// +bitangent, which points up the image (toward row 0).
    /// </summary>
    public static TextureData BeveledTiles(int size, int tiles, float bevel = 0.18f, float slope = 1.2f)
    {
        var rgba = new byte[size * size * 4];
        int tile = size / tiles;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float u = (x % tile + 0.5f) / tile;
                float v = (y % tile + 0.5f) / tile;
                float nx = u < bevel ? -slope : u > 1 - bevel ? slope : 0;
                // Near the top of a tile (small v) the bevel faces up the image: +bitangent.
                float ny = v < bevel ? slope : v > 1 - bevel ? -slope : 0;
                Vector3 n = Vector3.Normalize(new Vector3(nx, ny, 1));
                int i = (y * size + x) * 4;
                rgba[i] = (byte)MathF.Round((n.X * 0.5f + 0.5f) * 255);
                rgba[i + 1] = (byte)MathF.Round((n.Y * 0.5f + 0.5f) * 255);
                rgba[i + 2] = (byte)MathF.Round((n.Z * 0.5f + 0.5f) * 255);
                rgba[i + 3] = 255;
            }
        }
        return new TextureData(size, size, rgba, Srgb: false);
    }
}
