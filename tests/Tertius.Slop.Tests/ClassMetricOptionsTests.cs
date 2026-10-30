using System.Text.Json;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tertius.Slop.Tests;

[CollectionDefinition("Slop reference environment", DisableParallelization = true)]
public sealed class SlopReferenceEnvironment;

/// <summary>Metadata inputs bind inherited state and report invalid configuration at the command boundary.</summary>
[Collection("Slop reference environment")]
public sealed class ClassMetricOptionsTests
{
    [Fact]
    public void PrivateMetadataStoragePreventsAnIncorrectNoStorageConclusion()
    {
        using var git = new GitFolder();
        string assembly = Path.Combine(git.Root, "External.dll");
        var compilation = CSharpCompilation.Create(
            assemblyName: "External",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    text: "public class External { private int state; }",
                    cancellationToken: TestContext.Current.CancellationToken
                ),
            ],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(outputKind: OutputKind.DynamicallyLinkedLibrary)
        );
        compilation
            .Emit(outputPath: assembly, cancellationToken: TestContext.Current.CancellationToken)
            .Success.Should()
            .BeTrue();
        ClassMetrics metrics = ClassMetrics.Of(
            Source.UsedByATest(
                SourceFile.Parse("src/A.cs", "class A : External { public int F() => 0; public int G() => 0; }")
            ),
            new ClassMetricOptions { ReferencePaths = [assembly] }
        );

        metrics.Summary.Tcc.Should().Be(new ClassRatio(0, 1));
        metrics.Types.Single().TccApplicability!.Status.Should().Be("complete");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitMetadataConnectsMethodsThroughInheritedFields(bool environment)
    {
        using var git = new GitFolder();
        git.Write("src/A.cs", "class A : External { public int F() => x; public int G() => x; }");
        string assembly = Path.Combine(git.Root, "External.dll");
        var compilation = CSharpCompilation.Create(
            assemblyName: "External",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(
                    text: "public class External { protected int x; }",
                    cancellationToken: TestContext.Current.CancellationToken
                ),
            ],
            references: [MetadataReference.CreateFromFile(typeof(object).Assembly.Location)],
            options: new CSharpCompilationOptions(outputKind: OutputKind.DynamicallyLinkedLibrary)
        );
        compilation
            .Emit(outputPath: assembly, cancellationToken: TestContext.Current.CancellationToken)
            .Success.Should()
            .BeTrue();
        string list = Path.Combine(git.Root, "references.json");
        File.WriteAllText(list, JsonSerializer.Serialize(new[] { assembly }));
        using var output = new StringWriter();
        using var error = new StringWriter();
        string? previous = Environment.GetEnvironmentVariable("TERTIUS_SLOP_REFERENCE_LIST");
        try
        {
            Environment.SetEnvironmentVariable("TERTIUS_SLOP_REFERENCE_LIST", environment ? list : null);
            string[] args = environment ? ["all", git.Root] : ["all", git.Root, "--reference-list", list];

            int exit = Program.Run(args: args, output: output, error: error, jscpd: "jscpd");

            exit.Should().Be(0, error.ToString());
            using var json = JsonDocument.Parse(output.ToString());
            JsonElement summary = json.RootElement.GetProperty("classMetrics").GetProperty("summary");
            summary.GetProperty("tcc").GetProperty("numerator").GetInt32().Should().Be(1);
            summary.GetProperty("tcc").GetProperty("denominator").GetInt32().Should().Be(1);
            summary.GetProperty("bindingErrorCount").GetInt32().Should().Be(0);
        }
        finally
        {
            Environment.SetEnvironmentVariable("TERTIUS_SLOP_REFERENCE_LIST", previous);
        }
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("[")]
    [InlineData("[\"/no-such-slop-metadata.dll\"]")]
    public void InvalidMetadataListsReturnAnErrorWithoutAReport(string contents)
    {
        using var git = new GitFolder();
        git.Write("src/A.cs", "class A {}");
        git.Write("references.json", contents);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(
            args: ["all", git.Root, "--reference-list", Path.Combine(git.Root, "references.json")],
            output: output,
            error: error,
            jscpd: "jscpd"
        );

        exit.Should().Be(2);
        output.ToString().Should().BeEmpty();
        error.ToString().Should().StartWith("Tertius.Slop:");
    }

    [Fact]
    public void AMissingMetadataListArgumentExplainsTheRequiredValue()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = Program.Run(args: ["all", "--reference-list"], output: output, error: error, jscpd: "jscpd");

        exit.Should().Be(2);
        error.ToString().Should().Contain("--reference-list needs a value");
    }
}
