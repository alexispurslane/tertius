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
}

/// <summary>
/// The image of a reflected ray on the screen: the C# copy of ScreenRay in reflection.glsl, in 32-bit floats as the GPU
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
    /// <summary>κ: a surface at view depth z fills the depths from z to z (1 + κ).</summary>
    public const float ThicknessShare = 0.1f;

    /// <summary>In pixels: a position on a cell's edge belongs to the cell ahead of it.</summary>
    public const float CrossingNudge = 0.01f;

    /// <summary>How far the origin lies off the surface, in pixel footprints at the surface's view depth.</summary>
    public const float OriginOffsetPixels = 2;

    /// <summary>The cosine between the ray and the geometric normal below which a ray fades out.</summary>
    public const float SurfaceFadeCosine = 0.05f;

    /// <summary>The parameter of a segment that no limit ends. Not infinity, as in the shader.</summary>
    private const float NoEnd = 1e30f;

    /// <summary>The image of the ray from origin along the unit direction, both in the camera-relative axes.</summary>
    public static ScreenRay Of(Vector3 origin, Vector3 direction, in CameraView camera)
    {
        float originDepth = Vector3.Dot(origin, camera.Forward);
        float towardDepth = Vector3.Dot(direction, camera.Forward);
        // A second point in front of the eye, so that both projections are finite: one view depth along the ray, or
        // halfway to the eye's plane.
        float along = towardDepth >= 0 ? originDepth : 0.5f * originDepth / -towardDepth;
        Vector3 second = origin + direction * along;
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

    /// <summary>The image of the reflected ray of a surface point: its origin off the surface by OriginOffsetPixels
    /// footprints.</summary>
    /// <param name="point">The surface point, camera-relative.</param>
    /// <param name="geometricNormal">The unit normal of the surface that the depth sees.</param>
    /// <param name="direction">The unit direction of the reflected ray.</param>
    /// <param name="camera">The camera of the frame.</param>
    public static ScreenRay FromSurface(Vector3 point, Vector3 geometricNormal, Vector3 direction, in CameraView camera)
    {
        float footprint = Vector3.Dot(point, camera.Forward) * camera.PixelAngle;
        Vector3 origin = point + geometricNormal * (OriginOffsetPixels * footprint);
        return Of(origin, direction, camera);
    }

    /// <summary>The fade of a ray that points into its own geometric surface: 0 below it, 1 from a cosine of
    /// <see cref="SurfaceFadeCosine"/> above it.</summary>
    public static float SurfaceFade(Vector3 direction, Vector3 geometricNormal) =>
        Math.Clamp(Vector3.Dot(direction, geometricNormal) / SurfaceFadeCosine, 0, 1);

    /// <summary>
    /// Walks the image through the pyramid, as traceRay in reflection.glsl does: one level up after each cell that the
    /// ray crosses without meeting its slab, one level down into each cell whose slab it meets, a hit at level 0.
    /// </summary>
    /// <param name="pyramid">The depth pyramid of the frame.</param>
    /// <param name="maxSteps">The most cells that the trace examines before it gives up.</param>
    public RayTrace Trace(DepthPyramid pyramid, int maxSteps)
    {
        var travel = new Vector2(MathF.Sign(Step.X), MathF.Sign(Step.Y));
        float s = CellExit(level: 0, cell: Floor(Start), travel: travel);
        int level = 0;
        for (int steps = 0; steps < maxSteps; steps++)
        {
            if (s >= End)
                return new RayLeftTheSegment(EndsAt, steps);
            Vector2 cell = Floor((Start + Step * s + travel * CrossingNudge) / (1 << level));
            float exit = CellExit(level, cell, travel);
            (float low, float high) = pyramid.Bounds(level, (int)cell.X, (int)cell.Y).Slab(ThicknessShare);
            float inW = InverseDepthAt(s);
            float outW = InverseDepthAt(exit);
            bool meetsSlab = MathF.Max(MathF.Min(inW, outW), low) <= MathF.Min(MathF.Max(inW, outW), high);
            if (!meetsSlab)
            {
                s = exit;
                level = Math.Min(level + 1, pyramid.TopLevel);
                continue;
            }
            // GLSL's clamp, which never throws: exit can lie a rounding below s.
            float entry = MathF.Min(MathF.Max(SlabEntry(s: s, inW: inW, low: low, high: high), s), exit);
            if (level == 0)
            {
                var position = Vector2.Clamp(Start + Step * entry, cell, cell + Vector2.One);
                return new RayHit(
                    Position: position,
                    Pixel: ((int)cell.X, (int)cell.Y),
                    // From the inverse depth where the image enters the pixel, never at a solved s: that keeps a far
                    // surface's thin slab exact (docs/plans/screen-space-reflections.md, "Precision and determinism").
                    EntryShare: EntryShare(InverseDepthAt(PixelEntry(cell, travel)), low, high),
                    Steps: steps + 1
                );
            }
            s = entry;
            level--;
        }
        return s >= End ? new RayLeftTheSegment(EndsAt, maxSteps) : new RayOutOfSteps(maxSteps);
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

    /// <summary>The parameter where the image enters a pixel, on its near edge across or down.</summary>
    private float PixelEntry(Vector2 pixel, Vector2 travel)
    {
        Vector2 edge = pixel + Vector2.Max(-travel, Vector2.Zero);
        float entryX = travel.X != 0 ? (edge.X - Start.X) / Step.X : -NoEnd;
        float entryY = travel.Y != 0 ? (edge.Y - Start.Y) / Step.Y : -NoEnd;
        return MathF.Max(entryX, entryY);
    }

    /// <summary>The parameter where the image leaves a cell of a level, on its far edge across or down, or the
    /// segment's end.</summary>
    private float CellExit(int level, Vector2 cell, Vector2 travel)
    {
        Vector2 edge = (cell + Vector2.Max(travel, Vector2.Zero)) * (1 << level);
        float exitX = travel.X != 0 ? (edge.X - Start.X) / Step.X : NoEnd;
        float exitY = travel.Y != 0 ? (edge.Y - Start.Y) / Step.Y : NoEnd;
        return MathF.Min(MathF.Min(exitX, exitY), End);
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
