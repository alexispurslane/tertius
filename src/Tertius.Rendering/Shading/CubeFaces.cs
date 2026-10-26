using System.Numerics;

namespace Tertius.Rendering;

/// <summary>A face of a cube map, in the layer order of Vulkan's cube images.</summary>
internal enum CubeFace
{
    PositiveX,
    NegativeX,
    PositiveY,
    NegativeY,
    PositiveZ,
    NegativeZ,
}

/// <summary>A set of cube faces, bit i for layer i.</summary>
[Flags]
internal enum FaceMask : byte
{
    None = 0,
    PositiveX = 1,
    NegativeX = 2,
    PositiveY = 4,
    NegativeY = 8,
    PositiveZ = 16,
    NegativeZ = 32,
    All = 63,
}

/// <summary>
/// The axes of one face in camera-relative world axes (docs/plans/environment-map.md, "The faces and their axes"):
/// <paramref name="Right"/> is the way the texel column s grows, <paramref name="Up"/> the way the row t falls.
/// </summary>
internal readonly record struct FaceBasis(Vector3 Forward, Vector3 Right, Vector3 Up);

/// <summary>The six faces of a cube map, after Vulkan's table of cube map face selection. cube_faces.glsl is the GLSL copy.</summary>
internal static class CubeFaces
{
    public const int Count = 6;

    /// <summary>
    /// The forward, right and up axes of <paramref name="face"/> in the map's own axes, after the table of the doc. Each
    /// zero is +0, as a turn by the identity gives it, so the identity turns the table to its own bits.
    /// </summary>
    public static FaceBasis Basis(CubeFace face) =>
        face switch
        {
            CubeFace.PositiveX => new(Vector3.UnitX, new(0, 0, -1), Vector3.UnitY),
            CubeFace.NegativeX => new(new(-1, 0, 0), Vector3.UnitZ, Vector3.UnitY),
            CubeFace.PositiveY => new(Vector3.UnitY, Vector3.UnitX, new(0, 0, -1)),
            CubeFace.NegativeY => new(new(0, -1, 0), Vector3.UnitX, Vector3.UnitZ),
            CubeFace.PositiveZ => new(Vector3.UnitZ, Vector3.UnitX, Vector3.UnitY),
            CubeFace.NegativeZ => new(new(0, 0, -1), new(-1, 0, 0), Vector3.UnitY),
        };

    /// <summary>
    /// The axes of <paramref name="face"/> of a map that lies in the axes <paramref name="axes"/>, in world axes: the
    /// table's forward, right and up turned by <paramref name="axes"/> (docs/plans/environment-maps-per-frame.md, decision
    /// 9). The identity gives the table's bits.
    /// </summary>
    /// <param name="axes">The map's frame's axes in world axes, a unit quaternion.</param>
    public static FaceBasis Basis(CubeFace face, Quaternion axes)
    {
        FaceBasis inFrame = Basis(face);
        return new(
            Forward: Vector3.Transform(inFrame.Forward, axes),
            Right: Vector3.Transform(inFrame.Right, axes),
            Up: Vector3.Transform(inFrame.Up, axes)
        );
    }

    /// <summary>The set that holds only <paramref name="face"/>.</summary>
    public static FaceMask Of(CubeFace face) => (FaceMask)(1 << (int)face);

    /// <summary>The unit direction through the point (<paramref name="s"/>, <paramref name="t"/>) of a face, each in [0, 1].</summary>
    public static Vector3 Direction(CubeFace face, float s, float t)
    {
        FaceBasis basis = Basis(face);
        return Vector3.Normalize(basis.Forward + (2 * s - 1) * basis.Right + (1 - 2 * t) * basis.Up);
    }

    /// <summary>
    /// The face that a cube sample in <paramref name="direction"/> reads, and the point of it, by the major axis. A tie
    /// picks x before y before z.
    /// </summary>
    public static (CubeFace Face, float S, float T) Locate(Vector3 direction)
    {
        var size = Vector3.Abs(direction);
        CubeFace face;
        if (size.X >= size.Y && size.X >= size.Z)
            face = direction.X >= 0 ? CubeFace.PositiveX : CubeFace.NegativeX;
        else if (size.Y >= size.Z)
            face = direction.Y >= 0 ? CubeFace.PositiveY : CubeFace.NegativeY;
        else
            face = direction.Z >= 0 ? CubeFace.PositiveZ : CubeFace.NegativeZ;

        FaceBasis basis = Basis(face);
        float depth = Vector3.Dot(direction, basis.Forward);
        float s = 0.5f * (Vector3.Dot(direction, basis.Right) / depth + 1);
        float t = 0.5f * (1 - Vector3.Dot(direction, basis.Up) / depth);
        return (face, s, t);
    }

    /// <summary>
    /// The exact solid angle, in sr, of texel (<paramref name="x"/>, <paramref name="y"/>) of a face of
    /// <paramref name="size"/> texels on a side (Driscoll 2012, "Cubemap Texel Solid Angle").
    /// </summary>
    public static double TexelSolidAngle(int x, int y, int size)
    {
        double x0 = 2.0 * x / size - 1;
        double x1 = 2.0 * (x + 1) / size - 1;
        double y0 = 2.0 * y / size - 1;
        double y1 = 2.0 * (y + 1) / size - 1;
        return CornerArea(x0, y0) - CornerArea(x0, y1) - CornerArea(x1, y0) + CornerArea(x1, y1);
    }

    /// <summary>
    /// The view matrix of a face in row-vector form: x along right, y along up, and view space looking down −Z along
    /// forward.
    /// </summary>
    public static Matrix4x4 View(FaceBasis basis) =>
        new(
            m11: basis.Right.X,
            m12: basis.Up.X,
            m13: -basis.Forward.X,
            m14: 0,
            m21: basis.Right.Y,
            m22: basis.Up.Y,
            m23: -basis.Forward.Y,
            m24: 0,
            m31: basis.Right.Z,
            m32: basis.Up.Z,
            m33: -basis.Forward.Z,
            m34: 0,
            m41: 0,
            m42: 0,
            m43: 0,
            m44: 1
        );

    /// <summary>The view projection of a face for depth range <paramref name="range"/>: 90° and square.</summary>
    public static Matrix4x4 ViewProjection(FaceBasis basis, int range) =>
        View(basis) * DepthRanges.RangeProjection(range, MathF.PI / 2, 1);

    /// <summary>
    /// Whether a face culls the sphere at <paramref name="center"/>, camera-relative: it lies behind the near plane or
    /// outside one of the four side planes through the eye.
    /// </summary>
    public static bool Culls(FaceBasis basis, Vector3 center, float radius)
    {
        float depth = Vector3.Dot(center, basis.Forward);
        if (depth + radius < DepthRanges.NearPlane)
            return true;

        // The side planes have the normals (forward ∓ right) / √2 and (forward ∓ up) / √2.
        float across = Vector3.Dot(center, basis.Right);
        float along = Vector3.Dot(center, basis.Up);
        float reach = radius * MathF.Sqrt(2);
        bool outsideSides = depth - across < -reach || depth + across < -reach;
        bool outsideTopOrBottom = depth - along < -reach || depth + along < -reach;
        return outsideSides || outsideTopOrBottom;
    }

    /// <summary>The solid angle of the part of a face from its center to the point (x, y), signed.</summary>
    private static double CornerArea(double x, double y) => Math.Atan2(x * y, Math.Sqrt(x * x + y * y + 1));
}
