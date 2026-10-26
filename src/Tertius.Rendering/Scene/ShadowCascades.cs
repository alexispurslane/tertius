using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Rendering;

/// <summary>The light space of a light's shadow: <see cref="Z"/> points toward the light, <see cref="X"/> and
/// <see cref="Y"/> span the map.</summary>
internal readonly record struct LightBasis(Vector3 X, Vector3 Y, Vector3 Z)
{
    /// <summary>
    /// A basis that depends only on the direction toward the light, a vector that is not zero, so it is the same every
    /// frame while the light stands still.
    /// </summary>
    public static LightBasis FromDirection(Vector3 towardLight)
    {
        var z = Vector3.Normalize(towardLight);
        // Any fixed reference that is not parallel to z. The switch happens only for a light almost straight overhead
        // (or underfoot), where the basis turns once.
        Vector3 reference = MathF.Abs(z.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX;
        var x = Vector3.Normalize(Vector3.Cross(reference, z));
        return new(x, Vector3.Cross(z, x), z);
    }

    public Vector3 ToLight(Vector3 world) => new(Vector3.Dot(world, X), Vector3.Dot(world, Y), Vector3.Dot(world, Z));
}

/// <summary>One cascade of a light's cascade set, fitted for one frame.</summary>
internal struct ShadowCascade
{
    /// <summary>
    /// Camera-relative world space to shadow clip space, in System.Numerics row-vector form. Clip x and y are in
    /// [−1, 1] across the map; z is reversed: 1 on the light's side of the cascade box and 0 on the far side.
    /// </summary>
    public Matrix4x4 ViewProjection;

    /// <summary>The center of the cascade box in light space. x and y lie on the world-fixed texel grid.</summary>
    public Vector3 Center;

    /// <summary>Half the side of the cascade box across the light: x and y span 2 · HalfExtent.</summary>
    public float HalfExtent;

    /// <summary>Half the depth of the cascade box along the light: <see cref="HalfExtent"/> times the depth range scale.</summary>
    public float DepthHalfRange;

    /// <summary>The radius of the bounding sphere of the view slice that this cascade covers.</summary>
    public float Radius;

    /// <summary>The world size of one texel.</summary>
    public float TexelSize;

    /// <summary>The view depth where the slice starts: the previous cascade's blend start (the near plane for the first).</summary>
    public float NearDepth;

    /// <summary>The view depth where the slice ends: this cascade's split.</summary>
    public float FarDepth;
}

/// <summary>The cascades of one cascade set in one frame.</summary>
[InlineArray(ShadowCascades.Count)]
internal struct ShadowCascadeArray
{
    private ShadowCascade element;
}

/// <summary>
/// Cascaded shadow maps for the far lights (ARCHITECTURE.md, Shadows): each cascade set is four cascades over the first
/// 2 km of view depth, fitted along its own light's direction.
/// </summary>
/// <remarks>
/// <para><b>Splits.</b> The practical split scheme (Zhang et al. 2006) blends the logarithmic split n·(f/n)^(i/N), which
/// keeps the texel-to-pixel ratio constant, with the uniform split n + (f − n)·i/N: C_i = λ·log + (1 − λ)·uniform, with
/// n = 5 cm (the camera near plane), f = 2 km, N = 4 and λ = 0.9. That gives cascades ending at about 51 m, 109 m,
/// 277 m and 2 km. With 2048² maps and a 60° field of view, one texel is then about one pixel at 720p at the far end of
/// each cascade (6 cm, 13 cm, 32 cm and 2.3 m).</para>
/// <para><b>Blending.</b> Over the last <see cref="BlendFraction"/> of each cascade's depth interval the lighting pass
/// blends into the next cascade. The last cascade fades out to no shadow, so nothing beyond 2 km is shadowed by the
/// maps; planets and moons shadow through the analytic occluder spheres instead.</para>
/// <para><b>Stability.</b> Each cascade is the bounding sphere of its view slice (extended back over the previous
/// cascade's blend band). The sphere's radius depends only on the slice depths, the field of view and the aspect
/// ratio, so turning the camera never changes the texel size. The sphere's center is then snapped, in light space,
/// to a texel grid that is fixed in the world: positions are camera-relative, so the grid passes through a
/// world-fixed anchor, the origin of the camera's reference frame (<see cref="Anchor"/>). A static shadow therefore
/// keeps the same texels while the camera moves and turns, and does not shimmer.</para>
/// <para><b>Depth.</b> The cascade box is centered on the snapped center and is <c>depthRangeScale</c> times deeper
/// than it is wide (16 for D32). Casters between the box and the light are "pancaked" onto its light-side face by depth
/// clamping in the shadow pass. The extra depth works around what was measured on an Apple M1 Max through MoltenVK:
/// when a large caster that is steep to the light (such as the ground under a low sun) is clipped at the edges of
/// the viewport and its depth there falls outside [0, 1], it is stored with a flattened, wrong depth plane, as if the
/// depths of the clipped vertices were clamped before the plane was set up. With a cube-shaped box the ground then
/// shadowed itself. With the box 16 times deeper, the depth at the viewport edges stays in [0, 1] for casters up to
/// about 84 degrees to the light, and the stored depth matches the analytic plane. D32 keeps under 5 mm of
/// precision even over the 76 km depth of the last cascade.</para>
/// </remarks>
internal static class ShadowCascades
{
    public const int Count = 4;

    /// <summary>The most cascade sets that a frame holds, the frame block's array of them: <see cref="RendererOptions.MaxCascadedLights"/>.</summary>
    public const int MaxSets = RendererOptions.MaxCascadedLights;

    /// <summary>Shadow maps cover view depths up to here, and only casters whose bounding sphere comes this close to the camera are drawn.</summary>
    public const float Distance = 2_000f;

    /// <summary>The weight of the logarithmic split against the uniform one.</summary>
    public const float SplitLambda = 0.9f;

    /// <summary>The fraction of each cascade's depth interval over which it blends into the next.</summary>
    public const float BlendFraction = 0.1f;

    /// <summary>
    /// Texels kept free around the bounding sphere on every side: half a texel of snapping, the 4 × 4 PCF footprint
    /// (2 texels) and the normal offset (1 texel) stay inside the map.
    /// </summary>
    public const int MarginTexels = 4;

    private static readonly float[] Splits = ComputeSplits();

    /// <summary>
    /// True when the shadow maps draw a caster with bounding radius <paramref name="radius"/> whose center is
    /// <paramref name="distance"/> meters from the camera: its bounding sphere comes within <see cref="Distance"/>, however
    /// big it is, such as a ground plane or a planet under the camera. Every caster is also a far shadow, if it is big enough
    /// to notice; for a caster that the maps draw, that sphere counts only where the maps end.
    /// </summary>
    public static bool TakesCaster(float distance, float radius) => distance - radius <= Distance;

    /// <summary>The view depth where cascade <paramref name="cascade"/> ends.</summary>
    public static float Split(int cascade) => Splits[cascade];

    /// <summary>The view depth where cascade <paramref name="cascade"/> starts to blend into the next one.</summary>
    public static float BlendStart(int cascade)
    {
        float start = cascade == 0 ? DepthRanges.NearPlane : Splits[cascade - 1];
        return Splits[cascade] - BlendFraction * (Splits[cascade] - start);
    }

    private static float[] ComputeSplits()
    {
        float[] splits = new float[Count];
        double near = DepthRanges.NearPlane,
            far = Distance;
        for (int i = 1; i <= Count; i++)
        {
            double t = (double)i / Count;
            double logarithmic = near * Math.Pow(far / near, t);
            double uniform = near + (far - near) * t;
            splits[i - 1] = (float)(SplitLambda * logarithmic + (1 - SplitLambda) * uniform);
        }

        splits[Count - 1] = Distance;
        return splits;
    }

    /// <summary>
    /// The smallest sphere that holds the view frustum between view depths <paramref name="near"/> and
    /// <paramref name="far"/>. Its center is on the view axis at depth <c>CenterDepth</c>.
    /// </summary>
    public static (float CenterDepth, float Radius) BoundingSphere(
        float near,
        float far,
        float tanHalfX,
        float tanHalfY
    )
    {
        // The slice's corners are at lateral distance depth · k from the axis. The center that is equally far from
        // the near and far corners is at (f + n)(1 + k²) / 2. If that lies beyond the far plane, the far corners
        // alone decide and the sphere is centered on the far cap.
        double k2 = (double)tanHalfX * tanHalfX + (double)tanHalfY * tanHalfY;
        double center = Math.Min(0.5 * (far + near) * (1 + k2), far);
        double radius = Math.Sqrt((far - center) * (far - center) + far * far * k2);
        return ((float)center, (float)radius);
    }

    /// <summary>
    /// The anchor of the texel grid for one frame: the origin of the camera's reference frame (ARCHITECTURE.md,
    /// Shadows), in camera-relative world space. The render origin is at <paramref name="originInFrame"/> in the
    /// frame (<see cref="RenderSnapshot.OriginInFrame"/>), in the same axes as every snapshot position, and the
    /// interpolated eye is <paramref name="cameraOffset"/> from the render origin. So the frame origin is at
    /// −originInFrame − cameraOffset.
    /// </summary>
    /// <remarks>
    /// It is computed from the snapshot alone. Nothing carries over from earlier ticks, so a tick that the render
    /// thread never sees cannot shift the grid, and when the camera changes frame (a new
    /// <see cref="RenderSnapshot.OriginFrameId"/>) the grid moves to the new frame's origin at once, with no state
    /// from the old frame left to reset. The result is in doubles, and <see cref="Fit"/> snaps against it in doubles:
    /// rounding moves the grid by about 10⁻¹⁶ of the distance to the frame origin, well under a hundredth of a texel
    /// while the camera is within 10¹¹ m (an orbit around a star) of it. A non-finite origin falls back to the
    /// render origin, which keeps the cascades finite.
    /// </remarks>
    public static Double3 Anchor(Double3 originInFrame, Vector3 cameraOffset)
    {
        bool finite =
            double.IsFinite(originInFrame.X) && double.IsFinite(originInFrame.Y) && double.IsFinite(originInFrame.Z);
        return (finite ? -originInFrame : Double3.Zero) - cameraOffset;
    }

    /// <summary>
    /// Fits every cascade for this frame's camera.
    /// </summary>
    /// <param name="camera">The interpolated camera. Camera-relative world space has the eye at the origin.</param>
    /// <param name="light">The light basis of the set's light.</param>
    /// <param name="mapSize">The side of each cascade's shadow map in texels.</param>
    /// <param name="depthRangeScale">How many times deeper than wide each cascade box is.</param>
    /// <param name="anchor">A world-fixed point in camera-relative world space (see <see cref="Anchor"/>). The texel grid passes through it.</param>
    /// <param name="cascades">Receives the cascades.</param>
    public static void Fit(
        in CameraView camera,
        in LightBasis light,
        int mapSize,
        float depthRangeScale,
        Double3 anchor,
        ref ShadowCascadeArray cascades
    )
    {
        double anchorX = Double3.Dot(anchor, light.X);
        double anchorY = Double3.Dot(anchor, light.Y);
        for (int i = 0; i < Count; i++)
        {
            float near = i == 0 ? DepthRanges.NearPlane : BlendStart(i - 1);
            float far = Splits[i];
            (float centerDepth, float radius) = BoundingSphere(
                near: near,
                far: far,
                tanHalfX: camera.TanHalfX,
                tanHalfY: camera.TanHalfY
            );
            float texel = 2f * radius / (mapSize - 2 * MarginTexels);
            float halfExtent = 0.5f * texel * mapSize;

            Vector3 center = light.ToLight(camera.Forward * centerDepth);
            // Snap in double precision relative to the anchor, so the grid is the same in every frame however far
            // the anchor is from the current origin.
            center.X = (float)(anchorX + Math.Floor((center.X - anchorX) / texel + 0.5) * texel);
            center.Y = (float)(anchorY + Math.Floor((center.Y - anchorY) / texel + 0.5) * texel);

            ref ShadowCascade cascade = ref cascades[i];
            cascade.Center = center;
            cascade.HalfExtent = halfExtent;
            cascade.DepthHalfRange = halfExtent * depthRangeScale;
            cascade.Radius = radius;
            cascade.TexelSize = texel;
            cascade.NearDepth = near;
            cascade.FarDepth = far;
            cascade.ViewProjection = ViewProjection(
                light: light,
                center: center,
                halfExtent: halfExtent,
                depthHalfRange: cascade.DepthHalfRange
            );
        }
    }

    /// <summary>
    /// The orthographic world-to-clip matrix of a cascade box, in row-vector form: x and y map
    /// [center − h, center + h] to [−1, 1]; depth maps the light's face (z = center + d) to 1 and the far face
    /// (z = center − d) to 0.
    /// </summary>
    public static Matrix4x4 ViewProjection(in LightBasis light, Vector3 center, float halfExtent, float depthHalfRange)
    {
        float lateral = 1f / halfExtent;
        float depth = 0.5f / depthHalfRange;
        Vector3 x = light.X * lateral,
            y = light.Y * lateral,
            z = light.Z * depth;
        return new(
            m11: x.X,
            m12: y.X,
            m13: z.X,
            m14: 0f,
            m21: x.Y,
            m22: y.Y,
            m23: z.Y,
            m24: 0f,
            m31: x.Z,
            m32: y.Z,
            m33: z.Z,
            m34: 0f,
            m41: -center.X * lateral,
            m42: -center.Y * lateral,
            m43: 0.5f - center.Z * depth,
            m44: 1f
        );
    }
}
