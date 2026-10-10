using System.Numerics;

namespace Tertius;

public readonly record struct MeshId(int Value)
{
    public static MeshId None => new(-1);
    public bool IsNone => Value < 0;
}

public readonly record struct MaterialId(int Value)
{
    public static MaterialId None => new(-1);
    public bool IsNone => Value < 0;
}

public readonly record struct TextureId(int Value)
{
    public static TextureId None => new(-1);
    public bool IsNone => Value < 0;
}

[Flags]
public enum RenderFlags : uint
{
    None = 0,
    Visible = 1 << 0,
    CastsShadow = 1 << 1,
    Transparent = 1 << 2,
    /// <summary>Never replace this object with an impostor.</summary>
    NoImpostor = 1 << 3,
    /// <summary>A sky object such as a star or planet. It is drawn in the depth range that fits its distance.</summary>
    Celestial = 1 << 4,
}

/// <summary>
/// One object to draw. Positions are relative to the render origin, in the axes of the camera's frame.
/// The previous transform uses the same origin, so the render thread can interpolate.
/// </summary>
public struct RenderInstance
{
    public uint EntityIndex;
    public MeshId Mesh;
    public MaterialId Material;
    public RenderFlags Flags;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;
    public Vector3 PreviousPosition;
    public Quaternion PreviousRotation;
    public float BoundingRadius;
}

public enum LightKind : byte
{
    Point,
    Spot,
}

/// <summary>A dynamic light. Position and direction follow the same rules as <see cref="RenderInstance"/>.</summary>
public struct RenderLight
{
    public LightKind Kind;
    public Vector3 Position;
    public Vector3 Direction;
    public Vector3 Color;
    public float Intensity;
    public float Range;
    public float InnerConeAngle;
    public float OuterConeAngle;
}

/// <summary>The camera. Its current position is the render origin, so <see cref="Position"/> is zero.</summary>
public struct RenderCamera
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 PreviousPosition;
    public Quaternion PreviousRotation;
    /// <summary>Vertical field of view in radians.</summary>
    public float VerticalFov;
}

/// <summary>The sun as a directional light. <see cref="Direction"/> points from the camera toward the sun.</summary>
public struct RenderSun
{
    public Vector3 Direction;
    public Vector3 Color;
    public float Intensity;
    public float AngularRadius;
}

/// <summary>A sphere that can cast an eclipse or planet shadow, tested analytically in the lighting shader.</summary>
public struct RenderOccluder
{
    public Vector3 Center;
    public float Radius;
}

public struct AmbientLight
{
    public Vector3 SkyColor;
    public Vector3 GroundColor;
    public float Intensity;
}

/// <summary>Everything the renderer needs for one simulation tick. Built at the sync point.</summary>
public sealed class RenderSnapshot
{
    public long Tick;
    public double Time;
    /// <summary>The <see cref="System.Diagnostics.Stopwatch"/> timestamp at publication. The render thread uses it to interpolate.</summary>
    public long PublishedAt;
    public RenderCamera Camera;
    public RenderSun Sun;
    public AmbientLight Ambient;
    public readonly List<RenderInstance> Instances = new();
    public readonly List<RenderLight> Lights = new();
    public readonly List<RenderOccluder> Occluders = new();

    public void Clear()
    {
        Instances.Clear();
        Lights.Clear();
        Occluders.Clear();
    }
}

public struct Vertex
{
    public Vector3 Position;
    public Vector3 Normal;
    /// <summary>Tangent in xyz, bitangent sign in w.</summary>
    public Vector4 Tangent;
    public Vector2 TexCoord;
}

public sealed record MeshData(Vertex[] Vertices, uint[] Indices);

/// <summary>RGBA8 pixels, row-major, top row first.</summary>
public sealed record TextureData(int Width, int Height, byte[] Rgba, bool Srgb = true);

/// <summary>
/// An energy-normalized Blinn-Phong material. <see cref="Gloss"/> in [0, 1] maps to a specular exponent.
/// The gloss texture holds specular intensity in R and gloss in G.
/// </summary>
public sealed record MaterialDesc(
    Vector4 BaseColor,
    float SpecularIntensity = 0.5f,
    float Gloss = 0.5f,
    Vector3 Emissive = default,
    TextureId Albedo = default,
    TextureId Normal = default,
    TextureId SpecularGloss = default)
{
    public static MaterialDesc Color(Vector4 color, float gloss = 0.5f) =>
        new(color, Gloss: gloss, Albedo: TextureId.None, Normal: TextureId.None, SpecularGloss: TextureId.None);
}

/// <summary>
/// The renderer as the host sees it. The Create methods are thread-safe. Uploads happen on the render thread
/// before the next frame. <see cref="Render"/> runs only on the render thread.
/// </summary>
public interface IRenderer : IDisposable
{
    MeshId CreateMesh(MeshData mesh);
    TextureId CreateTexture(TextureData texture);
    MaterialId CreateMaterial(MaterialDesc material);

    void Resize(int width, int height);

    /// <summary>Draws one frame, interpolated between the previous and current transforms by <paramref name="alpha"/>.</summary>
    void Render(RenderSnapshot snapshot, float alpha);

    /// <summary>Saves the next rendered frame as a PNG file.</summary>
    void CaptureNextFrame(string path);
}
