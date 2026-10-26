using System.Numerics;
using System.Runtime.InteropServices;

namespace Tertius;

/// <summary>A renderer mesh handle. <c>default</c> is <see cref="None"/>.</summary>
public readonly record struct MeshId
{
    public static MeshId None => default;

    // Stored as value + 1, so that a zeroed struct means "none" rather than id 0.
    private readonly int raw;

    public MeshId(int value)
    {
        raw = value + 1;
    }

    public int Value => raw - 1;
    public bool IsNone => raw == 0;

    public override string ToString() => IsNone ? "MeshId(none)" : $"MeshId({Value})";
}

/// <summary>A renderer material handle. <c>default</c> is <see cref="None"/>.</summary>
public readonly record struct MaterialId
{
    public static MaterialId None => default;

    // Stored as value + 1, so that a zeroed struct means "none" rather than id 0.
    private readonly int raw;

    public MaterialId(int value)
    {
        raw = value + 1;
    }

    public int Value => raw - 1;
    public bool IsNone => raw == 0;

    public override string ToString() => IsNone ? "MaterialId(none)" : $"MaterialId({Value})";
}

/// <summary>A renderer texture handle. <c>default</c> is <see cref="None"/>.</summary>
public readonly record struct TextureId
{
    public static TextureId None => default;

    // Stored as value + 1, so that a zeroed struct means "none" rather than id 0.
    private readonly int raw;

    public TextureId(int value)
    {
        raw = value + 1;
    }

    public int Value => raw - 1;
    public bool IsNone => raw == 0;

    public override string ToString() => IsNone ? "TextureId(none)" : $"TextureId({Value})";
}

[Flags]
public enum RenderFlags : uint
{
    None = 0,
    Visible = 1 << 0,

    /// <summary>
    /// Casts shadows from every light that casts shadows, whether or not the instance is <see cref="Visible"/>, and onto
    /// objects in any frame. For a far light with a cascade set, a caster whose bounding sphere comes within 2 km of the
    /// camera casts shadow-map shadows, which have its real shape, onto what is within 2 km. Every caster also shadows as an
    /// analytic sphere of its bounding radius, a far shadow of a far light, where the maps do not reach, when it is big
    /// enough to notice. A sphere never shadows what lies inside it, so a caster never shadows itself. For a near light with
    /// tiles of the local shadow atlas, a caster within the light's reach casts shadows of its real shape. Leave it off a
    /// light's own emitter, such as a lamp's bulb or panel, so that it does not darken its own light.
    /// </summary>
    CastsShadow = 1 << 1,

    // 1 << 2 was Transparent. A material's alpha mode says that now (docs/plans/skin-and-hair.md, decision 8).

    /// <summary>
    /// Never replace this object with an impostor. Use it for objects that must receive shadow-map shadows or that
    /// change every frame.
    /// </summary>
    NoImpostor = 1 << 3,

    /// <summary>A sky object such as a star or planet. It is never an impostor.</summary>
    Celestial = 1 << 4,
}

/// <summary>
/// One object to draw. Positions are relative to the render origin, in the axes of the camera's frame.
/// The previous transform uses the same origin, so the render thread can interpolate. A zeroed instance is not
/// drawable (zero scale and zero rotations), so build instances with <see cref="Create"/>.
/// Below 200 px of projected height, the renderer may draw the object from a baked capture (an impostor). An
/// impostor keeps its lights, ambient light and far shadows, but receives no shadow-map shadows.
/// </summary>
public struct RenderInstance
{
    /// <summary>The entity slot. With <see cref="Part"/>, it identifies the object from tick to tick.</summary>
    public uint EntityIndex;

    /// <summary>Which of the entity's render parts this is, 0 for the first. The renderer keys impostor state on (EntityIndex, Part, Mesh, Material), so a part that changes its mesh or material gets a new capture.</summary>
    public ushort Part;

    /// <summary>
    /// The depth of the object's chain frame: the nearest frame of <see cref="RenderSnapshot.Chain"/> at or above the frame
    /// that holds it, 0 for the camera's frame (docs/plans/environment-maps-per-frame.md).
    /// </summary>
    public ushort ChainDepth;

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

    /// <summary>
    /// Which way is up for the object's hemisphere ambient light. The simulation takes it from the gravity of the object's
    /// own frame, so it does not depend on the frame that the camera rides in. It is not interpolated.
    /// </summary>
    public HemisphereUp Hemisphere;

    /// <summary>An instance that is not moving: the previous transform equals the current one.</summary>
    public static RenderInstance Create(
        MeshId mesh,
        MaterialId material,
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        float boundingRadius,
        HemisphereUp hemisphere,
        ushort chainDepth,
        RenderFlags flags = RenderFlags.Visible,
        uint entityIndex = 0,
        ushort part = 0
    ) =>
        new()
        {
            EntityIndex = entityIndex,
            Part = part,
            ChainDepth = chainDepth,
            Mesh = mesh,
            Material = material,
            Flags = flags,
            Position = position,
            Rotation = rotation,
            Scale = scale,
            PreviousPosition = position,
            PreviousRotation = rotation,
            BoundingRadius = boundingRadius,
            Hemisphere = hemisphere,
        };
}

/// <summary>
/// A frame of the camera's chain (docs/plans/environment-maps-per-frame.md): its id, and its axes in the axes of the
/// camera's frame.
/// </summary>
/// <param name="FrameId">The frame's generation in the high 32 bits and its index in the low 32 bits.</param>
/// <param name="Rotation">The frame's axes in the axes of the camera's frame: it turns a direction in the frame's axes into world axes.</param>
/// <param name="PreviousRotation"><paramref name="Rotation"/> at the previous build, or the same rotation when the build does not interpolate.</param>
public readonly record struct RenderChainFrame(long FrameId, Quaternion Rotation, Quaternion PreviousRotation);

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

    public AmbientLight Ambient;

    /// <summary>
    /// The chain: the camera's frame first, then each semantic parent up to the root of its tree. Empty without a camera.
    /// </summary>
    public readonly List<RenderChainFrame> Chain = [];

    /// <summary>The id of the camera's frame, the first of <see cref="Chain"/>. 0 without a camera.</summary>
    public long OriginFrameId => Chain.Count > 0 ? Chain[0].FrameId : 0;

    /// <summary>
    /// The axes of the root frame of the camera's frame tree, the last of <see cref="Chain"/>, in the axes of the camera's
    /// frame. The star field is fixed to the root's axes. Identity without a camera.
    /// </summary>
    public Quaternion RootRotation => Chain.Count > 0 ? Chain[^1].Rotation : Quaternion.Identity;

    /// <summary><see cref="RootRotation"/> at the previous build, by the rule of <see cref="RenderChainFrame.PreviousRotation"/>.</summary>
    public Quaternion PreviousRootRotation => Chain.Count > 0 ? Chain[^1].PreviousRotation : Quaternion.Identity;

    public readonly List<RenderInstance> Instances = [];
    public readonly List<RenderLight> Lights = [];
    public readonly List<FrameOutline> Outlines = [];

    /// <summary>The live puffs, in the order of the rows of the simulation's puff table.</summary>
    public readonly List<RenderPuff> Puffs = [];

    /// <summary>
    /// The look table at the build, by index, with its free entries. <see cref="RenderPuff.Look"/> indexes it. A free
    /// entry is one that no live puff uses.
    /// </summary>
    public readonly List<PuffLook> PuffLooks = [];

    /// <summary>
    /// The age, in s, that every puff gained since the previous build: the step when this build interpolates and the
    /// previous build was of the tick before, and 0 otherwise. A puff's age at the previous build is its
    /// <see cref="RenderPuff.Age"/> less this step.
    /// </summary>
    public float PuffAgeStep;

    /// <summary>Empties the lists of the snapshot, the chain among them, and sets <see cref="PuffAgeStep"/> to 0, so that a builder can fill it again.</summary>
    public void Clear()
    {
        Chain.Clear();
        Instances.Clear();
        Lights.Clear();
        Outlines.Clear();
        Puffs.Clear();
        PuffLooks.Clear();
        PuffAgeStep = 0;
    }
}

// The renderer copies vertices into a GPU buffer, so the field order is the memory layout. The cleanup's member layout
// leaves a type with StructLayout alone.
[StructLayout(LayoutKind.Sequential)]
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
/// A metallic-roughness material (docs/plans/pbr-brdf.md). Colors are linear RGB. A map left at its default is no map.
/// The metallic-roughness map has glTF's layout: roughness in G, metallic in B, which only the standard model reads. Each
/// map channel multiplies its factor.
/// </summary>
/// <param name="Model">How the surface sends light. The default is a standard non-metal.</param>
/// <param name="Alpha">
/// How the alpha draws. The default is opaque. BaseColor.W is the coverage under a cutout or fringed mode, the opacity
/// under glass, and unread under opaque.
/// </param>
public sealed record MaterialDesc(
    Vector4 BaseColor,
    float Roughness,
    float Reflectance = 0.5f,
    Vector3 Emissive = default,
    TextureId BaseColorMap = default,
    TextureId NormalMap = default,
    TextureId MetallicRoughnessMap = default,
    ShadingModel Model = default,
    AlphaMode Alpha = default
)
{
    public static MaterialDesc Color(Vector4 color, float roughness) => new(BaseColor: color, Roughness: roughness);
}

/// <summary>
/// The resource half of the renderer: what a scene needs to make the meshes, textures and materials that its render
/// rows name. The Create methods are thread-safe. Uploads happen on the render thread before the next frame.
/// </summary>
public interface IRenderResources
{
    MeshId CreateMesh(MeshData mesh);
    TextureId CreateTexture(TextureData texture);
    MaterialId CreateMaterial(MaterialDesc material);
}

/// <summary>
/// The renderer as the host sees it. <see cref="Render"/> runs only on the render thread.
/// </summary>
public interface IRenderer : IRenderResources, IDisposable
{
    void Resize(int width, int height);

    /// <summary>Draws one frame, interpolated between the previous and current transforms by <paramref name="alpha"/>.</summary>
    void Render(RenderSnapshot snapshot, float alpha);

    /// <summary>
    /// Asks for the pixels of the next frame that <see cref="Render"/> draws. Any thread may call it. That
    /// <c>Render</c> call waits for the frame on the GPU and hands its pixels to <paramref name="sink"/> before it
    /// returns, on its own thread, once. A frame that the output skips (a minimized window) hands nothing, and the
    /// request waits for the next frame that draws. A second request before the frame replaces the first.
    /// </summary>
    void CaptureNextFrame(ICaptureSink sink);
}
