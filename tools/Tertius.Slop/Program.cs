using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tertius.Slop;

/// <summary>
/// The command line: <c>Tertius.Slop &lt;metric&gt; [root] [--text]</c> (tools/README.md, "Slop metrics"). The metric
/// is a subcommand, so that each metric adds its own case. It prints JSON for the tools that read it, such as the commit
/// hook, and text with <c>--text</c>. The root is the repository, the current folder by default.
/// </summary>
public static class Program
{
    private const string TextFlag = "--text";
    private const string Usage = "usage: Tertius.Slop erosion [root] [--text]";

    /// <summary>Runs the command line on the console.</summary>
    public static int Main(string[] args) => Run(args, Console.Out, Console.Error);

    /// <summary>
    /// Runs the command line: 0 after the report, 2 for wrong arguments or a root without a <c>src</c> folder.
    /// </summary>
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        bool text = args.Contains(TextFlag);
        string[] positional = [.. args.Where(arg => arg != TextFlag)];
        (string metric, string root)? request = positional switch
        {
            [var metric] => (metric, "."),
            [var metric, var folder] => (metric, folder),
            _ => null
        };
        if (request is not ("erosion", var root))
        {
            error.WriteLine(Usage);
            return 2;
        }

        if (!Directory.Exists(Path.Combine(root, "src")))
        {
            error.WriteLine($"Tertius.Slop: no src folder under the root \"{root}\".");
            return 2;
        }

        ErosionReport report = ErosionReport.Of(Codebase.Under(root));
        output.WriteLine(text ? report.Describe() : JsonSerializer.Serialize(report, SlopJson.Default.ErosionReport));
        return 0;
    }
}

/// <summary>The JSON form of the reports, with camel-case names, written at build time.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(ErosionReport))]
internal sealed partial class SlopJson : JsonSerializerContext;
