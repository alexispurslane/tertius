using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

// C# mirrors of the shader structs in Shaders/frame.glsl. Layouts follow std430 (storage buffers) and std140 (the
// frame block). Change both sides together; RendererLayoutTests checks the sizes.

/// <summary>
/// A vertex as the global vertex buffer holds it: a staged <see cref="Vertex"/> and the number of its card in its mesh,
/// <see cref="MeshCards.Number"/>, which a hair pixel's card code takes (docs/plans/skin-and-hair.md, "The card number").
/// 52 bytes. deform.comp writes the same layout into the deform region.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuVertex
{
    public Vector3 Position;
    public Vector3 Normal;

    /// <summary>Tangent in xyz, bitangent sign in w.</summary>
    public Vector4 Tangent;

    public Vector2 TexCoord;

    /// <summary>The number of the vertex's card: the connected strip of triangles that holds it.</summary>
    public uint Card;

    /// <summary><paramref name="vertex"/>, staged, with the number of its <paramref name="card"/>.</summary>
    public static GpuVertex Of(in Vertex vertex, uint card) =>
        new()
        {
            Position = vertex.Position,
            Normal = vertex.Normal,
            Tangent = vertex.Tangent,
            TexCoord = vertex.TexCoord,
            Card = card,
        };
}

/// <summary>How a drawn instance relates to the last drawn frame (docs/plans/temporal-renderer.md, "The placement history").</summary>
internal enum MotionKind : uint
{
    /// <summary>The last frame drew the instance's key with the same mesh: it moves from the rows that the last frame drew.</summary>
    Moved = 0,

    /// <summary>The instance has no identity: its key is not above the key before it. It is still in the camera's frame.</summary>
    Still = 1,

    /// <summary>The last frame did not draw the key, or drew it with another mesh: the instance has no history.</summary>
    New = 2,
}

/// <summary>
/// Where one placed instance was in the last drawn frame, by <see cref="PreparedInstance.PlacedIndex"/>
/// (docs/plans/temporal-renderer.md, "The placement history"). 64 bytes in std430.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct GpuMotion
{
    /// <summary><see cref="PreviousFirstVertex"/> of an instance whose last frame kept no deformed copy of the same vertex count.</summary>
    public const uint NoKeptCopy = uint.MaxValue;

    /// <summary>The last frame's model rows, relative to the last frame's eye.</summary>
    public Vector4 PreviousRow0;

    public Vector4 PreviousRow1;
    public Vector4 PreviousRow2;

    /// <summary>The first vertex of the last frame's deformed copy in the kept positions, or <see cref="NoKeptCopy"/>.</summary>
    public uint PreviousFirstVertex;

    /// <summary>A <see cref="MotionKind"/>.</summary>
    public uint Kind;

    /// <summary>The first vertex of this frame's frame mesh in the vertex buffer, from which a vertex finds its place in its copy.</summary>
    public uint FirstVertex;
}

/// <summary>One drawn instance in the instance storage buffer. 92 bytes of fields, padded to 96, the struct's alignment of 16.</summary>
[StructLayout(LayoutKind.Sequential, Size = 96)]
internal struct GpuInstance
{
    /// <summary>Rows of the 3x4 model matrix that maps mesh space to camera-relative world space.</summary>
    public Vector4 ModelRow0;

    public Vector4 ModelRow1;
    public Vector4 ModelRow2;

    /// <summary>1 / scale², so the shader can form the normal matrix R · S⁻¹ from the model matrix.</summary>
    public Vector3 InverseScaleSquared;

    public uint Material;

    /// <summary>
    /// Which way is up for the hemisphere ambient light: xyz the up direction with w 1, or the camera-relative point that
    /// up leads away from with w 2. All zero mixes the sky and the ground evenly.
    /// </summary>
    public Vector4 Hemisphere;

    /// <summary>
    /// The instance's map: the index, root first, of the environment map that lights it (docs/plans/environment-maps-per-frame.md,
    /// "The served chain and the map of an object"). 0 with the map off.
    /// </summary>
    public uint Map;

    /// <summary>
    /// 1 when the instance's entity holds a light, whose face its glow is, else 0. The shaders' <c>glowSent</c>
    /// (frame.glsl) reads it: such a glow bounces no light (docs/plans/screen-space-illumination.md, "The bounce source"),
    /// and the environment map leaves it out (docs/plans/environment-map.md, "How a face is lit").
    /// </summary>
    public uint HoldsLight;

    /// <summary>
    /// The instance's place among the frame's placed instances, <see cref="PreparedInstance.PlacedIndex"/>: the same in
    /// every view and depth range that draws it. Its hair pixels' card codes mix it in (<see cref="CardCode.Of"/>).
    /// </summary>
    public uint PlacedIndex;

    /// <param name="map">The instance's map, from 0 to <see cref="EnvironmentChain.MaxMaps"/> − 1.</param>
    /// <param name="holdsLight">Whether the instance's entity holds a light.</param>
    /// <param name="placedIndex">
    /// The instance's place among the frame's placed instances, <see cref="PreparedInstance.PlacedIndex"/>. A draw that
    /// writes no card code passes 0. Required, so that no G-buffer draw gives all its instances one place by omission.
    /// </param>
    public static GpuInstance From(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        uint material,
        int map,
        bool holdsLight,
        int placedIndex,
        HemisphereUp hemisphere = default
    )
    {
        (Vector4 row0, Vector4 row1, Vector4 row2) = RowsOf(position: position, rotation: rotation, scale: scale);
        return new()
        {
            ModelRow0 = row0,
            ModelRow1 = row1,
            ModelRow2 = row2,
            InverseScaleSquared = new(InverseSquare(scale.X), InverseSquare(scale.Y), InverseSquare(scale.Z)),
            Material = material,
            Hemisphere = HemisphereOf(position, hemisphere),
            Map = (uint)map,
            HoldsLight = holdsLight ? 1u : 0u,
            PlacedIndex = (uint)placedIndex,
        };
    }

    /// <summary>The three rows of the 3 × 4 model matrix M = T · R · S of an instance, as <see cref="From"/> writes them.</summary>
    public static (Vector4 Row0, Vector4 Row1, Vector4 Row2) RowsOf(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale
    )
    {
        // Rotation matrix columns from the unit quaternion, then scaled per axis: M = T · R · S.
        float x = rotation.X,
            y = rotation.Y,
            z = rotation.Z,
            w = rotation.W;
        float xx = x * x,
            yy = y * y,
            zz = z * z,
            xy = x * y,
            xz = x * z,
            yz = y * z,
            wx = w * x,
            wy = w * y,
            wz = w * z;
        Vector3 axisX = new Vector3(1 - 2 * (yy + zz), 2 * (xy + wz), 2 * (xz - wy)) * scale.X;
        Vector3 axisY = new Vector3(2 * (xy - wz), 1 - 2 * (xx + zz), 2 * (yz + wx)) * scale.Y;
        Vector3 axisZ = new Vector3(2 * (xz + wy), 2 * (yz - wx), 1 - 2 * (xx + yy)) * scale.Z;
        return (
            new(x: axisX.X, y: axisY.X, z: axisZ.X, w: position.X),
            new(x: axisX.Y, y: axisY.Y, z: axisZ.Y, w: position.Y),
            new(x: axisX.Z, y: axisY.Z, z: axisZ.Z, w: position.Z)
        );
    }

    /// <summary>
    /// An instance whose model matrix has the given columns, for draws that are not meshes: an impostor billboard
    /// maps its quad space through it, keeps its atlas layer in <see cref="Material"/>, and reads its object's motion row
    /// at <paramref name="placedIndex"/>.
    /// </summary>
    public static GpuInstance FromAxes(
        Vector3 axisX,
        Vector3 axisY,
        Vector3 axisZ,
        Vector3 origin,
        uint material,
        int placedIndex,
        bool holdsLight
    ) =>
        new()
        {
            ModelRow0 = new(x: axisX.X, y: axisY.X, z: axisZ.X, w: origin.X),
            ModelRow1 = new(x: axisX.Y, y: axisY.Y, z: axisZ.Y, w: origin.Y),
            ModelRow2 = new(x: axisX.Z, y: axisY.Z, z: axisZ.Z, w: origin.Z),
            Material = material,
            PlacedIndex = (uint)placedIndex,
            HoldsLight = holdsLight ? 1u : 0u,
        };

    private static float InverseSquare(float s) => s != 0 ? 1f / (s * s) : 0f;

    /// <summary>
    /// The hemisphere of an instance at <paramref name="position"/> as the shaders read it. One that holds a number that is
    /// not finite mixes the sky and the ground evenly, as no hemisphere does.
    /// </summary>
    private static Vector4 HemisphereOf(Vector3 position, HemisphereUp hemisphere)
    {
        Vector4 gpu = hemisphere switch
        {
            UpAlong { Direction: var direction } => new(direction, 1),
            // The center moves with the instance, so it is exact wherever the instance is interpolated to.
            UpAwayFrom { Center: var center } => new(position + center, 2),
            null => Vector4.Zero,
        };
        return ShaderInput.IsFinite(new(gpu.X, gpu.Y, gpu.Z)) ? gpu : Vector4.Zero;
    }
}

/// <summary>One material in the material storage buffer (std430). 60 bytes of fields, padded to 64, the struct's alignment of 16.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct GpuMaterial
{
    /// <summary>The <see cref="NormalMap"/> of a material without a normal map, which shades with its vertex normal.</summary>
    public const uint NoMap = uint.MaxValue;

    public Vector4 BaseColor;
    public Vector3 Emissive;

    /// <summary>The metallic of a <see cref="StandardShading"/> model, 0 for every other model.</summary>
    public float Metallic;

    /// <summary>r₀, before the floor: the floor is the BRDF's.</summary>
    public float Roughness;
    public float Reflectance;

    /// <summary>Bindless index, already resolved to the default texture.</summary>
    public uint BaseColorMap;

    /// <summary>
    /// Bindless index, or <see cref="NoMap"/> for a material without a normal map. No default texture stands in for
    /// none, because no 8-bit UNORM texel decodes to +Z.
    /// </summary>
    public uint NormalMap;

    /// <summary>Bindless index, already resolved to the default texture.</summary>
    public uint MetallicRoughnessMap;

    /// <summary><see cref="ShadingCode.Word"/> of the material's shading model.</summary>
    public uint Shading;

    /// <summary>The cutoff of a cutout or fringed material, 0 for an opaque or glass one, which nothing tests.</summary>
    public float AlphaCutoff;
}

/// <summary>
/// One light in the light storage buffer (std430, binding 3 of the frame set). 128 bytes, the last 12 unused.
/// <see cref="From"/> is the one place that encodes a <see cref="PreparedLight"/>, its spread, its shadow slot, its map
/// and whether the main view keeps it for the shaders.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 128)]
internal struct GpuLight
{
    /// <summary>The kind of a point light in <see cref="IntensityKind"/>.w.</summary>
    public const float PointKind = 0f;

    /// <summary>The kind of a spot light in <see cref="IntensityKind"/>.w.</summary>
    public const float SpotKind = 1f;

    /// <summary>The kind of a tube light, a point light with a segment, in <see cref="IntensityKind"/>.w.</summary>
    public const float TubeKind = 2f;

    /// <summary>The kind of a rectangle light in <see cref="IntensityKind"/>.w.</summary>
    public const float RectKind = 3f;

    /// <summary>The shape of a point or a spot light, and its bit in <see cref="GpuFrameConstants.Counts"/>.W.</summary>
    public const uint PointShape = 1;

    /// <summary>The shape of a tube light, and its bit in <see cref="GpuFrameConstants.Counts"/>.W.</summary>
    public const uint TubeShape = 2;

    /// <summary>The shape of a rectangle light, and its bit in <see cref="GpuFrameConstants.Counts"/>.W.</summary>
    public const uint RectShape = 4;

    /// <summary>The slot of a light without shadows, in <see cref="Shadow"/>.x.</summary>
    public const uint NoSlot = 0;

    /// <summary>The slot of a <see cref="CascadeSet"/>, in <see cref="Shadow"/>.x.</summary>
    public const uint CascadeSetSlot = 1;

    /// <summary>The slot of a <see cref="FarShadowsOnly"/>, in <see cref="Shadow"/>.x.</summary>
    public const uint FarShadowsOnlySlot = 2;

    /// <summary>The slot of <see cref="AtlasTiles"/>, in <see cref="Shadow"/>.x.</summary>
    public const uint AtlasTilesSlot = 3;

    /// <summary>xyz camera-relative position, w the reach in meters.</summary>
    public Vector4 PositionReach;

    /// <summary>
    /// rgb color × intensity in W/sr, a tube light's across its segment and a rectangle light's across its face; w the kind:
    /// <see cref="PointKind"/>, <see cref="SpotKind"/>, <see cref="TubeKind"/> or <see cref="RectKind"/>.
    /// </summary>
    public Vector4 IntensityKind;

    /// <summary>
    /// xyz the unit spot axis, w cos(outer half-angle). For a tube light, xyz from its position to one end of its segment and
    /// w zero. For a rectangle light, xyz its half width and w zero. Zero for a point light.
    /// </summary>
    public Vector4 DirectionCosOuter;

    /// <summary>x cos(inner half-angle), zero for a point light; y the emitter's radius in meters.</summary>
    public Vector4 SpotRadius;

    /// <summary>xyz a rectangle light's half height, perpendicular to its half width; zero for every other light.</summary>
    public Vector4 HalfHeight;

    /// <summary>x the slot; y the cascade set or the first tile view; z the tile views.</summary>
    public UInt4 Shadow;

    /// <summary>x the first occluder, y the occluders, z how many of the first ones the cascade set also draws.</summary>
    public UInt3 Occluders;

    /// <summary>
    /// The light's map: the map of the entity that carries it. A face of map i keeps the light only when this is at most i
    /// (docs/plans/environment-maps-per-frame.md, decision 13).
    /// </summary>
    public uint Map;

    /// <summary>
    /// 1 when the main view keeps the light (<see cref="PreparedLight.InMainView"/>), else 0. The main view's shaders add
    /// only the lights with 1 (docs/plans/environment-map.md, decision 20).
    /// </summary>
    public uint InMainView;

    /// <summary>
    /// The GPU form of <paramref name="light"/>, read into the shaders' ranges: the intensity by
    /// <see cref="ShaderInput.Intensity(Vector3)"/>, and the radius at most <see cref="ShaderInput.PositionLimit"/>, so that its
    /// square stays finite.
    /// </summary>
    public static GpuLight From(in PreparedLight light)
    {
        (Vector4 axis, Vector4 halfHeight, float cosInner, float kind) = light.Spread switch
        {
            Cone { Axis: var direction, CosInner: var inner, CosOuter: var outer } => (
                new Vector4(direction, outer),
                Vector4.Zero,
                inner,
                SpotKind
            ),
            EveryDirection { HalfSegment: var half } when IsSegment(half) => (
                new Vector4(half, 0),
                Vector4.Zero,
                0f,
                TubeKind
            ),
            EveryDirection => (Vector4.Zero, Vector4.Zero, 0f, PointKind),
            RectFace { HalfWidth: var width, HalfHeight: var height } => (
                new Vector4(width, 0),
                new Vector4(height, 0),
                0f,
                RectKind
            ),
        };
        (UInt4 shadow, UInt3 occluders) = light.Shadow switch
        {
            CascadeSet { Set: var set, Occluders: var range, Mapped: var mapped } => (
                new UInt4(X: CascadeSetSlot, Y: (uint)set, Z: 0, W: 0),
                OccludersOf(range, mapped)
            ),
            FarShadowsOnly(var range) => (new UInt4(X: FarShadowsOnlySlot, Y: 0, Z: 0, W: 0), OccludersOf(range, 0)),
            AtlasTiles { First: var first, Count: var count } => (
                new UInt4(X: AtlasTilesSlot, Y: (uint)first, Z: (uint)count, W: 0),
                default
            ),
            null => (default, default),
        };
        return new()
        {
            PositionReach = new(light.Position, light.Reach),
            IntensityKind = new(ShaderInput.Intensity(light.RadiantIntensity), kind),
            DirectionCosOuter = axis,
            SpotRadius = new(x: cosInner, y: MathF.Min(light.Radius, ShaderInput.PositionLimit), z: 0, w: 0),
            HalfHeight = halfHeight,
            Shadow = shadow,
            Occluders = occluders,
            Map = (uint)light.Map,
            InMainView = light.InMainView ? 1u : 0u,
        };
    }

    /// <summary>
    /// The shape of <paramref name="light"/>, as the shaders read it from the kind that <see cref="From"/> gives:
    /// <see cref="PointShape"/>, <see cref="TubeShape"/> or <see cref="RectShape"/>. Each shape has a light loop of its own.
    /// </summary>
    public static uint ShapeOf(in PreparedLight light) =>
        light.Spread switch
        {
            Cone => PointShape,
            EveryDirection { HalfSegment: var half } when IsSegment(half) => TubeShape,
            EveryDirection => PointShape,
            RectFace => RectShape,
        };

    /// <summary>
    /// Whether a light with <paramref name="halfSegment"/> is a tube: a segment within 10^−6 m of its middle is a point, and
    /// a longer one's square stays a normal float in the shader.
    /// </summary>
    private static bool IsSegment(Vector3 halfSegment) => halfSegment.LengthSquared() > 1e-12f;

    private static UInt3 OccludersOf(OccluderRange range, int mapped) =>
        new(X: (uint)range.Start, Y: (uint)range.Count, Z: (uint)mapped);
}

/// <summary>Four unsigned integers, laid out as a GLSL <c>uvec4</c>.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct UInt4(uint X, uint Y, uint Z, uint W);

/// <summary>Three unsigned integers, laid out as a GLSL <c>uvec3</c> that a 4-byte member follows in its 16 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct UInt3(uint X, uint Y, uint Z);

/// <summary>
/// One tile view of the frame in the tile storage buffer (std430, binding 15 of the frame set). 80 bytes. A light's
/// <see cref="AtlasTiles"/> slot names its views, in cube face order for a point light. The last 4 bytes are unused.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 80)]
internal struct GpuShadowTile
{
    /// <summary>Camera-relative world space to the tile's clip space, row-vector form: reversed-Z, infinite far plane.</summary>
    public Matrix4x4 ViewProjection;

    /// <summary>The layer of the atlas that holds the tile.</summary>
    public uint Layer;

    /// <summary>The world size of one texel at 1 m from the light along the view axis.</summary>
    public float TexelPerMeter;

    /// <summary>The near plane, in meters from the light: max(radius, <see cref="DepthRanges.NearPlane"/>).</summary>
    public float Near;

    /// <summary>The GPU form of <paramref name="view"/> of <paramref name="light"/> in an atlas of <paramref name="tileSize"/>² tiles.</summary>
    public static GpuShadowTile Of(in PreparedLight light, TileView view, int tileSize) =>
        new()
        {
            ViewProjection = ShadowTiles.ViewProjection(light: light, aim: view.Aim, tileSize: tileSize),
            Layer = (uint)view.Layer,
            TexelPerMeter = ShadowTiles.TexelPerMeter(light: light, aim: view.Aim, tileSize: tileSize),
            Near = ShadowTiles.NearPlane(light),
        };
}

/// <summary>One frame outline in the outline storage buffer (std430). 48 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuOutline
{
    /// <summary><see cref="Flags"/> bit: the outline is a sphere, so seen from outside it also draws its rim as seen from the eye.</summary>
    public const uint SphereFlag = 1;

    /// <summary>The frame's axes in world axes, as a quaternion (x, y, z, w). The rings lie in the planes of these axes.</summary>
    public Vector4 Rotation;

    /// <summary>The frame's origin, camera-relative.</summary>
    public Vector3 Center;

    /// <summary>The display color, sRGB-encoded, as <c>unpackUnorm4x8</c> reads it: red in the lowest byte.</summary>
    public uint Color;

    /// <summary>The radii along the frame's axes, in meters.</summary>
    public Vector3 Radii;

    /// <summary><see cref="SphereFlag"/> or 0.</summary>
    public uint Flags;
}

/// <summary>
/// One puff to draw, in the puff storage buffer (std430, binding 7 of the frame set). 36 bytes of fields, padded to 48, the
/// struct's alignment of 16. The batcher writes one for each puff that it keeps, in the order of the transparent pass's runs.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 48)]
internal struct GpuPuff
{
    /// <summary>The case of <see cref="PuffShading"/> in <see cref="ColorKind"/>.w: a glow puff.</summary>
    public const float GlowKind = 0f;

    /// <summary>The case of <see cref="PuffShading"/> in <see cref="ColorKind"/>.w: a lit puff.</summary>
    public const float LitKind = 1f;

    /// <summary>xyz the camera-relative center at the render age, in m; w the radius at the render age, in m.</summary>
    public Vector4 PositionRadius;

    /// <summary>
    /// rgb the own share times the radiance of a glow puff or the albedo of a lit puff; w the case, <see cref="GlowKind"/>
    /// or <see cref="LitKind"/>.
    /// </summary>
    public Vector4 ColorKind;

    /// <summary>The puff's map, whose mean radiance is a lit puff's ambient light.</summary>
    public uint Map;
}

/// <summary>The frame uniform block (std140). 3,808 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuFrameConstants
{
    /// <summary><see cref="Counts"/>.Y of a frame whose light list holds no sun of an atmosphere.</summary>
    public const uint NoAirSun = uint.MaxValue;

    // System.Numerics matrices are row-major with row vectors. Their bytes read as a column-major GLSL mat4 give
    // the transpose, which is the matrix for column vectors. So M_numerics = (P · V)ᵀ uploads as P · V.
    public Matrix4x4 FarViewProj;
    public Matrix4x4 MiddleViewProj;
    public Matrix4x4 NearViewProj;
    public Vector4 CameraRight;
    public Vector4 CameraUp;
    public Vector4 CameraForward;
    public Vector4 AmbientSky;
    public Vector4 AmbientGround;
    public Vector4 Viewport;

    /// <summary>X the main view's mip bias, Y and Z the jitter in render pixels, W unused.</summary>
    public Vector4 RenderParams;
    public Vector4 OutputViewport;
    public Vector4 PreviousJitter;

    /// <summary>
    /// x: the most pre-exposed light that one puff adds to a pixel, <see cref="ShaderInput.PuffRadianceCap"/> of the puffs
    /// drawn, a share of <see cref="ShaderInput.PuffBudget"/>; y: time in s; z: angular size of one pixel; w: unused.
    /// </summary>
    public Vector4 Params;

    /// <summary>
    /// x: the lights; y: the place in the light list of the atmosphere's sun (<see cref="PreparedScene.AirSunLight"/>), or
    /// <see cref="NoAirSun"/>, by which the shaders choose the light loop that takes no tint; z: the cascades of each drawn
    /// cascade set, <see cref="ShadowCascades.Count"/>, or 0 when no set was drawn; w: the shapes of the list's lights, one
    /// bit each (<see cref="GpuLight.ShapeOf"/>), by which the shaders skip the loop of a shape that the list lacks.
    /// </summary>
    public UInt4 Counts;

    /// <summary>The view depth where each cascade ends, the same for every set.</summary>
    public Vector4 ShadowSplits;

    /// <summary>The view depth where each cascade starts to blend into the next one (or, for the last, to no shadow).</summary>
    public Vector4 ShadowBlendStart;

    /// <summary>The world size of one shadow map texel in each cascade, the same for every set.</summary>
    public Vector4 ShadowTexel;

    /// <summary>
    /// x: 1 / map size; y: normal offset in texels, written in every frame for the cascades and the tiles; z: constant
    /// depth bias; w: unused.
    /// </summary>
    public Vector4 ShadowParams;

    /// <summary>The root frame's axes in world axes, interpolated, as a quaternion (x, y, z, w). The star field is fixed to them.</summary>
    public Vector4 RootRotation;

    /// <summary>The cascade sets of this frame; set i belongs to the light whose slot names it.</summary>
    public CascadeSetArray CascadeSets;

    /// <summary>
    /// R_i, the axes of map i's frame in world axes, interpolated, as a unit quaternion (x, y, z, w), for the served maps,
    /// root first (docs/plans/environment-maps-per-frame.md, decision 9). The entries past the served maps are not read.
    /// </summary>
    public MapAxesArray MapAxes;

    /// <summary>The atmosphere of the frame (docs/plans/atmosphere.md, "The renderer").</summary>
    public GpuAtmosphere Atmosphere;

    /// <summary>
    /// This frame's view-projection without jitter and without translation, which the motion vectors take
    /// (docs/plans/temporal-renderer.md, "Jitter"). Only its x, y and w rows count.
    /// </summary>
    public Matrix4x4 MotionViewProj;

    /// <summary>The last drawn frame's view-projection without jitter and without translation, from its own eye.</summary>
    public Matrix4x4 PreviousViewProj;

    /// <summary>xyz: the last drawn frame's eye relative to this frame's eye, in m, in world axes; w: unused.</summary>
    public Vector4 PreviousEye;

    /// <summary>The last drawn frame's camera axes: right × tan(horizontal half-fov), up × tan(vertical half-fov), unit forward.</summary>
    public Vector4 PreviousCameraRight;

    public Vector4 PreviousCameraUp;
    public Vector4 PreviousCameraForward;

    /// <summary>The last drawn frame's render size: width, height, 1 / width, 1 / height.</summary>
    public Vector4 PreviousViewport;

    /// <summary>The last drawn frame's root axes in world axes, as a quaternion (x, y, z, w).</summary>
    public Vector4 PreviousRootRotation;

    /// <summary>The automatic geometry enclosures in each map's axes, relative to the eye, in meters.</summary>
    public MapBoxArray MapBoxes;
}

/// <summary>A projection enclosure. Minimum.w is 1 for a populated box and 0 for no geometry.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuMapBox
{
    public Vector4 Minimum;
    public Vector4 Maximum;
}

/// <summary>One enclosure per map, laid out as two std140 vec4 values per entry.</summary>
[InlineArray(EnvironmentChain.MaxMaps)]
internal struct MapBoxArray
{
    private GpuMapBox element;
}

/// <summary>
/// The atmosphere in the frame block (std140), 96 bytes: what the shaders of atmosphere.glsl and sky.glsl read
/// (docs/plans/atmosphere.md, "The renderer"). Without an atmosphere every value is 0 but the tint, which is 1, so the sky
/// is black, the haze lets everything through, there is no sun and no light takes a tint.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuAtmosphere
{
    /// <summary>xyz the planet's center relative to the interpolated eye, in m; w the planet's radius, in m.</summary>
    public Vector4 CenterRadius;

    /// <summary>rgb the sky, plain radiance in W/(m²·sr); w 1 / the scale height, in 1/m, 0 without an atmosphere.</summary>
    public Vector4 Sky;

    /// <summary>
    /// rgb the haze's light in full day, plain radiance in W/(m²·sr), which the shaders dim by the day factor at each hazed
    /// point; w 1 / the haze distance, in 1/m, 0 without a haze.
    /// </summary>
    public Vector4 Haze;

    /// <summary>rgb the tint of the sun's light at the surface, 1 without a tint; w unused.</summary>
    public Vector4 SunTint;

    /// <summary>xyz the sun's place relative to the interpolated eye, in m; w 1 with a sun, 0 without one.</summary>
    public Vector4 Sun;

    /// <summary>x the eye's height above the surface, in m; y the air's density share at the eye, from 0 to 1; zw unused.</summary>
    public Vector4 Eye;

    /// <summary>Whether the frame's air has a haze: 1 / the haze distance is above 0, the encoding that atmosphere.glsl reads too.</summary>
    public readonly bool HasHaze => Haze.W != 0;
}

/// <summary>One cascade set in the frame block (std140). 256 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuCascadeSet
{
    /// <summary>World to shadow clip space per cascade, row-vector form like the range matrices.</summary>
    public ShadowMatrices ViewProj;
}

/// <summary>The cascade sets of the frame block, laid out as a std140 <c>GpuCascadeSet[4]</c>.</summary>
[InlineArray(ShadowCascades.MaxSets)]
internal struct CascadeSetArray
{
    private GpuCascadeSet element;
}

/// <summary>One quaternion per map, laid out as a std140 <c>vec4[42]</c>.</summary>
[InlineArray(EnvironmentChain.MaxMaps)]
internal struct MapAxesArray
{
    private Vector4 element;
}

/// <summary>One matrix per shadow cascade, laid out as a std140 <c>mat4[4]</c>.</summary>
[InlineArray(ShadowCascades.Count)]
internal struct ShadowMatrices
{
    private Matrix4x4 element;
}

/// <summary>
/// The uniform block ScatterKernel of Shaders/skin_scatter.glsl (std140), which <see cref="ScatterImages.Kernel"/> holds: a
/// <see cref="ScatterKernel"/> as singles (docs/plans/skin-and-hair.md, "The two passes"). 532 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuScatterKernel
{
    /// <summary>Tap i: rgb its weight per channel, w its offset in m. The slots past the kernel's 2n + 1 taps hold zeros.</summary>
    public ScatterTapArray Taps;

    /// <summary>How many times wider than the measured profile the offsets are (<see cref="ScatterKernel.WidthScale"/>).</summary>
    public float WidthScale;
}

/// <summary>Room for the most taps of a scatter kernel, laid out as a std140 <c>vec4[33]</c>.</summary>
[InlineArray(ScatterImages.KernelTaps)]
internal struct ScatterTapArray
{
    private Vector4 element;
}

/// <summary>The push constants of every mesh pass (Shaders/draw.glsl). 80 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DrawPushConstants
{
    public Matrix4x4 ViewProj;
    public Vector4 Eye;
}

/// <summary>The push constants of the tone mapping pass.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ToneMapPushConstants
{
    public uint EncodeSrgb;

    /// <summary>The renderer's <see cref="Rendering.ToneCurve"/> as its number.</summary>
    public uint ToneCurve;

    /// <summary>The factor of the summed bloom levels, <see cref="Bloom.Strength"/>. Unread without bloom.</summary>
    public float GlowStrength;
}

/// <summary>The push constants of the bloom pass (Shaders/bloom_*.frag). Only its first draw reads them.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct BloomPushConstants
{
    /// <summary><see cref="Bloom.Threshold"/>, in the pre-exposed light of the HDR image.</summary>
    public float Threshold;

    /// <summary><see cref="Bloom.Knee"/>, a fraction of the threshold.</summary>
    public float Knee;

    /// <summary><see cref="Bloom.Levels"/>, which the prefilter divides the light by, so that the sum of the levels stays finite.</summary>
    public uint Levels;
}

/// <summary>The push constants of exposure_adapt.comp: the setting and the two approach factors of this draw, padded to 32 bytes.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
internal struct ExposureAdaptPushConstants
{
    public float DarkShare;
    public float BrightShare;
    public float MinStops;
    public float MaxStops;
    public float CompensationStops;

    /// <summary>f(Δt, IntoDarkSeconds): the share of the gap to the target that the exposure closes when it rises.</summary>
    public float IntoDarkFactor;

    /// <summary>f(Δt, IntoLightSeconds): the share when it falls.</summary>
    public float IntoLightFactor;
}

/// <summary>The push constants of the outline pass (Shaders/outline.frag).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OutlinePushConstants
{
    /// <summary>1 when the output format stores linear values and the shader must apply the sRGB curve, as the tone mapper does.</summary>
    public uint EncodeSrgb;
}

/// <summary>
/// The push constants of one dispatch of the shadow bounds, shadow_bounds.comp (<see cref="LocalShadowPass"/>): 128 bytes,
/// the push constant size that every Vulkan device offers.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ShadowBoundsPushConstants
{
    /// <summary>The drawn tiles of one dispatch: its workgroups' z runs from 0 to their count less 1.</summary>
    public const int MaxLayers = 32;

    /// <summary>The atlas layer of each drawn tile of the dispatch, each below <see cref="ShadowAtlas.Tiles"/>.</summary>
    public fixed uint Layers[MaxLayers];
}

/// <summary>The push constants of the surface test, surface_test.comp (<see cref="SurfaceTestPass"/>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SurfaceTestPushConstants
{
    /// <summary>1 when the frame resets every history (<see cref="FrameHistory.PendingFrame.Reset"/>), so that every tap fails, else 0.</summary>
    public uint Reset;
}

/// <summary>
/// The push constants of both dispatches of an accumulation, accumulate.glsl (<see cref="TemporalSignal"/>): the signal's
/// parameter set (<see cref="SignalParameters"/>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct AccumulatePushConstants
{
    public float Cap;
    public float SmoothCap;
    public float FastCap;
    public float HistoryFixFrames;
    public float ClampSigmas;
    public float AntiLagShare;

    /// <summary>The signal's light: 0 diffuse, 1 specular (<see cref="SignalLight"/>).</summary>
    public uint Light;

    public float ReflectedPlaceBelow;

    /// <summary>1 in a frame that resets every history, where no reprojection holds one; 0 otherwise.</summary>
    public uint Reset;

    public static AccumulatePushConstants Of(SignalParameters parameters, bool reset) =>
        new()
        {
            Cap = parameters.Cap,
            SmoothCap = parameters.SmoothCap,
            FastCap = parameters.FastCap,
            HistoryFixFrames = parameters.HistoryFixFrames,
            ClampSigmas = parameters.ClampSigmas,
            AntiLagShare = parameters.AntiLagShare,
            Light = (uint)parameters.Light,
            ReflectedPlaceBelow = parameters.ReflectedPlaceBelow,
            Reset = reset ? 1u : 0u,
        };
}

/// <summary>
/// The push constants of a wavelet dispatch of the diffuse filter, diffuse_filter_wavelet.comp (<see cref="DiffuseFilter"/>).
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct DiffuseFilterPushConstants
{
    /// <summary>The distance between the kernel's taps, in pixels: 2^i in dispatch i.</summary>
    public int Step;

    /// <summary>1 in the dispatch whose light the slow history takes (<see cref="DiffuseFilter.FeedbackDispatch"/>), else 0.</summary>
    public uint Feedback;
}

/// <summary>The push constants of the screen tracer's four passes, screen_trace.glsl (<see cref="ScreenTraceKernel"/>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ScreenTracePushConstants
{
    /// <summary>The draw's index since the last reset (<see cref="FrameHistory.PendingFrame.DrawIndex"/>), the second value of every draw.</summary>
    public uint DrawIndex;

    public float MaxRoughness;

    /// <summary><see cref="ScreenTrace.MaxRoughness"/> − <see cref="ScreenTrace.HandOverWidth"/>.</summary>
    public float HandOverStart;

    public uint ReflectionSteps;
    public uint DiffuseSteps;

    /// <summary>The share of the diffuse rays that the guided draw gives: <see cref="LightPyramid.GuidedShare"/>, or 0 with the guiding off.</summary>
    public float GuidedShare;

    /// <summary>
    /// The push constants of a draw of index <paramref name="drawIndex"/> under <paramref name="trace"/>, with the
    /// guided diffuse rays when <paramref name="guided"/>. The index wraps at 2^32 draws, more than two years at 60
    /// frames per second, which changes only the random numbers.
    /// </summary>
    public static ScreenTracePushConstants Of(ScreenTrace trace, long drawIndex, bool guided) =>
        new()
        {
            DrawIndex = unchecked((uint)drawIndex),
            MaxRoughness = trace.MaxRoughness,
            HandOverStart = trace.HandOverStart,
            ReflectionSteps = (uint)trace.ReflectionSteps,
            DiffuseSteps = (uint)trace.DiffuseSteps,
            GuidedShare = guided ? LightPyramid.GuidedShare : 0,
        };
}

/// <summary>The push constants of the depth pyramid's tail, depth_pyramid_tail.comp (<see cref="PyramidTail"/>).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct PyramidTailPushConstants
{
    /// <summary>The mips of the tail, from its first to 1 × 1: 1 to <see cref="PyramidTail.Mips"/>.</summary>
    public uint MipCount;
}

/// <summary>
/// The push constants of the light pass of an environment face (Shaders/environment_face_light.frag): the basis of the
/// face that it lights, each axis a <see cref="Vector4"/>, and the face's map. 52 bytes, inside the 80-byte range that
/// the face light layout shares with the scene layout.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FaceLightPushConstants
{
    /// <summary>xyz the face's forward axis, w 1 / W, which turns a pixel coordinate into the face's st.</summary>
    public Vector4 Forward;

    /// <summary>xyz the face's right, w 0.</summary>
    public Vector4 Right;

    /// <summary>xyz the face's up, w 0.</summary>
    public Vector4 Up;

    /// <summary>The map whose face the pass lights. The face keeps the lights of this map and of the maps outside it.</summary>
    public uint Map;
}

/// <summary>
/// One level of a dispatch of environment_prefilter.comp, k, in the binding of set 1 that its place in the dispatch
/// names. 16 bytes, a std430 struct of four scalars.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct EnvironmentPrefilterLevel
{
    /// <summary>The first workgroup of the dispatch that prefilters the level.</summary>
    public uint FirstGroup;

    /// <summary>W_k = W / 2^k: the texels along one side of each face of the level.</summary>
    public uint LevelSize;

    /// <summary>r_k = k / (L − 1): the roughness that the level holds.</summary>
    public float Roughness;

    /// <summary>The invocations that share one texel's samples, <see cref="PrefilterLevels.Lanes"/> of the level.</summary>
    public uint Lanes;
}

/// <summary>The levels of one dispatch of environment_prefilter.comp, a std430 array of four.</summary>
[InlineArray(PrefilterLevels.LevelsPerDispatch)]
internal struct EnvironmentPrefilterLevels
{
    private EnvironmentPrefilterLevel element;
}

/// <summary>
/// The push constants of environment_prefilter.comp, for the run of levels of the specular map that set 1 of
/// <see cref="DescriptorLayouts.EnvironmentPrefilterLayout"/> holds (<see cref="PrefilterLevels.LevelsOfDispatch"/>), of
/// map <see cref="Map"/>, of its faces <see cref="FirstFace"/> to <see cref="FirstFace"/> + <see cref="FaceCount"/> − 1.
/// 88 bytes. environment_irradiance.comp reads only those three.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct EnvironmentPrefilterPushConstants
{
    /// <summary>The levels in binding order. Those past <see cref="LevelCount"/> are unused.</summary>
    public EnvironmentPrefilterLevels Levels;

    /// <summary>The Hammersley samples per texel, <see cref="PrefilterLevels.SamplesPerTexel"/>.</summary>
    public uint Samples;

    /// <summary>The levels of the dispatch, 1 to <see cref="PrefilterLevels.LevelsPerDispatch"/>.</summary>
    public uint LevelCount;

    /// <summary>
    /// The workgroups of the dispatch's levels. Its grid (<see cref="ComputeLimits.Grid"/>) may hold a few more, which
    /// return at once.
    /// </summary>
    public uint GroupCount;

    /// <summary>
    /// The map whose layers, 6 · Map + <see cref="FirstFace"/> on, of the specular maps or of the irradiance cubes the
    /// dispatch writes.
    /// </summary>
    public uint Map;

    /// <summary>The first face, 0 to 5, of the run of faces that the dispatch writes (ARCHITECTURE.md, "The prefilter").</summary>
    public uint FirstFace;

    /// <summary>The faces of the run, 1 to 6, at most 6 − <see cref="FirstFace"/>.</summary>
    public uint FaceCount;
}

/// <summary>
/// The push constants of environment_downsample.comp, for the run of levels of the radiance map that bindings 1 to 3 of
/// set 1 hold, k + 1 to k + 3, made from level k in binding 0 (<see cref="PrefilterLevels.LevelsOfDownsample"/>), of map
/// <see cref="Map"/>. 12 bytes, which fit in the prefilter layout's range of
/// <see cref="EnvironmentPrefilterPushConstants"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct EnvironmentDownsamplePushConstants
{
    /// <summary>W / 2^(k + 1): the texels along one side of each face of the run's first level.</summary>
    public uint LevelSize;

    /// <summary>The levels of the run, 1 to <see cref="PrefilterLevels.LevelsPerDownsample"/>.</summary>
    public uint LevelCount;

    /// <summary>The map whose layers 6 · Map to 6 · Map + 5 the dispatch reads and writes.</summary>
    public uint Map;
}
