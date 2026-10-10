using System.Numerics;

namespace Tertius;

/// <summary>A renderer mesh handle. <c>default</c> is <see cref="None"/>.</summary>
public readonly record struct MeshId
{
    // Stored as value + 1, so that a zeroed struct means "none" rather than id 0.
    private readonly int raw;

    public MeshId(int value) => raw = value + 1;

    public int Value => raw - 1;
    public static MeshId None => default;
    public bool IsNone => raw == 0;

    public override string ToString() => IsNone ? "MeshId(none)" : $"MeshId({Value})";
}

/// <summary>A renderer material handle. <c>default</c> is <see cref="None"/>.</summary>
public readonly record struct MaterialId
{
    // Stored as value + 1, so that a zeroed struct means "none" rather than id 0.
    private readonly int raw;

    public MaterialId(int value) => raw = value + 1;

    public int Value => raw - 1;
    public static MaterialId None => default;
    public bool IsNone => raw == 0;

    public override string ToString() => IsNone ? "MaterialId(none)" : $"MaterialId({Value})";
}

/// <summary>A renderer texture handle. <c>default</c> is <see cref="None"/>.</summary>
public readonly record struct TextureId
{
    // Stored as value + 1, so that a zeroed struct means "none" rather than id 0.
    private readonly int raw;

    public TextureId(int value) => raw = value + 1;

    public int Value => raw - 1;
    public static TextureId None => default;
    public bool IsNone => raw == 0;

    public override string ToString() => IsNone ? "TextureId(none)" : $"TextureId({Value})";
}

[Flags]
public enum RenderFlags : uint
{
    None = 0,
    Visible = 1 << 0,
    /// <summary>
    /// Casts sun shadow-map shadows when within 2 km of the camera, whether or not the instance is
    /// <see cref="Visible"/>. Planets and moons shadow through <see cref="RenderOccluder"/> instead.
    /// </summary>
    CastsShadow = 1 << 1,
    /// <summary>Alpha-blended with <see cref="MaterialDesc.BaseColor"/>.W as opacity. Never an impostor.</summary>
    Transparent = 1 << 2,
    /// <summary>
    /// Never replace this object with an impostor. Use it for objects that must receive shadow-map shadows or that
    /// change every frame.
    /// </summary>
    NoImpostor = 1 << 3,
    /// <summary>A sky object such as a star or planet. It is drawn in the depth range that fits its distance, and it is never an impostor.</summary>
    Celestial = 1 << 4,
}

/// <summary>
/// One object to draw. Positions are relative to the render origin, in the axes of the camera's frame.
/// The previous transform uses the same origin, so the render thread can interpolate. A zeroed instance is not
/// drawable (zero scale and zero rotations), so build instances with <see cref="Create"/>.
/// Below 200 px of projected height, the renderer may draw the object from a baked capture (an impostor). An
/// impostor keeps its sun, ambient, eclipse and point or spot lighting, but receives no shadow-map shadows.
/// </summary>
public struct RenderInstance
{
    /// <summary>The entity slot. With <see cref="Part"/>, it identifies the object from tick to tick.</summary>
    public uint EntityIndex;
    /// <summary>Which of the entity's render parts this is, 0 for the first. The renderer keys impostor state on (EntityIndex, Part).</summary>
    public ushort Part;
    public MeshId Mesh;
    public MaterialId Material;
    public RenderFlags Flags;
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Scale;
    public Vector3 PreviousPosition;
    public Quaternion PreviousRotation;
    /// <summary>The world-space radius around <see cref="Position"/>, scale included. 0 means "derive it from the mesh".</summary>
    public float BoundingRadius;

    /// <summary>An instance that is not moving: the previous transform equals the current one.</summary>
    public static RenderInstance Create(
        MeshId mesh,
        MaterialId material,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        float boundingRadius,
        RenderFlags flags = RenderFlags.Visible,
        uint entityIndex = 0,
        ushort part = 0) => new()
    {
        EntityIndex = entityIndex,
        Part = part,
        Mesh = mesh,
        Material = material,
        Flags = flags,
        Position = position,
        Rotation = rotation,
        Scale = scale,
        PreviousPosition = position,
        PreviousRotation = rotation,
        BoundingRadius = boundingRadius,
    };
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
    /// <summary>Linear RGB.</summary>
    public Vector3 Color;
    /// <summary>Luminous intensity: irradiance at distance d is Color · Intensity / d², with a smooth window to zero at <see cref="Range"/>.</summary>
    public float Intensity;
    public float Range;
    /// <summary>Half-angle of the full-intensity cone of a spot light, in radians.</summary>
    public float InnerConeAngle;
    /// <summary>Half-angle of the outer edge of a spot light, in radians.</summary>
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

/// <summary>The sun as a directional light.</summary>
public struct RenderSun
{
    /// <summary>From the camera toward the sun. It need not be normalized, but it must not be zero.</summary>
    public Vector3 Direction;
    /// <summary>Linear RGB.</summary>
    public Vector3 Color;
    /// <summary>Irradiance. Zero or less disables the sun, including its shadow pass.</summary>
    public float Intensity;
    /// <summary>The sun's apparent angular radius, in radians. It sets the softness of eclipse edges.</summary>
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
    /// <summary>
    /// The render origin (the camera position at this tick) in the coordinates of the camera's reference frame.
    /// The renderer anchors world-fixed grids, such as the shadow texel grid, to the frame's origin.
    /// </summary>
    public Double3 OriginInFrame;
    /// <summary>An opaque id of the camera's reference frame. It changes when the camera changes frame.</summary>
    public long OriginFrameId;
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
/// The gloss texture holds specular intensity in R and gloss in G. Colors are linear RGB. BaseColor.W is opacity,
/// used only with <see cref="RenderFlags.Transparent"/>. A texture slot left at its default is "no texture".
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
    public static MaterialDesc Color(Vector4 color, float gloss = 0.5f) => new(color, Gloss: gloss);
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
