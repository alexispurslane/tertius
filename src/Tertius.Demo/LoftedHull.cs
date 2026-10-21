using System.Numerics;
using System.Runtime.CompilerServices;

namespace Tertius.Demo;

/// <summary>
/// A cross-section of a <see cref="LoftedHull"/>: the outline of its outer skin in the plane across its length at
/// <see cref="Z"/>, a convex polygon in (x, y) whose <see cref="Corners"/> run counterclockwise seen from +Z.
/// </summary>
public readonly record struct HullSection(float Z, Vector2[] Corners);

/// <summary>
/// The moments of inertia of a body about its principal axes, in kg·m², and the principal axes themselves:
/// <see cref="Axes"/> turns the body's X, Y and Z into the principal axes of the moments X, Y and Z, in the body's axes.
/// A body whose principal axes are its own has the identity.
/// </summary>
public readonly record struct PrincipalInertia(Vector3 Moments, Quaternion Axes);

/// <summary>
/// The mass properties of a solid of one density: its center of mass, in meters in its own axes, and its inertia about
/// that point.
/// </summary>
public readonly record struct HullMass(Vector3 CenterOfMass, PrincipalInertia Inertia);

/// <summary>
/// A pane of glass of a <see cref="LoftedHull"/>, with its scene id <see cref="Id"/>: its hole through the wall, as the
/// corners of the hole at the outer skin, <see cref="Outer"/>, and at the inner skin, <see cref="Inner"/>, in the same
/// order, in the hull's axes, and its <see cref="Mesh"/>, a slab 2 cm thick in the middle of the wall, with its vertices
/// relative to <see cref="Center"/>, the middle of the slab.
/// </summary>
public readonly record struct HullPane(string Id, Vector3 Center, Vector3[] Outer, Vector3[] Inner, MeshData Mesh);

/// <summary>
/// A hull lofted through cross-sections along its length, with a wall of one thickness: a mesh to draw, an outer collision
/// shape and an interior shape that match it, and its mass properties, all from one description, as
/// <see cref="RevolvedSolid"/> and <see cref="BoxMesh"/> give theirs. All positions are in the hull's own axes, in
/// meters. The design is docs/plans/blue-danube-demo.md, "The shuttle's tram shape and glass canopy".
/// </summary>
/// <remarks>
/// <para>
/// Terms. The corners of every cross-section are numbered alike, and edge i runs from corner i to the next. Between two
/// neighboring cross-sections each corner moves on a straight line. The rib places cut the length into bays, numbered from
/// 1 at the first cross-section. A cell is the four-sided piece of the outer skin over one edge in one bay. A face is a
/// run of edges that lie on one line in every cross-section. The outer skin and the inner skin are the hull's surface
/// outside and the cabin's surface inside, and the wall is the solid between them.
/// </para>
/// <para>
/// Every edge of every cross-section is parallel to the same edge of every other, and every cross-section but the last lies
/// at a rib place. So each cell is flat, and the loft between two cross-sections is their convex hull. The inner skin is
/// each cross-section moved in by the wall along each edge's normal, in the plane of the cross-section. With the edges'
/// directions fixed, each inner corner lies a fixed step from its outer corner. So each inner cell is flat and parallel to
/// its outer cell, and the plane through an outer corner and its inner corner, the mitre, is flat along the whole loft
/// piece. The inner skin ends one wall short of each end, where the end walls close it.
/// </para>
/// <para>
/// The mesh is closed. Each corner at each rib place and at each end is one place that the cells around it share, to the
/// bit, and a panel adds places only inside itself, so the mesh has no T-junction. Each panel has its own vertices with
/// its own flat normal, and the tangent runs along its first edge. The vertices have no texture coordinates. Front faces
/// wind counterclockwise, as in <see cref="Primitives"/>.
/// </para>
/// <para>
/// Glass. A pane's hole is its cell, or its part of the front, with each side moved in by its surround in the plane of the
/// cell, at both skins. Each inner cell is parallel to its outer cell and the moved sides stay parallel, so each side of a
/// hole, its reveal, is a flat face from the outer hole to the inner one. It slants a little at a fold, where the inner
/// cell is narrower.
/// </para>
/// </remarks>
public sealed class LoftedHull
{
    /// <summary>
    /// How far, relative to the lengths of two edges, two directions may be from parallel and still count as parallel. The
    /// corners are floats, so a direction made from two of them is off by a few parts in ten million.
    /// </summary>
    private const double Parallel = 1e-5;

    private readonly HullSection[] sections;
    private readonly float wall;
    private readonly float[] ribs;

    /// <summary>The step from each outer corner to its inner corner, in the plane of a cross-section, in meters.</summary>
    private readonly (double X, double Y)[] inset;

    /// <summary>True for each corner whose two edges lie on one line, so that it lies inside a face.</summary>
    private readonly bool[] straight;

    private readonly DoorCut? door;
    private readonly PocketCut? pocket;
    private readonly SurroundWidths? surrounds;

    /// <summary>The panes, in the order of the calls that made them.</summary>
    private readonly PaneCut[] panes = [];

    /// <summary>
    /// How far each side of a hole moves in from the side of its cell or panel, in meters: at a rib place or an end of the
    /// loft, at a fold whose neighbor cell holds glass too, and next to a solid cell or the cut line of the front.
    /// </summary>
    private sealed record SurroundWidths(float Rib, float Fold, float Solid);

    /// <summary>A pane with the scene id <see cref="Id"/> in the cell of <see cref="Edge"/> in <see cref="Bay"/>.</summary>
    private readonly record struct CellPane(string Id, int Edge, int Bay);

    /// <summary>A pane with the scene id <see cref="Id"/> in the front, in the part from <see cref="FromCorner"/> counterclockwise to <see cref="ToCorner"/>.</summary>
    private readonly record struct FrontPane(string Id, int FromCorner, int ToCorner);

    /// <summary>
    /// A pane: a <see cref="CellPane"/> or a <see cref="FrontPane"/>. This is a custom C# 15 union, like
    /// <see cref="ClipState"/>. The two cases keep their scene id in one place and their two numbers in two more.
    /// </summary>
    [Union]
    private readonly struct PaneCut
    {
        private readonly bool front;
        private readonly string id;
        // The edge and the bay of a cell pane, or the two corners of a front pane.
        private readonly int first, second;

        public PaneCut(CellPane pane) => (id, first, second) = (pane.Id, pane.Edge, pane.Bay);

        public PaneCut(FrontPane pane) => (front, id, first, second) = (true, pane.Id, pane.FromCorner, pane.ToCorner);

        public static implicit operator PaneCut(CellPane pane) => new(pane);
        public static implicit operator PaneCut(FrontPane pane) => new(pane);

        /// <summary>Always true: every value is a pane.</summary>
        public bool HasValue => true;

        /// <summary>The pane's scene id, in either case.</summary>
        public string Id => id;

        public bool TryGetValue(out CellPane value)
        {
            value = new CellPane(id, first, second);
            return !front;
        }

        public bool TryGetValue(out FrontPane value)
        {
            value = new FrontPane(id, first, second);
            return front;
        }

        public object Value => front ? new FrontPane(id, first, second) : new CellPane(id, first, second);
    }

    /// <summary>The doorway through a panel across <see cref="Axis"/>, 0 for X or 1 for Y.</summary>
    private sealed record DoorCut(BoundingBox Box, int Bay, int FromCorner, int ToCorner, int Axis);

    /// <summary>
    /// The pocket in the tail face: its middle (<see cref="X"/>, <see cref="Y"/>), its half side, its depth, and the
    /// thickness of its housing's walls.
    /// </summary>
    private sealed record PocketCut(float X, float Y, float Half, float Depth, float Housing);

    /// <summary>
    /// A hull through <paramref name="crossSections"/>, in order of Z. The first is the nose's front and the last the tail
    /// face. <paramref name="wall"/> is the wall's thickness in the plane of a cross-section, and <paramref name="ribs"/> are
    /// the rib places, from the first cross-section's Z to before the last. Throws <see cref="ArgumentException"/> for
    /// cross-sections with different counts of corners, one that is not convex or not counterclockwise, an edge that is
    /// not parallel to the same edge of every other cross-section, rib places that are not in order or lie outside the
    /// cabin, but for the first, a cross-section other than the last whose Z is not a rib place, and a wall too thick for
    /// the cross-sections.
    /// </summary>
    public LoftedHull(HullSection[] crossSections, float wall, float[] ribs)
    {
        ArgumentNullException.ThrowIfNull(crossSections);
        ArgumentNullException.ThrowIfNull(ribs);
        if (crossSections.Length < 2)
            throw new ArgumentException("A hull needs at least two cross-sections.", nameof(crossSections));
        if (!(float.IsFinite(wall) && wall > 0))
            throw new ArgumentOutOfRangeException(nameof(wall), wall, "The wall must be finite and positive.");
        int count = crossSections[0].Corners.Length;
        if (count < 3)
            throw new ArgumentException("A cross-section needs at least three corners.", nameof(crossSections));
        foreach (HullSection section in crossSections)
        {
            if (section.Corners.Length != count)
                throw new ArgumentException($"The cross-section at z = {section.Z} has {section.Corners.Length} corners, and the first has {count}. Every cross-section needs the same corners.", nameof(crossSections));
            CheckConvex(section);
        }
        for (int i = 1; i < crossSections.Length; i++)
        {
            if (!(crossSections[i].Z > crossSections[i - 1].Z))
                throw new ArgumentException($"The cross-sections must be in order of Z, but z = {crossSections[i].Z} follows {crossSections[i - 1].Z}.", nameof(crossSections));
            for (int e = 0; e < count; e++)
            {
                (double ax, double ay) = Edge(crossSections[0], e);
                (double bx, double by) = Edge(crossSections[i], e);
                double lengths = Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by));
                if (Math.Abs(ax * by - ay * bx) > Parallel * lengths || ax * bx + ay * by <= 0)
                    throw new ArgumentException($"Edge {e} of the cross-section at z = {crossSections[i].Z} is not parallel to the same edge at z = {crossSections[0].Z}, so its cells would not be flat.", nameof(crossSections));
            }
        }

        float first = crossSections[0].Z, last = crossSections[^1].Z;
        if (ribs.Length == 0)
            throw new ArgumentException($"A hull needs at least one rib place, the first cross-section's z = {first}, where the first bay starts.", nameof(ribs));
        for (int i = 1; i < ribs.Length; i++)
        {
            if (!(ribs[i] > ribs[i - 1]))
                throw new ArgumentException($"The rib places must be in order, but {ribs[i]} follows {ribs[i - 1]}.", nameof(ribs));
        }
        for (int i = 0; i < crossSections.Length - 1; i++)
        {
            if (Array.IndexOf(ribs, crossSections[i].Z) < 0)
                throw new ArgumentException($"The cross-section at z = {crossSections[i].Z} is not at a rib place, so a bay would hold a bend and its cells would not be flat.", nameof(crossSections));
        }
        // These checks also keep the first rib place at the first cross-section's Z, which lies at a rib place while every
        // later rib place lies in the cabin, and the last rib place before the tail.
        float innerFirst = first + wall, innerLast = last - wall;
        if (!(innerFirst < innerLast) || (ribs.Length > 1 && !(ribs[1] > innerFirst && ribs[^1] < innerLast)))
            throw new ArgumentException($"Every rib place but the first must lie inside the cabin, from z = {innerFirst} to {innerLast}, clear of the end walls.", nameof(ribs));

        sections = crossSections;
        this.wall = wall;
        this.ribs = ribs;
        straight = new bool[count];
        inset = new (double, double)[count];
        for (int k = 0; k < count; k++)
        {
            (double nx1, double ny1) = InwardNormal(crossSections[0], (k + count - 1) % count);
            (double nx2, double ny2) = InwardNormal(crossSections[0], k);
            double det = nx1 * ny2 - ny1 * nx2;
            straight[k] = Math.Abs(det) <= Parallel;
            inset[k] = straight[k] ? (wall * nx1, wall * ny1) : (wall * (ny2 - ny1) / det, wall * (nx1 - nx2) / det);
        }
        foreach (HullSection section in crossSections)
        {
            for (int e = 0; e < count; e++)
            {
                (double ox, double oy) = Edge(section, e);
                int next = (e + 1) % count;
                double ix = ox + inset[next].X - inset[e].X, iy = oy + inset[next].Y - inset[e].Y;
                if (ox * ix + oy * iy <= 0)
                    throw new ArgumentException($"The wall of {wall} m is too thick for edge {e} of the cross-section at z = {section.Z}: the cabin's edge would vanish.", nameof(wall));
            }
        }
    }

    private LoftedHull(LoftedHull from, DoorCut? door, PocketCut? pocket, SurroundWidths? surrounds, PaneCut[] panes)
    {
        sections = from.sections;
        wall = from.wall;
        ribs = from.ribs;
        inset = from.inset;
        straight = from.straight;
        this.door = door;
        this.pocket = pocket;
        this.surrounds = surrounds;
        this.panes = panes;
    }

    private int Corners => sections[0].Corners.Length;

    /// <summary>The number of bays.</summary>
    private int Bays => ribs.Length;

    /// <summary>The z of the outer skin's grid level <paramref name="level"/>, from 0 at the first rib place to <see cref="Bays"/> at the tail.</summary>
    private float OuterLevel(int level) => level < Bays ? ribs[level] : sections[^1].Z;

    /// <summary>The z of the inner skin's grid level: that of the outer skin, but one wall inside each end.</summary>
    private float InnerLevel(int level) => level == 0 ? sections[0].Z + wall : level == Bays ? sections[^1].Z - wall : ribs[level];

    /// <summary>
    /// A doorway through the flat panel of the edges from <paramref name="fromCorner"/> to <paramref name="toCorner"/> in
    /// <paramref name="bay"/>, counted from 1 at the nose. The panel must lie across X or Y, and <paramref name="door"/>
    /// spans the wall along that axis, as <see cref="Opening.ThroughX"/> does. Its cross-section in the panel is the hole.
    /// <see cref="Interior"/> cuts the wall piece of that face into four convex hulls round it. Throws
    /// <see cref="ArgumentException"/> for a panel whose edges do not lie in one plane across X or Y, a door that does not
    /// span the wall, and a door that reaches the edge of its panel, at either skin.
    /// </summary>
    public LoftedHull Door(BoundingBox door, int bay, int fromCorner, int toCorner)
    {
        if (this.door != null)
            throw new InvalidOperationException("The hull has a door already.");
        if (panes.Length > 0)
            throw new InvalidOperationException("A door comes before the glass, so that no pane lies in its panel.");
        ArgumentOutOfRangeException.ThrowIfLessThan(bay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bay, Bays);
        ArgumentOutOfRangeException.ThrowIfNegative(fromCorner);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromCorner, Corners);
        ArgumentOutOfRangeException.ThrowIfNegative(toCorner);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toCorner, Corners);
        if (fromCorner == toCorner)
            throw new ArgumentException("A door's panel needs at least one edge.", nameof(toCorner));
        int[] panel = [.. PanelCorners(fromCorner, toCorner)];
        foreach (int k in panel[1..^1])
        {
            if (!straight[k])
                throw new ArgumentException($"The edges from corner {fromCorner} to corner {toCorner} bend at corner {k}, so they do not lie in one plane.", nameof(toCorner));
        }
        float z0 = OuterLevel(bay - 1), z1 = OuterLevel(bay);
        Vector3 a0 = OuterPlace(fromCorner, z0), b0 = OuterPlace(toCorner, z0), a1 = OuterPlace(fromCorner, z1);
        int axis = a0.X == b0.X && a0.X == a1.X ? 0 : a0.Y == b0.Y && a0.Y == a1.Y ? 1 : -1;
        if (axis < 0)
            throw new ArgumentException($"The panel of the edges from corner {fromCorner} to corner {toCorner} in bay {bay} does not lie in one plane across X or Y.", nameof(toCorner));
        int along = 1 - axis;
        float iz0 = InnerLevel(bay - 1), iz1 = InnerLevel(bay);
        float outerPlane = Component(a0, axis), innerPlane = Component(InnerPlace(fromCorner, iz0), axis);
        if (!(Component(door.Min, axis) <= MathF.Min(outerPlane, innerPlane) + 1e-4f && Component(door.Max, axis) >= MathF.Max(outerPlane, innerPlane) - 1e-4f))
            throw new ArgumentException($"The door {door} must span the wall from {innerPlane} to {outerPlane} along {(axis == 0 ? "X" : "Y")}.", nameof(door));
        foreach ((Vector3 start, Vector3 end, float zStart, float zEnd) in new[]
        {
            (OuterPlace(fromCorner, z0), OuterPlace(toCorner, z0), z0, z1),
            (InnerPlace(fromCorner, iz0), InnerPlace(toCorner, iz0), iz0, iz1),
        })
        {
            float low = MathF.Min(Component(start, along), Component(end, along)), high = MathF.Max(Component(start, along), Component(end, along));
            bool inside = low < Component(door.Min, along) && Component(door.Max, along) < high && zStart < door.Min.Z && door.Max.Z < zEnd;
            if (!inside)
                throw new ArgumentException($"The door {door} reaches the edge of its panel in bay {bay}, from z = {zStart} to {zEnd} and from {low} to {high} along {(along == 0 ? "X" : "Y")}. It must lie inside the panel at both skins.", nameof(door));
        }
        return new LoftedHull(this, new DoorCut(door, bay, fromCorner, toCorner, axis), pocket, surrounds, panes);
    }

    /// <summary>
    /// A square pocket in the tail face, on the line along Z through the center of mass. It reaches
    /// <paramref name="half"/> from its middle to each side and is <paramref name="depth"/> deep, deeper than the wall. A
    /// housing with walls <paramref name="housing"/> thick closes it inside the cabin. Throws
    /// <see cref="ArgumentException"/> for a pocket no deeper than the wall, and a pocket whose housing reaches the hull's
    /// wall.
    /// </summary>
    public LoftedHull TailPocket(float half, float depth, float housing)
    {
        if (pocket != null)
            throw new InvalidOperationException("The hull has a tail pocket already.");
        foreach ((float value, string name) in new[] { (half, nameof(half)), (depth, nameof(depth)), (housing, nameof(housing)) })
        {
            if (!(float.IsFinite(value) && value > 0))
                throw new ArgumentOutOfRangeException(name, value, "The value must be finite and positive.");
        }
        if (!(depth > wall))
            throw new ArgumentException($"A pocket of {depth} m lies inside the tail wall of {wall} m. It must be deeper.", nameof(depth));
        Vector3 center = MassOf(1).CenterOfMass;
        float last = sections[^1].Z, front = last - depth - housing, outer = half + housing;
        if (!(front > InnerLevel(0)))
            throw new ArgumentException($"The housing reaches z = {front}, past the cabin's front.", nameof(depth));
        foreach (float z in new[] { last - wall, front })
        {
            foreach ((float sx, float sy) in new[] { (-1f, -1f), (1f, -1f), (1f, 1f), (-1f, 1f) })
            {
                if (!(InsideOuter(z, center.X + sx * outer, center.Y + sy * outer) > wall))
                    throw new ArgumentException($"The pocket's housing, {2 * outer} m square round ({center.X}, {center.Y}), reaches the hull's wall at z = {z}.", nameof(housing));
            }
        }
        return new LoftedHull(this, door, new PocketCut(center.X, center.Y, half, depth, housing), surrounds, panes);
    }

    /// <summary>
    /// The surround widths, in meters: how far a pane's hole stays from the side of its cell at a rib place or an end of the
    /// loft, <paramref name="rib"/>, half a rib; at a fold between two cells of glass, <paramref name="fold"/>, half a
    /// mullion; and next to a solid cell or the cut line of the front, <paramref name="solid"/>. Each move is in the plane of
    /// the cell. The surrounds come before the glass.
    /// </summary>
    public LoftedHull Surrounds(float rib, float fold, float solid)
    {
        if (surrounds != null)
            throw new InvalidOperationException("The hull has its surrounds already.");
        foreach ((float value, string name) in new[] { (rib, nameof(rib)), (fold, nameof(fold)), (solid, nameof(solid)) })
        {
            if (!(float.IsFinite(value) && value > 0))
                throw new ArgumentOutOfRangeException(name, value, "The value must be finite and positive.");
        }
        return new LoftedHull(this, door, pocket, new SurroundWidths(rib, fold, solid), panes);
    }

    /// <summary>
    /// A pane with the scene id <paramref name="pane"/> in the cell of <paramref name="edge"/> in <paramref name="bay"/>,
    /// counted from 1 at the nose. Its hole is the cell with each side moved in by its surround (see
    /// <see cref="Surrounds"/>), at both skins. Throws <see cref="InvalidOperationException"/> before the surrounds, and
    /// <see cref="ArgumentException"/> for a cell of the door's panel, a cell that holds a pane already, an id that is
    /// taken, and a pane in a cell narrower than its surrounds. A new pane makes the surrounds of its neighbors narrower, so
    /// each call checks every pane.
    /// </summary>
    public LoftedHull Glass(string pane, int edge, int bay)
    {
        ArgumentException.ThrowIfNullOrEmpty(pane);
        ArgumentOutOfRangeException.ThrowIfNegative(edge);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(edge, Corners);
        ArgumentOutOfRangeException.ThrowIfLessThan(bay, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(bay, Bays);
        if (door != null && door.Bay == bay && PanelCorners(door.FromCorner, door.ToCorner).SkipLast(1).Contains(edge))
            throw new ArgumentException($"The cell of edge {edge} in bay {bay} is part of the door's panel, which stays solid round the doorway.", nameof(edge));
        if (Cells().Any(cell => cell.Edge == edge && cell.Bay == bay))
            throw new ArgumentException($"The cell of edge {edge} in bay {bay} holds a pane already.", nameof(edge));
        return WithPane(new CellPane(pane, edge, bay));
    }

    /// <summary>
    /// A pane with the scene id <paramref name="pane"/> in the front, the first cross-section: in its part above the line
    /// from <paramref name="toCorner"/> to <paramref name="fromCorner"/>, the corners from <paramref name="fromCorner"/>
    /// counterclockwise to <paramref name="toCorner"/>. Its hole is that part moved in by the rib's surround along the rim
    /// and by the solid surround along the line, at both skins. Throws <see cref="InvalidOperationException"/> before the
    /// surrounds or for a second pane in the front, and <see cref="ArgumentException"/> for a part with fewer than three
    /// corners, an id that is taken, and a part narrower than its surrounds.
    /// </summary>
    public LoftedHull NoseGlass(string pane, int fromCorner, int toCorner)
    {
        ArgumentException.ThrowIfNullOrEmpty(pane);
        ArgumentOutOfRangeException.ThrowIfNegative(fromCorner);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(fromCorner, Corners);
        ArgumentOutOfRangeException.ThrowIfNegative(toCorner);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(toCorner, Corners);
        if (Fronts().Any())
            throw new InvalidOperationException("The front holds a pane already.");
        int count = PanelCorners(fromCorner, toCorner).Count();
        if (count < 3 || count > Corners - 1)
            throw new ArgumentException($"The part of the front from corner {fromCorner} to corner {toCorner} has {count} corners. It needs at least three, and the part below the line needs them too.", nameof(toCorner));
        return WithPane(new FrontPane(pane, fromCorner, toCorner));
    }

    /// <summary>A hull with <paramref name="pane"/> added, after it checks the id and the hole of every pane.</summary>
    private LoftedHull WithPane(PaneCut pane)
    {
        if (surrounds == null)
            throw new InvalidOperationException("Call Surrounds before the glass: the size of a pane's hole comes from them.");
        if (panes.Any(other => other.Id == pane.Id))
            throw new ArgumentException($"The scene id {pane.Id} belongs to another pane.", nameof(pane));
        var hull = new LoftedHull(this, door, pocket, surrounds, [.. panes, pane]);
        foreach (PaneCut each in hull.panes)
            hull.HoleOf(each);
        return hull;
    }

    /// <summary>
    /// The panes, in the order of the calls that made them: each with its hole at both skins and its mesh, a slab 2 cm
    /// thick in the middle of the wall. The slab's outline is the hole's outline halfway between the skins, grown by 1 cm on
    /// each side, so that its edges hide in the surround. Its vertices are relative to its center, the middle of that
    /// outline, so that the renderer sorts and culls the pane at its real place. Each call builds new meshes.
    /// </summary>
    public IReadOnlyList<HullPane> Panes() => [.. panes.Select(pane =>
    {
        (Double3[] outer, Double3[] inner, Double3 normal) = HoleOf(pane);
        Double3[] middle = [.. outer.Zip(inner, (o, i) => (o + i) / 2)];
        Double3 center = middle.Aggregate(Double3.Zero, (sum, corner) => sum + corner) / middle.Length;
        Double3[] outline = MovedIn(middle, [.. middle.Select(_ => -PaneGrowth)], normal);
        const double half = PaneThickness / 2;
        Vector3[] front = [.. outline.Select(corner => (corner + normal * half - center).ToVector3())];
        Vector3[] back = [.. outline.Select(corner => (corner - normal * half - center).ToVector3())];
        var mesh = new Builder();
        mesh.Polygon(normal.ToVector3(), front);
        mesh.Polygon((-normal).ToVector3(), back);
        int n = outline.Length;
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            Vector3 outward = OutOf(outline, i, normal).ToVector3();
            mesh.Polygon(outward, [front[i], front[next], back[next], back[i]]);
        }
        return new HullPane(pane.Id, center.ToVector3(), [.. outer.Select(corner => corner.ToVector3())], [.. inner.Select(corner => corner.ToVector3())], mesh.Build());
    })];

    /// <summary>The thickness of a pane's slab, in meters.</summary>
    private const double PaneThickness = 0.02;

    /// <summary>How far a pane's slab reaches past its hole on each side, in meters, into the surround.</summary>
    private const double PaneGrowth = 0.01;

    /// <summary>
    /// The hole of <paramref name="pane"/>: its corners at the outer skin and at the inner skin, in the same order, and the
    /// unit normal of its cell, out of the hull. Corner i lies between the hole's sides i - 1 and i, near corner i of its
    /// cell or front part. Throws <see cref="ArgumentException"/> when a side of the hole vanishes or turns round, because
    /// the cell is narrower than its surrounds.
    /// </summary>
    private (Double3[] Outer, Double3[] Inner, Double3 Normal) HoleOf(PaneCut pane)
    {
        SurroundWidths widths = surrounds!;
        Double3[] outerPanel, innerPanel;
        double[] distances;
        Double3 normal;
        switch (pane)
        {
            case CellPane cell:
            {
                int e = cell.Edge, next = (e + 1) % Corners, before = (e + Corners - 1) % Corners;
                float z0 = OuterLevel(cell.Bay - 1), z1 = OuterLevel(cell.Bay), iz0 = InnerLevel(cell.Bay - 1), iz1 = InnerLevel(cell.Bay);
                outerPanel = [OuterAt(e, z0), OuterAt(next, z0), OuterAt(next, z1), OuterAt(e, z1)];
                innerPanel = [InnerAt(e, iz0), InnerAt(next, iz0), InnerAt(next, iz1), InnerAt(e, iz1)];
                double FoldSide(int neighbor) => HasGlass(neighbor, cell.Bay) ? widths.Fold : widths.Solid;
                distances = [widths.Rib, FoldSide(next), widths.Rib, FoldSide(before)];
                normal = CellNormal(OuterPlace(e, z0), OuterPlace(next, z0), OuterPlace(e, z1), e);
                break;
            }
            case FrontPane front:
            {
                int[] corners = [.. PanelCorners(front.FromCorner, front.ToCorner)];
                outerPanel = [.. corners.Select(k => OuterAt(k, OuterLevel(0)))];
                innerPanel = [.. corners.Select(k => InnerAt(k, InnerLevel(0)))];
                distances = [.. corners.SkipLast(1).Select(_ => (double)widths.Rib), widths.Solid];
                normal = -Double3.UnitZ;
                break;
            }
            default:
                throw new InvalidOperationException($"Unknown pane {pane}.");
        }
        Double3[] outer = MovedIn(outerPanel, distances, normal), inner = MovedIn(innerPanel, distances, normal);
        foreach ((Double3[] panel, Double3[] hole) in new[] { (outerPanel, outer), (innerPanel, inner) })
        {
            for (int i = 0; i < panel.Length; i++)
            {
                int next = (i + 1) % panel.Length;
                Double3 side = panel[next] - panel[i];
                if (!(Double3.Dot(hole[next] - hole[i], side) > 1e-6 * side.LengthSquared))
                    throw new ArgumentException($"The pane {pane.Id} lies in a cell or part narrower than its surrounds: side {i} of its hole vanishes.", nameof(pane));
            }
        }
        return (outer, inner, normal);
    }

    /// <summary>True when the cell of <paramref name="edge"/> in <paramref name="bay"/> holds a pane.</summary>
    private bool HasGlass(int edge, int bay) => Cells().Any(cell => cell.Edge == edge && cell.Bay == bay);

    /// <summary>The panes in cells, in the order of the calls that made them.</summary>
    private IEnumerable<CellPane> Cells()
    {
        foreach (PaneCut pane in panes)
        {
            if (pane is CellPane cell)
                yield return cell;
        }
    }

    /// <summary>The pane in the front, if the hull has one.</summary>
    private IEnumerable<FrontPane> Fronts()
    {
        foreach (PaneCut pane in panes)
        {
            if (pane is FrontPane front)
                yield return front;
        }
    }

    /// <summary>
    /// The flat, convex <paramref name="polygon"/>, whose plane has the unit <paramref name="normal"/>, with each side i,
    /// from corner i to the next, moved in by <paramref name="distances"/>[i] in its plane, or out for a negative distance.
    /// Corner i of the result lies where sides i - 1 and i meet.
    /// </summary>
    private static Double3[] MovedIn(Double3[] polygon, double[] distances, Double3 normal)
    {
        int n = polygon.Length;
        var lines = new (Double3 Point, Double3 Along)[n];
        for (int i = 0; i < n; i++)
            lines[i] = (polygon[i] - OutOf(polygon, i, normal) * distances[i], (polygon[(i + 1) % n] - polygon[i]).Normalized());
        return [.. Enumerable.Range(0, n).Select(i =>
        {
            (Double3 Point, Double3 Along) a = lines[(i + n - 1) % n], b = lines[i];
            Double3 cross = Double3.Cross(a.Along, b.Along);
            return a.Point + a.Along * (Double3.Dot(Double3.Cross(b.Point - a.Point, b.Along), cross) / cross.LengthSquared);
        })];
    }

    /// <summary>The unit normal of side <paramref name="side"/> of the flat, convex <paramref name="polygon"/>, in its plane, out of it.</summary>
    private static Double3 OutOf(Double3[] polygon, int side, Double3 normal)
    {
        Double3 middle = polygon.Aggregate(Double3.Zero, (sum, corner) => sum + corner) / polygon.Length;
        Double3 outward = Double3.Cross((polygon[(side + 1) % polygon.Length] - polygon[side]).Normalized(), normal);
        return Double3.Dot(outward, polygon[side] - middle) < 0 ? -outward : outward;
    }

    /// <summary>
    /// The drawn hull: the outer skin, the inner skin facing into the cabin, the door's hole with its four sides, each
    /// pane's hole with its four reveals, the nose wall's two faces, and the tail wall's two faces with the pocket and its
    /// housing. A cell or a front part with a pane is drawn as one strip per side round its hole, at each skin. With a pane
    /// in the front, the front is two panels at each skin, below and above the pane's line. The panes themselves are not in
    /// this mesh (see <see cref="Panes"/>). Each call builds a new mesh.
    /// </summary>
    public MeshData Mesh()
    {
        var mesh = new Builder();
        int n = Corners;
        var outer = new Vector3[Bays + 1][];
        var inner = new Vector3[Bays + 1][];
        for (int level = 0; level <= Bays; level++)
        {
            outer[level] = [.. Enumerable.Range(0, n).Select(k => OuterPlace(k, OuterLevel(level)))];
            inner[level] = [.. Enumerable.Range(0, n).Select(k => InnerPlace(k, InnerLevel(level)))];
        }

        for (int bay = 1; bay <= Bays; bay++)
        {
            for (int e = 0; e < n; e++)
            {
                if (door != null && door.Bay == bay && PanelCorners(door.FromCorner, door.ToCorner).SkipLast(1).Contains(e))
                    continue;
                int next = (e + 1) % n;
                Vector3 normal = CellNormal(outer[bay - 1][e], outer[bay - 1][next], outer[bay][e], e);
                Vector3[] outerCell = [outer[bay - 1][e], outer[bay - 1][next], outer[bay][next], outer[bay][e]];
                Vector3[] innerCell = [inner[bay - 1][e], inner[bay - 1][next], inner[bay][next], inner[bay][e]];
                CellPane[] glass = [.. Cells().Where(cell => cell.Edge == e && cell.Bay == bay)];
                if (glass.Length == 0)
                {
                    mesh.Polygon(normal, outerCell);
                    mesh.Polygon(-normal, innerCell);
                }
                else
                {
                    HoleThrough(mesh, glass[0], normal, outerCell, innerCell);
                }
            }
        }
        if (door != null)
            DoorPanels(mesh, outer, inner);

        FrontPane[] fronts = [.. Fronts()];
        if (fronts.Length == 0)
        {
            mesh.Polygon(-Vector3.UnitZ, outer[0]);
            mesh.Polygon(Vector3.UnitZ, inner[0]);
        }
        else
        {
            // The front is two panels, below and above the line between the pane's two corners, and the upper one holds
            // the hole.
            FrontPane nose = fronts[0];
            int[] lower = [.. PanelCorners(nose.ToCorner, nose.FromCorner)], upper = [.. PanelCorners(nose.FromCorner, nose.ToCorner)];
            mesh.Polygon(-Vector3.UnitZ, [.. lower.Select(k => outer[0][k])]);
            mesh.Polygon(Vector3.UnitZ, [.. lower.Select(k => inner[0][k])]);
            HoleThrough(mesh, nose, -Vector3.UnitZ, [.. upper.Select(k => outer[0][k])], [.. upper.Select(k => inner[0][k])]);
        }
        if (pocket == null)
        {
            mesh.Polygon(Vector3.UnitZ, outer[Bays]);
            mesh.Polygon(-Vector3.UnitZ, inner[Bays]);
        }
        else
        {
            TailWall(mesh, outer[Bays], inner[Bays], pocket);
        }
        return mesh.Build();
    }

    /// <summary>
    /// The outer collision shape: the convex hull of the true corners of every cross-section, the corners where the outline
    /// bends, with the default convex radius. The corners inside a face add nothing to the hull. Each call builds a new shape.
    /// </summary>
    public ShapeDesc Shape() => new ConvexHullShapeDesc([.. sections.SelectMany(section => TrueCorners().Select(k => new Vector3(section.Corners[k], section.Z)))]);

    /// <summary>
    /// The interior: convex hulls of convex radius 0 that fill the drawn hull's solid, at its identity pose. The nose wall
    /// is the hull of the outer cross-sections at the front and one wall behind it. Each face has a wall piece in each loft
    /// piece, the hull of its outer and inner edges at both ends, and two neighbor pieces meet in their mitre. The door's
    /// face is cut into four hulls round the doorway. The tail wall is the hull of the outer cross-sections one wall before
    /// the tail and at it, solid over the pocket, and the housing, a box round the pocket, closes the rest of it. The
    /// caller adds its furniture. Each call builds new shapes.
    /// </summary>
    public IReadOnlyList<CompoundChild> Interior()
    {
        var pieces = new List<Vector3[]>();
        float first = sections[0].Z, last = sections[^1].Z;
        pieces.Add([.. TrueCorners().Select(k => OuterPlace(k, first)), .. TrueCorners().Select(k => OuterPlace(k, first + wall))]);
        var faces = Faces().ToArray();
        for (int j = 0; j < sections.Length - 1; j++)
        {
            float z0 = MathF.Max(sections[j].Z, InnerLevel(0)), z1 = MathF.Min(sections[j + 1].Z, InnerLevel(Bays));
            foreach ((int from, int to) in faces)
            {
                if (door != null && OuterLevel(door.Bay - 1) >= sections[j].Z && OuterLevel(door.Bay) <= sections[j + 1].Z && FaceOf(door.FromCorner) == (from, to))
                {
                    pieces.AddRange(DoorPieces(from, to, z0, z1));
                    continue;
                }
                pieces.Add(WallPiece(from, to, z0, z1));
            }
        }
        pieces.Add([.. TrueCorners().Select(k => OuterPlace(k, last - wall)), .. TrueCorners().Select(k => OuterPlace(k, last))]);
        if (pocket != null)
        {
            float outer = pocket.Half + pocket.Housing;
            Vector3 min = new(pocket.X - outer, pocket.Y - outer, last - pocket.Depth - pocket.Housing), max = new(pocket.X + outer, pocket.Y + outer, last - wall);
            pieces.Add([.. Enumerable.Range(0, 8).Select(i => new Vector3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z))]);
        }
        return [.. pieces.Select(points => new CompoundChild(new ConvexHullShapeDesc(points, ConvexRadius: 0), Pose.Identity))];
    }

    /// <summary>
    /// The mass properties of the outer loft at one density, for a hull of <paramref name="mass"/> kilograms: the solid that
    /// <see cref="Shape"/> bounds, without its convex radius, and with no door or pocket cut out. It integrates the loft
    /// exactly. Between two cross-sections, the corners of the cross-section at z move linearly in z, so its area is a
    /// polynomial in z of degree 2, its first moments of degree 3 and its second moments of degree 4. Each integrand, with
    /// its factor of z or z², has a degree of at most 4, and a Gauss rule of three points over each loft piece is exact to
    /// degree 5. It integrates in doubles and rounds its result to floats once. The principal axes are ordered so that each
    /// lies nearest to the body's axis of the same name, and points along it.
    /// </summary>
    public HullMass MassOf(float mass)
    {
        if (!(float.IsFinite(mass) && mass > 0))
            throw new ArgumentOutOfRangeException(nameof(mass), mass, "The mass must be finite and positive.");
        // The nodes and weights of the Gauss rule of three points on [0, 1].
        double spread = 0.5 * Math.Sqrt(0.6);
        ReadOnlySpan<double> nodes = [0.5 - spread, 0.5, 0.5 + spread];
        ReadOnlySpan<double> weights = [5.0 / 18, 8.0 / 18, 5.0 / 18];
        double volume = 0, mx = 0, my = 0, mz = 0, qxx = 0, qyy = 0, qzz = 0, qxy = 0, qxz = 0, qyz = 0;
        int n = Corners;
        var x = new double[n];
        var y = new double[n];
        for (int j = 0; j < sections.Length - 1; j++)
        {
            HullSection a = sections[j], b = sections[j + 1];
            double length = b.Z - a.Z;
            for (int g = 0; g < 3; g++)
            {
                double t = nodes[g], w = weights[g] * length, z = a.Z + t * length;
                for (int k = 0; k < n; k++)
                {
                    x[k] = a.Corners[k].X + t * (b.Corners[k].X - a.Corners[k].X);
                    y[k] = a.Corners[k].Y + t * (b.Corners[k].Y - a.Corners[k].Y);
                }
                // Green's theorem over the polygon: its area, its first moments and its second moments.
                double area = 0, sx = 0, sy = 0, sxx = 0, syy = 0, sxy = 0;
                for (int k = 0; k < n; k++)
                {
                    int next = (k + 1) % n;
                    double cross = x[k] * y[next] - x[next] * y[k];
                    area += cross / 2;
                    sx += (x[k] + x[next]) * cross / 6;
                    sy += (y[k] + y[next]) * cross / 6;
                    sxx += (x[k] * x[k] + x[k] * x[next] + x[next] * x[next]) * cross / 12;
                    syy += (y[k] * y[k] + y[k] * y[next] + y[next] * y[next]) * cross / 12;
                    sxy += (x[k] * y[next] + 2 * x[k] * y[k] + 2 * x[next] * y[next] + x[next] * y[k]) * cross / 24;
                }
                volume += w * area;
                mx += w * sx;
                my += w * sy;
                mz += w * z * area;
                qxx += w * sxx;
                qyy += w * syy;
                qzz += w * z * z * area;
                qxy += w * sxy;
                qxz += w * z * sx;
                qyz += w * z * sy;
            }
        }
        double density = mass / volume, cx = mx / volume, cy = my / volume, cz = mz / volume;
        // The second moments about the center of mass, as mass integrals.
        double cxx = density * (qxx - volume * cx * cx), cyy = density * (qyy - volume * cy * cy), czz = density * (qzz - volume * cz * cz);
        double cxy = density * (qxy - volume * cx * cy), cxz = density * (qxz - volume * cx * cz), cyz = density * (qyz - volume * cy * cz);
        double[,] tensor =
        {
            { cyy + czz, -cxy, -cxz },
            { -cxy, cxx + czz, -cyz },
            { -cxz, -cyz, cxx + cyy },
        };
        (double[] moments, double[,] axes) = Principal(tensor);
        return new HullMass(new Vector3((float)cx, (float)cy, (float)cz),
            new PrincipalInertia(new Vector3((float)moments[0], (float)moments[1], (float)moments[2]), AxesRotation(axes)));
    }

    /// <summary>
    /// The eigenvalues and the unit eigenvectors of the symmetric <paramref name="tensor"/>, by Jacobi's rotations, ordered
    /// so that eigenvector i, column i of the axes, lies nearest to axis i and points along it, and the three make a
    /// right-handed set.
    /// </summary>
    private static (double[] Values, double[,] Vectors) Principal(double[,] tensor)
    {
        var a = (double[,])tensor.Clone();
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 50; sweep++)
        {
            double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
            double scale = Math.Abs(a[0, 0]) + Math.Abs(a[1, 1]) + Math.Abs(a[2, 2]);
            if (off <= 1e-15 * scale)
                break;
            foreach ((int p, int q) in new[] { (0, 1), (0, 2), (1, 2) })
            {
                if (a[p, q] == 0)
                    continue;
                double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                double t = Math.Sign(theta == 0 ? 1 : theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                for (int k = 0; k < 3; k++)
                {
                    double akp = a[k, p], akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[k, q] = s * akp + c * akq;
                }
                for (int k = 0; k < 3; k++)
                {
                    double apk = a[p, k], aqk = a[q, k];
                    a[p, k] = c * apk - s * aqk;
                    a[q, k] = s * apk + c * aqk;
                }
                for (int k = 0; k < 3; k++)
                {
                    double vkp = v[k, p], vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq;
                    v[k, q] = s * vkp + c * vkq;
                }
            }
        }
        // Each body axis takes the eigenvector with the largest component along it, the largest first.
        var order = new int[3];
        var taken = new bool[3];
        var assigned = new bool[3];
        for (int round = 0; round < 3; round++)
        {
            (int axis, int vector, double size) best = (-1, -1, -1);
            for (int axis = 0; axis < 3; axis++)
                for (int vector = 0; vector < 3; vector++)
                    if (!assigned[axis] && !taken[vector] && Math.Abs(v[axis, vector]) > best.size)
                        best = (axis, vector, Math.Abs(v[axis, vector]));
            order[best.axis] = best.vector;
            assigned[best.axis] = true;
            taken[best.vector] = true;
        }
        var values = new double[3];
        var vectors = new double[3, 3];
        for (int i = 0; i < 3; i++)
        {
            int from = order[i];
            values[i] = a[from, from];
            double sign = v[i, from] < 0 ? -1 : 1;
            for (int k = 0; k < 3; k++)
                vectors[k, i] = sign * v[k, from];
        }
        // The third axis is the cross product of the first two, so the set is right-handed.
        vectors[0, 2] = vectors[1, 0] * vectors[2, 1] - vectors[2, 0] * vectors[1, 1];
        vectors[1, 2] = vectors[2, 0] * vectors[0, 1] - vectors[0, 0] * vectors[2, 1];
        vectors[2, 2] = vectors[0, 0] * vectors[1, 1] - vectors[1, 0] * vectors[0, 1];
        return (values, vectors);
    }

    /// <summary>The rotation that turns the body's X, Y and Z into the columns of <paramref name="axes"/>.</summary>
    private static Quaternion AxesRotation(double[,] axes)
    {
        // System.Numerics turns row vectors, so row i of the matrix is the image of axis i, column i of the axes.
        var matrix = new Matrix4x4(
            (float)axes[0, 0], (float)axes[1, 0], (float)axes[2, 0], 0,
            (float)axes[0, 1], (float)axes[1, 1], (float)axes[2, 1], 0,
            (float)axes[0, 2], (float)axes[1, 2], (float)axes[2, 2], 0,
            0, 0, 0, 1);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(matrix));
    }

    /// <summary>
    /// The outer and the inner panel of <paramref name="pane"/>, with corners <paramref name="outerPanel"/> and
    /// <paramref name="innerPanel"/>, round its hole, each drawn as one four-sided strip per side between the panel's edge
    /// and the hole's, and the reveals: one flat face for each side of the hole, from the outer hole to the inner one, facing
    /// into the hole. The outer panel faces along <paramref name="normal"/>.
    /// </summary>
    private void HoleThrough(Builder mesh, PaneCut pane, Vector3 normal, Vector3[] outerPanel, Vector3[] innerPanel)
    {
        (Double3[] outerHole, Double3[] innerHole, _) = HoleOf(pane);
        Vector3[] outer = [.. outerHole.Select(corner => corner.ToVector3())], inner = [.. innerHole.Select(corner => corner.ToVector3())];
        int n = outer.Length;
        Vector3 middle = outer.Concat(inner).Aggregate(Vector3.Zero, (sum, corner) => sum + corner) / (2 * n);
        for (int i = 0; i < n; i++)
        {
            int next = (i + 1) % n;
            mesh.Polygon(normal, [outerPanel[i], outerPanel[next], outer[next], outer[i]]);
            mesh.Polygon(-normal, [innerPanel[i], innerPanel[next], inner[next], inner[i]]);
            Vector3 reveal = Vector3.Normalize(Vector3.Cross(outer[next] - outer[i], inner[i] - outer[i]));
            mesh.Polygon(Vector3.Dot(reveal, middle - outer[i]) > 0 ? reveal : -reveal, [outer[i], outer[next], inner[next], inner[i]]);
        }
    }

    /// <summary>The door's outer and inner panels with their holes, and the four sides of the hole.</summary>
    private void DoorPanels(Builder mesh, Vector3[][] outer, Vector3[][] inner)
    {
        DoorCut cut = door!;
        int[] corners = [.. PanelCorners(cut.FromCorner, cut.ToCorner)];
        int axis = cut.Axis, along = 1 - axis;
        BoundingBox box = cut.Box;
        Vector3[] Outline(Vector3[][] places) => [.. corners.Select(k => places[cut.Bay - 1][k]), .. corners.Reverse().Select(k => places[cut.Bay][k])];
        // The hole's corners in the panel's plane, in order round it: (along, z) at (min, min), (max, min), (max, max) and (min, max).
        Vector3[] Hole(float plane)
        {
            Vector3 At(float s, float z) => axis == 0 ? new Vector3(plane, s, z) : new Vector3(s, plane, z);
            float s0 = Component(box.Min, along), s1 = Component(box.Max, along);
            return [At(s0, box.Min.Z), At(s1, box.Min.Z), At(s1, box.Max.Z), At(s0, box.Max.Z)];
        }
        Vector3[] outerPanel = Outline(outer), innerPanel = Outline(inner);
        Vector3 normal = CellNormal(outer[cut.Bay - 1][cut.FromCorner], outer[cut.Bay - 1][(cut.FromCorner + 1) % Corners], outer[cut.Bay][cut.FromCorner], cut.FromCorner);
        Vector3[] outerHole = Hole(Component(outerPanel[0], axis)), innerHole = Hole(Component(innerPanel[0], axis));
        mesh.Ring(normal, outerPanel, outerHole);
        mesh.Ring(-normal, innerPanel, innerHole);
        Vector3 alongAxis = axis == 0 ? Vector3.UnitY : Vector3.UnitX;
        // The side from hole corner i to the next faces into the hole: the side at the least z faces +Z, the one at the
        // greatest along faces back along, and so on round it.
        Vector3[] facing = [Vector3.UnitZ, -alongAxis, -Vector3.UnitZ, alongAxis];
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            mesh.Polygon(facing[i], [outerHole[i], outerHole[next], innerHole[next], innerHole[i]]);
        }
    }

    /// <summary>
    /// The tail face round the pocket's mouth, the pocket's four walls and its floor, and inside, the aft face round the
    /// housing, the housing's four walls and its front.
    /// </summary>
    private void TailWall(Builder mesh, Vector3[] outer, Vector3[] inner, PocketCut cut)
    {
        float last = sections[^1].Z;
        float floor = last - cut.Depth, aft = last - wall, front = floor - cut.Housing;
        Vector3[] Square(float half, float z) =>
            [new(cut.X - half, cut.Y - half, z), new(cut.X + half, cut.Y - half, z), new(cut.X + half, cut.Y + half, z), new(cut.X - half, cut.Y + half, z)];
        Vector3[] mouth = Square(cut.Half, last), bottom = Square(cut.Half, floor);
        float outerHalf = cut.Half + cut.Housing;
        Vector3[] back = Square(outerHalf, aft), face = Square(outerHalf, front);
        mesh.Ring(Vector3.UnitZ, outer, mouth);
        mesh.Ring(-Vector3.UnitZ, inner, back);
        // The sides of a square from corner i to the next face out of it along these.
        Vector3[] outward = [-Vector3.UnitY, Vector3.UnitX, Vector3.UnitY, -Vector3.UnitX];
        for (int i = 0; i < 4; i++)
        {
            int next = (i + 1) % 4;
            mesh.Polygon(-outward[i], [mouth[i], mouth[next], bottom[next], bottom[i]]);
            mesh.Polygon(outward[i], [back[i], back[next], face[next], face[i]]);
        }
        mesh.Polygon(Vector3.UnitZ, bottom);
        mesh.Polygon(-Vector3.UnitZ, face);
    }

    /// <summary>The wall piece of the face from corner <paramref name="from"/> to <paramref name="to"/>, from <paramref name="z0"/> to <paramref name="z1"/>.</summary>
    private Vector3[] WallPiece(int from, int to, float z0, float z1) =>
        [OuterPlace(from, z0), OuterPlace(to, z0), InnerPlace(from, z0), InnerPlace(to, z0), OuterPlace(from, z1), OuterPlace(to, z1), InnerPlace(from, z1), InnerPlace(to, z1)];

    /// <summary>
    /// The wall piece of the door's face, from <paramref name="z0"/> to <paramref name="z1"/>, cut round the doorway into four
    /// convex hulls: the whole piece ahead of the doorway and behind it, and beside it the piece from each of its two sides
    /// out to the face's mitre.
    /// </summary>
    private IEnumerable<Vector3[]> DoorPieces(int from, int to, float z0, float z1)
    {
        DoorCut cut = door!;
        BoundingBox box = cut.Box;
        yield return WallPiece(from, to, z0, box.Min.Z);
        yield return WallPiece(from, to, box.Max.Z, z1);
        int axis = cut.Axis, along = 1 - axis;
        // The door's side nearer to each end of the face.
        bool fromIsLow = Component(OuterPlace(from, box.Min.Z), along) < Component(OuterPlace(to, box.Min.Z), along);
        float nearFrom = fromIsLow ? Component(box.Min, along) : Component(box.Max, along);
        float nearTo = fromIsLow ? Component(box.Max, along) : Component(box.Min, along);
        Vector3 At(Vector3 onPlane, float s) => along == 1 ? onPlane with { Y = s } : onPlane with { X = s };
        foreach ((int end, float side) in new[] { (from, nearFrom), (to, nearTo) })
        {
            yield return
            [
                .. new[] { box.Min.Z, box.Max.Z }.SelectMany(z => new[]
                {
                    OuterPlace(end, z), InnerPlace(end, z), At(OuterPlace(end, z), side), At(InnerPlace(end, z), side),
                }),
            ];
        }
    }

    /// <summary>The corners from <paramref name="from"/> to <paramref name="to"/>, counterclockwise, with both ends.</summary>
    private IEnumerable<int> PanelCorners(int from, int to)
    {
        for (int k = from; k != to; k = (k + 1) % Corners)
            yield return k;
        yield return to;
    }

    /// <summary>The faces, each as its first and its last corner, counterclockwise, in order from the face after corner 0's bend.</summary>
    private IEnumerable<(int From, int To)> Faces()
    {
        int n = Corners;
        int start = Array.IndexOf(straight, false);
        for (int k = start; ;)
        {
            int end = (k + 1) % n;
            while (straight[end])
                end = (end + 1) % n;
            yield return (k, end);
            k = end;
            if (k == start)
                yield break;
        }
    }

    /// <summary>The face that holds the edge from corner <paramref name="corner"/> to the next.</summary>
    private (int From, int To) FaceOf(int corner)
    {
        int n = Corners;
        int from = corner;
        while (straight[from])
            from = (from + n - 1) % n;
        int to = (corner + 1) % n;
        while (straight[to])
            to = (to + 1) % n;
        return (from, to);
    }

    /// <summary>The corners where the outline bends, in order.</summary>
    private IEnumerable<int> TrueCorners() => Enumerable.Range(0, Corners).Where(k => !straight[k]);

    /// <summary>The unit normal of the cell with the corners <paramref name="a"/> to <paramref name="b"/> of edge <paramref name="edge"/> and the corner <paramref name="aNext"/> along the length, facing out of the hull.</summary>
    private Vector3 CellNormal(Vector3 a, Vector3 b, Vector3 aNext, int edge)
    {
        Vector3 normal = Vector3.Normalize(Vector3.Cross(b - a, aNext - a));
        (double nx, double ny) = InwardNormal(sections[0], edge);
        return nx * normal.X + ny * normal.Y > 0 ? -normal : normal;
    }

    /// <summary>Corner <paramref name="corner"/> of the outer skin at <paramref name="z"/>, in doubles.</summary>
    private (double X, double Y) Outer(int corner, float z)
    {
        int j = 0;
        while (j < sections.Length - 2 && z > sections[j + 1].Z)
            j++;
        HullSection a = sections[j], b = sections[j + 1];
        if (z == a.Z)
            return (a.Corners[corner].X, a.Corners[corner].Y);
        if (z == b.Z)
            return (b.Corners[corner].X, b.Corners[corner].Y);
        double t = ((double)z - a.Z) / ((double)b.Z - a.Z);
        return (a.Corners[corner].X + t * (b.Corners[corner].X - a.Corners[corner].X), a.Corners[corner].Y + t * (b.Corners[corner].Y - a.Corners[corner].Y));
    }

    /// <summary>Corner <paramref name="corner"/> of the outer skin at <paramref name="z"/>, in doubles.</summary>
    private Double3 OuterAt(int corner, float z)
    {
        (double x, double y) = Outer(corner, z);
        return new Double3(x, y, z);
    }

    /// <summary>Corner <paramref name="corner"/> of the inner skin at <paramref name="z"/>, in doubles.</summary>
    private Double3 InnerAt(int corner, float z)
    {
        (double x, double y) = Outer(corner, z);
        return new Double3(x + inset[corner].X, y + inset[corner].Y, z);
    }

    private Vector3 OuterPlace(int corner, float z)
    {
        (double x, double y) = Outer(corner, z);
        return new Vector3((float)x, (float)y, z);
    }

    private Vector3 InnerPlace(int corner, float z)
    {
        (double x, double y) = Outer(corner, z);
        return new Vector3((float)(x + inset[corner].X), (float)(y + inset[corner].Y), z);
    }

    /// <summary>How far the point (<paramref name="x"/>, <paramref name="y"/>) lies inside the outer cross-section at <paramref name="z"/>: the least distance to the line of an edge, negative outside.</summary>
    private double InsideOuter(float z, double x, double y)
    {
        double least = double.MaxValue;
        for (int e = 0; e < Corners; e++)
        {
            (double ax, double ay) = Outer(e, z);
            (double nx, double ny) = InwardNormal(sections[0], e);
            least = Math.Min(least, nx * (x - ax) + ny * (y - ay));
        }
        return least;
    }

    private static float Component(Vector3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static (double X, double Y) Edge(HullSection section, int edge)
    {
        Vector2 a = section.Corners[edge], b = section.Corners[(edge + 1) % section.Corners.Length];
        return ((double)b.X - a.X, (double)b.Y - a.Y);
    }

    /// <summary>The unit normal of edge <paramref name="edge"/> that points into the counterclockwise cross-section.</summary>
    private static (double X, double Y) InwardNormal(HullSection section, int edge)
    {
        (double x, double y) = Edge(section, edge);
        double length = Math.Sqrt(x * x + y * y);
        return (-y / length, x / length);
    }

    /// <summary>Refuses a cross-section with an edge of no length, one that runs clockwise, and one with a corner that bends outward.</summary>
    private static void CheckConvex(HullSection section)
    {
        Vector2[] corners = section.Corners;
        int n = corners.Length;
        double area = 0;
        for (int k = 0; k < n; k++)
        {
            area += (double)corners[k].X * corners[(k + 1) % n].Y - (double)corners[(k + 1) % n].X * corners[k].Y;
            (double x, double y) = Edge(section, k);
            if (x * x + y * y == 0)
                throw new ArgumentException($"Edge {k} of the cross-section at z = {section.Z} has no length.", "crossSections");
        }
        if (!(area > 0))
            throw new ArgumentException($"The cross-section at z = {section.Z} runs clockwise. Its corners must run counterclockwise seen from +Z.", "crossSections");
        for (int k = 0; k < n; k++)
        {
            (double ax, double ay) = Edge(section, (k + n - 1) % n);
            (double bx, double by) = Edge(section, k);
            if (ax * by - ay * bx < -Parallel * Math.Sqrt((ax * ax + ay * ay) * (bx * bx + by * by)))
                throw new ArgumentException($"The cross-section at z = {section.Z} is not convex: it bends outward at corner {k}.", "crossSections");
        }
    }

    /// <summary>Builds the mesh of flat panels, each with its own vertices and its own normal.</summary>
    private sealed class Builder
    {
        private readonly List<Vertex> vertices = new();
        private readonly List<uint> indices = new();

        public MeshData Build() => new([.. vertices], [.. indices]);

        /// <summary>
        /// A convex panel facing along <paramref name="normal"/>. A triangle, or a quad without a straight corner, is split
        /// between its corners. Any other panel is a fan from its middle, so that no triangle has its three corners on one
        /// line.
        /// </summary>
        public void Polygon(Vector3 normal, Vector3[] corners)
        {
            Vector3 tangent = Vector3.Normalize(corners[1] - corners[0]);
            bool bends = corners.Length <= 4 && Enumerable.Range(0, corners.Length).All(i =>
                Vector3.Cross(corners[(i + 1) % corners.Length] - corners[i], corners[(i + 2) % corners.Length] - corners[(i + 1) % corners.Length]).Length() > 1e-6f);
            if (bends)
            {
                for (int i = 1; i + 1 < corners.Length; i++)
                    Triangle(normal, tangent, corners[0], corners[i], corners[i + 1]);
                return;
            }
            Vector3 middle = corners.Aggregate(Vector3.Zero, (sum, corner) => sum + corner) / corners.Length;
            for (int i = 0; i < corners.Length; i++)
                Triangle(normal, tangent, middle, corners[i], corners[(i + 1) % corners.Length]);
        }

        /// <summary>
        /// A convex panel with a convex hole inside it, facing along <paramref name="normal"/>: a fan between the two
        /// outlines. The generator walks both outlines in angle round the hole's middle, and each triangle joins two
        /// neighbors on one outline to a corner of the other, whichever comes next in angle.
        /// </summary>
        public void Ring(Vector3 normal, Vector3[] panel, Vector3[] hole)
        {
            Vector3 middle = hole.Aggregate(Vector3.Zero, (sum, corner) => sum + corner) / hole.Length;
            Vector3 u = Vector3.Normalize(panel[1] - panel[0]), v = Vector3.Cross(normal, u);
            double Angle(Vector3 p) => Math.Atan2(Vector3.Dot(p - middle, v), Vector3.Dot(p - middle, u));
            double start = Angle(hole[0]);
            double From(Vector3 p) => ((Angle(p) - start) % Math.Tau + Math.Tau) % Math.Tau;
            Vector3[] outer = [.. panel.OrderBy(From)];
            Vector3[] inner = [.. hole.OrderBy(p => p == hole[0] ? -1 : From(p))];
            double OuterAngle(int i) => i == outer.Length ? From(outer[0]) + Math.Tau : From(outer[i]);
            double InnerAngle(int j) => j == inner.Length ? Math.Tau : j == 0 ? 0 : From(inner[j]);
            Vector3 tangent = u;
            int a = 0, b = 0;
            while (a < outer.Length || b < inner.Length)
            {
                Vector3 o = outer[a % outer.Length], h = inner[b % inner.Length];
                if (b == inner.Length || (a < outer.Length && OuterAngle(a + 1) <= InnerAngle(b + 1)))
                {
                    Expect(normal, o, outer[(a + 1) % outer.Length], h);
                    Triangle(normal, tangent, o, outer[(a + 1) % outer.Length], h);
                    a++;
                }
                else
                {
                    Expect(normal, o, inner[(b + 1) % inner.Length], h);
                    Triangle(normal, tangent, o, inner[(b + 1) % inner.Length], h);
                    b++;
                }
            }
        }

        /// <summary>Throws when the triangle of a ring does not wind counterclockwise round its normal, so the fan would fold over.</summary>
        private static void Expect(Vector3 normal, Vector3 a, Vector3 b, Vector3 c)
        {
            if (!(Vector3.Dot(Vector3.Cross(b - a, c - a), normal) > 1e-9f))
                throw new InvalidOperationException($"The fan between a panel and its hole folds over at {a}, {b}, {c}. The hole must lie well inside its panel.");
        }

        /// <summary>The triangle of a face whose normal is <paramref name="normal"/>, wound counterclockwise round it.</summary>
        private void Triangle(Vector3 normal, Vector3 tangent, Vector3 a, Vector3 b, Vector3 c)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), normal) < 0)
                (b, c) = (c, b);
            foreach (Vector3 corner in (ReadOnlySpan<Vector3>)[a, b, c])
            {
                indices.Add((uint)vertices.Count);
                vertices.Add(new Vertex { Position = corner, Normal = normal, Tangent = new Vector4(tangent, 1) });
            }
        }
    }
}
