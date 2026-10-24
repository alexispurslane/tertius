using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tertius.Slop;

/// <summary>
/// The command line: <c>Tertius.Slop &lt;metric&gt; [root] [--text]</c> (tools/README.md, "Slop metrics"). The metric
/// is a subcommand: <c>erosion</c>, <c>verbosity</c>, or <c>all</c> for both from one parse of src, which the commit
/// hook runs. It prints JSON for the tools that read it, and text with <c>--text</c>. The root is the repository, the
/// current folder by default.
/// </summary>
public static class Program
{
    private const string TextFlag = "--text";
    private const string Usage = "usage: Tertius.Slop erosion|verbosity|all [root] [--text]";

    private static readonly string[] Metrics = ["erosion", "verbosity", "all"];

    /// <summary>Runs the command line on the console, with the jscpd of the search path, as the commit report does.</summary>
    public static int Main(string[] args) =>
        Run(args: args, output: Console.Out, error: Console.Error, jscpd: "jscpd");

    /// <summary>
    /// Runs the command line: 0 after the report, 2 for wrong arguments, a root without a <c>src</c> folder, or a
    /// verbosity without jscpd. <paramref name="jscpd"/> names the jscpd program that the verbosity runs.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error, string jscpd)
    {
        bool text = args.Contains(TextFlag);
        string[] positional = [.. args.Where(arg => arg != TextFlag)];
        (string metric, string root)? request = positional switch
        {
            [var name] => (name, "."),
            [var name, var folder] => (name, folder),
            _ => null
        };
        if (request is not var (metric, root) || !Metrics.Contains(metric))
        {
            error.WriteLine(Usage);
            return 2;
        }

        if (!Directory.Exists(Path.Combine(root, "src")))
        {
            error.WriteLine($"Tertius.Slop: no src folder under the root \"{root}\".");
            return 2;
        }

        try
        {
            output.WriteLine(Report(metric: metric, root: root, text: text, jscpd: jscpd));
            return 0;
        }
        catch (JscpdMissingException missing)
        {
            error.WriteLine(missing.Message);
            return 2;
        }
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
