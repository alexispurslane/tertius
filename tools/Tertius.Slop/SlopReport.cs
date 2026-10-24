namespace Tertius.Slop;

/// <summary>Both metrics of one codebase, from one parse of its source.</summary>
public sealed record SlopReport(ErosionReport Erosion, VerbosityReport Verbosity)
{
    /// <summary>The erosion and the verbosity of <paramref name="codebase"/>.</summary>
    public static SlopReport Of(Codebase codebase, Clones clones) =>
        new(ErosionReport.Of(codebase), VerbosityReport.Of(codebase, clones));

    /// <summary>Both reports as text for a person.</summary>
    public string Describe() => $"{Erosion.Describe()}\n{Verbosity.Describe()}";
}
