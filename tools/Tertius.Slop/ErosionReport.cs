using System.Text;

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

    /// <summary>The erosion: <see cref="HighCcMass"/> divided by <see cref="TotalMass"/>, from 0 to 1. 0 for no functions.</summary>
    public required double Erosion { get; init; }

    /// <summary>The sum of the mass of every function.</summary>
    public required double TotalMass { get; init; }

    /// <summary>The sum of the mass of the functions with a complexity above <see cref="ComplexityLimit"/>.</summary>
    public required double HighCcMass { get; init; }

    /// <summary>The number of functions.</summary>
    public required int FunctionCount { get; init; }

    /// <summary>The number of functions with a complexity above <see cref="ComplexityLimit"/>.</summary>
    public required int HighCcFunctionCount { get; init; }

    /// <summary>The number of lines that hold code in every file, inside functions or not.</summary>
    public required int Sloc { get; init; }

    /// <summary>The number of files.</summary>
    public required int FileCount { get; init; }

    /// <summary>
    /// The <see cref="TopCount"/> functions of the greatest mass, from the greatest down. Functions of equal mass are
    /// ordered by file, then by line, so that the report is the same on every run.
    /// </summary>
    public required IReadOnlyList<FunctionMeasure> TopFunctions { get; init; }

    /// <summary>The report of the <paramref name="functions"/> of a codebase of <paramref name="fileCount"/> files.</summary>
    /// <param name="sloc">The number of lines that hold code in every file, inside functions or not.</param>
    public static ErosionReport Of(IReadOnlyList<FunctionMeasure> functions, int sloc, int fileCount)
    {
        FunctionMeasure[] highCc = [.. functions.Where(function => function.Complexity > ComplexityLimit)];
        double totalMass = functions.Sum(function => function.Mass);
        double highCcMass = highCc.Sum(function => function.Mass);
        return new()
        {
            Erosion = totalMass > 0 ? highCcMass / totalMass : 0,
            TotalMass = totalMass,
            HighCcMass = highCcMass,
            FunctionCount = functions.Count,
            HighCcFunctionCount = highCc.Length,
            Sloc = sloc,
            FileCount = fileCount,
            TopFunctions =
            [
                .. functions
                    .OrderByDescending(function => function.Mass)
                    .ThenBy(function => function.File, StringComparer.Ordinal)
                    .ThenBy(function => function.Line)
                    .Take(TopCount),
            ],
        };
    }

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
