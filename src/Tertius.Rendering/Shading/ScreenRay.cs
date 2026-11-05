using System.Numerics;

namespace Tertius.Rendering;

/// <summary>Where the image of a ray ends on the screen.</summary>
internal enum SegmentEnd
{
    /// <summary>The image leaves the screen.</summary>
    ScreenEdge,

    /// <summary>The ray comes toward the eye and reaches the near plane.</summary>
    NearPlane,

    /// <summary>The ray goes away to infinity and reaches its vanishing point, where the inverse depth is 0.</summary>
    VanishingPoint,

    /// <summary>
    /// The ray has no segment: the depth of its origin or of its second point, as the floats compute it, lies far below
    /// the surface's view depth. The rounding of a view ray longer than about 10^7 passes its forward part of 1, so the
    /// computed depth of a point on it can be 0 or below, where no projection is defined.
    /// </summary>
    NoSegment,
}

/// <summary>
/// The image of a ray on the screen: the C# copy of ScreenRay in screen_ray.glsl, in 32-bit floats as the GPU
/// computes (docs/plans/screen-space-reflections.md, "The ray's image on the screen" and "The trace through the
/// pyramid"). At the parameter s from 0 to End, the image is at Start + s · Step in pixels, with the inverse depth
/// StartInverseDepth + s · StepInverseDepth in 1/m.
/// </summary>
internal readonly record struct ScreenRay(
    Vector2 Start,
    Vector2 Step,
    float StartInverseDepth,
    float StepInverseDepth,
    float End,
    SegmentEnd EndsAt
)
{
    /// <summary>How far the origin lies off the surface, in pixel footprints at the surface's view depth.</summary>
    public const float OriginOffsetPixels = 2;

    /// <summary>
    /// The most that the origin lies off the surface, as a share of the surface's view depth. A pixel footprint can pass
    /// half the view depth, in a window a few pixels tall or at a field of view near a half turn, and then two of them
    /// along the normal of a surface that faces the eye would put the origin on or behind the eye's plane.
    /// </summary>
    public const float MostOriginOffsetShare = 0.5f;

    /// <summary>
    /// The cosine between the ray and the geometric normal below which a ray fades out, for a pixel of every model
    /// (docs/plans/skin-and-hair.md, decision 35).
    /// </summary>
    public const float SurfaceFadeCosine = 0.05f;

    /// <summary>The highest level that the two-depth walk climbs to: cells of 256 pixels, as the lean walk V1 did
    /// (docs/research/temporal-renderer/profiling-spike.md, "V6, the two-depth walk").</summary>
    public const int WalkTopLevel = 8;

    /// <summary>The parameter of a segment that no limit ends. Not infinity, as in the shader.</summary>
    private const float NoEnd = 1e30f;

    /// <summary>The image of the ray from origin along the unit direction, both in the camera-relative axes.</summary>
    /// <param name="origin">The ray's origin, camera-relative.</param>
    /// <param name="direction">The ray's unit direction.</param>
    /// <param name="viewDepth">The view depth of the surface point that the ray leaves, from the pixel's distance, or
    /// of a bare origin its own: the depth that the origin's computed depth must not fall far below.</param>
    /// <param name="camera">The camera of the frame.</param>
    public static ScreenRay Of(Vector3 origin, Vector3 direction, float viewDepth, in CameraView camera)
    {
        float originDepth = Vector3.Dot(origin, camera.Forward);
        float towardDepth = Vector3.Dot(direction, camera.Forward);
        // A second point in front of the eye and within one view depth of the origin, so that both projections are
        // finite and its depth does not cancel: one view depth along the ray, or halfway to the eye's plane for a ray
        // that comes toward it faster than half its length. Either way the second point lies at least half as deep.
        float along = towardDepth >= -0.5f ? originDepth : 0.5f * originDepth / -towardDepth;
        Vector3 second = origin + direction * along;
        // The origin lies at least half as deep as its surface, and the second point half as deep again. A computed
        // depth far below those comes from the rounding of a view ray longer than about 10^7, and can be 0 or below.
        if (originDepth < viewDepth / 4 || Vector3.Dot(second, camera.Forward) < viewDepth / 8)
            return new(
                Start: Vector2.Zero,
                Step: Vector2.Zero,
                StartInverseDepth: 0,
                StepInverseDepth: 0,
                End: -1,
                EndsAt: SegmentEnd.NoSegment
            );
        Vector2 start = camera.PixelOf(origin);
        Vector2 step = camera.PixelOf(second) - start;
        float startInverseDepth = camera.InverseDepthOf(origin);
        float stepInverseDepth = camera.InverseDepthOf(second) - startInverseDepth;
        float screenEnd = MathF.Min(
            ParameterAtLimit(start.X, step.X, camera.Width),
            ParameterAtLimit(start.Y, step.Y, camera.Height)
        );
        float depthEnd = ParameterAtLimit(startInverseDepth, stepInverseDepth, 1 / DepthRanges.NearPlane);
        if (depthEnd < screenEnd)
        {
            SegmentEnd depthLimit = stepInverseDepth > 0 ? SegmentEnd.NearPlane : SegmentEnd.VanishingPoint;
            return new(
                Start: start,
                Step: step,
                StartInverseDepth: startInverseDepth,
                StepInverseDepth: stepInverseDepth,
                End: depthEnd,
                EndsAt: depthLimit
            );
        }
        return new(
            Start: start,
            Step: step,
            StartInverseDepth: startInverseDepth,
            StepInverseDepth: stepInverseDepth,
            End: screenEnd,
            EndsAt: SegmentEnd.ScreenEdge
        );
    }

    /// <summary>How far the origin lies off a surface at <paramref name="viewDepth"/>: <see cref="OriginOffsetPixels"/>
    /// footprints of <paramref name="pixelAngle"/>, and at most <see cref="MostOriginOffsetShare"/> of the view depth, so
    /// that the origin lies at least half as deep as the surface.</summary>
    public static float OriginOffset(float viewDepth, float pixelAngle) =>
        MathF.Min(OriginOffsetPixels * pixelAngle, MostOriginOffsetShare) * viewDepth;

    /// <summary>The image of the reflected ray of a surface point: its origin off the surface by
    /// <see cref="OriginOffset"/>.</summary>
    /// <param name="point">The surface point, camera-relative.</param>
    /// <param name="viewDepth">The surface point's view depth from the pixel's distance, as the shader computes it:
    /// the distance over the length of the pixel's view ray.</param>
    /// <param name="geometricNormal">The unit normal of the surface that the depth sees.</param>
    /// <param name="direction">The unit direction of the reflected ray.</param>
    /// <param name="camera">The camera of the frame.</param>
    public static ScreenRay FromSurface(
        Vector3 point,
        float viewDepth,
        Vector3 geometricNormal,
        Vector3 direction,
        in CameraView camera
    )
    {
        Vector3 origin = point + geometricNormal * OriginOffset(viewDepth, camera.PixelAngle);
        return Of(origin: origin, direction: direction, viewDepth: viewDepth, camera: camera);
    }

    /// <summary>The fade of a ray that points into its own geometric surface: 0 below it, 1 from a cosine of
    /// <see cref="SurfaceFadeCosine"/> above it.</summary>
    public static float SurfaceFade(Vector3 direction, Vector3 geometricNormal) =>
        Math.Clamp(Vector3.Dot(direction, geometricNormal) / SurfaceFadeCosine, 0, 1);

    /// <summary>
    /// The two-depth walk, as walkScreen in screen_walk.glsl walks it (docs/plans/temporal-renderer.md, section 6, "The
    /// traversal"). A step reads one cell: the bounds of a level above 0, or a pixel's center w at the finest level. A
    /// ray that passes in front of the cell's slab or behind it crosses the cell and goes up a level, one that meets the
    /// slab of a level above 0 goes down a level to where it enters it, and one that meets a pixel's flat slab hits,
    /// unless the pixel's surface faces away from the ray.
    /// </summary>
    /// <param name="pyramid">The depth pyramid of the frame.</param>
    /// <param name="direction">The ray's unit direction, camera-relative, which the facing test reads.</param>
    /// <param name="maxSteps">The step cap: a walk that reaches it ends as <see cref="RayOutOfSteps"/>, a miss.</param>
    public RayTrace Walk(DepthPyramid pyramid, Vector3 direction, int maxSteps)
    {
        var travel = new Vector2(MathF.Sign(Step.X), MathF.Sign(Step.Y));
        (float s, Vector2 crossed) = CellExit(level: 0, cell: Floor(Start), travel: travel);
        // The cell that the walk stands in, at its level: the pixel past the origin's pixel to start with.
        Vector2 cell = Floor(Start) + travel * crossed;
        int top = Math.Min(pyramid.TopLevel, WalkTopLevel);
        int level = 0;
        // Whether a descent solved s as a slab's entry, rather than a crossing reaching it on a cell's edge.
        bool solved = false;
        for (int steps = 0; steps < maxSteps; steps++)
        {
            if (s >= End)
                return new RayLeftTheSegment(EndsAt, steps);
            (float cellExit, crossed) = CellExit(level, cell, travel);
            float exit = MathF.Max(cellExit, s);
            (int x, int y) = ((int)cell.X, (int)cell.Y);
            (float near, float back) = WalkSlab(pyramid: pyramid, level: level, x: x, y: y);
            float inW = InverseDepthAt(s);
            bool meetsSlab = MeetsWalkSlab(near: near, back: back, inW: inW, outW: InverseDepthAt(exit));

            // GLSL's clamp, which never throws: exit can lie a rounding below s.
            float entry = meetsSlab
                ? MathF.Min(MathF.Max(SlabEntry(s: s, inW: inW, low: back, high: near), s), exit)
                : s;
            if (meetsSlab && level > 0)
            {
                level--;
                solved = solved || entry > s;
                cell = ChildAt(parent: cell, level: level, s: entry, travel: travel, leaving: solved);
                s = entry;
                continue;
            }
            if (meetsSlab && Vector3.Dot(pyramid.GeometricNormal(x, y), direction) < 0)
                return new RayHit(
                    Position: Vector2.Clamp(Start + Step * entry, cell, cell + Vector2.One),
                    Pixel: (x, y),
                    EntryShare: back < near ? EntryShare(inW, back, near) : 0,
                    Steps: steps + 1
                );
            // Across the cell and up a level: in front of its slab, behind it, or past a surface that faces away.
            s = exit;
            solved = false;
            cell += travel * crossed;
            if (level < top)
            {
                level++;
                cell = Floor(cell / 2);
            }
        }
        return s >= End ? new RayLeftTheSegment(EndsAt, maxSteps) : new RayOutOfSteps(maxSteps);
    }

    /// <summary>The slab of a cell of the two-depth walk: from its far bound times 1 / (1 + κ) to its near bound, and at
    /// the finest level from a pixel's center w over 1 + κ to w.</summary>
    private static (float Near, float Back) WalkSlab(DepthPyramid pyramid, int level, int x, int y)
    {
        // A product by the share, as the shader takes it, not a quotient, which rounds otherwise.
        const float backShare = 1 / (1 + CellBounds.ThicknessShare);
        CellBounds bounds = level == 0 ? CellBounds.OfSurface(pyramid.Finest(x, y)) : pyramid.Bounds(level, x, y);
        return (bounds.Near, bounds.Far * backShare);
    }

    /// <summary>Whether the ray's inverse depths over a cell, from inW to outW, meet the cell's slab: neither in front
    /// of it, nor behind it. A cell without a surface has a near bound of 0, which no ray meets.</summary>
    private static bool MeetsWalkSlab(float near, float back, float inW, float outW)
    {
        bool inFront = near <= 0 || MathF.Min(inW, outW) > near;
        bool behind = MathF.Max(inW, outW) < back;
        return !inFront && !behind;
    }

    /// <summary>
    /// The cell of a level, inside its parent of the level above, where the image stands at s, by the parameters where
    /// the image reaches the parent's center lines, which the crossings compare too. At an s that a descent solved as a
    /// slab's entry, the walk stands in the cell whose stretch of the image reaches the entry, so an entry on a center
    /// line stands in the cell that the image leaves there. At an s that a crossing reached, it stands in the cell that
    /// the image enters, so that a walk that crossed a cell does not go back to it.
    /// </summary>
    private Vector2 ChildAt(Vector2 parent, int level, float s, Vector2 travel, bool leaving)
    {
        float side = 1 << level;
        float reached = leaving ? MathF.BitDecrement(s) : s;
        Vector2 center = EdgeParameters(edge: (parent * 2 + Vector2.One) * side, still: NoEnd);
        return new(
            ChildAlong(parent: parent.X, start: Start.X / side, travel: travel.X, passedCenter: center.X <= reached),
            ChildAlong(parent: parent.Y, start: Start.Y / side, travel: travel.Y, passedCenter: center.Y <= reached)
        );
    }

    /// <summary>On one axis, the child of a parent cell: the second along the direction of travel where the image
    /// passed the center line, and where the image stands still the child that holds its start.</summary>
    private static float ChildAlong(float parent, float start, float travel, bool passedCenter)
    {
        if (travel == 0)
            return Math.Clamp(MathF.Floor(start), parent * 2, parent * 2 + 1);
        return parent * 2 + (passedCenter == travel > 0 ? 1 : 0);
    }

    /// <summary>The parameter where a coordinate that moves by step per unit reaches 0 or limit, or NoEnd when it
    /// stands still.</summary>
    private static float ParameterAtLimit(float position, float step, float limit)
    {
        if (step > 0)
            return (limit - position) / step;
        if (step < 0)
            return -position / step;
        return NoEnd;
    }

    /// <summary>q: 0 when the ray stands in front of the slab where it enters the pixel, 1 behind it, and between inside.
    /// </summary>
    private static float EntryShare(float inW, float low, float high)
    {
        if (inW > high)
            return 0;
        if (inW < low)
            return 1;
        return (high - inW) / (high - low);
    }

    private static Vector2 Floor(Vector2 position) => new(MathF.Floor(position.X), MathF.Floor(position.Y));

    private float InverseDepthAt(float s) => StartInverseDepth + StepInverseDepth * s;

    /// <summary>The parameter where the ray enters a slab that it meets: its front from in front, its back from behind,
    /// or s when it is already inside.</summary>
    private float SlabEntry(float s, float inW, float low, float high)
    {
        if (inW > high)
            return (high - StartInverseDepth) / StepInverseDepth;
        if (inW < low)
            return (low - StartInverseDepth) / StepInverseDepth;
        return s;
    }

    /// <summary>The parameters where the image reaches the line x = edge.X and the line y = edge.Y, or still on an axis
    /// where the image stands still. The walk takes a cell's edges from here.
    /// </summary>
    private Vector2 EdgeParameters(Vector2 edge, float still) =>
        new(Step.X != 0 ? (edge.X - Start.X) / Step.X : still, Step.Y != 0 ? (edge.Y - Start.Y) / Step.Y : still);

    /// <summary>The parameter where the image leaves a cell of a level, on its far edge across or down, or the
    /// segment's end; and 1 on each axis whose far edge gives the earlier of the two parameters, 0 on the other, both
    /// at a corner.</summary>
    private (float Exit, Vector2 Crossed) CellExit(int level, Vector2 cell, Vector2 travel)
    {
        Vector2 exits = EdgeParameters(edge: (cell + Vector2.Max(travel, Vector2.Zero)) * (1 << level), still: NoEnd);
        float exit = MathF.Min(exits.X, exits.Y);
        return (MathF.Min(exit, End), new(exits.X <= exit ? 1 : 0, exits.Y <= exit ? 1 : 0));
    }
}

/// <summary>A ray that met a slab at level 0.</summary>
/// <param name="Position">The hit on the screen, in pixels.</param>
/// <param name="Pixel">The pixel of the hit.</param>
/// <param name="EntryShare">q: 0 entering through the front of the slab, 1 through its back.</param>
/// <param name="Steps">The cells examined.</param>
internal readonly record struct RayHit(Vector2 Position, (int X, int Y) Pixel, float EntryShare, int Steps);

/// <summary>A ray that reached the end of its segment with no hit.</summary>
/// <param name="End">Where the segment ends.</param>
/// <param name="Steps">The cells examined.</param>
internal readonly record struct RayLeftTheSegment(SegmentEnd End, int Steps);

/// <summary>A ray that used all its steps with no hit.</summary>
/// <param name="Steps">The cells examined: the trace's most.</param>
internal readonly record struct RayOutOfSteps(int Steps);

/// <summary>
/// The result of a trace: <see cref="RayHit"/>, <see cref="RayLeftTheSegment"/> or <see cref="RayOutOfSteps"/>.
/// This is a generated union (docs/plans/union-tooling.md): the union generator writes its members.
/// </summary>
[GeneratedUnion(typeof(RayHit), typeof(RayLeftTheSegment), typeof(RayOutOfSteps))]
internal readonly partial struct RayTrace;
