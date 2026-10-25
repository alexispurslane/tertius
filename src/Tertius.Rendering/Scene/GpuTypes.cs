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

/// <summary>The push constants of the occlusion passes (Shaders/occlusion.glsl). 64 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OcclusionPushConstants
{
    /// <summary>xyz the unit right of the camera in world axes, w the tangent of half the horizontal field of view.</summary>
    public Vector4 CameraRight;

    /// <summary>xyz the unit up, w the tangent of half the vertical field of view.</summary>
    public Vector4 CameraUp;

    /// <summary>xyz the unit forward, w 0.</summary>
    public Vector4 CameraForward;

    public float RadiusMeters;
    public uint Slices;
    public uint StepsPerSide;

    /// <summary>1 in the last denoise dispatch, 0 otherwise.</summary>
    public uint LastDenoise;

    /// <summary>
    /// The push constants of one dispatch. The camera is the one that the frame constants hold, so the passes and the
    /// lighting pass never disagree about it.
    /// </summary>
    public static OcclusionPushConstants For(in CameraView camera, GroundTruthOcclusion setting, bool lastDenoise) =>
        new()
        {
            CameraRight = new(camera.Right, camera.TanHalfX),
            CameraUp = new(camera.Up, camera.TanHalfY),
            CameraForward = new(camera.Forward, 0),
            RadiusMeters = setting.RadiusMeters,
            Slices = (uint)setting.Slices,
            StepsPerSide = (uint)setting.StepsPerSide,
            LastDenoise = lastDenoise ? 1u : 0u,
        };
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

/// <summary>The push constants of the three SMAA passes (Shaders/smaa.glsl).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SmaaPushConstants
{
    /// <summary>1 / width, 1 / height, width and height of the output, in pixels: the header's SMAA_RT_METRICS.</summary>
    public Vector4 Metrics;

    /// <summary>1 when the output format is UNORM and the blending pass must apply the sRGB curve itself.</summary>
    public uint EncodeSrgb;
}

/// <summary>The push constants of the outline pass (Shaders/outline.frag).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct OutlinePushConstants
{
    /// <summary>1 when the output format stores linear values and the shader must apply the sRGB curve, as the tone mapper does.</summary>
    public uint EncodeSrgb;
}

/// <summary>
/// The push constants of the environment sky pass (Shaders/environment_sky.frag): the basis of the face it draws, each
/// axis a <see cref="Vector4"/> with w 0. 48 bytes, inside the scene layout's 80-byte range.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct SkyFacePushConstants
{
    public Vector4 Forward;
    public Vector4 Right;
    public Vector4 Up;
}

/// <summary>The push constants of environment_prefilter.comp. 16 bytes. environment_irradiance.comp reads none.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct EnvironmentPrefilterPushConstants
{
    /// <summary>The level of the specular map that the dispatch writes, k.</summary>
    public uint Level;

    /// <summary>W_k = W / 2^k: the texels along one side of each face of that level.</summary>
    public uint LevelSize;

    /// <summary>r_k = k / (L − 1): the roughness that the level holds.</summary>
    public float Roughness;

    /// <summary>The Hammersley samples per texel, <see cref="PrefilterLevels.SamplesPerTexel"/>.</summary>
    public uint Samples;
}

/// <summary>
/// The push constants of environment_downsample.comp, 8 bytes, which fit in the prefilter layout's range of
/// <see cref="EnvironmentPrefilterPushConstants"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct EnvironmentDownsamplePushConstants
{
    /// <summary>The level of the radiance map that the dispatch writes, k ≥ 1, from level k − 1.</summary>
    public uint Level;

    /// <summary>W / 2^k: the texels along one side of each face of that level.</summary>
    public uint LevelSize;
}
