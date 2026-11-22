using System.Numerics;

namespace Tertius.Rendering;

/// <summary>The slab of a cell of the depth pyramid, in inverse view depth: the interval of w that the walk treats as
/// solid there (docs/plans/back-face-depth-layer.md, "The terms").</summary>
/// <param name="Near">The front w of the slab, in 1/m: the largest w of the cell's surface pixels. 0 without a surface.
/// </param>
/// <param name="Back">The back w of the slab, in 1/m: the smallest back w of its pixels. <see cref="EmptyBack"/> without
/// a surface.</param>
internal readonly record struct CellBounds(float Near, float Back)
{
    /// <summary>
    /// How far the center of a depth sample's edge neighbor may stand off the sample's plane where the neighbor continues
    /// the plane, as a share of inverse depth (<see cref="PixelPlane.Holds"/>). The sample's center may stand off the
    /// neighbor's plane by as much. At a share of 0.1, the plane of a curved mesh's pixel reaches edges where the
    /// neighbor's surface stands off it by several percent. Rays then slip through the step between the two surfaces,
    /// which shows as dotted lines on the reflections of curved meshes.
    /// </summary>
    public const float ContinuationShare = 0.01f;

    /// <summary>The back w of a cell with no surface: far above any surface's inverse depth. Finite, not infinity: a
    /// shader compiler's fast math may assume that no value is infinite, on any Vulkan driver.</summary>
    public const float EmptyBack = 1e30f;

    /// <summary>The bounds of a cell with no surface. Merged with any bounds, it gives those bounds.</summary>
    public static CellBounds Empty => new(0, EmptyBack);

    public CellBounds Merge(CellBounds other) => new(MathF.Max(Near, other.Near), MathF.Min(Back, other.Back));

    /// <summary>
    /// The w of the back of what is solid behind a surface at <paramref name="inverseDepth"/> whose back share is
    /// <paramref name="share"/> (docs/plans/back-face-depth-layer.md, decision 3): w · share for a share between 0 and 1,
    /// and w itself for a sheet, whose share is 0 or 1. slabBack in depth_pyramid.glsl is the GLSL copy.
    /// </summary>
    public static float SlabBack(float inverseDepth, float share) =>
        share is > 0 and < 1 ? inverseDepth * share : inverseDepth;
}

/// <summary>
/// The plane of a depth sample that sees a surface: through its center point along its geometric normal, as an inverse
/// depth that is affine in the place on the screen, in units of the sample's grid. A sample is a pixel of level 0, in
/// pixels, or a half-resolution texel of the bounce light's near steps, in texels. An edge-on plane keeps its center's
/// inverse depth, with no gradient. The C# copy of PixelPlane in depth_pyramid.glsl.
/// </summary>
/// <param name="Center">The sample's center, in units of its grid from the screen's top left.</param>
/// <param name="CenterInverseDepth">The inverse view depth at the center, in 1/m.</param>
/// <param name="Gradient">The plane's change of inverse depth per unit of the grid, across and down, in 1/m. Zero when
/// edge-on.</param>
/// <param name="EdgeOn">Whether the plane holds the sample's view ray to within <see cref="EdgeOnShare"/>.</param>
internal readonly record struct PixelPlane(Vector2 Center, float CenterInverseDepth, Vector2 Gradient, bool EdgeOn)
{
    /// <summary>
    /// The share of a sample's distance below which its plane counts as edge-on: the plane holds the sample's view ray to
    /// within a millionth of a radian. Such a sample keeps its center's inverse depth over its whole square, and no
    /// neighbor continues it, because its gradient would grow past any float.
    /// </summary>
    private const float EdgeOnShare = 1e-6f;

    /// <summary>The plane of a pixel, as depth_pyramid.glsl builds it from the distance target and the geometric
    /// normals, or null for a pixel that sees the sky.</summary>
    /// <param name="x">The pixel's column, inside the screen.</param>
    /// <param name="y">The pixel's row, inside the screen.</param>
    /// <param name="distances">The distance target: each pixel's distance to the eye in m, row by row from the top, 0
    /// or less for sky.</param>
    /// <param name="geometricNormals">Each pixel's unit geometric normal, camera-relative, row by row from the top.
    /// </param>
    /// <param name="camera">The camera of the frame, whose size is the screen's.</param>
    public static PixelPlane? Of(
        int x,
        int y,
        ReadOnlySpan<float> distances,
        ReadOnlySpan<Vector3> geometricNormals,
        in CameraView camera
    )
    {
        float distanceToEye = distances[y * camera.Width + x];
        if (distanceToEye <= 0)
            return null;
        var center = new Vector2(x + 0.5f, y + 0.5f);
        Vector3 centerRay = camera.ViewRayThrough(center);
        float centerInverseDepth = centerRay.Length() / distanceToEye;
        Vector3 normal = geometricNormals[y * camera.Width + x];
        // The view ray changes by 2 tan / size along each axis per pixel.
        return OfSample(
            center: center,
            centerInverseDepth: centerInverseDepth,
            planeDistance: Vector3.Dot(normal, Vector3.Normalize(centerRay) * distanceToEye),
            pointDistance: distanceToEye,
            normalAlongGrid: new(
                Vector3.Dot(normal, camera.Right) * camera.TanHalfX * 2 / camera.Width,
                -Vector3.Dot(normal, camera.Up) * camera.TanHalfY * 2 / camera.Height
            )
        );
    }

    /// <summary>The plane of a depth sample in its own grid. The plane holds the points p with n · p = d. On the view
    /// ray r through a place, whose forward part is 1, it lies at the inverse depth n · r / d, which is affine in the
    /// place. samplePlane in depth_pyramid.glsl is the GLSL copy.</summary>
    /// <param name="center">The sample's center, in units of its grid.</param>
    /// <param name="centerInverseDepth">The inverse view depth at the center, in 1/m.</param>
    /// <param name="planeDistance">d = n · P, with n the sample's unit geometric normal and P its center point, in m.
    /// </param>
    /// <param name="pointDistance">|P|, the center point's distance to the eye, in m.</param>
    /// <param name="normalAlongGrid">n · Δr: the normal's dot product with the view ray's change per unit of the grid,
    /// across and down.</param>
    public static PixelPlane OfSample(
        Vector2 center,
        float centerInverseDepth,
        float planeDistance,
        float pointDistance,
        Vector2 normalAlongGrid
    ) =>
        MathF.Abs(planeDistance) <= EdgeOnShare * pointDistance
            ? new(Center: center, CenterInverseDepth: centerInverseDepth, Gradient: Vector2.Zero, EdgeOn: true)
            : new(
                Center: center,
                CenterInverseDepth: centerInverseDepth,
                Gradient: normalAlongGrid / planeDistance,
                EdgeOn: false
            );

    /// <summary>Whether an inverse depth at a screen position lies within <see cref="CellBounds.ContinuationShare"/> of
    /// the plane's there, in either direction.</summary>
    public bool Holds(Vector2 position, float inverseDepth)
    {
        float planeInverseDepth = CenterInverseDepth + Vector2.Dot(Gradient, position - Center);
        const float tolerance = 1 + CellBounds.ContinuationShare;
        return inverseDepth <= planeInverseDepth * tolerance && inverseDepth * tolerance >= planeInverseDepth;
    }

    /// <summary>
    /// Whether an edge neighbor continues this plane. A neighbor beyond the image continues it, as no depth edge lies
    /// there, and one that sees the sky does not. One that sees a surface continues it where neither plane is edge-on and
    /// each plane holds the other sample's center. Both directions, because a steep plane at a depth edge can pass the far
    /// surface's center one sample away by chance, and the far surface's plane never passes back through the near center.
    /// The one rule of where a depth sample's plane holds: the pixels of the depth pyramid and the near steps of the
    /// bounce light both call it, each with planes in its own grid. neighborContinues in depth_pyramid.glsl is the GLSL
    /// copy.
    /// </summary>
    /// <param name="insideImage">Whether the neighbor lies inside the image of the sample's grid.</param>
    /// <param name="neighbor">The plane of a neighbor inside the image, or null for one that sees the sky. Not read for
    /// a neighbor beyond the image, so a caller reads no sample there.</param>
    public bool ContinuesInto(bool insideImage, PixelPlane? neighbor) =>
        !insideImage
        || neighbor is { } other
            && !EdgeOn
            && !other.EdgeOn
            && Holds(other.Center, other.CenterInverseDepth)
            && other.Holds(Center, CenterInverseDepth);
}

/// <summary>
/// The surface of a depth sample that sees a surface (docs/plans/screen-space-reflections.md, "The pixel's surface"):
/// its <see cref="PixelPlane"/> over the part of its square, one unit of its grid wide, where the plane holds. Toward
/// each edge neighbor the part reaches the sample's edge where the neighbor continues the plane, or lies beyond the
/// image, and stops at the sample's center line otherwise. Past the part, the surface keeps the plane's value at the
/// part's edge. The pyramid's bounds and the trace's level-0 test read the surface of a pixel, and the bounce light's
/// near steps the surface of a half-resolution texel. The C# copy of PixelSurface in depth_pyramid.glsl.
/// </summary>
/// <param name="Center">The sample's center, in units of its grid from the screen's top left.</param>
/// <param name="CenterInverseDepth">The inverse view depth at the center, in 1/m.</param>
/// <param name="Gradient">The plane's change of inverse depth per unit of the grid, across and down, in 1/m.</param>
/// <param name="Low">The part's corner toward the screen's top left, in units of the grid.</param>
/// <param name="High">The part's opposite corner, in units of the grid.</param>
internal readonly record struct PixelSurface(
    Vector2 Center,
    float CenterInverseDepth,
    Vector2 Gradient,
    Vector2 Low,
    Vector2 High
)
{
    /// <summary>The bounds of the surface over the pixel's square: its nearest and its farthest inverse depth. The
    /// surface is affine over its part and constant past it, so the part's corners hold both. The far w is at least 0.
    /// A steep plane can pass behind the eye inside its square, and no point behind the eye is part of the slab. A
    /// back w of 0 also keeps every back w that a share scales at most the plane's far w, which the cell invariant
    /// needs. pixelBounds in depth_pyramid.glsl is the GLSL copy.</summary>
    public (float Near, float Far) Bounds
    {
        get
        {
            Vector2 toLow = Gradient * (Low - Center);
            Vector2 toHigh = Gradient * (High - Center);
            return (
                Near: CenterInverseDepth + MathF.Max(toLow.X, toHigh.X) + MathF.Max(toLow.Y, toHigh.Y),
                Far: MathF.Max(0, CenterInverseDepth + MathF.Min(toLow.X, toHigh.X) + MathF.Min(toLow.Y, toHigh.Y))
            );
        }
    }

    /// <summary>The slab that level 1 merges for the pixel whose back share is <paramref name="share"/>: from its plane's
    /// near w to the back w of its plane's far w. pixelSlabBounds in depth_pyramid.glsl is the GLSL copy.</summary>
    public CellBounds SlabBounds(float share)
    {
        (float near, float far) = Bounds;
        return new(Near: near, Back: CellBounds.SlabBack(far, share));
    }

    /// <summary>The surface of a depth sample: its plane over the part of its square that reaches the edge toward each
    /// neighbor named true, and stops at the center line toward the others. sampleSurface in depth_pyramid.glsl is the
    /// GLSL copy.</summary>
    /// <param name="plane">The sample's plane.</param>
    /// <param name="left">Whether the part reaches the edge toward the left neighbor.</param>
    /// <param name="up">Whether it reaches the edge toward the neighbor above.</param>
    /// <param name="right">Whether it reaches the edge toward the right neighbor.</param>
    /// <param name="down">Whether it reaches the edge toward the neighbor below.</param>
    public static PixelSurface Over(PixelPlane plane, bool left, bool up, bool right, bool down) =>
        new(
            Center: plane.Center,
            CenterInverseDepth: plane.CenterInverseDepth,
            Gradient: plane.Gradient,
            Low: plane.Center - 0.5f * new Vector2(left ? 1 : 0, up ? 1 : 0),
            High: plane.Center + 0.5f * new Vector2(right ? 1 : 0, down ? 1 : 0)
        );

    /// <summary>The surface of a pixel that sees a surface at <paramref name="inverseDepth"/> facing the eye's forward
    /// axis: one inverse depth over the whole square.</summary>
    public static PixelSurface FacingTheEye(int x, int y, float inverseDepth) =>
        new(
            Center: new(x + 0.5f, y + 0.5f),
            CenterInverseDepth: inverseDepth,
            Gradient: Vector2.Zero,
            Low: new(x, y),
            High: new(x + 1, y + 1)
        );

    /// <summary>The surface of a pixel, as depth_pyramid.glsl builds it from the distance target and the geometric
    /// normals, or null for a pixel that sees the sky.</summary>
    /// <param name="x">The pixel's column, inside the screen.</param>
    /// <param name="y">The pixel's row, inside the screen.</param>
    /// <param name="distances">The distance target: each pixel's distance to the eye in m, row by row from the top, 0
    /// or less for sky.</param>
    /// <param name="geometricNormals">Each pixel's unit geometric normal, camera-relative, row by row from the top.
    /// </param>
    /// <param name="camera">The camera of the frame, whose size is the screen's.</param>
    public static PixelSurface? Of(
        int x,
        int y,
        ReadOnlySpan<float> distances,
        ReadOnlySpan<Vector3> geometricNormals,
        in CameraView camera
    )
    {
        if (
            PixelPlane.Of(x: x, y: y, distances: distances, geometricNormals: geometricNormals, camera: camera)
            is not { } plane
        )
            return null;
        // Whether the plane continues toward the left, upper, right and lower neighbor.
        Span<bool> continues = stackalloc bool[4];
        ReadOnlySpan<(int X, int Y)> neighbors = [(x - 1, y), (x, y - 1), (x + 1, y), (x, y + 1)];
        for (int side = 0; side < 4; side++)
            continues[side] = Continues(
                plane: plane,
                neighbor: neighbors[side],
                distances: distances,
                geometricNormals: geometricNormals,
                camera: camera
            );
        return Over(plane: plane, left: continues[0], up: continues[1], right: continues[2], down: continues[3]);
    }

    /// <summary>The surface's inverse depth on the view ray through a place, in units of the sample's grid, in 1/m.
    /// </summary>
    public float InverseDepthAt(Vector2 position) =>
        CenterInverseDepth + Vector2.Dot(Gradient, Vector2.Clamp(position, Low, High) - Center);

    /// <summary>Whether an edge neighbor continues the pixel's plane (<see cref="PixelPlane.ContinuesInto"/>). Reads
    /// the neighbor's pixel only inside the screen.</summary>
    private static bool Continues(
        PixelPlane plane,
        (int X, int Y) neighbor,
        ReadOnlySpan<float> distances,
        ReadOnlySpan<Vector3> geometricNormals,
        in CameraView camera
    )
    {
        (int x, int y) = neighbor;
        bool inside = x >= 0 && y >= 0 && x < camera.Width && y < camera.Height;
        return plane.ContinuesInto(
            insideImage: inside,
            neighbor: inside
                ? PixelPlane.Of(x: x, y: y, distances: distances, geometricNormals: geometricNormals, camera: camera)
                : null
        );
    }
}

/// <summary>
/// The depth pyramid on the CPU: the C# copy of depth_pyramid.glsl and the pyramid shaders. Cell (x, y) of level k
/// covers the pixels whose coordinates shifted right by k give (x, y) (docs/plans/screen-space-reflections.md, "The
/// depth pyramid"). Every level holds slabs: a front w and a back w (docs/plans/back-face-depth-layer.md).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The slab of every cell of every level holds the <see cref="PixelSlab"/> of every surface pixel under it: a
/// front w no smaller and a back w no larger.</item>
/// </list>
/// </remarks>
internal sealed class DepthPyramid
{
    /// <summary>The most levels of a pyramid: a 16,384 px side halves to 1 px in 14 levels.</summary>
    public const int MaxLevels = 14;

    /// <summary>The surface of each pixel of level 0, row by row from the top, null for sky.</summary>
    private readonly PixelSurface?[] surfaces;

    /// <summary>The cells of each level from 0 to <see cref="TopLevel"/>, row by row from the top. Level 0 holds the
    /// <see cref="PixelSurface.SlabBounds"/> of <see cref="surfaces"/>, which level 1 merges.</summary>
    private readonly CellBounds[][] levels;

    /// <summary>The unit geometric normal of each pixel, camera-relative, row by row from the top.</summary>
    private readonly Vector3[] geometricNormals;

    /// <summary>The back share of each pixel, row by row from the top (<see cref="RenderTargets.BackShare"/>).</summary>
    private readonly float[] backShares;

    private DepthPyramid(
        PixelSurface?[] surfaces,
        Vector3[] geometricNormals,
        float[] backShares,
        int width,
        int height
    )
    {
        this.surfaces = surfaces;
        this.geometricNormals = geometricNormals;
        this.backShares = backShares;
        int topLevel = LevelCount(width, height);
        levels = new CellBounds[topLevel + 1][];
        levels[0] =
        [
            .. surfaces.Select((surface, pixel) => surface?.SlabBounds(backShares[pixel]) ?? CellBounds.Empty),
        ];
        for (int level = 1; level <= topLevel; level++)
            levels[level] = MergedLevel(
                levels[level - 1],
                LevelSize(width, height, level - 1),
                LevelSize(width, height, level)
            );
        Width = width;
        Height = height;
    }

    /// <summary>The screen's width in pixels: level 0's width in cells.</summary>
    public int Width { get; }

    /// <summary>The screen's height in pixels.</summary>
    public int Height { get; }

    /// <summary>The level whose one cell covers the screen.</summary>
    public int TopLevel => levels.Length - 1;

    /// <summary>
    /// The top level L, whose one cell covers the screen: ⌈log₂ max(width, height)⌉, at least 1. It is also the count
    /// of the depth pyramid image's mips, which hold levels 1 to L.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">A side is below 1 px.</exception>
    public static int LevelCount(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        int side = Math.Max(width, height);
        return side == 1 ? 1 : BitOperations.Log2((uint)(side - 1)) + 1;
    }

    /// <summary>The size of a level in cells: each side halved and rounded up, level times.</summary>
    /// <exception cref="ArgumentOutOfRangeException">A side is below 1 px, or the level is negative.</exception>
    public static (int Width, int Height) LevelSize(int width, int height, int level)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        // ⌈n / 2^k⌉ for n ≥ 1, which repeated halving with rounding up gives.
        return (((width - 1) >> level) + 1, ((height - 1) >> level) + 1);
    }

    /// <summary>
    /// A pyramid over surfaces that face the eye's forward axis, each at one inverse depth given row by row from the
    /// top, 0 for sky. Their geometric normal is +Z, toward the eye of a camera that looks along −Z.
    /// </summary>
    /// <param name="backShares">The back share of each pixel, row by row from the top: 0 for no back face, 1 for a
    /// sheet's own fragment (<see cref="RenderTargets.BackShare"/>).</param>
    /// <exception cref="ArgumentException">A span does not hold width × height values.</exception>
    public static DepthPyramid FromInverseDepths(
        ReadOnlySpan<float> inverseDepths,
        ReadOnlySpan<float> backShares,
        int width,
        int height
    )
    {
        if (inverseDepths.Length != width * height || backShares.Length != width * height)
            throw new ArgumentException(
                $"A {width} × {height} pyramid needs {width * height} inverse depths and back shares, not "
                    + $"{inverseDepths.Length} and {backShares.Length}."
            );
        var surfaces = new PixelSurface?[inverseDepths.Length];
        for (int index = 0; index < inverseDepths.Length; index++)
            if (inverseDepths[index] > 0)
                surfaces[index] = PixelSurface.FacingTheEye(index % width, index / width, inverseDepths[index]);
        return new(
            surfaces: surfaces,
            geometricNormals: [.. Enumerable.Repeat(Vector3.UnitZ, inverseDepths.Length)],
            backShares: backShares.ToArray(),
            width: width,
            height: height
        );
    }

    /// <summary>The pyramid of a frame, as the GPU builds it from the distance target and the geometric normals.</summary>
    /// <param name="camera">The camera of the frame, whose size is the screen's.</param>
    /// <param name="distances">Each pixel's distance to the eye in m, row by row from the top, 0 or less for sky.</param>
    /// <param name="geometricNormals">Each pixel's unit geometric normal, camera-relative; any value for sky.</param>
    /// <param name="backShares">The back share of each pixel, row by row from the top: 0 for no back face, 1 for a
    /// sheet's own fragment (<see cref="RenderTargets.BackShare"/>).</param>
    /// <exception cref="ArgumentException">A span does not hold one value per pixel.</exception>
    public static DepthPyramid Of(
        in CameraView camera,
        ReadOnlySpan<float> distances,
        ReadOnlySpan<Vector3> geometricNormals,
        ReadOnlySpan<float> backShares
    )
    {
        int pixels = camera.Width * camera.Height;
        if (distances.Length != pixels || geometricNormals.Length != pixels || backShares.Length != pixels)
            throw new ArgumentException(
                $"A {camera.Width} × {camera.Height} pyramid needs {pixels} distances, normals and back shares, not "
                    + $"{distances.Length}, {geometricNormals.Length} and {backShares.Length}."
            );
        var surfaces = new PixelSurface?[pixels];
        for (int y = 0; y < camera.Height; y++)
        for (int x = 0; x < camera.Width; x++)
            surfaces[y * camera.Width + x] = PixelSurface.Of(
                x: x,
                y: y,
                distances: distances,
                geometricNormals: geometricNormals,
                camera: camera
            );
        return new(
            surfaces: surfaces,
            geometricNormals: geometricNormals.ToArray(),
            backShares: backShares.ToArray(),
            width: camera.Width,
            height: camera.Height
        );
    }

    /// <summary>The surface of a pixel of level 0, or null for a pixel that sees the sky or lies outside the screen.
    /// </summary>
    public PixelSurface? Surface(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? null : surfaces[y * Width + x];

    /// <summary>The center w of a pixel, R of the finest level: the inverse view depth of its center, in 1/m, or 0 for
    /// a pixel that sees the sky or lies outside the screen (docs/plans/temporal-renderer.md, decision 40).</summary>
    public float Finest(int x, int y) => Surface(x, y)?.CenterInverseDepth ?? 0;

    /// <summary>
    /// The slab of a pixel, the finest level's texel (docs/plans/back-face-depth-layer.md, decision 3): its center w, and
    /// the back w of that or its plane's far w, whichever lies farther. A sheet's slab so reaches the far end of its plane
    /// over its square. (0, 0) for a pixel that sees the sky or lies outside the screen. pixelSlab in depth_pyramid.glsl
    /// is the GLSL copy.
    /// </summary>
    public CellBounds PixelSlab(int x, int y)
    {
        if (Surface(x, y) is not { } surface)
            return new(Near: 0, Back: 0);
        float center = surface.CenterInverseDepth;
        return new(
            Near: center,
            Back: MathF.Min(CellBounds.SlabBack(center, backShares[y * Width + x]), surface.Bounds.Far)
        );
    }

    /// <summary>The slab of a cell that the walk reads, as the GPU stores it: the <see cref="PixelSlab"/> at the finest
    /// level, and the <see cref="Bounds"/> above it.</summary>
    public CellBounds WalkSlab(int level, int x, int y) => level == 0 ? PixelSlab(x, y) : Bounds(level, x, y);

    /// <summary>Checks the invariants in the class remarks. O(levels × pixels).</summary>
    internal void AssertInvariants()
    {
        for (int level = 0; level <= TopLevel; level++)
        for (int y = 0; y < Height; y++)
        for (int x = 0; x < Width; x++)
        {
            CellBounds pixel = PixelSlab(x, y);
            CellBounds cell = Bounds(level, x >> level, y >> level);
            if (pixel.Near > 0)
                Contract.Invariant(cell.Near >= pixel.Near && cell.Back <= pixel.Back);
        }
    }

    /// <summary>The unit geometric normal of a pixel, camera-relative, which the walk's facing test reads, or the zero
    /// vector outside the screen.</summary>
    public Vector3 GeometricNormal(int x, int y) =>
        x < 0 || y < 0 || x >= Width || y >= Height ? Vector3.Zero : geometricNormals[y * Width + x];

    /// <summary>The bounds of a cell, or <see cref="CellBounds.Empty"/> for a cell outside its level: no surface lies
    /// beyond the screen's edges. Level 0 gives each pixel's <see cref="PixelSurface.SlabBounds"/>, the input of level
    /// 1's merge, not the finest level's texel, which <see cref="PixelSlab"/> gives.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The level is not from 0 to <see cref="TopLevel"/>.</exception>
    public CellBounds Bounds(int level, int x, int y)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(level);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(level, TopLevel);
        (int levelWidth, int levelHeight) = LevelSize(Width, Height, level);
        bool outside = x < 0 || y < 0 || x >= levelWidth || y >= levelHeight;
        return outside ? CellBounds.Empty : levels[level][y * levelWidth + x];
    }

    /// <summary>The next level: each cell merges the up to four cells of the level below that it covers.</summary>
    private static CellBounds[] MergedLevel(
        CellBounds[] below,
        (int Width, int Height) belowSize,
        (int Width, int Height) size
    )
    {
        var cells = new CellBounds[size.Width * size.Height];
        for (int y = 0; y < size.Height; y++)
        for (int x = 0; x < size.Width; x++)
        {
            CellBounds merged = CellBounds.Empty;
            for (int childY = 2 * y; childY < Math.Min(2 * y + 2, belowSize.Height); childY++)
            for (int childX = 2 * x; childX < Math.Min(2 * x + 2, belowSize.Width); childX++)
                merged = merged.Merge(below[childY * belowSize.Width + childX]);
            cells[y * size.Width + x] = merged;
        }
        return cells;
    }
}
