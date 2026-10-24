using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Style.Tests;

/// <summary>
/// Runs the code fix of TS0002 in an in-process workspace, as an editor or <c>dotnet format</c> does, and checks that
/// the fixed source compiles and has no TS0002 left.
/// </summary>
internal static class Fixes
{
    /// <summary>The source after the fix of the first TS0002 in <paramref name="source"/>.</summary>
    public static async Task<string> First(string source)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        Document document = DocumentOf(source);
        Diagnostic first = (await Diagnostics(document)).First();
        List<CodeAction> actions = [];
        CodeFixContext context = new(document: document, diagnostic: first,
            registerCodeFix: (action, _) => actions.Add(action), cancellationToken: cancel);
        await new NameArgumentsFix().RegisterCodeFixesAsync(context);
        return await Applied(document, actions.Single());
    }

    /// <summary>The source after the fix of every TS0002 in <paramref name="source"/> at once, as dotnet format does.</summary>
    public static async Task<string> All(string source)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        Document document = DocumentOf(source);
        ImmutableArray<Diagnostic> diagnostics = await Diagnostics(document);
        NameArgumentsFix fix = new();
        FixAllProvider provider = fix.GetFixAllProvider() ??
                                  throw new InvalidOperationException("The fix has no FixAllProvider.");
        FixAllContext context = new(document: document, codeFixProvider: fix, scope: FixAllScope.Document,
            codeActionEquivalenceKey: NameArgumentsFix.EquivalenceKey, diagnosticIds: ["TS0002"],
            fixAllDiagnosticProvider: new Found(diagnostics), cancellationToken: cancel);
        CodeAction action = await provider.GetFixAsync(context) ??
                            throw new InvalidOperationException("The FixAllProvider gave no fix.");
        return await Applied(document, action);
    }

    private static Document DocumentOf(string source)
    {
        AdhocWorkspace workspace = new();
        Project project = workspace.AddProject(ProjectInfo.Create(id: ProjectId.CreateNewId(),
            version: VersionStamp.Default, name: "Probe", assemblyName: "Probe", language: LanguageNames.CSharp,
            compilationOptions: TestCompilation.Options, parseOptions: TestCompilation.Parse,
            metadataReferences: TestCompilation.References));
        return workspace.AddDocument(project.Id, "Probe.cs", SourceText.From(source));
    }

    private static async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        Compilation compilation = await document.Project.GetCompilationAsync(cancel) ??
                                  throw new InvalidOperationException("The project has no compilation.");
        ImmutableArray<Diagnostic> found = await compilation.WithAnalyzers([new StyleAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancel);
        return [.. found.Where(diagnostic => diagnostic.Id == "TS0002")];
    }

    private static async Task<string> Applied(Document document, CodeAction action)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(cancel);
        Solution changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        string fixedSource = (await changed.GetDocument(document.Id)!.GetTextAsync(cancel)).ToString();
        (await Analysis.Ids(fixedSource)).Should().NotContain("TS0002", "the fix names every argument that needs a name");
        return fixedSource;
    }

    /// <summary>The diagnostics that the fix-all context hands to the provider: the ones that the test found.</summary>
    private sealed class Found(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(Document document,
            CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(Project project,
            CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>([]);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(Project project,
            CancellationToken cancellationToken) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
