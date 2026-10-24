using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace Tertius.Slop;

/// <summary>
/// The lines of src that jscpd finds in a clone (docs/plans/code-quality-tooling.md, decision 17). jscpd runs with the
/// settings of the commit report, tools/commit_report.py, so that both name the same clones. A clone line is a line of
/// either copy of a clone.
/// </summary>
public sealed class Clones(IReadOnlyDictionary<string, IReadOnlySet<int>> linesByFile)
{
    // The two copies of a clone in a jscpd report.
    private static readonly string[] Sides = ["firstFile", "secondFile"];

    /// <summary>The clone lines in a jscpd JSON report of the src folder, whose file names are relative to src.</summary>
    public static Clones FromReport(string json)
    {
        using var report = JsonDocument.Parse(json);
        IEnumerable<(string File, int Line)> lines =
            from duplicate in report.RootElement.GetProperty("duplicates").EnumerateArray()
            from side in Sides
            let fragment = duplicate.GetProperty(side)
            let start = fragment.GetProperty("start").GetInt32()
            let end = fragment.GetProperty("end").GetInt32()
            from line in Enumerable.Range(start, end - start + 1)
            select ($"src/{fragment.GetProperty("name").GetString()}", line);
        return new(
            lines
                .GroupBy(each => each.File, each => each.Line, StringComparer.Ordinal)
                .ToDictionary(
                    group => group.Key,
                    IReadOnlySet<int> (group) => group.ToHashSet(),
                    StringComparer.Ordinal
                )
        );
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
        linesByFile.TryGetValue(path, out IReadOnlySet<int>? lines) ? lines : new HashSet<int>();

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

/// <summary>
/// jscpd is not on the search path. An exception type of its own lets the command line print the message and fail
/// cleanly; .NET makes an exception type derive from <see cref="Exception"/>.
/// </summary>
public sealed class JscpdMissingException(string message, Exception inner) : Exception(message, inner);
