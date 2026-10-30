using System.Globalization;
using System.Text.Json.Serialization;

namespace Tertius.Slop;

/// <summary>Raw counts retain their ratio only when applicability permits it and the denominator is nonzero.</summary>
public sealed record ClassRatio(
    long Numerator,
    long Denominator,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MetricApplicability? Applicability = null
)
{
    public double? Ratio =>
        Denominator > 0 && (Applicability is null || Applicability.Status == "complete")
            ? (double)Numerator / Denominator
            : null;
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

public sealed record StateMethod(
    ClassMember Member,
    IReadOnlyList<string> Fields,
    bool Visible,
    IReadOnlyList<UnresolvedStateAccess>? UnresolvedAccesses = null
);

public sealed record UnresolvedStateAccess(SourcePlace Location, string Reason, int Column, int EndColumn);

/// <summary>Source-structural applicability, separate from the retained raw counts.</summary>
public sealed record MetricApplicability(string Status, string? Reason = null);

public sealed record MetricPopulation(int Included, int NotApplicable, int Incomplete);

public sealed record SharedStatePair(string First, string Second, IReadOnlyList<string> Fields);

public sealed record BindingIssue(string Id, string Message, SourcePlace? Location);

/// <summary>Declared surface, existing function weight and normalized instance-storage evidence for one source type.</summary>
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
    public ClassRatio Depth => new(Wmc, ApiEntries, DepthApplicability);
    public MetricApplicability? DepthApplicability { get; init; }
    public required ClassRatio Tcc { get; init; }
    public MetricApplicability? TccApplicability => Tcc.Applicability;
    public IReadOnlyList<UnresolvedStateAccess>? UnresolvedAccesses { get; init; }

    internal static MetricApplicability ImplementationApplicability(int functionCount, int apiEntries)
    {
        if (functionCount == 0)
            return new("not-applicable", "no-handwritten-functions");
        if (apiEntries == 0)
            return new("not-applicable", "no-api-entries");
        return new("complete");
    }
}

/// <summary>Counting version and actual metadata inputs, for comparisons across snapshots.</summary>
public sealed record ClassMetricProvenance(string Version, string References);

public sealed record ClassMetricSummary(
    ClassMetricProvenance Provenance,
    ClassRatio Tcc,
    int TypeCount,
    int BindingErrorCount,
    ClassRatio? Depth = null,
    MetricPopulation? CohesionPopulation = null,
    MetricPopulation? DepthPopulation = null,
    int? UnresolvedStateAccessCount = null
)
{
    internal string TccText => CohesionPopulation is { Included: 0, Incomplete: > 0 } ? "incomplete" : Show(Tcc);

    public string DescribeAgainst(ClassMetricSummary? before)
    {
        bool comparable = before?.Provenance == Provenance;
        var lines = new List<string>
        {
            DescribeCount(
                name: "TCC",
                count: Tcc,
                before: comparable ? before?.Tcc : null,
                units: "shared/possible method pairs",
                numerator: "shared pairs",
                denominator: "possible pairs",
                display: TccText
            ),
        };
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
        if (CohesionPopulation is { } population)
        {
            lines.Add(
                $"Cohesion types: {population.Included} included, {population.NotApplicable} not applicable, {population.Incomplete} incomplete."
            );
            if (population.Incomplete > 0)
                lines.Add("Cohesion excludes incomplete evidence; known counts remain in type details.");
        }
        else
            lines.Add("Cohesion applicability unavailable (legacy note).");
        if (DepthPopulation is { } depthPopulation)
            lines.Add(
                $"Depth types: {depthPopulation.Included} included, {depthPopulation.NotApplicable} not applicable; declaration-only API entries excluded."
            );
        else
            lines.Add("Depth applicability unavailable (legacy note).");
        if (UnresolvedStateAccessCount is > 0)
            lines.Add($"Unresolved state access locations: {UnresolvedStateAccessCount} (see details).");
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
        string denominator,
        string? display = null
    )
    {
        string line = $"{name} {display ?? Show(count)}";
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
        ClassMeasure[] included = [.. types.Where(type => type.TccApplicability?.Status == "complete")];
        int incomplete = types.Count(type => type.TccApplicability?.Status == "incomplete");
        ClassMeasure[] implementation = [.. types.Where(type => type.Functions.Count > 0)];
        return new(
            new(
                Provenance: analysis.Provenance,
                Tcc: new(included.Sum(type => type.Tcc.Numerator), included.Sum(type => type.Tcc.Denominator)),
                TypeCount: types.Length,
                BindingErrorCount: diagnostics.Length,
                Depth: new(
                    implementation.Sum(type => (long)type.Wmc),
                    implementation.Sum(type => (long)type.ApiEntries)
                ),
                CohesionPopulation: new(included.Length, types.Length - included.Length - incomplete, incomplete),
                DepthPopulation: new(
                    implementation.Count(type => type.ApiEntries > 0),
                    types.Count(type => type.DepthApplicability?.Status == "not-applicable"),
                    0
                ),
                UnresolvedStateAccessCount: types.SelectMany(type => type.UnresolvedAccesses ?? []).Distinct().Count()
            ),
            types,
            diagnostics
        );
    }
}
