using System.Numerics;

namespace Tertius.Rendering;

/// <summary>One star of the sky: its direction in root axes and its peak radiance in the sky, b = tint × brightness.</summary>
internal readonly record struct Star(Vector3 Direction, Vector3 Peak);

/// <summary>
/// The C# copy of the star code in sky.glsl (docs/plans/environment-map.md, "The reflected stars"). Directions are in
/// root axes. The hash runs in float, in the shader's order, so the CPU and the GPU agree on almost every star.
/// </summary>
internal static class StarField
{
    public const float CellsPerUnit = 150f;
    public const float CellWidth = 1f / CellsPerUnit;
    public const float StarShare = 0.02f;

    /// <summary>
    /// The peak radiance, in W/(m²·sr), of a star of brightness 1 (docs/plans/local-light-shadows.md, settled fork 1,
    /// physical stars). At 1080p and a 60° view, the brightest stars then send about 10⁻⁸ W/m².
    /// </summary>
    public const float UnitRadiance = 1f / 1024;

    public const float MeanBrightness = UnitRadiance * (0.05f + 3f / 9f);

    /// <summary>The tint of a star whose hash's z is 0.</summary>
    private static readonly Vector3 WarmTint = new(1f, 0.82f, 0.62f);

    /// <summary>The tint of a star whose hash's z is 1.</summary>
    private static readonly Vector3 CoolTint = new(0.72f, 0.82f, 1f);

    public static readonly Vector3 MeanTint = new(0.86f, 0.82f, 0.81f);

    /// <summary>hash33 of the shader: three numbers in [0, 1) from a point, each float operation in the shader's order.</summary>
    public static Vector3 Hash33(Vector3 p)
    {
        float x = Fract(p.X * 0.1031f);
        float y = Fract(p.Y * 0.1030f);
        float z = Fract(p.Z * 0.0973f);
        // dot(p, p.yxz + 33.33), summed left to right as written.
        float dot = x * (y + 33.33f) + y * (x + 33.33f) + z * (z + 33.33f);
        x += dot;
        y += dot;
        z += dot;
        return new(Fract((x + y) * z), Fract((x + x) * y), Fract((y + x) * x));
    }

    /// <summary>
    /// Whether the sphere of radius <see cref="CellsPerUnit"/> crosses <paramref name="cell"/>, the unit cube from its
    /// integer corner: whether some direction of the sky reads it.
    /// </summary>
    public static bool SkyReads(Vector3 cell)
    {
        Vector3 far = cell + Vector3.One;
        var farthest = Vector3.Max(Vector3.Abs(cell), Vector3.Abs(far));
        // The corners are integers, so no cell's span holds 0 inside it, and the nearer end is the nearest point.
        var nearest = Vector3.Min(Vector3.Abs(cell), Vector3.Abs(far));
        const float radiusSquared = CellsPerUnit * CellsPerUnit;
        return nearest.LengthSquared() <= radiusSquared && farthest.LengthSquared() >= radiusSquared;
    }

    /// <summary>The star of <paramref name="cell"/>, or null for a cell with no star: one cell in 50 holds one.</summary>
    public static Star? OfCell(Vector3 cell)
    {
        Vector3 hash = Hash33(cell);
        if (hash.X > StarShare)
            return null;
        var direction = Vector3.Normalize(cell + Hash33(cell + new Vector3(17)));
        float brightness = UnitRadiance * (0.05f + 3f * MathF.Pow(hash.Y, 8));
        return new(direction, Vector3.Lerp(WarmTint, CoolTint, hash.Z) * brightness);
    }

    /// <summary>
    /// w = √(f² + 4α²), in rad: the width of a reflected star on a lobe of <paramref name="roughness"/>, floored, under
    /// a pixel's <paramref name="footprint"/> f.
    /// </summary>
    public static float LobeWidth(float footprint, float roughness)
    {
        float alpha = (float)new GgxLobe(roughness).Alpha;
        return MathF.Sqrt(footprint * footprint + 4 * alpha * alpha);
    }

    /// <summary>
    /// k(w): the share of a star's light that its window keeps at <paramref name="width"/>, for widths up to a few rad.
    /// In double, because the closed form cancels for wide stars.
    /// </summary>
    public static float KeptShare(float width)
    {
        double a = (double)CellWidth * CellWidth / ((double)width * width);
        return (float)(1 - 2 / a + 2 * (1 - Math.Exp(-a)) / (a * a));
    }

    /// <summary>t(w) = smoothstep(c, 2c, w): how far single stars have faded into their mean at <paramref name="width"/>.</summary>
    public static float Fade(float width)
    {
        float t = Math.Clamp((width - CellWidth) / CellWidth, 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// M(ω): the mean star radiance near the unit <paramref name="direction"/>, for a main view of
    /// <paramref name="pixelAngle"/> rad per pixel.
    /// </summary>
    public static Vector3 MeanRadiance(Vector3 direction, float pixelAngle)
    {
        // A sphere of radius R crosses R² |ω|₁ cells per sr around ω, and each star sends E[b] π p².
        float cellsPerSteradian =
            CellsPerUnit * CellsPerUnit * (MathF.Abs(direction.X) + MathF.Abs(direction.Y) + MathF.Abs(direction.Z));
        float light = MeanBrightness * MathF.PI * pixelAngle * pixelAngle;
        return cellsPerSteradian * StarShare * light * MeanTint;
    }

    /// <summary>
    /// stars(d, r, f) before the open-sky share: the stars around the unit <paramref name="direction"/> d as a lobe of
    /// <paramref name="width"/> w reflects them, faded into their mean between w = c and 2c.
    /// </summary>
    public static Vector3 Reflected(Vector3 direction, float width, float pixelAngle)
    {
        float fade = Fade(width);
        Vector3 mean = MeanRadiance(direction, pixelAngle);
        if (fade >= 1)
            return mean;

        Vector3 scaled = direction * CellsPerUnit;
        var home = new Vector3(MathF.Floor(scaled.X), MathF.Floor(scaled.Y), MathF.Floor(scaled.Z));
        float peak = pixelAngle * pixelAngle / (width * width);
        Vector3 stars = Vector3.Zero;
        for (int z = -1; z <= 1; z++)
        for (int y = -1; y <= 1; y++)
        for (int x = -1; x <= 1; x++)
        {
            Vector3 cell = home + new Vector3(x, y, z);
            // A cell that no sky direction reads holds no star of the sky either.
            if (SkyReads(cell) && OfCell(cell) is { } star)
                stars += star.Peak * (peak * Kernel(Vector3.DistanceSquared(direction, star.Direction), width));
        }

        return (1 - fade) * stars + (1 - (1 - fade) * KeptShare(width)) * mean;
    }

    /// <summary>
    /// K(θ): the Gaussian of <paramref name="width"/> at <paramref name="angleSquared"/> θ², cut off smoothly at one
    /// cell, so that the sum does not depend on where the direction lies in its cell.
    /// </summary>
    private static float Kernel(float angleSquared, float width)
    {
        float window = 1 - angleSquared / (CellWidth * CellWidth);
        if (window <= 0)
            return 0;
        return MathF.Exp(-angleSquared / (width * width)) * window * window;
    }

    /// <summary>fract of GLSL: <paramref name="value"/> − floor(<paramref name="value"/>).</summary>
    private static float Fract(float value) => value - MathF.Floor(value);
}
