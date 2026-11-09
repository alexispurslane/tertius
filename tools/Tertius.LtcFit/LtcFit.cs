using Tertius.Rendering;

namespace Tertius.LtcFit;

/// <summary>
/// The fit of <see cref="LtcTable"/> to our GGX lobe, after Heitz, Dupuy, Hill and Neubelt 2016 (ARCHITECTURE.md,
/// "Rectangle lights"). Each texel minimizes the cubed difference between the lobe and the transformed cosine, sampled
/// from both, with Nelder–Mead.
/// </summary>
internal static class LtcFit
{
    /// <summary>The samples per axis of each error estimate, from each of the two distributions.</summary>
    public const int SamplesPerAxis = 32;

    /// <summary>The simplex's first step and the stop of Nelder–Mead, as Heitz's fit takes them.</summary>
    private const double Step = 0.05,
        Tolerance = 1e-5;

    private const int MaxIterations = 100;

    /// <summary>
    /// Every texel of the table. The rows run in parallel: each row starts from its own first guess at μ = 1 and then
    /// walks toward the horizon, each fit starting from the last, so the bits do not depend on the threads.
    /// </summary>
    public static Half[] Table()
    {
        var texels = new Half[LtcTable.Size * LtcTable.Size * LtcTable.Channels];
        Parallel.For(fromInclusive: 0, toExclusive: LtcTable.Size, body: row => Row(row).CopyTo(texels.AsSpan(row * LtcTable.Size * LtcTable.Channels)));
        return texels;
    }

    /// <summary>The <see cref="LtcTable.Size"/> texels of row <paramref name="row"/>, four halves each.</summary>
    public static Half[] Row(int row)
    {
        var lobe = new GgxLobe(BrdfTables.TexelValue(row, LtcTable.Size));
        var texels = new Half[LtcTable.Size * LtcTable.Channels];
        // Head on, the lobe is round about the normal, and the light's angle off it is twice the half vector's: a scale of
        // 2α on x and y starts near the fit.
        double scale = Math.Min(2 * lobe.Alpha, 1);
        (double M11, double M22, double M13) shape = (scale, scale, 0);
        for (int column = 0; column < LtcTable.Size; column++)
        {
            double nDotV = ViewCosineOf(column);
            var target = new Target(lobe, nDotV);
            bool round = column == 0;
            shape = Fit(target, shape, round);
            LtcMatrix inverse = target.Cosine(shape).Inverse;
            int texel = column * LtcTable.Channels;
            texels[texel] = (Half)inverse.M00;
            texels[texel + 1] = (Half)inverse.M02;
            texels[texel + 2] = (Half)inverse.M20;
            texels[texel + 3] = (Half)inverse.M22;
        }
        return texels;
    }

    /// <summary>
    /// The view cosine μ that column <paramref name="column"/> holds: 1 − x² with x = column / (n − 1), at least
    /// <see cref="MaterialSample.MinViewCosine"/>, the floor that the shading takes too. <see cref="LtcTable.Sample"/>
    /// reads the column back from √(1 − μ).
    /// </summary>
    private static double ViewCosineOf(int column)
    {
        double x = BrdfTables.TexelValue(column, LtcTable.Size);
        return Math.Max(1 - x * x, MaterialSample.MinViewCosine);
    }

    /// <summary>The fitted shape of <paramref name="target"/> from <paramref name="start"/>; one scale only if <paramref name="round"/>.</summary>
    private static (double M11, double M22, double M13) Fit(
        Target target,
        (double M11, double M22, double M13) start,
        bool round
    )
    {
        (double M11, double M22, double M13) Shape(ReadOnlySpan<double> p) =>
            round
                ? (Math.Max(p[0], 1e-7), Math.Max(p[0], 1e-7), 0)
                : (Math.Max(p[0], 1e-7), Math.Max(p[1], 1e-7), p[2]);
        double[] best = NelderMead.Minimize(
            start: round ? [start.M11] : [start.M11, start.M22, start.M13],
            step: Step,
            tolerance: Tolerance,
            maxIterations: MaxIterations,
            objective: p => target.Error(target.Cosine(Shape(p)))
        );
        return Shape(best);
    }

    /// <summary>
    /// The lobe of one roughness at the view <c>(√(1 − μ²), 0, μ)</c>, with its magnitude ∫ f cos and the mean direction of
    /// its light, which orients the cosine.
    /// </summary>
    private sealed class Target
    {
        private readonly GgxLobe lobe;
        private readonly double nDotV;
        private readonly Double3 toView;
        private readonly double magnitude;
        private readonly Double3 meanDirection;

        public Target(GgxLobe lobe, double nDotV)
        {
            this.lobe = lobe;
            this.nDotV = nDotV;
            toView = new(Math.Sqrt(1 - nDotV * nDotV), 0, nDotV);
            Double3 sum = Double3.Zero;
            double total = 0;
            ForEachSample(
                (u1, u2) =>
                {
                    Double3 toLight = SampleLobe(u1, u2);
                    (double value, double density) = Evaluate(toLight);
                    if (density <= 0)
                        return;
                    total += value / density;
                    sum += toLight * (value / density);
                }
            );
            magnitude = total / (SamplesPerAxis * SamplesPerAxis);
            meanDirection = new Double3(sum.X, 0, sum.Z).Normalized();
        }

        /// <summary>The cosine of <paramref name="shape"/>, turned so that its z axis is the lobe's mean direction.</summary>
        public Cosine Cosine((double M11, double M22, double M13) shape)
        {
            // Head on the mean direction is the normal itself; the turn keeps the frame right-handed.
            Double3 z = meanDirection;
            var x = new Double3(z.Z, 0, -z.X);
            return new(x: x, z: z, m11: shape.M11, m22: shape.M22, m13: shape.M13);
        }

        /// <summary>
        /// The cubed difference between the lobe and <paramref name="cosine"/> times the magnitude, over the sum of their
        /// sampling densities: the estimate of both samplings together.
        /// </summary>
        public double Error(Cosine cosine)
        {
            double error = 0;
            ForEachSample(
                (u1, u2) =>
                {
                    error += ErrorAt(cosine, cosine.Sample(u1, u2));
                    error += ErrorAt(cosine, SampleLobe(u1, u2));
                }
            );
            return error / (SamplesPerAxis * SamplesPerAxis);
        }

        private double ErrorAt(Cosine cosine, Double3 toLight)
        {
            (double value, double density) = Evaluate(toLight);
            double fitted = cosine.Density(toLight);
            double difference = Math.Abs(value - magnitude * fitted);
            double densities = density + fitted;
            // A direction that neither distribution reaches adds no error.
            return densities > 0 ? difference * difference * difference / densities : 0;
        }

        /// <summary>f cos with F = 1 toward <paramref name="toLight"/>, and the density of <see cref="SampleLobe"/> there.</summary>
        private (double Value, double Density) Evaluate(Double3 toLight)
        {
            if (toLight.Z <= 0)
                return (0, 0);
            Double3 half = (toView + toLight).Normalized();
            double distribution = lobe.Distribution(half.Z);
            double value = distribution * lobe.Visibility(nDotL: toLight.Z, nDotV: nDotV) * toLight.Z;
            double density = distribution * half.Z / (4 * Double3.Dot(toView, half));
            return (value, density);
        }

        /// <summary>The direction that the half vector of (<paramref name="u1"/>, <paramref name="u2"/>) reflects the view to.</summary>
        private Double3 SampleLobe(double u1, double u2)
        {
            Double3 half = lobe.SampleHalfVector(u1: u1, u2: u2);
            return 2 * Double3.Dot(toView, half) * half - toView;
        }

        /// <summary>The stratified centers of the unit square, in the order of the rows, so each sum is the same bits.</summary>
        private static void ForEachSample(Action<double, double> body)
        {
            for (int j = 0; j < SamplesPerAxis; j++)
            for (int i = 0; i < SamplesPerAxis; i++)
                body((i + 0.5) / SamplesPerAxis, (j + 0.5) / SamplesPerAxis);
        }
    }

    /// <summary>
    /// A clamped cosine D_o mapped by M = [x y z] · [[m11, 0, m13], [0, m22, 0], [0, 0, 1]], with x and z in the plane of the
    /// view and the normal. M keeps y, so it is a 2 × 2 map of (x, z) and a scale of y.
    /// </summary>
    private readonly struct Cosine
    {
        // M on (x, z): [[a, b], [c, d]].
        private readonly double a,
            b,
            c,
            d,
            yScale;

        public Cosine(Double3 x, Double3 z, double m11, double m22, double m13)
        {
            a = x.X * m11;
            b = x.X * m13 + z.X;
            c = x.Z * m11;
            d = x.Z * m13 + z.Z;
            yScale = m22;
        }

        private double Determinant => (a * d - b * c) * yScale;

        /// <summary>M⁻¹ scaled so that its middle term is 1, which leaves the directions that it gives unchanged.</summary>
        public LtcMatrix Inverse
        {
            get
            {
                double planar = a * d - b * c;
                double scale = yScale / planar;
                return new(M00: d * scale, M02: -b * scale, M20: -c * scale, M22: a * scale);
            }
        }

        /// <summary>The density D(ω) of the mapped cosine, per sr, which integrates to 1 over the sphere.</summary>
        public double Density(Double3 direction)
        {
            double planar = a * d - b * c;
            var original = new Double3(
                (d * direction.X - b * direction.Z) / planar,
                direction.Y / yScale,
                (-c * direction.X + a * direction.Z) / planar
            );
            double length = original.Length;
            if (original.Z <= 0)
                return 0;
            // D_o at the unit original direction, times the Jacobian |M⁻¹| / |M⁻¹ ω|³ of the map back.
            return original.Z / length / Math.PI / (Math.Abs(Determinant) * length * length * length);
        }

        /// <summary>The unit direction that the cosine-distributed point (<paramref name="u1"/>, <paramref name="u2"/>) maps to.</summary>
        public Double3 Sample(double u1, double u2)
        {
            double sine = Math.Sqrt(1 - u1),
                cosine = Math.Sqrt(u1);
            double azimuth = 2 * Math.PI * u2;
            double x = sine * Math.Cos(azimuth),
                y = sine * Math.Sin(azimuth);
            return new Double3(a * x + b * cosine, yScale * y, c * x + d * cosine).Normalized();
        }
    }
}
