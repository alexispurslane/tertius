namespace Tertius.Slop;

/// <summary>The metrics of one codebase, from one parse of its source.</summary>
public sealed record SlopReport(ErosionReport Erosion, VerbosityReport Verbosity)
{
    /// <summary>Class statistics and the evidence behind them.</summary>
    public ClassMetrics? ClassMetrics { get; init; }

    public static SlopReport Of(Codebase codebase, Clones clones, ClassMetricOptions? options = null) =>
        new(ErosionReport.Of(codebase), VerbosityReport.Of(codebase, clones))
        {
            ClassMetrics = ClassMetrics.Of(codebase, options),
        };

    /// <summary>The reports as text for a person.</summary>
    public string Describe() =>
        $"{Erosion.Describe()}\n{Verbosity.Describe()}\n{ClassMetrics?.Summary.DescribeAgainst(null)}";
}
