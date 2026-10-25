using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tertius.Rendering;

// C# mirrors of the shader structs in Shaders/frame.glsl. Layouts follow std430 (storage buffers) and std140 (the
// frame block). Change both sides together; RendererLayoutTests checks the sizes.

/// <summary>One drawn instance in the instance storage buffer. 80 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
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

    public static GpuInstance From(
        Vector3 position,
        Quaternion rotation,
        Vector3 scale,
        uint material,
        HemisphereUp hemisphere = default
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
        return new()
        {
            ModelRow0 = new(x: axisX.X, y: axisY.X, z: axisZ.X, w: position.X),
            ModelRow1 = new(x: axisX.Y, y: axisY.Y, z: axisZ.Y, w: position.Y),
            ModelRow2 = new(x: axisX.Z, y: axisY.Z, z: axisZ.Z, w: position.Z),
            InverseScaleSquared = new(InverseSquare(scale.X), InverseSquare(scale.Y), InverseSquare(scale.Z)),
            Material = material,
            Hemisphere = hemisphere switch
            {
                UpAlong { Direction: var direction } => new(direction, 1),
                // The center moves with the instance, so it is exact wherever the instance is interpolated to.
                UpAwayFrom { Center: var center } => new(position + center, 2),
                null => Vector4.Zero,
            },
        };
    }

    /// <summary>
    /// An instance whose model matrix has the given columns, for draws that are not meshes: an impostor billboard
    /// maps its quad space through it and keeps its atlas layer in <see cref="Material"/>.
    /// </summary>
    public static GpuInstance FromAxes(Vector3 axisX, Vector3 axisY, Vector3 axisZ, Vector3 origin, uint material) =>
        new()
        {
            ModelRow0 = new(x: axisX.X, y: axisY.X, z: axisZ.X, w: origin.X),
            ModelRow1 = new(x: axisX.Y, y: axisY.Y, z: axisZ.Y, w: origin.Y),
            ModelRow2 = new(x: axisX.Z, y: axisY.Z, z: axisZ.Z, w: origin.Z),
            Material = material,
        };

    private static float InverseSquare(float s) => s != 0 ? 1f / (s * s) : 0f;
}

/// <summary>One material in the material storage buffer (std430). 52 bytes of fields, padded to 64, the struct's alignment of 16.</summary>
[StructLayout(LayoutKind.Sequential, Size = 64)]
internal struct GpuMaterial
{
    public Vector4 BaseColor;
    public Vector3 Emissive;
    public float Metallic;

    /// <summary>r₀, before the floor: the floor is the BRDF's.</summary>
    public float Roughness;
    public float Reflectance;

    /// <summary>Bindless indices, already resolved to the default textures.</summary>
    public uint BaseColorMap;
    public uint NormalMap;
    public uint MetallicRoughnessMap;
}

/// <summary>One point or spot light in the light storage buffer. 64 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuLight
{
    /// <summary>xyz camera-relative position, w range.</summary>
    public Vector4 PositionRange;

    /// <summary>rgb color × intensity, w kind: 0 point, 1 spot.</summary>
    public Vector4 RadianceKind;

    /// <summary>xyz unit spot direction, w cos(outer half-angle). Zero for a point light, which has no cone.</summary>
    public Vector4 DirectionCosOuter;

    /// <summary>x cos(inner half-angle). Zero for a point light.</summary>
    public Vector4 Spot;
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
/// One puff to draw, in the puff storage buffer (std430, binding 7 of the frame set). 32 bytes. The batcher writes one for
/// each puff that it keeps, in the order of the transparent pass's runs.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
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
}

/// <summary>The frame uniform block (std140). 720 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GpuFrameConstants
{
    // System.Numerics matrices are row-major with row vectors. Their bytes read as a column-major GLSL mat4 give
    // the transpose, which is the matrix for column vectors. So M_numerics = (P · V)ᵀ uploads as P · V.
    public Matrix4x4 FarViewProj;
    public Matrix4x4 MiddleViewProj;
    public Matrix4x4 NearViewProj;
    public Vector4 CameraRight;
    public Vector4 CameraUp;
    public Vector4 CameraForward;
    public Vector4 SunDirection;
    public Vector4 SunRadiance;
    public Vector4 AmbientSky;
    public Vector4 AmbientGround;
    public Vector4 Viewport;
    public Vector4 Params;
    public uint LightCount;
    public uint OccluderCount;

    /// <summary>Sun shadow cascades to sample: <see cref="ShadowCascades.Count"/>, or 0 when no cascade was drawn.</summary>
    public uint ShadowCascadeCount;

    /// <summary>How many far shadows, at the front of the list, belong to casters that the shadow maps also draw.</summary>
    public uint MappedOccluderCount;

    /// <summary>World to shadow clip space per cascade, row-vector form like the range matrices.</summary>
    public ShadowMatrices ShadowViewProj;

    /// <summary>The view depth where each cascade ends.</summary>
    public Vector4 ShadowSplits;

    /// <summary>The view depth where each cascade starts to blend into the next one (or, for the last, to no shadow).</summary>
    public Vector4 ShadowBlendStart;

    /// <summary>The world size of one shadow map texel in each cascade.</summary>
    public Vector4 ShadowTexel;

    /// <summary>
    /// xyz: the light-space x axis in world space (the z axis is the sun direction). w: the cascade boxes' lateral
    /// half-extent over their depth half-range, which turns a light-space slope into depth per uv.
    /// </summary>
    public Vector4 ShadowAxisX;

    /// <summary>xyz: the light-space y axis in world space.</summary>
    public Vector4 ShadowAxisY;

    /// <summary>x: 1 / map size; y: normal offset in texels; z: constant depth bias; w: smallest cosine for the receiver-plane slope.</summary>
    public Vector4 ShadowParams;

    /// <summary>The root frame's axes in world axes, interpolated, as a quaternion (x, y, z, w). The star field is fixed to them.</summary>
    public Vector4 RootRotation;
}

/// <summary>One matrix per shadow cascade, laid out as a std140 <c>mat4[4]</c>.</summary>
[InlineArray(ShadowCascades.Count)]
internal struct ShadowMatrices
{
    private Matrix4x4 element;
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
    public float Exposure;
    public uint EncodeSrgb;

    /// <summary>The renderer's <see cref="Rendering.ToneCurve"/> as its number.</summary>
    public uint ToneCurve;
}

/// <summary>The push constants of the outline pass (Shaders/outline.frag).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OutlinePushConstants
{
    /// <summary>1 when the output format stores linear values and the shader must apply the sRGB curve, as the tone mapper does.</summary>
    public uint EncodeSrgb;
}
