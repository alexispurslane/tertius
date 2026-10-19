using System.Numerics;

namespace Tertius.Demo;

/// <summary>
/// A solid that a profile sweeps out as it turns once about the Y axis, in equal segments: a cylinder, a ring or a torus,
/// with a mesh to draw and a collision shape that matches it. The profile is a convex polygon in the half plane of
/// (ρ, y), where ρ ≥ 0 is the distance from the axis and y the height along it. Segment k of n spans the angles
/// θ_k = 2π k / n to θ_(k+1), measured from +X toward -Z, so the profile's corner (ρ, y) at the angle θ is the point
/// (ρ cos θ, y, -ρ sin θ). The first segment's edge is on +X. All positions are in the solid's own axes, in meters.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Mesh"/> joins each edge of the profile at θ_k to the same edge at θ_(k+1) with a flat quad, so each segment
/// is flat between its two angles. The facets lie inside a convex curve and outside a concave one by up to
/// ρ (1 - cos(π / n)), the faceting error: 1.5 mm for a floor 20 m from the axis in 256 segments. A body rests on the
/// facets, not on the true curve.
/// </para>
/// <para>
/// <see cref="Shape"/> is made of convex hulls of the mesh's corners, as few as the solid allows, so a body rests on it
/// where the mesh is. A cylinder is convex, so its shape is one hull of all its corners. A ring or a torus is not, so its
/// shape is a compound of one hull for each segment, of the profile's corners at the segment's two angles. That hull is
/// exactly the segment of the mesh. Each quad is symmetric about the plane through the axis and the segment's middle, so
/// it is flat, and its two parallel edges cross that plane at right angles. Projected onto that plane, the segment's
/// corners fall on the profile scaled by cos(π / n) away from the axis, which is still convex, and each quad falls on one
/// of its edges. So each quad's plane has the whole segment on one side. The planes of the profile at the two angles meet
/// at the axis less than half a turn apart, so each has the other on one side too. So the segment is convex.
/// </para>
/// <para>
/// Jolt rounds the edges of a hull by its convex radius, 5 cm by default. Two rounded hulls side by side leave a groove
/// along their joint: a ball of 0.25 m in radius sinks 3.7 mm into it on a ring's floor, and 4.2 mm on a torus. So every
/// hull here has the convex radius 0, and its edges are as sharp as the mesh's. A ball rests on the facets within 0.03 mm,
/// and in the crease at a joint of a ring's floor within 0.12 mm of both facets.
/// </para>
/// <para>
/// Where the hulls of two segments meet in one plane, as on the flat top and bottom of a torus's tube, a ball that lands
/// within about 2 cm of the joint also touches the edge of the other hull, and that contact pushes it 5 to 17 mm away from
/// the joint before it rests. A ball that lands exactly on the joint, or farther from it, rests where it landed. A
/// cylinder in segments has such a joint along each radius of each cap. A ball that lands on the cap's center rests there,
/// within 0.0001 mm, but one that lands 5 mm from a joint moves 11 mm. That is why a cylinder is one hull, which has no
/// joints, and why it has at most 128 segments.
/// </para>
/// </remarks>
public sealed class RevolvedSolid
{
    /// <summary>The most points that Jolt keeps in a convex hull.</summary>
    private const int MaxHullPoints = 256;

    /// <summary>The profile's corners, (ρ, y), counterclockwise with ρ to the right and y up.</summary>
    private readonly Vector2[] corners;

    /// <summary>
    /// True when the profile stands for a smooth curve, a torus's tube, so a corner has one normal between its two edges.
    /// False when its corners are sharp, so each edge has its own normal.
    /// </summary>
    private readonly bool smooth;

    /// <summary>
    /// True when the solid is convex, a cylinder, so that one hull of all its corners is its collision shape. False when
    /// each segment needs a hull of its own.
    /// </summary>
    private readonly bool convex;

    /// <summary>The cosine and sine of θ_k, for each of the segments.</summary>
    private readonly double[] cos, sin;

    private RevolvedSolid(Vector2[] corners, bool smooth, bool convex, int segments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        this.corners = corners;
        this.smooth = smooth;
        this.convex = convex;
        cos = new double[segments];
        sin = new double[segments];
        for (int k = 0; k < segments; k++)
        {
            double theta = Math.Tau * k / segments;
            cos[k] = Math.Cos(theta);
            sin[k] = Math.Sin(theta);
        }
    }

    /// <summary>
    /// A cylinder of <paramref name="radius"/> about the Y axis, from -<paramref name="halfHeight"/> to
    /// +<paramref name="halfHeight"/> along it, in <paramref name="segments"/> segments, from 3 to 128: its hull has two
    /// points for each segment, and Jolt keeps at most 256. Its caps are flat and its side is smooth.
    /// </summary>
    public static RevolvedSolid Cylinder(float radius, float halfHeight, int segments)
    {
        Positive(radius, nameof(radius));
        Positive(halfHeight, nameof(halfHeight));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(segments, MaxHullPoints / 2);
        return new([new(0, -halfHeight), new(radius, -halfHeight), new(radius, halfHeight), new(0, halfHeight)], smooth: false, convex: true, segments);
    }

    /// <summary>
    /// A ring about the Y axis: an annulus from <paramref name="innerRadius"/> to <paramref name="outerRadius"/>, from
    /// -<paramref name="halfWidth"/> to +<paramref name="halfWidth"/> along the axis, in <paramref name="segments"/>
    /// segments, at least 3. Its inner face is the floor of a station that spins about the axis. Its ends are flat and its
    /// inner and outer faces smooth.
    /// </summary>
    public static RevolvedSolid Ring(float innerRadius, float outerRadius, float halfWidth, int segments)
    {
        Positive(innerRadius, nameof(innerRadius));
        if (!(float.IsFinite(outerRadius) && outerRadius > innerRadius))
            throw new ArgumentOutOfRangeException(nameof(outerRadius), outerRadius, $"The outer radius must be finite and more than the inner radius {innerRadius}.");
        Positive(halfWidth, nameof(halfWidth));
        return new([new(innerRadius, -halfWidth), new(outerRadius, -halfWidth), new(outerRadius, halfWidth), new(innerRadius, halfWidth)], smooth: false, convex: false, segments);
    }

    /// <summary>
    /// A torus about the Y axis: a smooth tube of <paramref name="minorRadius"/> round the circle of
    /// <paramref name="majorRadius"/> in the plane y = 0, in <paramref name="segments"/> segments, at least 3. The tube
    /// has <paramref name="sides"/> sides, from 3 to 128, because a segment's hull has two points for each side and Jolt
    /// keeps at most 256. The corners of the sides are half a side off the outermost point, at the angles
    /// 2π (j + 1/2) / sides about the tube's center from +ρ toward +y. So when the sides are a multiple of 4, the tube's
    /// top and bottom are flat and the torus lies on a floor on them.
    /// </summary>
    public static RevolvedSolid Torus(float majorRadius, float minorRadius, int segments, int sides)
    {
        Positive(minorRadius, nameof(minorRadius));
        if (!(float.IsFinite(majorRadius) && majorRadius > minorRadius))
            throw new ArgumentOutOfRangeException(nameof(majorRadius), majorRadius, $"The major radius must be finite and more than the minor radius {minorRadius}, so that the torus has a hole.");
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sides, MaxHullPoints / 2);
        var tube = new Vector2[sides];
        for (int j = 0; j < sides; j++)
        {
            double phi = Math.Tau * (j + 0.5) / sides;
            tube[j] = new Vector2((float)(majorRadius + minorRadius * Math.Cos(phi)), (float)(minorRadius * Math.Sin(phi)));
        }
        return new(tube, smooth: true, convex: false, segments);
    }

    private int Segments => cos.Length;

    /// <summary>
    /// The mesh: a flat quad for each edge of the profile in each segment, or a triangle where the edge has one end on the
    /// axis, wound counterclockwise around the outward normal. An edge along the axis has no area and adds nothing. The
    /// vertices at each angle are shared by the two segments that meet there, and their normals are those of the true
    /// curved surface at that angle, so the solid looks smooth around the axis. A sharp corner of the profile has a vertex
    /// for each of its two edges. The tangent runs around the axis, toward growing θ. The vertices have no texture
    /// coordinates, because the demo draws these solids in plain colors. Each call builds a new mesh.
    /// </summary>
    public MeshData Mesh()
    {
        // The vertices of the profile, each a corner with a normal, and its edges between them, which leave out the axis.
        var profile = new List<(int Corner, Vector2 Normal)>();
        var edges = new List<(int From, int To)>();
        for (int i = 0; i < corners.Length; i++)
        {
            int next = (i + 1) % corners.Length;
            if (corners[i].X == 0 && corners[next].X == 0)
                continue;
            if (smooth)
            {
                edges.Add((i, next));
            }
            else
            {
                edges.Add((profile.Count, profile.Count + 1));
                profile.Add((i, EdgeNormal(i)));
                profile.Add((next, EdgeNormal(i)));
            }
        }
        if (smooth)
        {
            for (int i = 0; i < corners.Length; i++)
                profile.Add((i, Vector2.Normalize(EdgeNormal((i + corners.Length - 1) % corners.Length) + EdgeNormal(i))));
        }

        int n = Segments;
        var vertices = new Vertex[n * profile.Count];
        for (int k = 0; k < n; k++)
        {
            var tangent = new Vector4((float)-sin[k], 0, (float)-cos[k], 1);
            for (int p = 0; p < profile.Count; p++)
            {
                (int corner, Vector2 normal) = profile[p];
                vertices[k * profile.Count + p] = new Vertex
                {
                    Position = Point(corner, k),
                    Normal = new Vector3((float)(normal.X * cos[k]), normal.Y, (float)(-normal.X * sin[k])),
                    Tangent = tangent,
                };
            }
        }

        var indices = new List<uint>();
        for (int k = 0; k < n; k++)
        {
            int next = (k + 1) % n;
            foreach ((int from, int to) in edges)
            {
                uint a = (uint)(k * profile.Count + from), aNext = (uint)(next * profile.Count + from);
                uint b = (uint)(k * profile.Count + to), bNext = (uint)(next * profile.Count + to);
                // Seen from outside, the profile's edge runs from a to b and the segment from θ_k to θ_(k+1), so a, aNext,
                // bNext, b goes counterclockwise. An end on the axis is one point at both angles, so its triangle drops out.
                if (corners[profile[from].Corner].X != 0)
                    indices.AddRange([a, aNext, bNext]);
                if (corners[profile[to].Corner].X != 0)
                    indices.AddRange([a, bNext, b]);
            }
        }
        return new MeshData(vertices, [.. indices]);
    }

    /// <summary>
    /// The collision shape. For a cylinder, the convex hull of its corners off the axis at every angle. For a ring or a
    /// torus, a compound of one convex hull for each segment, of the profile's corners at the segment's two angles, which
    /// is exactly the mesh's segment. Every hull has the convex radius 0, so that Jolt does not round its edges (see the
    /// remarks of <see cref="RevolvedSolid"/>). Each call builds a new shape, and a backend builds one native shape for each, so a
    /// caller that spawns several bodies of one solid keeps the shape and shares it.
    /// </summary>
    public ShapeDesc Shape()
    {
        int n = Segments;
        IEnumerable<int> all = Enumerable.Range(0, corners.Length);
        // A cylinder's corners on the axis are inside the hull of its rims, and Jolt refuses a hull with points that
        // coincide, as the corners on the axis at every angle would.
        if (convex)
            return new ConvexHullShapeDesc([.. Enumerable.Range(0, n).SelectMany(k => all.Where(c => corners[c].X != 0).Select(c => Point(c, k)))], ConvexRadius: 0);
        var children = new CompoundChild[n];
        for (int k = 0; k < n; k++)
        {
            int next = (k + 1) % n;
            children[k] = new CompoundChild(new ConvexHullShapeDesc([.. all.SelectMany(c => new[] { Point(c, k), Point(c, next) })], ConvexRadius: 0), Pose.Identity);
        }
        return new CompoundShapeDesc(children);
    }

    /// <summary>
    /// The profile's corner <paramref name="corner"/> at the angle θ_<paramref name="k"/>. A corner on the axis is the same
    /// point at every angle, to the bit.
    /// </summary>
    private Vector3 Point(int corner, int k)
    {
        Vector2 c = corners[corner];
        return c.X == 0 ? new Vector3(0, c.Y, 0) : new Vector3((float)(c.X * cos[k]), c.Y, (float)(-c.X * sin[k]));
    }

    /// <summary>The outward unit normal, in (ρ, y), of the profile's edge from corner <paramref name="i"/> to the next.</summary>
    private Vector2 EdgeNormal(int i)
    {
        Vector2 along = corners[(i + 1) % corners.Length] - corners[i];
        // The corners run counterclockwise, so the outside is to the right of each edge.
        return Vector2.Normalize(new Vector2(along.Y, -along.X));
    }

    private static void Positive(float value, string name)
    {
        if (!(float.IsFinite(value) && value > 0))
            throw new ArgumentOutOfRangeException(name, value, "The value must be finite and positive.");
    }
}
