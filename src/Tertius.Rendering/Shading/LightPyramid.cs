using System.Numerics;

namespace Tertius.Rendering;

/// <summary>
/// The light pyramid on the CPU: the C# copy of the light_pyramid shaders and of the guided draw in screen_trace.glsl
/// (ARCHITECTURE.md, "Guided diffuse rays"). Level 0 holds each pixel's guide light, and each cell of level k the sum of
/// the up to four cells under it, at the sizes of <see cref="DepthPyramid.LevelSize"/>.
/// </summary>
internal sealed class LightPyramid
{
    /// <summary>The share of the diffuse rays that aim at a pixel drawn from the pyramid: g of the mixture density.</summary>
    public const float GuidedShare = 0.25f;

    /// <summary>The end of a chord that no side of the slab ends. Not infinity, as in the shader.</summary>
    private const float NoEnd = 1e30f;

    /// <summary>
    /// How deep a pixel's target box reaches behind its center, the front of its slab, as a share of its view depth: κ / 2.
    /// A ray from a low angle meets the front of the slabs, and one from behind enters a slab near its front.
    /// </summary>
    public const float TargetDepthShare = CellBounds.ThicknessShare / 2;

    /// <summary>Each level from 0 to <see cref="TopLevel"/>.</summary>
    private readonly Level[] levels;

    private LightPyramid(Level[] levels) => this.levels = levels;

    /// <summary>The level whose one cell covers the screen.</summary>
    public int TopLevel => levels.Length - 1;

    /// <summary>The guide light of the whole screen: the top cell.</summary>
    public float Total => levels[TopLevel].At(0, 0);

    /// <summary>The pyramid over each pixel's guide light, row by row from the top.</summary>
    /// <exception cref="ArgumentException">The span does not hold width × height values.</exception>
    public static LightPyramid Of(ReadOnlySpan<float> lights, int width, int height)
    {
        if (lights.Length != width * height)
            throw new ArgumentException(
                $"A {width} × {height} pyramid needs {width * height} lights, not {lights.Length}.",
                nameof(lights)
            );
        int topLevel = DepthPyramid.LevelCount(width, height);
        var levels = new Level[topLevel + 1];
        levels[0] = new(lights.ToArray(), width, height);
        for (int level = 1; level <= topLevel; level++)
        {
            (int levelWidth, int levelHeight) = DepthPyramid.LevelSize(width, height, level);
            float[] cells = new float[levelWidth * levelHeight];
            for (int y = 0; y < levelHeight; y++)
            for (int x = 0; x < levelWidth; x++)
                cells[y * levelWidth + x] = levels[level - 1].ChildSum(x, y);
            levels[level] = new(cells, levelWidth, levelHeight);
        }
        return new(levels);
    }

    /// <summary>The light of a cell of <paramref name="level"/>, 0 outside the level.</summary>
    public float Light(int level, int x, int y) => levels[level].At(x, y);

    /// <summary>
    /// The pixel that the descent draws, each pixel in proportion to its light: from the top, each level takes a child in
    /// proportion to its light, by one unit in [0, 1) per level, <paramref name="units"/>[i] for level TopLevel − i.
    /// The total must be above 0.
    /// </summary>
    public (int X, int Y) Drawn(ReadOnlySpan<float> units)
    {
        (int x, int y) = (0, 0);
        Span<float> lights = stackalloc float[4];
        for (int level = TopLevel; level >= 1; level--)
        {
            float total = 0;
            for (int child = 0; child < 4; child++)
            {
                lights[child] = Light(level - 1, 2 * x + (child & 1), 2 * y + (child >> 1));
                total += lights[child];
            }
            float reach = units[TopLevel - level] * total;
            int chosen = 0;
            float below = 0;
            for (int child = 0; child < 4; child++)
            {
                if (lights[child] <= 0)
                    continue;
                chosen = child;
                below += lights[child];
                if (reach < below)
                    break;
            }
            (x, y) = (2 * x + (chosen & 1), 2 * y + (chosen >> 1));
        }
        return (x, y);
    }

    /// <summary>
    /// The point that a guided ray aims at in <paramref name="pixel"/>: in the pixel's target box, its volume from its
    /// center's depth to <see cref="TargetDepthShare"/> behind it, on the view ray through <paramref name="place"/> in the
    /// pixel, at the share <paramref name="depthShare"/> of the box's depth. Uniform units give a point uniform in the
    /// pixel's place and in depth. Under a jitter, <paramref name="place"/> holds it, as viewRayThrough adds it.
    /// </summary>
    public static Vector3 TargetOf(
        in CameraView camera,
        (int X, int Y) pixel,
        Vector2 place,
        float depthShare,
        float inverseDepth
    ) =>
        camera.ViewRayThrough(new Vector2(pixel.X, pixel.Y) + place)
        * ((1 + TargetDepthShare * depthShare) / inverseDepth);

    /// <summary>
    /// The density per steradian with which a guided ray from <paramref name="origin"/> runs along
    /// <paramref name="direction"/> and counts: it counts when the walk hits a pixel of the 3 × 3 around its target. So the
    /// density sums <see cref="PixelDensity"/> over the 3 × 3 pixels around <paramref name="hit"/>, in rows from the top.
    /// </summary>
    /// <param name="camera">The camera of the frame.</param>
    /// <param name="depth">The depth pyramid of the frame, whose finest level gives each pixel's slab.</param>
    /// <param name="origin">The ray's origin, camera-relative.</param>
    /// <param name="direction">The ray's unit direction.</param>
    /// <param name="hit">The pixel that the walk hit.</param>
    /// <param name="jitter">The frame's jitter in pixels, which moves every pixel's edges, as in viewRayThrough.</param>
    public float GuidedDensity(
        in CameraView camera,
        DepthPyramid depth,
        Vector3 origin,
        Vector3 direction,
        (int X, int Y) hit,
        Vector2 jitter
    )
    {
        float density = 0;
        for (int y = hit.Y - 1; y <= hit.Y + 1; y++)
        for (int x = hit.X - 1; x <= hit.X + 1; x++)
            density += PixelDensity(
                camera: camera,
                origin: origin,
                direction: direction,
                pixel: (x, y),
                inverseDepth: depth.Finest(x, y),
                jitter: jitter
            );
        return density;
    }

    /// <summary>
    /// The density per steradian of the directions of the guided rays from <paramref name="origin"/> that aim at
    /// <paramref name="pixel"/>, along <paramref name="direction"/>: the pixel's share of the light times the integral of
    /// t² over its target box along the ray, over the box's volume element, t the distance from the origin. 0 for a
    /// direction that misses the box, and for a pixel without light, such as one beyond the screen.
    /// </summary>
    /// <param name="inverseDepth">The pixel's center w, the finest level of the depth pyramid.</param>
    /// <param name="jitter">The frame's jitter in pixels, which moves the pixel's edges, as in viewRayThrough.</param>
    public float PixelDensity(
        in CameraView camera,
        Vector3 origin,
        Vector3 direction,
        (int X, int Y) pixel,
        float inverseDepth,
        Vector2 jitter
    )
    {
        float light = Light(level: 0, pixel.X, pixel.Y);
        if (light <= 0 || inverseDepth <= 0)
            return 0;
        float front = 1 / inverseDepth;
        float back = (1 + TargetDepthShare) / inverseDepth;
        float originDepth = Vector3.Dot(origin, camera.Forward);
        float along = Vector3.Dot(direction, camera.Forward);
        // The chord of the ray through the box: t from 0 where every side of the box holds the ray's point.
        var chord = new Chord(0, NoEnd);
        chord = chord.Inside(originDepth - front, along).Inside(back - originDepth, -along);
        Vector3 right = camera.Right / camera.TanHalfX;
        Vector3 up = camera.Up / camera.TanHalfY;
        for (int side = 0; side < 2; side++)
        {
            // The pixel's columns and rows, each edge a plane through the eye, moved by the jitter.
            var edge = new Vector2(
                2f * (pixel.X + side + jitter.X) / camera.Width - 1,
                2f * (pixel.Y + side + jitter.Y) / camera.Height - 1
            );
            float facing = side == 0 ? 1 : -1;
            Vector3 acrossX = (right - edge.X * camera.Forward) * facing;
            Vector3 acrossY = (-up - edge.Y * camera.Forward) * facing;
            chord = chord
                .Inside(Vector3.Dot(origin, acrossX), Vector3.Dot(direction, acrossX))
                .Inside(Vector3.Dot(origin, acrossY), Vector3.Dot(direction, acrossY));
        }
        if (chord.End <= chord.Start)
            return 0;
        // The pixel's area at depth 1 times the box's depth: a uniform point of the box has the density 1 / (A z² dz).
        float areaDepth = 4 * camera.TanHalfX * camera.TanHalfY / (camera.Width * camera.Height) * (back - front);
        return light / Total * chord.SquareOverDepths(originDepth, along) / areaDepth;
    }

    /// <summary>
    /// Whether a guided ray that aims at <paramref name="target"/> counts: the walk hit a pixel of the 3 × 3 around it,
    /// and the ray crosses the target's box.
    /// </summary>
    public bool Counts(
        in CameraView camera,
        DepthPyramid depth,
        Vector3 origin,
        Vector3 direction,
        (int X, int Y) hit,
        (int X, int Y) target,
        Vector2 jitter
    )
    {
        bool near = Math.Abs(hit.X - target.X) <= 1 && Math.Abs(hit.Y - target.Y) <= 1;
        return near
            && PixelDensity(
                camera: camera,
                origin: origin,
                direction: direction,
                pixel: target,
                inverseDepth: depth.Finest(target.X, target.Y),
                jitter: jitter
            ) > 0;
    }

    /// <summary>The density of the one-sample mixture: the cosine draw's density and the guided draw's, by their shares.</summary>
    public static float MixtureDensity(float cosineDensity, float guidedDensity) =>
        (1 - GuidedShare) * cosineDensity + GuidedShare * guidedDensity;

    /// <summary>The distances t from Start to End along a ray inside a slab.</summary>
    private readonly record struct Chord(float Start, float End)
    {
        /// <summary>The chord where also α + β t ≥ 0.</summary>
        public Chord Inside(float alpha, float beta)
        {
            if (beta > 0)
                return this with { Start = MathF.Max(Start, -alpha / beta) };
            if (beta < 0)
                return this with { End = MathF.Min(End, -alpha / beta) };
            return alpha < 0 ? this with { End = Start } : this;
        }

        /// <summary>
        /// The integral over the chord of t² / z², z = <paramref name="originDepth"/> + t <paramref name="along"/> the
        /// point's view depth, by Simpson's rule: the slab's volume element per steradian over its own, A z² dz. z changes
        /// by at most <see cref="TargetDepthShare"/> inside the box, so t² / z² is nearly a quadratic, which the rule
        /// integrates exactly.
        /// </summary>
        public float SquareOverDepths(float originDepth, float along)
        {
            float middle = 0.5f * (Start + End);
            return (End - Start)
                / 6
                * (Ratio(Start, originDepth, along) + 4 * Ratio(middle, originDepth, along) + Ratio(End, originDepth, along));
        }

        private static float Ratio(float t, float originDepth, float along)
        {
            float ratio = t / (originDepth + t * along);
            return ratio * ratio;
        }
    }

    /// <summary>One level's cells, row by row from the top, and its size in cells.</summary>
    private readonly record struct Level(float[] Cells, int Width, int Height)
    {
        /// <summary>The light of a cell, 0 outside the level.</summary>
        public float At(int x, int y)
        {
            bool outside = x < 0 || y < 0 || x >= Width || y >= Height;
            return outside ? 0 : Cells[y * Width + x];
        }

        /// <summary>The sum of the up to four cells under a cell of the next level, in the order of the shaders.</summary>
        public float ChildSum(int x, int y) =>
            At(2 * x, 2 * y) + At(2 * x + 1, 2 * y) + At(2 * x, 2 * y + 1) + At(2 * x + 1, 2 * y + 1);
    }
}
