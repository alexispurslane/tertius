namespace Tertius.Rendering.Tests;

/// <summary>
/// FLIP, NVIDIA's perceptual difference of two images, in its form for display-encoded (LDR) images: Andersson,
/// Nilsson, Akenine-Möller, Oskarsson, Åström and Fairchild, "FLIP: A Difference Evaluator for Alternating Images",
/// Proc. ACM Comput. Graph. Interact. Tech. 3(2), 2020. Written in C# from that paper. FLIP gives each pixel a value
/// from 0 to 1: how much the pixel seems to change when a viewer flips between the two images.
/// <para>
/// The color pipeline blurs each image as the eye does at the given viewing distance (a contrast sensitivity filter
/// for each opponent channel of YCxCz), then measures the HyAB distance of the blurred colors in CIELAB, with a* and b*
/// scaled by lightness (the Hunt effect). It maps that distance to [0, 1]: 95 % of the scale goes to the first 40 % of
/// the largest distance, the distance of pure green from pure blue.
/// </para>
/// <para>
/// The feature pipeline finds edges and points in the linear luminance of each image with the first and second
/// derivatives of a Gaussian as wide as the eye's edge detector. A pixel whose edge or point strength differs between
/// the images gets a feature difference f, and its FLIP value is its color error raised to 1 − f. So a feature that
/// changed makes a color error count more.
/// </para>
/// <para>
/// The images are opaque sRGB; alpha is ignored. The filters repeat the border pixels beyond the border. Every filter
/// is a sum of separable Gaussians or of products of a derivative and a Gaussian, so each is applied as a row pass and
/// a column pass, which equals the paper's 2D kernels with their normalization.
/// </para>
/// </summary>
internal static class Flip
{
    /// <summary>
    /// Pixels per degree of view for the paper's default viewer: 0.7 m from a monitor 0.7 m wide with 3,840 pixels
    /// across, so 3840 · π / 180.
    /// </summary>
    public const double DefaultPixelsPerDegree = 67.02064327658226;

    // The paper's constants: qc, qf, pc, pt and w.
    private const double ColorExponent = 0.7;
    private const double FeatureExponent = 0.5;
    private const double CutoffShareOfLargestError = 0.4;
    private const double ValueAtCutoff = 0.95;
    private const double EdgeDetectorWidthDegrees = 0.082;

    /// <summary>The largest b of the three filters. It sets the radius of all three.</summary>
    private const double WidestSensitivity = 0.04;

    private static readonly Sensitivity Achromatic = new(A1: 1, B1: 0.0047, A2: 0, B2: 1e-5);
    private static readonly Sensitivity RedGreen = new(A1: 1, B1: 0.0053, A2: 0, B2: 1e-5);
    private static readonly Sensitivity BlueYellow = new(A1: 34.1, B1: 0.04, A2: 13.5, B2: 0.025);

    /// <summary>sRGB primaries to CIE XYZ, for the D65 white.</summary>
    private static readonly double[,] RgbToXyz =
    {
        { 10135552.0 / 24577794, 8788810.0 / 24577794, 4435075.0 / 24577794 },
        { 2613072.0 / 12288897, 8788810.0 / 12288897, 887015.0 / 12288897 },
        { 1425312.0 / 73733382, 8788810.0 / 73733382, 70074185.0 / 73733382 }
    };

    private static readonly double[,] XyzToRgb = Invert(RgbToXyz);

    /// <summary>The XYZ of white, linear RGB (1, 1, 1): the row sums of <see cref="RgbToXyz"/>.</summary>
    private static readonly (double X, double Y, double Z) White =
        (RgbToXyz[0, 0] + RgbToXyz[0, 1] + RgbToXyz[0, 2], RgbToXyz[1, 0] + RgbToXyz[1, 1] + RgbToXyz[1, 2],
            RgbToXyz[2, 0] + RgbToXyz[2, 1] + RgbToXyz[2, 2]);

    /// <summary>The linear light of each sRGB byte.</summary>
    private static readonly float[] LinearOfByte =
    [
        .. Enumerable.Range(0, 256).Select(v =>
        {
            double c = v / 255.0;
            return (float)(c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4));
        })
    ];

    /// <summary>cmax: the color error of pure green against pure blue, the largest that FLIP scales to.</summary>
    private static readonly double LargestColorError =
        Math.Pow(HyAB(HuntLab(LinearToXyz(0, 1, 0)), HuntLab(LinearToXyz(0, 0, 1))), ColorExponent);

    /// <summary>The contrast sensitivity filter of one opponent channel: a1, b1, a2, b2 of the paper (b in degrees²).</summary>
    private readonly record struct Sensitivity(double A1, double B1, double A2, double B2);

    /// <summary>
    /// The FLIP value of each pixel of <paramref name="test"/> against <paramref name="reference"/>, for a viewer who
    /// sees <paramref name="pixelsPerDegree"/> pixels in each degree of view.
    /// </summary>
    public static FlipMap Compare(Capture reference, Capture test, double pixelsPerDegree = DefaultPixelsPerDegree)
    {
        if (reference.Width != test.Width || reference.Height != test.Height)
            throw new ArgumentException(
                $"FLIP compares images of one size, not {reference.Width}×{reference.Height} and {test.Width}×{test.Height}.",
                nameof(test));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pixelsPerDegree);

        Opponent referenceOpponent = Opponent.Of(reference);
        Opponent testOpponent = Opponent.Of(test);
        float[] colorError = ColorError(referenceOpponent, testOpponent, pixelsPerDegree);
        float[] featureDifference =
            FeatureDifference(referenceOpponent.Lightness, testOpponent.Lightness, pixelsPerDegree);

        float[] values = new float[colorError.Length];
        for (int i = 0; i < values.Length; i++)
            values[i] = MathF.Pow(colorError[i], 1 - featureDifference[i]);
        return new(reference.Width, reference.Height, values);
    }

    /// <summary>The color error of each pixel, in [0, 1], after the contrast sensitivity filters.</summary>
    private static float[] ColorError(Opponent reference, Opponent test, double pixelsPerDegree)
    {
        int radius = (int)Math.Ceiling(3 * Math.Sqrt(WidestSensitivity / (2 * Math.PI * Math.PI)) * pixelsPerDegree);
        Opponent filteredReference = reference.Filtered(radius, pixelsPerDegree);
        Opponent filteredTest = test.Filtered(radius, pixelsPerDegree);

        double cutoff = CutoffShareOfLargestError * LargestColorError;
        float[] error = new float[reference.Lightness.Values.Length];
        for (int i = 0; i < error.Length; i++)
        {
            double e = Math.Pow(HyAB(HuntLab(filteredReference.XyzAt(i)), HuntLab(filteredTest.XyzAt(i))),
                ColorExponent);
            error[i] = (float)(e < cutoff
                ? ValueAtCutoff / cutoff * e
                : ValueAtCutoff + (e - cutoff) / (LargestColorError - cutoff) * (1 - ValueAtCutoff));
        }

        return error;
    }

    /// <summary>The feature difference f of each pixel, in [0, 1], from the YCxCz lightness Y of the two images.</summary>
    private static float[] FeatureDifference(Plane referenceLightness, Plane testLightness, double pixelsPerDegree)
    {
        double sigma = 0.5 * EdgeDetectorWidthDegrees * pixelsPerDegree;
        int radius = (int)Math.Ceiling(3 * sigma);
        double[] gaussian = new double[2 * radius + 1];
        double[] edge = new double[2 * radius + 1];
        double[] point = new double[2 * radius + 1];
        for (int i = -radius; i <= radius; i++)
        {
            double g = Math.Exp(-i * i / (2 * sigma * sigma));
            gaussian[i + radius] = g;
            edge[i + radius] = -i * g;
            point[i + radius] = (i * i / (sigma * sigma) - 1) * g;
        }

        float[] blur = Normalized(gaussian);
        float[] firstDerivative = SignNormalized(edge);
        float[] secondDerivative = SignNormalized(point);

        // Y + 16 over 116 is the linear luminance, from 0 for black to 1 for white.
        Plane referenceLuminance = referenceLightness.Map(y => (y + 16) / 116);
        Plane testLuminance = testLightness.Map(y => (y + 16) / 116);
        float[] referenceEdges = Strength(referenceLuminance, firstDerivative, blur);
        float[] testEdges = Strength(testLuminance, firstDerivative, blur);
        float[] referencePoints = Strength(referenceLuminance, secondDerivative, blur);
        float[] testPoints = Strength(testLuminance, secondDerivative, blur);

        float[] difference = new float[referenceEdges.Length];
        for (int i = 0; i < difference.Length; i++)
        {
            float change = MathF.Max(MathF.Abs(referenceEdges[i] - testEdges[i]),
                MathF.Abs(referencePoints[i] - testPoints[i]));
            difference[i] = (float)Math.Pow(change / Math.Sqrt(2), FeatureExponent);
        }

        return difference;
    }

    /// <summary>
    /// The length of the feature vector at each pixel: the derivative across the columns (blurred down the rows), and
    /// the derivative down the rows (blurred across the columns).
    /// </summary>
    private static float[] Strength(Plane luminance, float[] derivative, float[] blur)
    {
        Plane across = luminance.Filtered(derivative, blur);
        Plane down = luminance.Filtered(blur, derivative);
        float[] strength = new float[across.Values.Length];
        for (int i = 0; i < strength.Length; i++)
            strength[i] = MathF.Sqrt(across.Values[i] * across.Values[i] + down.Values[i] * down.Values[i]);
        return strength;
    }

    /// <summary>The weights scaled to sum to 1.</summary>
    private static float[] Normalized(double[] weights)
    {
        double sum = weights.Sum();
        return [.. weights.Select(w => (float)(w / sum))];
    }

    /// <summary>
    /// The weights with the positive ones scaled to sum to 1 and the negative ones to sum to −1, as the paper does for
    /// its edge and point detectors. At a scale so small that one side is empty, the detector sees nothing: all zero.
    /// </summary>
    private static float[] SignNormalized(double[] weights)
    {
        double positive = weights.Where(w => w > 0).Sum();
        double negative = -weights.Where(w => w < 0).Sum();
        return positive == 0 || negative == 0
            ? new float[weights.Length]
            : [.. weights.Select(w => (float)(w > 0 ? w / positive : w / negative))];
    }

    /// <summary>The 1D Gaussian of one term of a contrast sensitivity filter, exp(−π² x² / b), x in degrees.</summary>
    private static double[] SensitivityGaussian(double b, int radius, double pixelsPerDegree)
    {
        double[] weights = new double[2 * radius + 1];
        for (int i = -radius; i <= radius; i++)
        {
            double x = i / pixelsPerDegree;
            weights[i + radius] = Math.Exp(-Math.PI * Math.PI * x * x / b);
        }

        return weights;
    }

    /// <summary>
    /// Applies a contrast sensitivity filter. The paper's 2D kernel is a1·√(π/b1)·G1(x)G1(y) + a2·√(π/b2)·G2(x)G2(y),
    /// divided by its sum. A term of it, normalized, is two passes of its normalized 1D Gaussian, and its share of the
    /// sum is a·√(π/b)·S², where S is the sum of its 1D Gaussian.
    /// </summary>
    private static Plane FilterChannel(Plane channel, Sensitivity filter, int radius, double pixelsPerDegree)
    {
        double[] first = SensitivityGaussian(filter.B1, radius, pixelsPerDegree);
        double firstSum = first.Sum();
        double firstShare = filter.A1 * Math.Sqrt(Math.PI / filter.B1) * firstSum * firstSum;
        Plane filtered = channel.Filtered(Normalized(first), Normalized(first));
        if (filter.A2 == 0)
            return filtered;

        double[] second = SensitivityGaussian(filter.B2, radius, pixelsPerDegree);
        double secondSum = second.Sum();
        double secondShare = filter.A2 * Math.Sqrt(Math.PI / filter.B2) * secondSum * secondSum;
        Plane secondFiltered = channel.Filtered(Normalized(second), Normalized(second));
        float firstWeight = (float)(firstShare / (firstShare + secondShare));
        float secondWeight = (float)(secondShare / (firstShare + secondShare));
        return filtered.Combine(secondFiltered, (a, b) => firstWeight * a + secondWeight * b);
    }

    private static (double X, double Y, double Z) LinearToXyz(double r, double g, double b) =>
        (RgbToXyz[0, 0] * r + RgbToXyz[0, 1] * g + RgbToXyz[0, 2] * b,
            RgbToXyz[1, 0] * r + RgbToXyz[1, 1] * g + RgbToXyz[1, 2] * b,
            RgbToXyz[2, 0] * r + RgbToXyz[2, 1] * g + RgbToXyz[2, 2] * b);

    /// <summary>
    /// CIELAB of an XYZ color, after the paper clamps it to the sRGB gamut in linear RGB, with a* and b* scaled by
    /// L* / 100 (the Hunt effect: colors look less colorful in the dark).
    /// </summary>
    private static (double L, double A, double B) HuntLab((double X, double Y, double Z) xyz)
    {
        double r = Math.Clamp(XyzToRgb[0, 0] * xyz.X + XyzToRgb[0, 1] * xyz.Y + XyzToRgb[0, 2] * xyz.Z, 0, 1);
        double g = Math.Clamp(XyzToRgb[1, 0] * xyz.X + XyzToRgb[1, 1] * xyz.Y + XyzToRgb[1, 2] * xyz.Z, 0, 1);
        double b = Math.Clamp(XyzToRgb[2, 0] * xyz.X + XyzToRgb[2, 1] * xyz.Y + XyzToRgb[2, 2] * xyz.Z, 0, 1);
        (double x, double y, double z) = LinearToXyz(r, g, b);
        double fx = LabCurve(x / White.X), fy = LabCurve(y / White.Y), fz = LabCurve(z / White.Z);
        double lightness = 116 * fy - 16;
        return (lightness, 500 * (fx - fy) * lightness / 100, 200 * (fy - fz) * lightness / 100);
    }

    private static double LabCurve(double t)
    {
        const double delta = 6.0 / 29;
        return t > delta * delta * delta ? Math.Cbrt(t) : t / (3 * delta * delta) + 4.0 / 29;
    }

    /// <summary>The HyAB distance: the lightness difference plus the Euclidean distance in the a*b* plane.</summary>
    private static double HyAB((double L, double A, double B) p, (double L, double A, double B) q) =>
        Math.Abs(p.L - q.L) + Math.Sqrt((p.A - q.A) * (p.A - q.A) + (p.B - q.B) * (p.B - q.B));

    private static double[,] Invert(double[,] m)
    {
        double a = m[0, 0],
            b = m[0, 1],
            c = m[0, 2],
            d = m[1, 0],
            e = m[1, 1],
            f = m[1, 2],
            g = m[2, 0],
            h = m[2, 1],
            k = m[2, 2];
        double determinant = a * (e * k - f * h) - b * (d * k - f * g) + c * (d * h - e * g);
        return new[,]
        {
            { (e * k - f * h) / determinant, (c * h - b * k) / determinant, (b * f - c * e) / determinant },
            { (f * g - d * k) / determinant, (a * k - c * g) / determinant, (c * d - a * f) / determinant },
            { (d * h - e * g) / determinant, (b * g - a * h) / determinant, (a * e - b * d) / determinant }
        };
    }

    /// <summary>An image in YCxCz, the opponent space of the color filters: Y = 116·y − 16, Cx = 500·(x − y), Cz = 200·(y − z), with x, y, z the XYZ over the white.</summary>
    private sealed class Opponent(Plane lightness, Plane redGreen, Plane blueYellow)
    {
        public Plane Lightness { get; } = lightness;

        public static Opponent Of(Capture image)
        {
            int count = image.Width * image.Height;
            float[] y = new float[count];
            float[] cx = new float[count];
            float[] cz = new float[count];
            for (int i = 0; i < count; i++)
            {
                (double x, double luminance, double z) = LinearToXyz(LinearOfByte[image.Rgba[4 * i]],
                    LinearOfByte[image.Rgba[4 * i + 1]], LinearOfByte[image.Rgba[4 * i + 2]]);
                double xr = x / White.X, yr = luminance / White.Y, zr = z / White.Z;
                y[i] = (float)(116 * yr - 16);
                cx[i] = (float)(500 * (xr - yr));
                cz[i] = (float)(200 * (yr - zr));
            }

            return new(new(image.Width, image.Height, y), new(image.Width, image.Height, cx),
                new(image.Width, image.Height, cz));
        }

        public Opponent Filtered(int radius, double pixelsPerDegree) => new(
            FilterChannel(channel: Lightness, filter: Achromatic, radius: radius, pixelsPerDegree: pixelsPerDegree),
            FilterChannel(channel: redGreen, filter: RedGreen, radius: radius, pixelsPerDegree: pixelsPerDegree),
            FilterChannel(channel: blueYellow, filter: BlueYellow, radius: radius, pixelsPerDegree: pixelsPerDegree));

        public (double X, double Y, double Z) XyzAt(int i)
        {
            double y = (Lightness.Values[i] + 16.0) / 116;
            return ((y + redGreen.Values[i] / 500.0) * White.X, y * White.Y,
                (y - blueYellow.Values[i] / 200.0) * White.Z);
        }
    }

    /// <summary>One channel of an image, row-major, top row first.</summary>
    private sealed class Plane(int width, int height, float[] values)
    {
        public float[] Values { get; } = values;

        public Plane Map(Func<float, float> f) => new(width, height, [.. Values.Select(f)]);

        public Plane Combine(Plane other, Func<float, float, float> f) =>
            new(width, height, [.. Values.Zip(other.Values, f)]);

        /// <summary>
        /// Correlates each row with <paramref name="acrossColumns"/>, then each column of the result with
        /// <paramref name="downRows"/>. Both have odd length, centered. Pixels beyond the border repeat the border.
        /// </summary>
        public Plane Filtered(float[] acrossColumns, float[] downRows)
        {
            float[] rowPass = new float[Values.Length];
            int radius = acrossColumns.Length / 2;
            for (int y = 0; y < height; y++)
            {
                int row = y * width;
                for (int x = 0; x < width; x++)
                {
                    float sum = 0;
                    for (int i = -radius; i <= radius; i++)
                        sum += acrossColumns[i + radius] * Values[row + Math.Clamp(x + i, 0, width - 1)];
                    rowPass[row + x] = sum;
                }
            }

            float[] result = new float[Values.Length];
            radius = downRows.Length / 2;
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                float sum = 0;
                for (int i = -radius; i <= radius; i++)
                    sum += downRows[i + radius] * rowPass[Math.Clamp(y + i, 0, height - 1) * width + x];
                result[y * width + x] = sum;
            }

            return new(width, height, result);
        }
    }
}
