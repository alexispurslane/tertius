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
/// hull here has the convex radius 0, and its edges are as sharp as the mesh's. That also makes the compound a shape
/// made of sharp pieces, which physics gives edge removal (below). A ball rests on the facets within 0.03 mm, and in the
/// crease at a joint of a ring's floor within 0.12 mm of both facets.
/// </para>
/// <para>
/// Jolt also drops a point that lies within its hull tolerance, 1 mm, of the hull of the other points. Take a polygon of
/// n corners on a circle of radius ρ. Before Jolt adds a corner whose two neighbors are already in the hull, the corner
/// lies ρ (1 - cos(2π / n)) outside the hull, the bend of the polygon at that corner. So a cylinder refuses a count of
/// segments, and a torus a count of sides, whose bend is 1 mm or less. Its hull would be up to that bend inside the mesh.
/// </para>
/// <para>
/// Where two segments meet, a body that lands up to about 2 cm from the joint also meets the edge of the other segment,
/// and Jolt's contact with that edge tilts away from the joint. Jolt's own mesh shape of the same mesh has the same
/// contact. Without more, a ball dropped from 10 cm onto the flat top of a torus's tube, 2, 5, 10 or 20 mm from the
/// joint, moves 7, 11, 17 or 5 mm away from it, a box that slides across the joints of the compound trips over each one
/// and tumbles, and a character that walks across the joints of a ring's floor in the compound is lifted up to 3.7 mm
/// at each one. Physics sets Jolt's enhanced internal edge removal on every body whose shape is made of sharp pieces,
/// so on this shape and on the mesh, and on every character (ter-fb3w). It drops the contact with the edge beyond about
/// 5 mm from a flat joint and 10 mm from a concave crease, such as a joint of a ring's floor. So a ball that lands 5 mm
/// or more from a joint of a torus's top, or 20 mm from a joint of a ring's floor, stays where it lands, a box slides
/// across the joints without tripping, and a character that walks across them is never lifted and keeps within 1.3 % of
/// its walking speed. Closer to a joint, the push remains, and the engine accepts it for now: a ball that lands 2 mm
/// from a joint of a torus's top moves 6 mm, and one that lands 2 to 10 mm from a joint of a ring's floor moves 12 to
/// 17 mm, and on a level facet it can roll on. A ball that lands exactly on the joint rests there. Only a body that
/// rolls carries the push on: friction stops a box that lands there, and a character's walk sets its own speed along
/// the floor. A cylinder in segments has such a flat joint along each radius of each cap. With edge removal, the push
/// remains only within about 5 mm of each one, as on a torus's top, where a ball that lands 2 mm from a joint moves
/// 6 mm. One hull has no joints at all, so a cylinder is one hull, at the cost below, and it has at most 128 segments,
/// two points each in a hull of at most 256.
/// </para>
/// <para>
/// One hull has a cost too. Jolt makes the contacts of a body that rests on a face of a hull from at most 16 corners of
/// that face, and skips the others evenly. A cap of 48 segments is one face of 48 corners, so a box on it touches only
/// inside a polygon of 12 of them, which lies up to ρ (1 - cos(π / 12)) inside the rim: 3.4 cm for a radius of 1 m. A
/// box whose center of mass is that close to the rim can tip over where the mesh would hold it. A ball touches at one
/// point and does not feel this. Hulls of segments would not have this cost, because each of their faces on a cap is a
/// triangle, but a ball that lands within about 5 mm of one of their joints would still be pushed away from it (above).
/// </para>
/// </remarks>
public sealed class RevolvedSolid
{
    /// <summary>The most points that Jolt keeps in a convex hull.</summary>
    private const int MaxHullPoints = 256;

    /// <summary>
    /// How far, in meters, a point may lie outside the hull of the other points before Jolt keeps it in a convex hull:
    /// Jolt's hull tolerance, <c>ConvexHullShapeSettings::mHullTolerance</c>, which the binding does not expose. Jolt
    /// drops a point that lies within it, and the hull is then coarser than the mesh.
    /// </summary>
    private const double HullTolerance = 0.001;

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

    /// <summary>For each segment, true when it is left out (see <see cref="LeavingOut"/>).</summary>
    private readonly bool[] open;

    private RevolvedSolid(Vector2[] corners, bool smooth, bool convex, int segments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        this.corners = corners;
        this.smooth = smooth;
        this.convex = convex;
        cos = new double[segments];
        sin = new double[segments];
        open = new bool[segments];
        for (int k = 0; k < segments; k++)
        {
            double theta = Math.Tau * k / segments;
            cos[k] = Math.Cos(theta);
            sin[k] = Math.Sin(theta);
        }
    }

    private RevolvedSolid(RevolvedSolid solid, bool[] open)
    {
        corners = solid.corners;
        smooth = solid.smooth;
        convex = solid.convex;
        cos = solid.cos;
        sin = solid.sin;
        this.open = open;
    }

    private int Segments => cos.Length;

    /// <summary>
    /// A cylinder of <paramref name="radius"/> about the Y axis, from -<paramref name="halfHeight"/> to
    /// +<paramref name="halfHeight"/> along it, in <paramref name="segments"/> segments, from 3 to 128: its hull has two
    /// points for each segment, and Jolt keeps at most 256. The segments must also be few enough that the side bends by
    /// more than 1 mm at each edge between two facets, <paramref name="radius"/> (1 - cos(2π / segments)) > 1 mm, or Jolt
    /// would drop points from the hull (see the remarks of <see cref="RevolvedSolid"/>). Its caps are flat and its side is
    /// smooth.
    /// </summary>
    public static RevolvedSolid Cylinder(float radius, float halfHeight, int segments)
    {
        Positive(radius, nameof(radius));
        Positive(halfHeight, nameof(halfHeight));
        ArgumentOutOfRangeException.ThrowIfLessThan(segments, 3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(segments, MaxHullPoints / 2);
        ExpectBends(radius, segments, nameof(segments));
        return new(
            corners: [new(0, -halfHeight), new(radius, -halfHeight), new(radius, halfHeight), new(0, halfHeight)],
            smooth: false, convex: true, segments: segments);
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
            throw new ArgumentOutOfRangeException(nameof(outerRadius), outerRadius,
                $"The outer radius must be finite and more than the inner radius {innerRadius}.");
        Positive(halfWidth, nameof(halfWidth));
        return new(
            corners:
            [
                new(innerRadius, -halfWidth), new(outerRadius, -halfWidth), new(outerRadius, halfWidth),
                new(innerRadius, halfWidth)
            ], smooth: false, convex: false, segments: segments);
    }

    /// <summary>
    /// A torus about the Y axis: a smooth tube of <paramref name="minorRadius"/> round the circle of
    /// <paramref name="majorRadius"/> in the plane y = 0, in <paramref name="segments"/> segments, at least 3. The tube
    /// has <paramref name="sides"/> sides, from 3 to 128, because a segment's hull has two points for each side and Jolt
    /// keeps at most 256. The sides must also be few enough that the tube bends by more than 1 mm at each edge between two
    /// sides, <paramref name="minorRadius"/> (1 - cos(2π / sides)) > 1 mm, or Jolt would drop points from the hulls (see
    /// the remarks of <see cref="RevolvedSolid"/>). The corners of the sides are half a side off the outermost point, at the angles
    /// 2π (j + 1/2) / sides about the tube's center from +ρ toward +y. So when the sides are a multiple of 4, the tube's
    /// top and bottom are flat and the torus lies on a floor on them.
    /// </summary>
    public static RevolvedSolid Torus(float majorRadius, float minorRadius, int segments, int sides)
    {
        Positive(minorRadius, nameof(minorRadius));
        if (!(float.IsFinite(majorRadius) && majorRadius > minorRadius))
            throw new ArgumentOutOfRangeException(nameof(majorRadius), majorRadius,
                $"The major radius must be finite and more than the minor radius {minorRadius}, so that the torus has a hole.");
        ArgumentOutOfRangeException.ThrowIfLessThan(sides, 3);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(sides, MaxHullPoints / 2);
        ExpectBends(minorRadius, sides, nameof(sides));
        var tube = new Vector2[sides];
        for (int j = 0; j < sides; j++)
        {
            double phi = Math.Tau * (j + 0.5) / sides;
            tube[j] = new((float)(majorRadius + minorRadius * Math.Cos(phi)), (float)(minorRadius * Math.Sin(phi)));
        }

        return new(corners: tube, smooth: true, convex: false, segments: segments);
    }

    /// <summary>
    /// This solid with <paramref name="segment"/> left out, as well as every segment that this solid leaves out: the shape
    /// has no hull for it, and the mesh draws none of its faces. The mesh caps each cut end, where a segment that is drawn
    /// meets one that is left out, with the profile, so it stays closed. A ring of the station's ceiling leaves out the
    /// segment under a spoke this way (docs/plans/blue-danube-demo.md, "The exit"). Throws
    /// <see cref="ArgumentOutOfRangeException"/> for a segment that the solid does not have, and
    /// <see cref="InvalidOperationException"/> for a cylinder, which is one convex hull with no segments of its own.
    /// </summary>
    public RevolvedSolid LeavingOut(int segment)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(segment);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(segment, Segments);
        if (convex)
            throw new InvalidOperationException("A cylinder is one convex hull, so it cannot leave out a segment.");
        bool[] more = [.. open];
        more[segment] = true;
        return new(this, more);
    }

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
            for (int i = 0; i < corners.Length; i++)
                profile.Add((i,
                    Vector2.Normalize(EdgeNormal((i + corners.Length - 1) % corners.Length) + EdgeNormal(i))));

        int n = Segments;
        var vertices = new Vertex[n * profile.Count];
        for (int k = 0; k < n; k++)
        {
            var tangent = new Vector4(x: (float)-sin[k], y: 0, z: (float)-cos[k], w: 1);
            for (int p = 0; p < profile.Count; p++)
            {
                (int corner, Vector2 normal) = profile[p];
                vertices[k * profile.Count + p] = new()
                {
                    Position = Point(corner, k),
                    Normal = new((float)(normal.X * cos[k]), normal.Y, (float)(-normal.X * sin[k])),
                    Tangent = tangent
                };
            }
        }

        var indices = new List<uint>();
        for (int k = 0; k < n; k++)
        {
            if (open[k])
                continue;
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

        // The caps at the cut ends: the profile at the angle where a drawn segment meets one that is left out. The profile
        // winds counterclockwise about -dθ, the way the angle falls, so the cap at the end of a drawn segment reverses it.
        var all = new List<Vertex>(vertices);
        for (int k = 0; k < n; k++)
        {
            int before = (k + n - 1) % n;
            if (open[k] == open[before])
                continue;
            // Segment k starts at θ_k. The cap faces back along -dθ when k is drawn after a gap, and along dθ when the
            // segment before is drawn and k is left out.
            bool facesBack = !open[k];
            var along = new Vector3((float)-sin[k], 0, (float)-cos[k]);
            Vector3 normal = facesBack ? -along : along;
            uint first = (uint)all.Count;
            for (int c = 0; c < corners.Length; c++)
                all.Add(new()
                {
                    Position = Point(c, k), Normal = normal,
                    Tangent = new(x: (float)cos[k], y: 0, z: (float)-sin[k], w: 1)
                });
            for (int c = 1; c + 1 < corners.Length; c++)
            {
                uint b = first + (uint)c, d = first + (uint)c + 1;
                indices.AddRange(facesBack ? [first, b, d] : [first, d, b]);
            }
        }

        return new([.. all], [.. indices]);
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
            return new ConvexHullShapeDesc(
                [.. Enumerable.Range(0, n).SelectMany(k => all.Where(c => corners[c].X != 0).Select(c => Point(c, k)))],
                ConvexRadius: 0);
        var children = new List<CompoundChild>(n);
        for (int k = 0; k < n; k++)
        {
            if (open[k])
                continue;
            int next = (k + 1) % n;
            children.Add(new(
                new ConvexHullShapeDesc([.. all.SelectMany(c => new[] { Point(c, k), Point(c, next) })],
                    ConvexRadius: 0), Pose.Identity));
        }

        return new CompoundShapeDesc([.. children]);
    }

    /// <summary>
    /// The profile's corner <paramref name="corner"/> at the angle θ_<paramref name="k"/>. A corner on the axis is the same
    /// point at every angle, to the bit.
    /// </summary>
    private Vector3 Point(int corner, int k)
    {
        Vector2 c = corners[corner];
        return c.X == 0 ? new(0, c.Y, 0) : new Vector3((float)(c.X * cos[k]), c.Y, (float)(-c.X * sin[k]));
    }

    /// <summary>The outward unit normal, in (ρ, y), of the profile's edge from corner <paramref name="i"/> to the next.</summary>
    private Vector2 EdgeNormal(int i)
    {
        Vector2 along = corners[(i + 1) % corners.Length] - corners[i];
        // The corners run counterclockwise, so the outside is to the right of each edge.
        return Vector2.Normalize(new(along.Y, -along.X));
    }

    /// <summary>
    /// Refuses a polygon of <paramref name="count"/> corners on a circle of <paramref name="radius"/> whose bend at each
    /// corner, radius (1 - cos(2π / count)), is <see cref="HullTolerance"/> or less, because Jolt would drop corners of it
    /// from a hull. The bend is 0.6 mm for 128 corners on a circle of 0.5 m, and 1.2 mm for 128 corners on 1 m.
    /// </summary>
    private static void ExpectBends(float radius, int count, string name)
    {
        double bend = radius * (1 - Math.Cos(Math.Tau / count));
        if (!(bend > HullTolerance))
            throw new ArgumentOutOfRangeException(name, count,
                $"{count} corners on a circle of {radius} m bend by {bend * 1000:0.###} mm at each corner, and Jolt drops a corner of a hull that bends by {HullTolerance * 1000} mm or less, so the collision shape would not match the mesh. Use fewer.");
    }

    private static void Positive(float value, string name)
    {
        if (!(float.IsFinite(value) && value > 0))
            throw new ArgumentOutOfRangeException(name, value, "The value must be finite and positive.");
    }
}
