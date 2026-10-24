using System.Text.Json.Serialization;

namespace Tertius.Slop;

/// <summary>
/// The measures of one function: where it is, its cyclomatic complexity and its source lines of code
/// (docs/plans/code-quality-tooling.md, decision 16).
/// </summary>
public readonly record struct FunctionMeasure
{
    /// <summary>The path of the file, relative to the root of the repository, with <c>/</c> between folders.</summary>
    public required string File { get; init; }

    /// <summary>The line of the function's name, from 1.</summary>
    public required int Line { get; init; }

    /// <summary>The function's name, after the names of the types and functions that hold it, such as <c>World.Step</c>.</summary>
    public required string Name { get; init; }

    /// <summary>The cyclomatic complexity: 1, plus 1 for each branch (<see cref="Function.Complexity"/>).</summary>
    [JsonPropertyName("cc")]
    public required int Complexity { get; init; }

    /// <summary>The number of lines in the function that hold code.</summary>
    public required int Sloc { get; init; }

    /// <summary>The weight of the function in the erosion: its complexity times the square root of its lines of code.</summary>
    public double Mass => Complexity * Math.Sqrt(Sloc);
}
