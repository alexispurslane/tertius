namespace Tertius.Slop;

/// <summary>
/// One place that a verbosity rule flags (docs/plans/code-quality-tooling.md, decision 17): the lines of the node that
/// the rule's rewrite would replace or delete.
/// </summary>
public readonly record struct Finding
{
    /// <summary>The id of the rule, such as <c>manual-min-max</c>.</summary>
    public required string Rule { get; init; }

    /// <summary>The path of the file, relative to the root of the repository, with <c>/</c> between folders.</summary>
    public required string File { get; init; }

    /// <summary>The first line that the finding covers, from 1.</summary>
    public required int Line { get; init; }

    /// <summary>The last line that the finding covers, from 1. The same as <see cref="Line"/> for a finding on one line.</summary>
    public required int EndLine { get; init; }
}
