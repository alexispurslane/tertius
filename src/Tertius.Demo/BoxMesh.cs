using System.Numerics;

namespace Tertius.Demo;

/// <summary>
/// A hole through a wall: a doorway or a window. <see cref="Bounds"/> spans the wall's thickness along the axis that the
/// opening goes through, X or Z, and its cross-section in the other two axes is the hole.
/// </summary>
public readonly record struct Opening
{
    private Opening(BoundingBox bounds, Vector3 through)
    {
        Bounds = bounds;
        Through = through;
    }

    public BoundingBox Bounds { get; }

    /// <summary>
    /// The axis that the opening goes through: <see cref="Vector3.UnitX"/> or <see cref="Vector3.UnitZ"/>. The default
    /// opening has none.
    /// </summary>
    public Vector3 Through { get; }

    /// <summary>An opening through a wall across X.</summary>
    public static Opening ThroughX(BoundingBox bounds) => new(bounds, Vector3.UnitX);

    /// <summary>An opening through a wall across Z.</summary>
    public static Opening ThroughZ(BoundingBox bounds) => new(bounds, Vector3.UnitZ);
}

/// <summary>
/// Builds a mesh of axis-aligned boxes, whose faces across X or Z can have openings through them, and of the faces
/// inside those openings. Neighboring faces of a box share their corners, and a face with openings adds vertices only
/// inside itself, never on its edges. So a box with its openings is closed and has no T-junction, where a vertex on
/// another triangle's edge can leave a crack of single pixels. So is a hollow box, an outer box around an inward one,
/// with openings through both. Boxes that only touch are separate solids, so a corner of one can lie on an edge of
/// another. In the ship of <see cref="DemoScenes"/>, the walls of the airlock's chamber stand against the cabin's faces,
/// so their corners lie on the cabin's edges, and the chamber's inner wall stands against its side walls, so its corners
/// lie on their edges. But those faces go on behind the walls, so no crack can show there. Front faces wind
/// counterclockwise, as in <see cref="Primitives"/>. All positions are in the mesh's own axes, in meters.
/// </summary>
public sealed class BoxMesh
{
    private static readonly Vector3[] Normals =
        [Vector3.UnitX, -Vector3.UnitX, Vector3.UnitY, -Vector3.UnitY, Vector3.UnitZ, -Vector3.UnitZ];

    private readonly List<Vertex> vertices = [];
    private readonly List<uint> indices = [];

    /// <summary>
    /// Adds the six faces of <paramref name="box"/>, facing out of it, or into it if <paramref name="inward"/>. A face
    /// across the axis of an opening in <paramref name="openings"/> gets a hole, the opening's cross-section, when its
    /// plane lies within the opening's range along that axis. <see cref="Sides"/> then closes the solid around the hole.
    /// Each opening must cut a face, and must lie inside that face, clear of its edges. The openings in one face must not
    /// overlap along the face's horizontal axis, so they stand in a row, but they can differ in height.
    /// </summary>
    public BoxMesh Box(BoundingBox box, bool inward = false, params ReadOnlySpan<Opening> openings)
    {
        // Every face's holes, found and checked before any face is added, so that a wrong opening adds nothing.
        var holes = new List<BoundingBox>[Normals.Length];
        bool[] cut = new bool[openings.Length];
        for (int face = 0; face < Normals.Length; face++)
        {
            Vector3 side = Normals[face];
            holes[face] = [];
            for (int i = 0; i < openings.Length; i++)
            {
                // Only a face across the opening's axis. The default opening has no axis and cuts no face.
                Vector3 axis = openings[i].Through;
                if (Vector3.Dot(side, axis) == 0)
                    continue;
                float plane = Vector3.Dot(Vector3.Dot(side, axis) > 0 ? box.Max : box.Min, axis);
                if (plane >= Vector3.Dot(openings[i].Bounds.Min, axis) &&
                    plane <= Vector3.Dot(openings[i].Bounds.Max, axis))
                {
                    holes[face].Add(openings[i].Bounds);
                    cut[i] = true;
                }
            }

            CheckHoles(box, side, holes[face]);
        }

        for (int i = 0; i < openings.Length; i++)
            if (!cut[i])
                throw new ArgumentException(
                    $"The opening {openings[i].Bounds} through {openings[i].Through} cuts no face of the box {box}.",
                    nameof(openings));

        for (int face = 0; face < Normals.Length; face++)
            if (holes[face].Count == 0)
                Face(box, Normals[face], inward);
            else
                HoledFace(box: box, side: Normals[face], inward: inward, holes: holes[face]);
        return this;
    }

    /// <summary>
    /// Adds the four faces inside <paramref name="opening"/>, along the axis that it goes through, facing into it. Its ends
    /// across that axis stay open.
    /// </summary>
    public BoxMesh Sides(Opening opening)
    {
        if (opening.Through == Vector3.Zero)
            throw new ArgumentException(
                "The opening goes through no axis. Make it with Opening.ThroughX or Opening.ThroughZ.",
                nameof(opening));
        foreach (Vector3 normal in Normals)
            if (Vector3.Dot(normal, opening.Through) == 0)
                Face(opening.Bounds, normal, inward: true);
        return this;
    }

    public MeshData Build() => new([.. vertices], [.. indices]);

    /// <summary>
    /// The coordinate of <paramref name="point"/> along the horizontal axis of the face on the side of
    /// <paramref name="side"/>, across X or Z: its Z for a face across X, and its X for a face across Z. The openings in a
    /// face stand in a row along that axis.
    /// </summary>
    private static float Along(Vector3 side, Vector3 point) => side.X != 0 ? point.Z : point.X;

    /// <summary>
    /// Sorts <paramref name="holes"/> along the face of <paramref name="box"/> on the side of <paramref name="side"/>, and
    /// checks that each lies inside the face, clear of its edges, and that no two overlap along it.
    /// </summary>
    private static void CheckHoles(BoundingBox box, Vector3 side, List<BoundingBox> holes)
    {
        holes.Sort((a, b) => Along(side, a.Min).CompareTo(Along(side, b.Min)));
        float start = Along(side, box.Min), end = Along(side, box.Max);
        for (int i = 0; i < holes.Count; i++)
        {
            BoundingBox hole = holes[i];
            bool inside = start < Along(side, hole.Min) && Along(side, hole.Min) < Along(side, hole.Max) &&
                          Along(side, hole.Max) < end
                          && box.Min.Y < hole.Min.Y && hole.Min.Y < hole.Max.Y && hole.Max.Y < box.Max.Y;
            if (!inside)
                throw new ArgumentException(
                    $"The opening {hole} must lie inside the face {side} of the box {box}, clear of its edges.",
                    "openings");
            if (i > 0 && Along(side, holes[i - 1].Max) >= Along(side, hole.Min))
                throw new ArgumentException(
                    $"The openings {holes[i - 1]} and {hole} overlap along the face {side} of the box {box}. Openings in one face must stand in a row.",
                    "openings");
        }
    }

    /// <summary>Adds the face of <paramref name="box"/> on the side of <paramref name="side"/>, an axis direction, as one quad.</summary>
    private void Face(BoundingBox box, Vector3 side, bool inward)
    {
        Span<Vector3> corners = stackalloc Vector3[4];
        Corners(box: box, side: side, inward: inward, corners: corners);
        Vector3 normal = inward ? -side : side;
        Triangle(normal: normal, a: corners[0], b: corners[1], c: corners[2]);
        Triangle(normal: normal, a: corners[0], b: corners[2], c: corners[3]);
    }

    /// <summary>
    /// Adds the face of <paramref name="box"/> on the side of <paramref name="side"/>, across X or Z, with
    /// <paramref name="holes"/> through it, sorted along the face. Call the face's horizontal axis h. The band is the box
    /// around all the holes: from the first hole's start to the last hole's end along h, and from the lowest hole's bottom
    /// to the highest hole's top. Four trapezoids join the face's edges to the band's edges. Inside the band, each hole has
    /// a rectangle below it and one above it, as wide as the hole, and each gap between two holes is a rectangle across the
    /// band. Each of these pieces lies between two parallel lines, and <see cref="Strip"/> splits it into triangles.
    /// </summary>
    /// <remarks>
    /// The only vertices on the face's edges are its four corners, so its edges are whole edges of the neighboring faces
    /// too. Each hole's edges are whole edges, which <see cref="Sides"/> shares. Where two pieces touch, both have a vertex
    /// at every point where either has one: a line across h at a hole's side holds the band's bottom and top and the
    /// hole's corners, and the band's bottom and top hold every hole's start and end. Each coordinate is the box's or a
    /// hole's own minimum or maximum, never a sum, so pieces that share a vertex share it to the bit.
    /// </remarks>
    private void HoledFace(BoundingBox box, Vector3 side, bool inward, List<BoundingBox> holes)
    {
        Vector3 normal = inward ? -side : side;
        bool acrossX = side.X != 0;
        float plane = acrossX ? side.X > 0 ? box.Max.X : box.Min.X : side.Z > 0 ? box.Max.Z : box.Min.Z;
        Vector3 At(float h, float y) => acrossX ? new(plane, y, h) : new Vector3(h, y, plane);

        float bottom = holes.Min(hole => hole.Min.Y), top = holes.Max(hole => hole.Max.Y);

        // The points on the line across h at one side of a hole, from the band's bottom to its top.
        Vector3[] Column(float h, BoundingBox hole)
        {
            var points = new List<Vector3> { At(h, bottom) };
            if (hole.Min.Y > bottom)
                points.Add(At(h, hole.Min.Y));
            if (hole.Max.Y < top)
                points.Add(At(h, hole.Max.Y));
            points.Add(At(h, top));
            return [.. points];
        }

        // The points on the band's bottom or top: every hole's start and end.
        Vector3[] Row(float y) =>
        [
            .. holes.SelectMany(hole => new[] { At(Along(side, hole.Min), y), At(Along(side, hole.Max), y) })
        ];

        float start = Along(side, box.Min), end = Along(side, box.Max);
        Strip(normal, [At(start, box.Min.Y), At(start, box.Max.Y)], Column(Along(side, holes[0].Min), holes[0]));
        Strip(normal, Column(Along(side, holes[^1].Max), holes[^1]), [At(end, box.Min.Y), At(end, box.Max.Y)]);
        Strip(normal, [At(start, box.Min.Y), At(end, box.Min.Y)], Row(bottom));
        Strip(normal, Row(top), [At(start, box.Max.Y), At(end, box.Max.Y)]);
        for (int i = 0; i < holes.Count; i++)
        {
            BoundingBox hole = holes[i];
            float holeStart = Along(side, hole.Min), holeEnd = Along(side, hole.Max);
            if (hole.Min.Y > bottom)
                Strip(normal, [At(holeStart, bottom), At(holeStart, hole.Min.Y)],
                    [At(holeEnd, bottom), At(holeEnd, hole.Min.Y)]);
            if (hole.Max.Y < top)
                Strip(normal, [At(holeStart, hole.Max.Y), At(holeStart, top)],
                    [At(holeEnd, hole.Max.Y), At(holeEnd, top)]);
            if (i + 1 < holes.Count)
                Strip(normal, Column(holeEnd, hole), Column(Along(side, holes[i + 1].Min), holes[i + 1]));
        }
    }

    /// <summary>
    /// Splits the convex polygon between <paramref name="a"/> and <paramref name="b"/> into triangles that face along
    /// <paramref name="normal"/>. The points of each lie on one line, in the same direction along it, and the two lines are
    /// parallel and apart. Each triangle joins two neighboring points of one line to a point of the other, so none is
    /// degenerate. The triangles move along both lines together: each one takes the point that lies next along the lines,
    /// from either line. So no triangle spans more of a line than the gap between two neighboring points. The triangles
    /// are not close to equilateral. Where one line has only two points, its ends, the strip is a fan from one end of that
    /// line, of long thin triangles. In the hull they reach about 54 m in length and 7 mm in height. The edges of the
    /// polygon are still whole edges that the neighboring pieces share exactly, so no crack shows.
    /// </summary>
    private void Strip(Vector3 normal, ReadOnlySpan<Vector3> a, ReadOnlySpan<Vector3> b)
    {
        Vector3 direction = a[^1] - a[0];
        int i = 0, j = 0;
        while (i < a.Length - 1 || j < b.Length - 1)
        {
            bool alongA = j == b.Length - 1 ||
                          (i < a.Length - 1 && Vector3.Dot(a[i + 1], direction) <= Vector3.Dot(b[j + 1], direction));
            if (alongA)
            {
                Triangle(normal: normal, a: a[i], b: a[i + 1], c: b[j]);
                i++;
            }
            else
            {
                Triangle(normal: normal, a: a[i], b: b[j + 1], c: b[j]);
                j++;
            }
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
            var corner = new Vector3(sign.X > 0 ? box.Max.X : box.Min.X, sign.Y > 0 ? box.Max.Y : box.Min.Y,
                sign.Z > 0 ? box.Max.Z : box.Min.Z);
            // Inward, the order runs backward, so it winds counterclockwise around the inward normal.
            corners[inward ? 3 - i : i] = corner;
        }
    }

    /// <summary>
    /// Adds the triangle <paramref name="a"/>, <paramref name="b"/>, <paramref name="c"/> of a face whose normal is
    /// <paramref name="normal"/>, wound counterclockwise around it: if the three points run clockwise, two of them swap.
    /// </summary>
    private void Triangle(Vector3 normal, Vector3 a, Vector3 b, Vector3 c)
    {
        if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0)
            (b, c) = (c, b);
        // The texture runs along the face in meters, with u along the tangent and v against the bitangent.
        Vector3 tangent =
            Vector3.Normalize(Vector3.Cross(MathF.Abs(normal.Y) > 0.5f ? Vector3.UnitX : Vector3.UnitY, normal));
        Vector3 bitangent = Vector3.Cross(normal, tangent);
        foreach (Vector3 corner in (ReadOnlySpan<Vector3>)[a, b, c])
        {
            indices.Add((uint)vertices.Count);
            vertices.Add(new()
            {
                Position = corner,
                Normal = normal,
                Tangent = new(tangent, 1),
                TexCoord = new(Vector3.Dot(corner, tangent), -Vector3.Dot(corner, bitangent))
            });
        }
    }
}
