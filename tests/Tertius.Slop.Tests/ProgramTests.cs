using System.Text.Json;
using AwesomeAssertions;

namespace Tertius.Slop.Tests;

/// <summary>
/// The command line (tools/README.md, "Slop metrics"): <c>erosion [root] [--text]</c> prints the erosion of the C#
/// files under <c>root/src</c> as JSON, or as text. Each test builds a repository in a new temporary folder.
/// </summary>
public sealed class ProgramTests : IDisposable
{
    private readonly string root = Directory.CreateTempSubdirectory("tertius-slop-").FullName;
    private readonly StringWriter output = new();
    private readonly StringWriter error = new();

    public ProgramTests()
    {
        Directory.CreateDirectory(Path.Combine(root, "src"));
        File.WriteAllText(Path.Combine(root, "src", "C.cs"), """
                                                             class C
                                                             {
                                                                 int F(int x)
                                                                 {
                                                                     if (x > 0) return 1;
                                                                     return 0;
                                                                 }
                                                             }
                                                             """);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
        output.Dispose();
        error.Dispose();
    }

    [Fact]
    public void TheErosionCommandPrintsTheReportAsJson()
    {
        int exit = Program.Run(["erosion", root], output, error);

        exit.Should().Be(0);
        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement report = json.RootElement;
        (report.GetProperty("erosion").GetDouble(), report.GetProperty("totalMass").GetDouble(),
                report.GetProperty("highCcMass").GetDouble(), report.GetProperty("functionCount").GetInt32(),
                report.GetProperty("highCcFunctionCount").GetInt32(), report.GetProperty("sloc").GetInt32(),
                report.GetProperty("fileCount").GetInt32())
            .Should().Be((0.0, 2 * Math.Sqrt(5), 0.0, 1, 0, 8, 1));
    }

    [Fact]
    public void TheJsonNamesEachTopFunctionWithItsPlaceAndMeasures()
    {
        Program.Run(["erosion", root], output, error);

        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement top = json.RootElement.GetProperty("topFunctions").EnumerateArray().Should().ContainSingle()
            .Subject;
        (top.GetProperty("file").GetString(), top.GetProperty("line").GetInt32(), top.GetProperty("name").GetString(),
                top.GetProperty("cc").GetInt32(), top.GetProperty("sloc").GetInt32(),
                top.GetProperty("mass").GetDouble())
            .Should().Be(("src/C.cs", 3, "C.F", 2, 5, 2 * Math.Sqrt(5)));
    }

    [Fact]
    public void TheTextFlagPrintsTheReportForAPerson()
    {
        int exit = Program.Run(["erosion", root, "--text"], output, error);

        exit.Should().Be(0);
        output.ToString().Should().Contain("Erosion 0.000").And.Contain("C.F").And.Contain("src/C.cs:3");
    }

    [Fact]
    public void AnUnknownMetricPrintsTheUsageAndFails()
    {
        int exit = Program.Run(["sloppiness", root], output, error);

        (exit, output.ToString()).Should().Be((2, ""));
        error.ToString().Should().Contain("usage");
    }

    [Fact]
    public void MoreThanAMetricAndARootPrintTheUsageAndFail()
    {
        int exit = Program.Run(["erosion", root, "extra"], output, error);

        (exit, output.ToString()).Should().Be((2, ""));
        error.ToString().Should().Contain("usage");
    }

    [Fact]
    public void WithoutARootTheCurrentFolderIsTheRoot()
    {
        // The tests run in their output folder, which has no src folder.
        int exit = Program.Run(["erosion"], output, error);

        exit.Should().Be(2);
        error.ToString().Should().Contain("no src folder under the root \".\"");
    }

    [Fact]
    public void ARootWithoutASrcFolderFails()
    {
        int exit = Program.Run(["erosion", Path.Combine(root, "src")], output, error);

        exit.Should().Be(2);
        error.ToString().Should().Contain("no src folder");
    }
}
