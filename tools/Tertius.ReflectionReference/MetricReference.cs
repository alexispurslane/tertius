namespace Tertius.ReflectionReference;

internal sealed record MetricScalar(double? Value, string[] Reasons);
internal sealed record MetricDistribution(int Count, double? Mean, double? Minimum, double? Maximum, double? SampleStandardDeviation);
internal sealed record MetricRowCoverage(MetricDistribution Values, int InvalidRows, Dictionary<string, int> InvalidReasons, double? EligibleRowReferenceFraction);
internal sealed record MetricRowObservable(string Name, MetricRowCoverage Actual, MetricRowCoverage Change);
internal sealed record MetricRegionResult(
    string Name, RadianceTotals Totals, int ValidRows, int InvalidRows, Dictionary<string, int> InvalidReasons,
    double CoveredReferenceRadiance, double? ReferenceCoverageFraction, double? EligibleRowReferenceFraction
);
internal sealed record MetricComparison(
    MetricRegionResult[] Regions, MetricRowObservable[] RowObservables, Dictionary<string, int> StatusChanges,
    MetricProfile ColumnMean, MetricProfile RowMean
);
internal sealed record MetricPixelTemporal(MetricTemporal Temporal, double MeanSignedError);
internal sealed record MetricRegionTemporal(
    MetricRegionResult MeanImage, MetricTemporal Sum, MetricTemporal Missing, MetricTemporal Excess,
    double? PixelTemporalRms, double? PersistentMeanErrorRms
);
internal sealed record MetricTemporalResult(MetricPixelTemporal[] Pixels, MetricRegionTemporal[] Regions);
internal sealed record MetricBandRow(string Name, int PixelY, string[] Reasons, double ReferenceRadiance, double RowReferenceRadiance, double[] Weights);

internal sealed class MetricReference
{
    internal static readonly string[] ScalarNames = ["left50", "right50", "left10_90", "right10_90", "central5_95"];
    private static readonly string[] LevelNames = ["10", "50", "90"];
    private static readonly string[] RegionNames = ["full", "left_outer", "right_outer", "outer", "left_tail", "right_tail", "tails", "core"];
    private readonly MetricProfileBasis[] rowBases;
    private readonly MetricProfileBasis columnMeanBasis;
    private readonly MetricProfileBasis rowMeanBasis;
    private readonly MetricProfile[] anchorRows;
    private readonly Dictionary<string, double[]> regionWeights = [];
    private readonly Dictionary<string, string[][]> regionReasons = [];
    private readonly double[] rowReferenceSums;
    private readonly double referenceSum;

    public ReferenceCrop Crop { get; }
    public double[] Reference { get; }
    public double[] Coverage { get; }

    public MetricReference(ReferenceCrop crop, double[] reference, double[] coverage)
    {
        MetricMath.Validate(reference);
        ArgumentOutOfRangeException.ThrowIfNotEqual(reference.Length, crop.Width * crop.Height);
        ArgumentOutOfRangeException.ThrowIfNotEqual(coverage.Length, reference.Length);
        Crop = crop;
        Reference = (double[])reference.Clone();
        Coverage = (double[])coverage.Clone();
        referenceSum = reference.Sum();
        rowReferenceSums = [.. Enumerable.Range(0, crop.Height).Select(row => Row(reference, row).Sum())];
        rowBases = [.. Enumerable.Range(0, crop.Height).Select(row => new MetricProfileBasis(Row(reference, row), Row(coverage, row), crop.Left))];
        anchorRows = Rows(reference);
        columnMeanBasis = new(ColumnMean(reference), ColumnMean(coverage), crop.Left);
        rowMeanBasis = new(RowMean(reference), RowMean(coverage), crop.Top);
        foreach (string name in RegionNames)
        {
            var weights = new double[reference.Length];
            var reasons = new string[crop.Height][];
            for (int row = 0; row < crop.Height; row++)
            {
                (MetricInterval[] intervals, string[] invalid) = Band(anchorRows[row], name);
                reasons[row] = invalid;
                foreach (MetricInterval interval in intervals)
                {
                    double[] local = MetricMath.Weights(crop.Width, new(interval.Start - crop.Left, interval.End - crop.Left));
                    for (int column = 0; column < crop.Width; column++)
                        weights[row * crop.Width + column] += local[column];
                }
            }
            regionWeights.Add(name, weights);
            regionReasons.Add(name, reasons);
        }
    }

    public double[] Row(double[] values, int row) => values[(row * Crop.Width)..((row + 1) * Crop.Width)];

    public double[] ColumnMean(double[] values) => [.. Enumerable.Range(0, Crop.Width).Select(column =>
        Enumerable.Range(0, Crop.Height).Average(row => values[row * Crop.Width + column]))];

    public double[] RowMean(double[] values) => [.. Enumerable.Range(0, Crop.Height).Select(row => Row(values, row).Average())];

    public MetricProfile[] Rows(double[] values) => [.. rowBases.Select((basis, row) => basis.Measure(Row(values, row)))];

    public MetricComparison Compare(double[] expected, double[] actual)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(expected.Length, Reference.Length);
        ArgumentOutOfRangeException.ThrowIfNotEqual(actual.Length, Reference.Length);
        MetricProfile[] expectedRows = Rows(expected);
        MetricProfile[] actualRows = Rows(actual);
        var statusChanges = new Dictionary<string, int>();
        for (int row = 0; row < Crop.Height; row++)
            for (int level = 0; level < 3; level++)
            {
                CountStatusChange(expected: expectedRows[row].Levels[level].Left, actual: actualRows[row].Levels[level].Left, name: "left" + LevelNames[level], counts: statusChanges);
                CountStatusChange(expected: expectedRows[row].Levels[level].Right, actual: actualRows[row].Levels[level].Right, name: "right" + LevelNames[level], counts: statusChanges);
            }
        return new(
            Regions: MeasureRegions(expected, actual),
            RowObservables: [.. ScalarNames.Select(name => SummarizeRows(name, expectedRows, actualRows))],
            StatusChanges: statusChanges, ColumnMean: columnMeanBasis.Measure(ColumnMean(actual)), RowMean: rowMeanBasis.Measure(RowMean(actual))
        );
    }

    public MetricRegionResult[] MeasureRegions(double[] expected, double[] actual) => [.. RegionNames.Select(name => Region(name, expected, actual))];

    public IEnumerable<MetricBandRow> BandRows()
    {
        foreach (string name in RegionNames)
            for (int row = 0; row < Crop.Height; row++)
            {
                double[] weights = Row(regionWeights[name], row);
                double[] values = Row(Reference, row);
                yield return new(
                    Name: name, PixelY: Crop.Top + row, Reasons: regionReasons[name][row],
                    ReferenceRadiance: MetricMath.Measure(values, values, weights).ReferenceSum,
                    RowReferenceRadiance: rowReferenceSums[row], Weights: weights
                );
            }
    }

    public MetricTemporalResult Temporal(double[][] frames)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(frames.Length, 32);
        foreach (double[] frame in frames)
        {
            ArgumentOutOfRangeException.ThrowIfNotEqual(frame.Length, Reference.Length);
            MetricMath.Validate(frame);
        }
        MetricPixelTemporal[] pixels = [.. Enumerable.Range(0, Reference.Length).Select(pixel =>
        {
            MetricTemporal temporal = MetricMath.Temporal([.. frames.Select(frame => frame[pixel])]);
            return new MetricPixelTemporal(temporal, temporal.Mean - Reference[pixel]);
        })];
        double[] mean = [.. pixels.Select(pixel => pixel.Temporal.Mean)];
        MetricRegionTemporal[] regions = [.. RegionNames.Select(name =>
        {
            double[] weights = regionWeights[name];
            RadianceTotals[] values = [.. frames.Select(frame => MetricMath.Measure(Reference, frame, weights))];
            double weight = weights.Sum();
            double rms = 0;
            double biasRms = 0;
            if (weight > 0)
                for (int pixel = 0; pixel < pixels.Length; pixel++)
                {
                    rms += pixels[pixel].Temporal.SampleStandardDeviation * pixels[pixel].Temporal.SampleStandardDeviation * weights[pixel] / weight;
                    biasRms += pixels[pixel].MeanSignedError * pixels[pixel].MeanSignedError * weights[pixel] / weight;
                }
            return new MetricRegionTemporal(
                MeanImage: Region(name, Reference, mean), Sum: MetricMath.Temporal([.. values.Select(value => value.CandidateSum)]),
                Missing: MetricMath.Temporal([.. values.Select(value => value.Missing)]), Excess: MetricMath.Temporal([.. values.Select(value => value.Excess)]),
                PixelTemporalRms: weight > 0 ? Math.Sqrt(rms) : null, PersistentMeanErrorRms: weight > 0 ? Math.Sqrt(biasRms) : null
            );
        })];
        return new(pixels, regions);
    }

    public static Dictionary<string, MetricScalar> Scalars(MetricProfile profile) => new()
    {
        ["left50"] = new(profile.Levels[1].Left.Position, profile.Levels[1].Left.Reasons),
        ["right50"] = new(profile.Levels[1].Right.Position, profile.Levels[1].Right.Reasons),
        ["left10_90"] = new(profile.LeftTransition, [.. profile.Levels[0].Left.Reasons.Concat(profile.Levels[2].Left.Reasons).Distinct()]),
        ["right10_90"] = new(profile.RightTransition, [.. profile.Levels[0].Right.Reasons.Concat(profile.Levels[2].Right.Reasons).Distinct()]),
        ["central5_95"] = new(profile.Central90.Width, profile.Central90.Reason == null ? [] : [profile.Central90.Reason]),
    };

    public static MetricDistribution Distribution(double[] values)
    {
        if (values.Length == 0)
            return new(Count: 0, Mean: null, Minimum: null, Maximum: null, SampleStandardDeviation: null);
        double mean = values.Average();
        double? deviation = null;
        if (values.Length > 1)
            deviation = Math.Sqrt(values.Sum(value => (value - mean) * (value - mean)) / (values.Length - 1));
        return new(Count: values.Length, Mean: mean, Minimum: values.Min(), Maximum: values.Max(), SampleStandardDeviation: deviation);
    }

    private MetricRegionResult Region(string name, double[] expected, double[] actual)
    {
        string[][] reasons = regionReasons[name];
        double[] weights = regionWeights[name];
        double covered = MetricMath.Measure(Reference, Reference, weights).ReferenceSum;
        double eligible = Enumerable.Range(0, Crop.Height).Where(row => reasons[row].Length == 0).Sum(row => rowReferenceSums[row]);
        int valid = reasons.Count(row => row.Length == 0);
        return new(
            Name: name, Totals: MetricMath.Measure(expected, actual, weights), ValidRows: valid, InvalidRows: Crop.Height - valid,
            InvalidReasons: Counts(reasons.SelectMany(row => row)), CoveredReferenceRadiance: covered,
            ReferenceCoverageFraction: Fraction(covered), EligibleRowReferenceFraction: Fraction(eligible)
        );
    }

    private MetricRowObservable SummarizeRows(string name, MetricProfile[] expected, MetricProfile[] actual)
    {
        MetricScalar[] before = [.. expected.Select(profile => Scalars(profile)[name])];
        MetricScalar[] after = [.. actual.Select(profile => Scalars(profile)[name])];
        MetricScalar[] changes = [.. before.Select((value, row) => new MetricScalar(
            after[row].Value - value.Value, [.. value.Reasons.Select(reason => "EXPECTED_" + reason).Concat(after[row].Reasons).Distinct()]
        ))];
        return new(name, Summarize(after), Summarize(changes));
    }

    private MetricRowCoverage Summarize(MetricScalar[] values)
    {
        double[] valid = [.. values.Where(value => value.Value.HasValue).Select(value => value.Value.GetValueOrDefault())];
        double eligible = Enumerable.Range(0, Crop.Height).Where(row => values[row].Value.HasValue).Sum(row => rowReferenceSums[row]);
        return new(
            Values: Distribution(valid), InvalidRows: Crop.Height - valid.Length,
            InvalidReasons: Counts(values.Where(value => !value.Value.HasValue).SelectMany(value => value.Reasons)), EligibleRowReferenceFraction: Fraction(eligible)
        );
    }

    private (MetricInterval[] Intervals, string[] Reasons) Band(MetricProfile profile, string name)
    {
        if (name == "full")
            return ([new(Crop.Left, Crop.Left + Crop.Width)], []);
        MetricEdge left10 = profile.Levels[0].Left;
        MetricEdge left50 = profile.Levels[1].Left;
        MetricEdge right10 = profile.Levels[0].Right;
        MetricEdge right50 = profile.Levels[1].Right;
        MetricEdge[] needed = name switch
        {
            "left_outer" => [left10, left50],
            "right_outer" => [right50, right10],
            "outer" => [left10, left50, right50, right10],
            "left_tail" => [left10],
            "right_tail" => [right10],
            "tails" => [left10, right10],
            "core" => [left50, right50],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        string[] reasons = [.. needed.SelectMany(edge => edge.Reasons).Distinct()];
        if (reasons.Length > 0)
            return ([], reasons);
        double lower10 = left10.Position.GetValueOrDefault();
        double lower50 = left50.Position.GetValueOrDefault();
        double upper10 = right10.Position.GetValueOrDefault();
        double upper50 = right50.Position.GetValueOrDefault();
        MetricInterval[] intervals = name switch
        {
            "left_outer" => [new(lower10, lower50)],
            "right_outer" => [new(upper50, upper10)],
            "outer" => [new(lower10, lower50), new(upper50, upper10)],
            "left_tail" => [new(Crop.Left, lower10)],
            "right_tail" => [new(upper10, Crop.Left + Crop.Width)],
            "tails" => [new(Crop.Left, lower10), new(upper10, Crop.Left + Crop.Width)],
            "core" => [new(lower50, upper50)],
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
        if (intervals.Any(interval => interval.End < interval.Start) || (intervals.Length == 2 && intervals[0].End > intervals[1].Start))
            return ([], ["INCONSISTENT_CROSSING_ORDER"]);
        return (intervals, []);
    }

    private double? Fraction(double value) => referenceSum > 0 ? value / referenceSum : null;

    private static Dictionary<string, int> Counts(IEnumerable<string> reasons) => reasons.GroupBy(reason => reason).ToDictionary(group => group.Key, group => group.Count());

    private static void CountStatusChange(MetricEdge expected, MetricEdge actual, string name, Dictionary<string, int> counts)
    {
        if (expected.Reasons.SequenceEqual(actual.Reasons))
            return;
        string key = name + ":" + string.Join("+", expected.Reasons.DefaultIfEmpty("VALID")) + "=>" + string.Join("+", actual.Reasons.DefaultIfEmpty("VALID"));
        counts[key] = counts.GetValueOrDefault(key) + 1;
    }
}
