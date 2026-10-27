using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tertius.Slop;

/// <summary>
/// The clones that jscpd finds in src (docs/plans/code-quality-tooling.md, decision 17), and their lines. A clone line
/// is a line of either copy of a clone. The commit report names the clones that a commit changes.
/// </summary>
/// <param name="all">Every clone, ordered by its first copy, then by its second.</param>
public sealed class Clones(IReadOnlyList<Clone> all)
{
    private readonly Dictionary<string, HashSet<int>> linesByFile = all.SelectMany(clone => clone.Copies)
        .GroupBy(copy => copy.File, StringComparer.Ordinal)
        .ToDictionary(
            file => file.Key,
            file => file.SelectMany(copy => copy.Lines).ToHashSet(),
            StringComparer.Ordinal
        );

    /// <summary>Every clone, ordered by the file and line of its first copy, then of its second.</summary>
    public IReadOnlyList<Clone> All { get; } = all;

    /// <summary>The clones in a jscpd JSON report of the src folder, whose file names are relative to src.</summary>
    public static Clones FromReport(string json)
    {
        using var report = JsonDocument.Parse(json);
        return new([
            .. report
                .RootElement.GetProperty("duplicates")
                .EnumerateArray()
                .Select(duplicate => new Clone(
                    CopyIn(duplicate.GetProperty("firstFile")),
                    CopyIn(duplicate.GetProperty("secondFile"))
                ))
                .OrderBy(clone => clone.First.File, StringComparer.Ordinal)
                .ThenBy(clone => clone.First.Line)
                .ThenBy(clone => clone.Second.File, StringComparer.Ordinal)
                .ThenBy(clone => clone.Second.Line),
        ]);
    }

    /// <summary>
    /// Runs jscpd, the program named <paramref name="jscpd"/> on the search path, on the src folder of the repository at
    /// <paramref name="root"/>. Throws <see cref="JscpdMissingException"/> when the search path has no such program.
    /// </summary>
    public static Clones Find(string root, string jscpd)
    {
        string output = Directory.CreateTempSubdirectory("tertius-slop-jscpd-").FullName;
        try
        {
            var start = new ProcessStartInfo(jscpd)
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList =
                {
                    "src",
                    "--format",
                    "csharp",
                    "--min-tokens",
                    "70",
                    "--mode",
                    "mild",
                    "--reporters",
                    "json",
                    "--output",
                    output,
                    "--silent",
                },
            };
            Run(start);
            return FromReport(File.ReadAllText(Path.Combine(output, "jscpd-report.json")));
        }
        finally
        {
            Directory.Delete(output, recursive: true);
        }
    }

    /// <summary>The lines of the file at <paramref name="path"/> in a clone, from 1. Empty for a file without clones.</summary>
    public IReadOnlySet<int> LinesIn(string path) =>
        linesByFile.TryGetValue(path, out HashSet<int>? lines) ? lines : [];

    // One fragment of a jscpd report: a file relative to src and its first and last lines.
    private static CloneCopy CopyIn(JsonElement fragment) =>
        new()
        {
            File = $"src/{fragment.GetProperty("name").GetString()}",
            Line = fragment.GetProperty("start").GetInt32(),
            EndLine = fragment.GetProperty("end").GetInt32(),
        };

    private static void Run(ProcessStartInfo start)
    {
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException($"{start.FileName} did not start.");
        }
        catch (Win32Exception missing)
        {
            throw new JscpdMissingException(
                $"Tertius.Slop: {start.FileName} is not on the search path. The verbosity needs jscpd, which the dev "
                    + "shell has: run the tool inside nix develop.",
                missing
            );
        }

        using (process)
        {
            // Both streams are read at once, so that a full pipe never blocks jscpd.
            Task<string> errors = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException(
                    $"{start.FileName} failed with exit code {process.ExitCode}: {errors.Result}"
                );
        }
    }
}

/// <summary>A clone that jscpd found: two copies of the same code.</summary>
public sealed record Clone(CloneCopy First, CloneCopy Second)
{
    /// <summary>Both copies, the first one first.</summary>
    [JsonIgnore]
    public IEnumerable<CloneCopy> Copies => [First, Second];
}

/// <summary>One copy of a <see cref="Clone"/>: a file and the lines that the copy covers.</summary>
public readonly record struct CloneCopy
{
    /// <summary>The path of the file, relative to the root of the repository, with <c>/</c> between folders.</summary>
    public required string File { get; init; }

    /// <summary>The first line of the copy, from 1.</summary>
    public required int Line { get; init; }

    /// <summary>The last line of the copy, from 1.</summary>
    public required int EndLine { get; init; }

    /// <summary>Every line of the copy, from <see cref="Line"/> to <see cref="EndLine"/>.</summary>
    [JsonIgnore]
    public IEnumerable<int> Lines => Enumerable.Range(Line, EndLine - Line + 1);
}

/// <summary>
/// jscpd is not on the search path. An exception type of its own lets the command line print the message and fail
/// cleanly; .NET makes an exception type derive from <see cref="Exception"/>.
/// </summary>
public sealed class JscpdMissingException(string message, Exception inner) : Exception(message, inner);
