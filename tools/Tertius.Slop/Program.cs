using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tertius.Slop.History;

namespace Tertius.Slop;

/// <summary>
/// The command line (tools/README.md, "Slop metrics"). <c>Tertius.Slop &lt;metric&gt; [root] [--text]</c> measures
/// <c>src</c>: the metric is <c>erosion</c>, <c>verbosity</c>, or <c>all</c> for both from one parse. It prints JSON
/// for the tools that read it, and text with <c>--text</c>. The commands <c>measure</c>, <c>note</c>,
/// <c>history</c> and <c>summary</c> write and read the history of the metrics in git notes
/// (<see cref="HistoryCommands"/>). <c>-C &lt;folder&gt;</c> first runs the command in that folder, the repository,
/// which is the current folder by default.
/// </summary>
public static class Program
{
    private const string TextFlag = "--text";

    private const string Usage = """
                                 usage: Tertius.Slop [-C folder] erosion|verbosity|all [root] [--text]
                                        Tertius.Slop [-C folder] measure <tree> --branch <name> --source pre-commit|pre-push|seed
                                        Tertius.Slop [-C folder] note <from> <tip> --branch <name> --source pre-commit|pre-push|seed
                                        Tertius.Slop [-C folder] history [git log options and revisions]
                                        Tertius.Slop [-C folder] summary <from> <tip>
                                 """;

    private static readonly string[] Metrics = ["erosion", "verbosity", "all"];

    /// <summary>Runs the command line on the console, with the jscpd of the search path, as the commit report does.</summary>
    public static int Main(string[] args) =>
        Run(args: args, output: Console.Out, error: Console.Error, jscpd: "jscpd");

    /// <summary>
    /// Runs the command line: 0 after the command, 1 when git or jscpd fails, 2 for wrong arguments, a root without a
    /// <c>src</c> folder, or a verbosity without jscpd. <paramref name="jscpd"/> names the jscpd program that the
    /// verbosity runs.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error, string jscpd)
    {
        (string folder, string[] command) = args is ["-C", var given, .. var rest] ? (given, rest) : (".", args);
        var history = new HistoryCommands(repository: new(folder), output: output, jscpd: jscpd);
        try
        {
            switch (command)
            {
                case ["measure", var tree, "--branch", var branch, "--source", var name]
                    when SlopNote.SourceNamed(name) is { } source:
                    history.Measure(tree: tree, branch: branch, source: source);
                    return 0;
                case ["note", var from, var tip, "--branch", var branch, "--source", var name]
                    when SlopNote.SourceNamed(name) is { } source:
                    history.Note(from: from, tip: tip, branch: branch, source: source);
                    return 0;
                case ["history", .. var arguments]:
                    history.History(arguments);
                    return 0;
                case ["summary", var from, var tip]:
                    history.Summary(from, tip);
                    return 0;
                default:
                    return Metric(args: command, root: folder, output: output, error: error, jscpd: jscpd);
            }
        }
        catch (JscpdMissingException missing)
        {
            error.WriteLine(missing.Message);
            return 2;
        }
        catch (InvalidOperationException failed)
        {
            error.WriteLine($"Tertius.Slop: {failed.Message}");
            return 1;
        }
    }

    // A metric of src: the report as JSON, or as text with --text. The root comes after the metric, or it is the
    // folder of -C.
    private static int Metric(string[] args, string root, TextWriter output, TextWriter error, string jscpd)
    {
        bool text = args.Contains(TextFlag);
        string[] positional = [.. args.Where(arg => arg != TextFlag)];
        (string Metric, string Root)? request = positional switch
        {
            [var name] => (name, root),
            [var name, var folder] => (name, folder),
            _ => null
        };
        if (request is not var (metric, folderRoot) || !Metrics.Contains(metric))
        {
            error.WriteLine(Usage);
            return 2;
        }

        if (!Directory.Exists(Path.Combine(folderRoot, "src")))
        {
            error.WriteLine($"Tertius.Slop: no src folder under the root \"{folderRoot}\".");
            return 2;
        }

        output.WriteLine(Report(metric: metric, root: folderRoot, text: text, jscpd: jscpd));
        return 0;
    }

    // The report of one known metric, as text or as JSON.
    private static string Report(string metric, string root, bool text, string jscpd)
    {
        Codebase codebase = Codebase.Under(root);
        return metric switch
        {
            "erosion" when text => ErosionReport.Of(codebase).Describe(),
            "erosion" => JsonSerializer.Serialize(ErosionReport.Of(codebase), SlopJson.Default.ErosionReport),
            "verbosity" when text => VerbosityReport.Of(codebase, Clones.Find(root, jscpd)).Describe(),
            "verbosity" => JsonSerializer.Serialize(VerbosityReport.Of(codebase, Clones.Find(root, jscpd)),
                SlopJson.Default.VerbosityReport),
            "all" when text => SlopReport.Of(codebase, Clones.Find(root, jscpd)).Describe(),
            "all" => JsonSerializer.Serialize(SlopReport.Of(codebase, Clones.Find(root, jscpd)),
                SlopJson.Default.SlopReport),
            _ => throw new UnreachableException($"{metric} is not one of the metrics that Run accepts.")
        };
    }
}

/// <summary>The JSON form of the reports, with camel-case names, written at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ErosionReport))]
[JsonSerializable(typeof(VerbosityReport))]
[JsonSerializable(typeof(SlopReport))]
internal sealed partial class SlopJson : JsonSerializerContext;
