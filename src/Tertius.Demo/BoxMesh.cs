using System.Numerics;

namespace Tertius.Demo;

/// <summary>
/// Builds a mesh of axis-aligned boxes, whose faces across X can have a doorway through them, and of the faces inside
/// those doorways. Neighboring faces of a box share their corners, and a face with a doorway is four trapezoids between
/// its corners and the doorway's. So a box with its doorways is closed and has no T-junction, where a vertex on another
/// triangle's edge can leave a crack of single pixels. So is a hollow box, an outer box around an inward one, with a
/// doorway through both. Boxes that only touch are separate solids, so a corner of one can lie on an edge of another.
/// In the ship of <see cref="DemoScenes"/>, the walls of the airlock's chamber stand against the cabin's faces, so their
/// corners lie on the cabin's edges, and the chamber's inner wall stands against its side walls, so its corners lie on
/// their edges. But those faces go on behind the walls, so no crack can show there. Front faces wind counterclockwise,
/// as in <see cref="Primitives"/>. All positions are in the mesh's own axes, in meters.
/// </summary>
public sealed class BoxMesh
{
    private static readonly Vector3[] Normals = [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

    private readonly List<Vertex> vertices = new();
    private readonly List<uint> indices = new();

    /// <summary>
    /// Adds the six faces of <paramref name="box"/>, facing out of it, or into it if <paramref name="inward"/>. A face across
    /// X that lies within the X range of <paramref name="doorway"/> gets a hole, the doorway's cross-section in Y and Z.
    /// <see cref="Doorway"/> then closes the solid around the hole.
    /// </summary>
    public BoxMesh Box(BoundingBox box, bool inward = false, BoundingBox? doorway = null)
    {
        foreach (Vector3 normal in Normals)
        {
            float x = normal.X > 0 ? box.Max.X : box.Min.X;
            bool holed = normal.X != 0 && doorway is { } d && x >= d.Min.X && x <= d.Max.X;
            Face(box, normal, inward, holed ? doorway : null);
        }
        return this;
    }

    /// <summary>Adds the four faces inside <paramref name="doorway"/>, across Y and Z, facing into it. Its ends across X stay open.</summary>
    public BoxMesh Doorway(BoundingBox doorway)
    {
        foreach (Vector3 normal in Normals)
        {
            if (normal.X == 0)
                Face(doorway, normal, inward: true, hole: null);
        }
        return this;
    }

    public MeshData Build() => new([.. vertices], [.. indices]);

    /// <summary>
    /// Adds the face of <paramref name="box"/> on the side of <paramref name="side"/>, an axis direction. With a hole, it is
    /// four trapezoids, each between one edge of the face and the matching edge of the hole.
    /// </summary>
    private void Face(BoundingBox box, Vector3 side, bool inward, BoundingBox? hole)
    {
        Span<Vector3> corners = stackalloc Vector3[4];
        Corners(box, side, inward, corners);
        Vector3 normal = inward ? -side : side;
        if (hole is not { } h)
        {
            Polygon(normal, corners[0], corners[1], corners[2], corners[3]);
            return;
        }
        // The hole's corners, moved into the face's plane. Corners gives them in the same order as the face's own, so
        // inner[i] has the same role as corners[i]: it is at the same end, the minimum or the maximum, of each of the
        // face's two axes. So each quad joins an edge of the face to the parallel edge of the hole on the same side, and
        // because the hole lies within the face, the quad is a convex trapezoid. The nearest face corner does not always
        // have the same role. The hatch is near the floor, so its upper corners are nearer to the lower corners of the
        // cabin's port face than to its upper ones. Quads between corners of different roles do not tile the face.
        Span<Vector3> inner = stackalloc Vector3[4];
        Corners(h, side, inward, inner);
        float plane = side.X > 0 ? box.Max.X : box.Min.X;
        for (int i = 0; i < 4; i++)
            inner[i].X = plane;
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            Polygon(normal, corners[i], corners[next], inner[next], inner[i]);
        }
    }

    /// <summary>
    /// The four corners of the face of <paramref name="box"/> on the side of <paramref name="side"/>, counterclockwise
    /// around the way that the face looks: out along <paramref name="side"/>, or back along it if
    /// <paramref name="inward"/>.
    /// </summary>
    private static void Corners(BoundingBox box, Vector3 side, bool inward, Span<Vector3> corners)
    {
        // The face's own axes, as in Primitives.Box: the corners in the order (-, -), (+, -), (+, +), (-, +) wind
        // counterclockwise around the outward normal.
        Vector3 up = MathF.Abs(side.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY;
        Vector3 tangent = Vector3.Cross(up, side), bitangent = Vector3.Cross(side, tangent);
        for (int i = 0; i < 4; i++)
        {
            float s = i is 1 or 2 ? 1 : -1, t = i >= 2 ? 1 : -1;
            // Each coordinate is the box's own minimum or maximum, never a sum, so faces that share a corner share it to
            // the bit.
            Vector3 sign = side + s * tangent + t * bitangent;
            var corner = new Vector3(sign.X > 0 ? box.Max.X : box.Min.X, sign.Y > 0 ? box.Max.Y : box.Min.Y, sign.Z > 0 ? box.Max.Z : box.Min.Z);
            // Inward, the order runs backward, so it winds counterclockwise around the inward normal.
            corners[inward ? 3 - i : i] = corner;
        }
    }

    /// <summary>Adds the flat convex quad <paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/>, <paramref name="d"/>, counterclockwise around <paramref name="normal"/>.</summary>
    private void Polygon(Vector3 normal, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        // The texture runs along the face in meters, with u along the tangent and v against the bitangent.
        Vector3 tangent = Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY, normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        uint first = (uint)vertices.Count;
        foreach (Vector3 corner in (ReadOnlySpan<Vector3>)[a, b, c, d])
        {
            vertices.Add(new Vertex
            {
                Position = corner,
                Normal = normal,
                Tangent = new Vector4(tangent, 1),
                TexCoord = new Vector2(Vector3.Dot(corner, tangent), -Vector3.Dot(corner, bitangent)),
            });
        }
        indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
    }
}
