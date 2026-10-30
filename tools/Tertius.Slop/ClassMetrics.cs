using System.Globalization;

namespace Tertius.Slop;

/// <summary>A pooled count. An empty denominator has no applicable ratio.</summary>
public sealed record ClassRatio(long Numerator, long Denominator)
{
    public double? Ratio => Denominator > 0 ? (double)Numerator / Denominator : null;
}

public sealed record SourcePlace(string File, int Line, int EndLine);

public sealed record ClassMember(
    string Name,
    SourcePlace Location,
    int ParameterCount,
    string Visibility,
    string Kind,
    bool IsStatic,
    bool IsApiEntry
);

public sealed record StateMethod(ClassMember Member, IReadOnlyList<string> Fields, bool Visible);

public sealed record SharedStatePair(string First, string Second, IReadOnlyList<string> Fields);

public sealed record BindingIssue(string Id, string Message, SourcePlace? Location);

/// <summary>Declared surface, existing function weight and direct shared-state evidence for one source type.</summary>
public sealed record ClassMeasure
{
    public required string Name { get; init; }
    public required string Identity { get; init; }
    public required string Kind { get; init; }
    public required bool IsRecord { get; init; }
    public required bool IsStatic { get; init; }
    public required IReadOnlyList<SourcePlace> Locations { get; init; }
    public required IReadOnlyList<ClassMember> Members { get; init; }
    public required IReadOnlyList<FunctionMeasure> Functions { get; init; }
    public int Wmc => Functions.Sum(function => function.Complexity);
    public required IReadOnlyList<StateMethod> Methods { get; init; }
    public required IReadOnlyList<SharedStatePair> SharedPairs { get; init; }
    public int ApiEntries => Members.Count(member => member.IsApiEntry);
    public ClassRatio Depth => new(Wmc, ApiEntries);
    public required ClassRatio Tcc { get; init; }
}

/// <summary>Counting version and actual metadata inputs, for comparisons across snapshots.</summary>
public sealed record ClassMetricProvenance(string Version, string References);

public sealed record ClassMetricSummary(
    ClassMetricProvenance Provenance,
    ClassRatio Tcc,
    int TypeCount,
    int BindingErrorCount,
    ClassRatio? Depth = null
)
{
    public string DescribeAgainst(ClassMetricSummary? before)
    {
        bool comparable = before?.Provenance == Provenance;
        var lines = new List<string>();
        lines.Add(
            DescribeCount(
                name: "TCC",
                count: Tcc,
                before: comparable ? before?.Tcc : null,
                units: "shared/possible method pairs",
                numerator: "shared pairs",
                denominator: "possible pairs"
            )
        );
        if (Depth is { } depth)
            lines.Add(
                DescribeCount(
                    name: "Depth",
                    count: depth,
                    before: comparable ? before?.Depth : null,
                    units: "WMC/API entries",
                    numerator: "WMC",
                    denominator: "API entries"
                )
            );
        else
            lines.Add("Depth unavailable (legacy note)");
        lines.Add($"Type metrics {Provenance.Version}");
        if (BindingErrorCount > 0)
            lines.Add($"Binding errors: {BindingErrorCount}; counts use available symbols (see details).");
        if (before is not null && !comparable)
            lines.Add("No type metric deltas: configuration differs.");
        return string.Join('\n', lines);
    }

    private static string DescribeCount(
        string name,
        ClassRatio count,
        ClassRatio? before,
        string units,
        string numerator,
        string denominator
    )
    {
        string line = $"{name} {Show(count)}";
        if (count.Ratio is { } ratio && before?.Ratio is { } oldRatio)
            line += string.Create(CultureInfo.InvariantCulture, $" ({ratio - oldRatio:+0.0000;-0.0000;+0.0000})");
        line += $" ({count.Numerator}/{count.Denominator} {units}";
        if (before is not null)
            line +=
                $"; {numerator} {count.Numerator - before.Numerator:+0;-0;+0}, {denominator} {count.Denominator - before.Denominator:+0;-0;+0}";
        return line + ")";
    }

    public static string Show(ClassRatio count) =>
        count.Ratio is { } ratio ? ratio.ToString("F4", CultureInfo.InvariantCulture) : "n/a";
}

/// <summary>One source-symbol analysis supplies pooled TCC, depth and their constituent evidence.</summary>
public sealed record ClassMetrics(
    ClassMetricSummary Summary,
    IReadOnlyList<ClassMeasure> Types,
    IReadOnlyList<BindingIssue> BindingDiagnostics
)
{
    public static ClassMetrics Of(Codebase codebase, ClassMetricOptions? options = null)
    {
        var analysis = new ClassAnalysis(codebase, options ?? new());
        ClassMeasure[] types = analysis.Measure();
        BindingIssue[] diagnostics = analysis.BindingDiagnostics();
        return new(
            new(
                Provenance: analysis.Provenance,
                Tcc: new(types.Sum(type => type.Tcc.Numerator), types.Sum(type => type.Tcc.Denominator)),
                TypeCount: types.Length,
                BindingErrorCount: diagnostics.Length,
                Depth: new(types.Sum(type => (long)type.Wmc), types.Sum(type => (long)type.ApiEntries))
            ),
            types,
            diagnostics
        );
    }
}
