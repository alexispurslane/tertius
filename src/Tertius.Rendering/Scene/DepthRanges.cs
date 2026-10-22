using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The three depth ranges from ARCHITECTURE.md, drawn far to near into one G-buffer with the depth buffer cleared
/// between them. Indices follow the draw order.
/// </summary>
/// <remarks>
/// <para>Each range is a slab of view depth (distance along the view axis). It reaches 1% past the nominal boundaries,
/// so neighboring slabs overlap and a surface exactly on a boundary is never cut off by both.</para>
/// <para>The far boundary of a slab is not a clipping plane. Every range uses an infinite reversed-Z projection from
/// its near plane, and the depth buffer is cleared to the depth of the slab's far boundary
/// (<see cref="ClearDepth"/>), so with compare GREATER anything beyond it fails the depth test. The result is the same
/// as clipping, without discards. The reason was measured on an Apple M1 Max through MoltenVK: a triangle that has
/// to be clipped against both the near and the far plane (a large ground plane that reaches behind the camera and
/// past 2 km) was rasterized with wrong depth and hid objects standing in front of it. Clipping against either plane
/// alone was correct.</para>
/// <para>Range rule for opaque objects: an object whose bounding sphere spans view depths [z − r, z + r] is drawn in
/// every range whose slab it overlaps, walking from near to far and stopping at the first slab whose far boundary is
/// beyond z + r. Each draw is cut off at its slab's boundaries, so the pieces together cover the object; where two
/// pieces overlap, the nearer range draws last and both pieces show the same surface. Nothing is lost at a boundary,
/// and an object that fits in one slab is drawn once.</para>
/// <para>Transparent objects do not write depth, so the range only sets their z. Each one is drawn once, in the range
/// that holds its center, with depth clamping on, so it is not clipped at that range's near plane either.</para>
/// </remarks>
internal static class DepthRanges
{
    public const int Far = 0;
    public const int Middle = 1;
    public const int Near = 2;
    public const int Count = 3;

    /// <summary>The camera near plane: 5 cm.</summary>
    public const float NearPlane = 0.05f;

    /// <summary>The boundary between the near and middle ranges: 2 km.</summary>
    public const float NearMiddle = 2_000f;

    /// <summary>The boundary between the middle and far ranges: 2,000 km.</summary>
    public const float MiddleFar = 2_000_000f;

    /// <summary>How far each slab reaches past a boundary, as a fraction of the boundary distance.</summary>
    public const float Overlap = 0.01f;

    /// <summary>The near plane of a range's projection.</summary>
    public static float NearOf(int range) => range switch
    {
        Near => NearPlane,
        Middle => NearMiddle * (1 - Overlap),
        Far => MiddleFar * (1 - Overlap),
        _ => throw new ArgumentOutOfRangeException(nameof(range))
    };

    /// <summary>The far boundary of a range. The far range is infinite.</summary>
    public static float FarOf(int range) => range switch
    {
        Near => NearMiddle * (1 + Overlap),
        Middle => MiddleFar * (1 + Overlap),
        Far => float.PositiveInfinity,
        _ => throw new ArgumentOutOfRangeException(nameof(range))
    };

    /// <summary>
    /// The depth a range's depth buffer is cleared to: the depth of its far boundary under <see cref="RangeProjection"/>,
    /// so that nothing beyond it passes the GREATER depth test. 0 for the infinite far range.
    /// </summary>
    public static float ClearDepth(int range)
    {
        float far = FarOf(range);
        return float.IsPositiveInfinity(far) ? 0f : NearOf(range) / far;
    }

    /// <summary>The projection the geometry passes draw a range with: infinite reversed-Z from the range's near plane.</summary>
    public static Matrix4x4 RangeProjection(int range, float verticalFov, float aspect) =>
        Projection(verticalFov, aspect, NearOf(range), float.PositiveInfinity);

    /// <summary>The range that holds a view depth by the nominal boundaries.</summary>
    public static int Containing(float depth) =>
        depth < NearMiddle ? Near : depth < MiddleFar ? Middle : Far;

    /// <summary>
    /// The ranges an opaque sphere at view depth <paramref name="depth"/> with radius <paramref name="radius"/> is
    /// drawn in, as a bit mask (bit i for range i). See the remarks on <see cref="DepthRanges"/>.
    /// </summary>
    public static int OpaqueMask(float depth, float radius)
    {
        float min = depth - radius;
        float max = depth + radius;
        int mask = 0;
        for (int range = Near; range >= Far; range--)
        {
            float near = NearOf(range);
            float far = FarOf(range);
            if (max < near || min > far)
                continue;
            mask |= 1 << range;
            // The slabs drawn so far chain without gaps from the nearest one this object touches, so they cover
            // everything up to this slab's far plane.
            if (max <= far)
                break;
        }

        return mask;
    }

    /// <summary>
    /// The reversed-Z perspective projection of a range for Vulkan, in System.Numerics row-vector form. View space
    /// looks down −Z with +Y up. The result maps depth <paramref name="near"/> to 1 and <paramref name="far"/> to 0
    /// (or approaches 0 at infinity for an infinite far plane) and flips y for Vulkan's y-down clip space.
    /// </summary>
    public static Matrix4x4 Projection(float verticalFov, float aspect, float near, float far)
    {
        float sy = 1f / MathF.Tan(verticalFov * 0.5f);
        float sx = sy / aspect;
        // Clip z = a·z + b and w = −z. depth = (a·z + b) / −z is 1 at z = −near and 0 at z = −far.
        float a, b;
        if (float.IsPositiveInfinity(far))
        {
            a = 0f;
            b = near;
        }
        else
        {
            a = near / (far - near);
            b = near * far / (far - near);
        }

        return new(
            sx, 0, 0, 0,
            0, -sy, 0, 0,
            0, 0, a, -1,
            0, 0, b, 0);
    }
}
