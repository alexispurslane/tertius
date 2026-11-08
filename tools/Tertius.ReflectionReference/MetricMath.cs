namespace Tertius.ReflectionReference;

internal sealed record RadianceTotals(double ReferenceSum, double CandidateSum, double Missing, double Excess)
{
    public double SignedDifference => CandidateSum - ReferenceSum;
    public double? SignedFraction => ReferenceSum > 0 ? SignedDifference / ReferenceSum : null;
    public double? MissingFraction => ReferenceSum > 0 ? Missing / ReferenceSum : null;
    public double? ExcessFraction => ReferenceSum > 0 ? Excess / ReferenceSum : null;
    public double? RelativeL1 => ReferenceSum > 0 ? (Missing + Excess) / ReferenceSum : null;
    public string? NormalizationReason => ReferenceSum > 0 ? null : "NO_POSITIVE_REFERENCE_LIGHT";
}

internal sealed record MetricRoot(double Position, string Direction, bool Occluded);
internal sealed record MetricInterval(double Start, double End);
internal sealed record MetricRoots(MetricRoot[] Points, MetricInterval[] Plateaus);
internal sealed record MetricEdge(double? Position, string[] Reasons);
internal sealed record MetricLevel(double Height, MetricRoots Roots, MetricEdge Left, MetricEdge Right);
internal sealed record MetricWidth(double? Lower, double? Upper, string? Reason)
{
    public double? Width => Upper - Lower;
}
internal sealed record MetricTemporal(double Mean, double SampleStandardDeviation, double First8Last8Difference);
internal sealed record MetricProfile(MetricLevel[] Levels, MetricWidth Central90, double? LeftTransition, double? RightTransition);

internal static class MetricMath
{
    public static RadianceTotals Measure(double[] reference, double[] candidate, double[] weights)
    {
        Validate(reference);
        Validate(candidate);
        ArgumentOutOfRangeException.ThrowIfNotEqual(candidate.Length, reference.Length);
        ArgumentOutOfRangeException.ThrowIfNotEqual(weights.Length, reference.Length);
        double sum = 0;
        double actualSum = 0;
        double missing = 0;
        double excess = 0;
        for (int pixel = 0; pixel < reference.Length; pixel++)
        {
            double weight = weights[pixel];
            if (!double.IsFinite(weight) || weight < 0 || weight > 1)
                throw new ArgumentOutOfRangeException(nameof(weights));
            double expected = reference[pixel];
            double actual = candidate[pixel];
            sum += expected * weight;
            actualSum += actual * weight;
            missing += Math.Max(0, expected - actual) * weight;
            excess += Math.Max(0, actual - expected) * weight;
        }
        return new(ReferenceSum: sum, CandidateSum: actualSum, Missing: missing, Excess: excess);
    }

    public static MetricRoots Roots(double[] values, double[] coverage, double level)
    {
        var roots = new List<MetricRoot>();
        var plateaus = new List<MetricInterval>();
        for (int index = 0; index < values.Length; index++)
        {
            double first = values[index];
            if (first == level)
            {
                int end = index;
                while (end + 1 < values.Length && values[end + 1] == level)
                    end++;
                if (end > index)
                    plateaus.Add(new(index + 0.5, end + 0.5));
                else
                {
                    string direction = "boundary";
                    if (index > 0 && index + 1 < values.Length)
                    {
                        direction = "touch";
                        if (values[index - 1] < level && values[index + 1] > level)
                            direction = "rising";
                        if (values[index - 1] > level && values[index + 1] < level)
                            direction = "falling";
                    }
                    bool occluded = coverage[Math.Max(0, index - 1)..Math.Min(values.Length, index + 2)].Any(value => value < 1);
                    roots.Add(new(index + 0.5, direction, occluded));
                }
                index = end;
                continue;
            }
            if (index + 1 == values.Length)
                continue;
            double second = values[index + 1];
            if (level <= Math.Min(first, second) || level >= Math.Max(first, second))
                continue;
            roots.Add(new(index + 0.5 + (level - first) / (second - first), second > first ? "rising" : "falling", coverage[index] < 1 || coverage[index + 1] < 1));
        }
        return new([.. roots], [.. plateaus]);
    }

    public static MetricLevel Level(double[] values, double[] coverage, double level, int origin)
    {
        Validate(values);
        ArgumentOutOfRangeException.ThrowIfNotEqual(values.Length, coverage.Length);
        if (coverage.Any(value => !double.IsFinite(value) || value < 0 || value > 1))
            throw new ArgumentOutOfRangeException(nameof(coverage));
        if (!double.IsFinite(level) || level < 0)
            throw new ArgumentOutOfRangeException(nameof(level));
        MetricRoots local = Roots(values, coverage, level);
        var roots = new MetricRoots(
            [.. local.Points.Select(root => root with { Position = root.Position + origin })],
            [.. local.Plateaus.Select(interval => new MetricInterval(interval.Start + origin, interval.End + origin))]
        );
        MetricEdge Side(string direction, bool open)
        {
            MetricRoot[] selected = [.. roots.Points.Where(root => root.Direction == direction)];
            var reasons = new List<string>();
            if (level == 0)
                reasons.Add("NO_CONTRAST");
            if (open)
                reasons.Add("CROP_TRUNCATED");
            if (roots.Plateaus.Length > 0)
                reasons.Add("AMBIGUOUS_PLATEAU");
            if (roots.Points.Any(root => root.Direction is "touch" or "boundary"))
                reasons.Add("UNBRACKETED_LEVEL_TOUCH");
            if (selected.Length > 1)
                reasons.Add("AMBIGUOUS_MULTIPLE_CROSSINGS");
            if (selected.Any(root => root.Occluded))
                reasons.Add("OCCLUSION_AFFECTED");
            if (selected.Length == 0 && !open && level > 0)
                reasons.Add(values.Max() < level ? "MISSING_AT_LEVEL" : "NO_DIRECTED_CROSSING");
            double? position = null;
            if (reasons.Count == 0 && selected.Length == 1)
                position = selected[0].Position;
            return new(position, [.. reasons]);
        }
        return new(Height: level, Roots: roots, Left: Side("rising", values[0] > level), Right: Side("falling", values[^1] > level));
    }

    public static double[] Weights(int length, MetricInterval interval) =>
        [.. Enumerable.Range(0, length).Select(index => Math.Max(0, Math.Min(index + 1, interval.End) - Math.Max(index, interval.Start)))];

    public static MetricWidth Central90(double[] values, int origin)
    {
        Validate(values);
        double total = values.Sum();
        if (total == 0)
            return new(null, null, "NO_POSITIVE_LIGHT");
        double Quantile(double fraction)
        {
            double target = total * fraction;
            double sum = 0;
            for (int index = 0; index < values.Length; index++)
            {
                if (values[index] > 0 && sum + values[index] >= target)
                    return origin + index + (target - sum) / values[index];
                sum += values[index];
            }
            return origin + values.Length;
        }
        return new(Quantile(0.05), Quantile(0.95), null);
    }

    public static MetricTemporal Temporal(double[] values)
    {
        if (values.Length < 16 || values.Any(value => !double.IsFinite(value)))
            throw new ArgumentException("Temporal metrics require at least sixteen finite recorded values.", nameof(values));
        double mean = values.Average();
        double variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1);
        return new(mean, Math.Sqrt(variance), values[^8..].Average() - values[..8].Average());
    }

    public static void Validate(double[] values)
    {
        if (values.Length == 0 || values.Any(value => !double.IsFinite(value) || value < 0))
            throw new ArgumentException("Radiance samples must be nonempty, finite and nonnegative.", nameof(values));
    }
}

internal sealed class MetricProfileBasis
{
    private static readonly double[] Fractions = [0.1, 0.5, 0.9];
    private readonly double amplitude;
    private readonly double[] coverage;
    private readonly int origin;

    public MetricProfileBasis(double[] reference, double[] coverage, int origin)
    {
        MetricMath.Validate(reference);
        ArgumentOutOfRangeException.ThrowIfNotEqual(reference.Length, coverage.Length);
        amplitude = reference.Max();
        this.coverage = (double[])coverage.Clone();
        this.origin = origin;
    }

    public MetricProfile Measure(double[] candidate)
    {
        MetricLevel[] levels = [.. Fractions.Select(fraction => MetricMath.Level(values: candidate, coverage: coverage, level: amplitude * fraction, origin: origin))];
        return new(Levels: levels, Central90: MetricMath.Central90(candidate, origin), LeftTransition: levels[2].Left.Position - levels[0].Left.Position, RightTransition: levels[0].Right.Position - levels[2].Right.Position);
    }
}
