namespace Tertius.Rendering;

/// <summary>The axis along which one pass of the separable scatter runs: the rows first, then the columns.</summary>
internal enum ScatterAxis
{
    Rows,
    Columns,
}

/// <summary>
/// The images that the scatter reads, each <paramref name="Width"/> × <paramref name="Height"/> pixels in rows from the
/// top: D, the skin mask and the view depth.
/// </summary>
/// <param name="Light">D, the diffuse image's rgb, pre-exposed: the rows' source.</param>
/// <param name="Skin">The diffuse image's alpha: whether a pixel is skin.</param>
/// <param name="ViewDepth">z, each pixel's distance along the camera's forward axis, in meters.</param>
/// <param name="TanHalfY">tan(φ_y / 2) of the vertical field of view.</param>
internal sealed record ScatterField(
    int Width,
    int Height,
    Double3[] Light,
    bool[] Skin,
    double[] ViewDepth,
    double TanHalfY
);

/// <summary>
/// One pass of the separable scatter at one skin pixel, the C# copy of scatterAlong in skin_scatter.glsl
/// (docs/plans/skin-and-hair.md, "The two passes").
/// </summary>
internal static class SkinScatter
{
    /// <summary>
    /// tan 60°: the steepest rise of depth per meter of the measured profile across the view at which a tap still counts
    /// as the center's surface. A tap's offset counts its kernel's <see cref="ScatterKernel.WidthScale"/> times shorter in
    /// this rule, so the wider scatter crosses no larger depth step than the measured profile does (user, 2026-10-31).
    /// </summary>
    public const double SlopeLimit = 1.7320508075688772;

    // The largest image side. A tap's offset is clamped to it before its conversion to an integer, which would overflow
    // for a tap far past the image.
    private const int MaxSide = 16_384;

    /// <summary>s(z): how many pixels one meter across the view covers at <paramref name="viewDepth"/> meters.</summary>
    public static double PixelsPerMeter(double viewDepth, int height, double tanHalfY) =>
        height / (2 * viewDepth * tanHalfY);

    /// <summary>
    /// The kernel's sum along <paramref name="axis"/> at the skin pixel (<paramref name="x"/>, <paramref name="y"/>) of
    /// <paramref name="source"/>: D for the rows, the row image for the columns. Each tap reads the one pixel nearest it.
    /// The kernel gives the taps' offsets, their reach and the unit of the offsets in the slope rule, its width scale.
    /// </summary>
    public static Double3 Pass(
        ScatterField field,
        ReadOnlySpan<Double3> source,
        int x,
        int y,
        ScatterAxis axis,
        ScatterKernel kernel
    )
    {
        int center = y * field.Width + x;
        Double3 centerLight = source[center];
        double depth = field.ViewDepth[center];
        double pixelsPerMeter = PixelsPerMeter(viewDepth: depth, height: field.Height, tanHalfY: field.TanHalfY);
        if (kernel.Reach * pixelsPerMeter < 0.5)
            return centerLight;
        (int stepX, int stepY) = axis switch
        {
            ScatterAxis.Rows => (1, 0),
            ScatterAxis.Columns => (0, 1),
        };
        int middle = kernel.Taps.Length / 2;
        Double3 sum = Double3.Zero;
        for (int i = 0; i < kernel.Taps.Length; i++)
        {
            ScatterTap tap = kernel.Taps[i];
            int offset = (int)Math.Clamp(Math.Floor(tap.Offset * pixelsPerMeter + 0.5), -MaxSide, MaxSide);
            int atX = Math.Clamp(x + stepX * offset, 0, field.Width - 1);
            int atY = Math.Clamp(y + stepY * offset, 0, field.Height - 1);
            int at = atY * field.Width + atX;
            Double3 light = centerLight;
            if (i != middle && field.Skin[at])
            {
                // Up to the slope limit the tap keeps its own light, and up to twice the limit it gives way to the center's.
                double run = Math.Abs(tap.Offset) / kernel.WidthScale;
                double rise = Math.Abs(field.ViewDepth[at] - depth) / (run * SlopeLimit);
                light = Double3.Lerp(centerLight, source[at], Math.Clamp(2 - rise, 0, 1));
            }
            sum += tap.Weight * light;
        }
        return sum;
    }
}
