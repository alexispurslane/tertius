using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tertius.Slop.History;

/// <summary>
/// The slop metrics of one commit, as a git note under <c>refs/notes/slop</c> (docs/plans/code-quality-tooling.md,
/// decision 18): the measures of the commit's tree, and the branch, the hook and the time of the measurement. The note
/// holds only what git cannot tell later. The tickets of the commit are in its message, so <see cref="Commit"/> reads
/// them from there.
/// </summary>
public sealed record SlopNote
{
    /// <summary>The erosion of the tree's <c>src</c> (decision 16).</summary>
    public required double Erosion { get; init; }

    /// <summary>The verbosity of the tree's <c>src</c> (decision 17).</summary>
    public required double Verbosity { get; init; }

    /// <summary>The mass of functions with CC above 10, or null in notes from before this measure.</summary>
    public double? HighCcMass { get; init; }

    /// <summary>The number of functions with CC above 10, or null in older notes.</summary>
    public int? HighCcFunctionCount { get; init; }

    /// <summary>The number of flagged or cloned lines, or null in older notes.</summary>
    public int? VerboseLines { get; init; }

    public ClassMetricSummary? ClassMetrics { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; init; }

    /// <summary>The lines of code of the tree's <c>src</c>.</summary>
    public required int Sloc { get; init; }

    /// <summary>The branch that the commit was on when the hook measured it, or empty for a detached head.</summary>
    public required string Branch { get; init; }

    /// <summary>The hook or the command that measured the tree.</summary>
    public required NoteSource Source { get; init; }

    /// <summary>When the tree was measured.</summary>
    public required DateTimeOffset Measured { get; init; }

    /// <summary>The note of a tree whose <paramref name="report"/> was measured at <paramref name="measured"/>.</summary>
    public static SlopNote Of(SlopReport report, string branch, NoteSource source, DateTimeOffset measured) =>
        new SlopNote()
        {
            Erosion = report.Erosion.Erosion,
            Verbosity = report.Verbosity.Verbosity,
            Sloc = report.Erosion.Sloc,
            Branch = branch,
            Source = source,
            Measured = measured,
        }.WithCounts(report);

    /// <summary>
    /// The note in the text of a git note: the first line with absolute counts, or the first readable line, or null.
    /// Two clones can merge their notes into several lines (decisions 18 and 21).
    /// </summary>
    public static SlopNote? Parse(string text)
    {
        SlopNote?[] notes = [.. text.Split('\n').Select(ParseLine)];
        return notes.OfType<SlopNote>().OrderByDescending(note => note.Completeness).FirstOrDefault();
    }

    /// <summary>Whether every absolute count is present.</summary>
    [JsonIgnore]
    public bool HasAbsoluteCounts =>
        HighCcMass is not null && HighCcFunctionCount is not null && VerboseLines is not null;

    [JsonIgnore]
    public bool HasAllCounts =>
        HasAbsoluteCounts
        && ClassMetrics is { Depth: not null } metrics
        && (
            metrics.Provenance.Version != "type-metrics-v3"
            || metrics
                is { CohesionPopulation: not null, DepthPopulation: not null, UnresolvedStateAccessCount: not null }
        );

    private int Completeness =>
        (HighCcMass is not null ? 1 : 0)
        + (HighCcFunctionCount is not null ? 1 : 0)
        + (VerboseLines is not null ? 1 : 0)
        + (ClassMetrics is not null ? 4 : 0)
        + (ClassMetrics?.Depth is not null ? 1 : 0)
        + (ClassMetrics?.CohesionPopulation is not null ? 1 : 0)
        + (ClassMetrics?.DepthPopulation is not null ? 1 : 0)
        + (ClassMetrics?.UnresolvedStateAccessCount is not null ? 1 : 0);

    /// <summary>Whether any note line still lacks an absolute count.</summary>
    public static bool NeedsBackfill(string text) =>
        text.Split('\n').Select(ParseLine).Any(note => note is { HasAllCounts: false });

    /// <summary>Fills missing absolute counts in every note line and keeps any other line.</summary>
    public static string BackfillText(string text, SlopReport report) =>
        string.Join(
            '\n',
            text.Split('\n')
                .Select(line =>
                {
                    if (ParseLine(line) is not { HasAllCounts: false } note)
                        return line;
                    SlopNote enriched = note.WithCounts(report);
                    // Preserve the original text, including retired nested fields, when no count can be added.
                    return enriched == note ? line : enriched.ToLine();
                })
        );

    /// <summary>The source that <paramref name="name"/> names as a note does, such as <c>pre-commit</c>, or null.</summary>
    public static NoteSource? SourceNamed(string name)
    {
        try
        {
            return JsonSerializer.Deserialize(
                JsonSerializer.Serialize(name, NoteJson.Default.String),
                NoteJson.Default.NoteSource
            );
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The note as one line of JSON, the text of the git note.</summary>
    public string ToLine() => JsonSerializer.Serialize(this, NoteJson.Default.SlopNote);

    private SlopNote WithCounts(SlopReport report) =>
        this with
        {
            HighCcMass = HighCcMass ?? report.Erosion.HighCcMass,
            HighCcFunctionCount = HighCcFunctionCount ?? report.Erosion.HighCcFunctionCount,
            VerboseLines = VerboseLines ?? report.Verbosity.VerboseLines,
            ClassMetrics = EnrichClassMetrics(report.ClassMetrics?.Summary),
        };

    private ClassMetricSummary? EnrichClassMetrics(ClassMetricSummary? measured)
    {
        if (ClassMetrics is null)
            return measured;
        if (measured?.Provenance == ClassMetrics.Provenance)
            return ClassMetrics with
            {
                Depth = ClassMetrics.Depth ?? measured.Depth,
                CohesionPopulation = ClassMetrics.CohesionPopulation ?? measured.CohesionPopulation,
                DepthPopulation = ClassMetrics.DepthPopulation ?? measured.DepthPopulation,
                UnresolvedStateAccessCount =
                    ClassMetrics.UnresolvedStateAccessCount ?? measured.UnresolvedStateAccessCount,
            };
        return ClassMetrics;
    }

    private static SlopNote? ParseLine(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, NoteJson.Default.SlopNote);
        }
        catch (JsonException)
        {
            // A line that another tool wrote, or that misses a measure, holds no note.
            return null;
        }
    }
}

/// <summary>What measured the tree of a <see cref="SlopNote"/>.</summary>
public enum NoteSource
{
    /// <summary>The pre-commit hook, which measures the staged tree.</summary>
    [JsonStringEnumMemberName("pre-commit")]
    PreCommit,

    /// <summary>The pre-push hook, which notes the pushed commits that have no note.</summary>
    [JsonStringEnumMemberName("pre-push")]
    PrePush,

    /// <summary>A note made by hand for a commit from before the hooks.</summary>
    [JsonStringEnumMemberName("seed")]
    Seed,
}

/// <summary>The JSON form of a note: one line, camel-case names, the source by name.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(SlopNote))]
[JsonSerializable(typeof(NoteSource))]
[JsonSerializable(typeof(string))]
internal sealed partial class NoteJson : JsonSerializerContext;
