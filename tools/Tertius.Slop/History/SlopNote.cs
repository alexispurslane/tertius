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

    /// <summary>The lines of code of the tree's <c>src</c>.</summary>
    public required int Sloc { get; init; }

    /// <summary>The branch that the commit was on when the hook measured it, or empty for a detached head.</summary>
    public required string Branch { get; init; }

    /// <summary>The hook or the command that measured the tree.</summary>
    public required NoteSource Source { get; init; }

    /// <summary>When the tree was measured.</summary>
    public required DateTimeOffset Measured { get; init; }

    /// <summary>The note of a tree whose <paramref name="report"/> was measured at <paramref name="measured"/>.</summary>
    public static SlopNote Of(SlopReport report, string branch, NoteSource source, DateTimeOffset measured) => new()
    {
        Erosion = report.Erosion.Erosion,
        Verbosity = report.Verbosity.Verbosity,
        Sloc = report.Erosion.Sloc,
        Branch = branch,
        Source = source,
        Measured = measured
    };

    /// <summary>
    /// The note in the text of a git note: its first line that holds a note, or null. Two clones that both noted a
    /// commit merge their notes into two lines (decision 18), and the first line wins.
    /// </summary>
    public static SlopNote? Parse(string text) =>
        text.Split('\n').Select(ParseLine).FirstOrDefault(note => note is not null);

    /// <summary>The source that <paramref name="name"/> names as a note does, such as <c>pre-commit</c>, or null.</summary>
    public static NoteSource? SourceNamed(string name)
    {
        try
        {
            return JsonSerializer.Deserialize(JsonSerializer.Serialize(name, NoteJson.Default.String),
                NoteJson.Default.NoteSource);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>The note as one line of JSON, the text of the git note.</summary>
    public string ToLine() => JsonSerializer.Serialize(this, NoteJson.Default.SlopNote);

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
    [JsonStringEnumMemberName("pre-push")] PrePush,

    /// <summary>A note made by hand for a commit from before the hooks.</summary>
    [JsonStringEnumMemberName("seed")] Seed
}

/// <summary>The JSON form of a note: one line, camel-case names, the source by name.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(SlopNote))]
[JsonSerializable(typeof(NoteSource))]
[JsonSerializable(typeof(string))]
internal sealed partial class NoteJson : JsonSerializerContext;
