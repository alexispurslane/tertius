using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Style.Tests;

/// <summary>
/// The folders where the repository's <c>.editorconfig</c> turns a rule off: TS0006 in <c>benchmarks</c>, whose classes
/// BenchmarkDotNet derives from, and TS0009 in <c>tests</c>, which keep their own shapes.
/// </summary>
public sealed class RuleScopeTests
{
    private static readonly string[] Folders = ["src", "tests", "benchmarks", "analyzers"];

    [Fact]
    public async Task TheRuleOfSealedClassesHoldsEverywhereButTheBenchmarks() =>
        (await FoldersThatReport("TS0006", folder => $"public class In_{folder};"))
            .Should()
            .Equal(["src", "tests", "analyzers"], "BenchmarkDotNet takes only benchmark classes that are not sealed");

    [Fact]
    public async Task TheRuleOfTryMethodsHoldsEverywhereButTheTests() =>
        (
            await FoldersThatReport(
                "TS0009",
                folder =>
                    $"public static class In_{folder} {{ public static bool Find(out int v) {{ v = 0; return true; }} }}"
            )
        )
            .Should()
            .Equal(["src", "benchmarks", "analyzers"], "the tests keep their own shapes");

    /// <summary>
    /// The folders, of <see cref="Folders"/>, in which <paramref name="rule"/> reports the source that
    /// <paramref name="source"/> writes for the folder, under the repository's <c>.editorconfig</c>.
    /// </summary>
    private static async Task<IReadOnlyList<string>> FoldersThatReport(string rule, Func<string, string> source)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        string root = Repository.Root();
        using AdhocWorkspace workspace = new();
        Project project = workspace.AddProject(
            ProjectInfo.Create(
                id: ProjectId.CreateNewId(),
                version: VersionStamp.Default,
                name: "Probe",
                assemblyName: "Probe",
                language: LanguageNames.CSharp,
                compilationOptions: TestCompilation.Options,
                parseOptions: TestCompilation.Parse,
                metadataReferences: TestCompilation.References
            )
        );
        string editorConfig = Path.Combine(root, ".editorconfig");
        project = project
            .AddAnalyzerConfigDocument(
                name: ".editorconfig",
                text: SourceText.From(await File.ReadAllTextAsync(editorConfig, cancel)),
                filePath: editorConfig
            )
            .Project;
        foreach (string folder in Folders)
            project = project
                .AddDocument(
                    name: "Probe.cs",
                    text: source(folder),
                    filePath: Path.Combine(path1: root, path2: folder, path3: "Probe", path4: "Probe.cs")
                )
                .Project;
        Compilation compilation =
            await project.GetCompilationAsync(cancel)
            ?? throw new InvalidOperationException("The project has no compilation.");

        ImmutableArray<Diagnostic> found = await compilation
            .WithAnalyzers([new StyleAnalyzer()], project.AnalyzerOptions)
            .GetAnalyzerDiagnosticsAsync(cancel);

        return
        [
            .. Folders.Where(folder =>
                found.Any(diagnostic =>
                    diagnostic.Id == rule
                    && Path.GetRelativePath(root, diagnostic.Location.SourceTree!.FilePath)
                        .StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                )
            ),
        ];
    }
}
