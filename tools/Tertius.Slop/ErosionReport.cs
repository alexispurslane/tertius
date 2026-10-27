using System.Text;
using System.Text.Json.Serialization;

namespace Tertius.Slop;

/// <summary>
/// The erosion of a codebase (docs/plans/code-quality-tooling.md, decision 16), after
/// https://earendil.com/posts/measuring-code-sloppiness/: the share of the mass of all functions that sits in functions
/// with a cyclomatic complexity above <see cref="ComplexityLimit"/>. The mass of a function is its complexity times the
/// square root of its lines of code (<see cref="FunctionMeasure.Mass"/>).
/// </summary>
public sealed record ErosionReport
{
    /// <summary>A function erodes when its cyclomatic complexity is strictly greater than this.</summary>
    public const int ComplexityLimit = 10;

    /// <summary>The number of functions in <see cref="TopFunctions"/>.</summary>
    public const int TopCount = 10;

    /// <summary><see cref="ComplexityLimit"/>, for the tools that read the JSON.</summary>
    [JsonPropertyName("complexityLimit")]
    public int Limit => ComplexityLimit;

    /// <summary>The erosion: <see cref="HighCcMass"/> divided by <see cref="TotalMass"/>, from 0 to 1. 0 for no functions.</summary>
    public double Erosion => TotalMass > 0 ? HighCcMass / TotalMass : 0;

    /// <summary>The sum of the mass of every function.</summary>
    public required double TotalMass { get; init; }

    /// <summary>The sum of the mass of <see cref="HighCcFunctions"/>.</summary>
    public double HighCcMass => HighCcFunctions.Sum(function => function.Mass);

    /// <summary>The number of functions.</summary>
    public required int FunctionCount { get; init; }

    /// <summary>The number of <see cref="HighCcFunctions"/>.</summary>
    public int HighCcFunctionCount => HighCcFunctions.Count;

    /// <summary>The number of lines that hold code in every file, inside functions or not.</summary>
    public required int Sloc { get; init; }

    /// <summary>The number of files.</summary>
    public required int FileCount { get; init; }

    /// <summary>
    /// The <see cref="TopCount"/> functions of the greatest mass, from the greatest down. Functions of equal mass are
    /// ordered by file, then by line, so that the report is the same on every run.
    /// </summary>
    public required IReadOnlyList<FunctionMeasure> TopFunctions { get; init; }

    /// <summary>
    /// Every function with a complexity above <see cref="ComplexityLimit"/>, in the order of
    /// <see cref="TopFunctions"/>. The commit report names the ones that a commit changes.
    /// </summary>
    public required IReadOnlyList<FunctionMeasure> HighCcFunctions { get; init; }

    /// <summary>The report of the <paramref name="functions"/> of a codebase of <paramref name="fileCount"/> files.</summary>
    /// <param name="sloc">The number of lines that hold code in every file, inside functions or not.</param>
    public static ErosionReport Of(IReadOnlyList<FunctionMeasure> functions, int sloc, int fileCount) =>
        new()
        {
            TotalMass = functions.Sum(function => function.Mass),
            FunctionCount = functions.Count,
            Sloc = sloc,
            FileCount = fileCount,
            TopFunctions = [.. HeaviestFirst(functions).Take(TopCount)],
            HighCcFunctions = [.. HeaviestFirst(functions.Where(function => function.Complexity > ComplexityLimit))],
        };

    /// <summary>The report of every function in <paramref name="codebase"/>, measured in parallel.</summary>
    public static ErosionReport Of(Codebase codebase) =>
        Of(
            [
                .. codebase
                    .Files.AsParallel()
                    .AsOrdered()
                    .SelectMany(file => file.Functions.Select(function => function.Measure(file.Path))),
            ],
            sloc: codebase.Files.AsParallel().Sum(file => file.Sloc),
            fileCount: codebase.Files.Count
        );

    // Functions of equal mass are ordered by file, then by line, so that the report is the same on every run.
    private static IEnumerable<FunctionMeasure> HeaviestFirst(IEnumerable<FunctionMeasure> functions) =>
        functions
            .OrderByDescending(function => function.Mass)
            .ThenBy(function => function.File, StringComparer.Ordinal)
            .ThenBy(function => function.Line);

    /// <summary>The report as a few lines of text for a person.</summary>
    public string Describe()
    {
        var text = new StringBuilder();
        text.AppendLine(
            $"Erosion {Erosion:F3}: mass {HighCcMass:F1} of {TotalMass:F1} is in the {HighCcFunctionCount} of "
                + $"{FunctionCount} functions with CC above {ComplexityLimit}."
        );
        text.AppendLine($"{FileCount} files, {Sloc} lines of code.");
        text.AppendLine("Heaviest functions:");
        text.AppendLine($"{"mass", 8} {"CC", 4} {"SLOC", 5}  name (file:line)");
        foreach (FunctionMeasure function in TopFunctions)
            text.AppendLine(
                $"{function.Mass, 8:F1} {function.Complexity, 4} {function.Sloc, 5}  {function.Name} ({function.File}:{function.Line})"
            );
        return text.ToString();
    }
}
