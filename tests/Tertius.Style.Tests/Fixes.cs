using System.Collections.Immutable;
using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Style.Tests;

/// <summary>
/// Runs the code fix of one rule in an in-process workspace, as an editor or <c>dotnet format</c> does, and checks that
/// the fixed source compiles and has no diagnostic of the rule left.
/// </summary>
/// <param name="rule">The id of the rule that the fix fixes.</param>
/// <param name="fix">A new instance of the code fix.</param>
internal sealed class Fixes(string rule, Func<CodeFixProvider> fix)
{
    /// <summary>The fix of TS0002, which names the arguments of a call.</summary>
    public static readonly Fixes NameArguments = new("TS0002", () => new NameArgumentsFix());

    /// <summary>The fix of TS0004, which turns a positional pattern into a property pattern.</summary>
    public static readonly Fixes PropertyPattern = new("TS0004", () => new PropertyPatternFix());

    /// <summary>The fix of TS0005, which marks a default interface method for inlining.</summary>
    public static readonly Fixes Inline = new("TS0005", () => new InlineDefaultMethodFix());

    /// <summary>The fix of TS0006, which seals a class.</summary>
    public static readonly Fixes Seal = new("TS0006", () => new SealClassFix());

    /// <summary>The fix of TS0016, which deletes the discard arm that only throws UnreachableException.</summary>
    public static readonly Fixes DiscardArm = new("TS0016", () => new DiscardArmFix());

    /// <summary>The source after the fix of the first diagnostic of the rule in <paramref name="source"/>.</summary>
    public async Task<string> First(string source)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        using AdhocWorkspace workspace = new();
        Document document = DocumentOf(workspace, source);
        Diagnostic first = (await Diagnostics(document)).First();
        List<CodeAction> actions = [];
        CodeFixContext context = new(
            document: document,
            diagnostic: first,
            registerCodeFix: (action, _) => actions.Add(action),
            cancellationToken: cancel
        );
        await fix().RegisterCodeFixesAsync(context);
        return await Applied(document, actions.Single());
    }

    /// <summary>The number of fixes that the code fix offers for the first diagnostic of the rule in <paramref name="source"/>
    /// .</summary>
    public async Task<int> Offered(string source)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        using AdhocWorkspace workspace = new();
        Document document = DocumentOf(workspace, source);
        Diagnostic first = (await Diagnostics(document)).First();
        List<CodeAction> actions = [];
        await fix()
            .RegisterCodeFixesAsync(
                new(
                    document: document,
                    diagnostic: first,
                    registerCodeFix: (action, _) => actions.Add(action),
                    cancellationToken: cancel
                )
            );
        return actions.Count;
    }

    /// <summary>
    /// The source after the fix of every diagnostic of the rule in <paramref name="source"/> at once, as dotnet format
    /// does.
    /// </summary>
    public Task<string> All(string source) => FixAll(source, untilClean: true);

    /// <summary>
    /// The source after one pass of the fix-all over <paramref name="source"/>, which may leave diagnostics for a later
    /// pass.
    /// </summary>
    public Task<string> AllOnce(string source) => FixAll(source, untilClean: false);

    private async Task<string> FixAll(string source, bool untilClean)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        using AdhocWorkspace workspace = new();
        Document document = DocumentOf(workspace, source);
        ImmutableArray<Diagnostic> diagnostics = await Diagnostics(document);
        CodeFixProvider provider = fix();
        List<CodeAction> actions = [];
        await provider.RegisterCodeFixesAsync(
            new(
                document: document,
                diagnostic: diagnostics.First(),
                registerCodeFix: (action, _) => actions.Add(action),
                cancellationToken: cancel
            )
        );
        FixAllProvider fixAll =
            provider.GetFixAllProvider() ?? throw new InvalidOperationException("The fix has no FixAllProvider.");
        FixAllContext context = new(
            document: document,
            codeFixProvider: provider,
            scope: FixAllScope.Document,
            codeActionEquivalenceKey: actions.Single().EquivalenceKey,
            diagnosticIds: [rule],
            fixAllDiagnosticProvider: new Found(diagnostics),
            cancellationToken: cancel
        );
        CodeAction action =
            await fixAll.GetFixAsync(context) ?? throw new InvalidOperationException("The FixAllProvider gave no fix.");
        return untilClean ? await Applied(document, action) : await Changed(document, action);
    }

    /// <summary>A document of <paramref name="source"/> in a new project of <paramref name="workspace"/>.</summary>
    private static Document DocumentOf(AdhocWorkspace workspace, string source)
    {
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
        return workspace.AddDocument(project.Id, "Probe.cs", SourceText.From(source));
    }

    private async Task<ImmutableArray<Diagnostic>> Diagnostics(Document document)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        Compilation compilation =
            await document.Project.GetCompilationAsync(cancel)
            ?? throw new InvalidOperationException("The project has no compilation.");
        ImmutableArray<Diagnostic> found = await compilation
            .WithAnalyzers([new StyleAnalyzer()])
            .GetAnalyzerDiagnosticsAsync(cancel);
        return
        [
            .. found
                .Where(diagnostic => diagnostic.Id == rule)
                .OrderBy(diagnostic => diagnostic.Location.SourceSpan.Start),
        ];
    }

    private async Task<string> Applied(Document document, CodeAction action)
    {
        string fixedSource = await Changed(document, action);
        (await Analysis.Ids(rule, fixedSource)).Should().BeEmpty("the fix fixes every diagnostic of its rule");
        return fixedSource;
    }

    /// <summary>The source of <paramref name="document"/> after <paramref name="action"/>.</summary>
    private static async Task<string> Changed(Document document, CodeAction action)
    {
        CancellationToken cancel = TestContext.Current.CancellationToken;
        ImmutableArray<CodeActionOperation> operations = await action.GetOperationsAsync(cancel);
        Solution changed = operations.OfType<ApplyChangesOperation>().Single().ChangedSolution;
        return (await changed.GetDocument(document.Id)!.GetTextAsync(cancel)).ToString();
    }

    /// <summary>The diagnostics that the fix-all context hands to the provider: the ones that the test found.</summary>
    private sealed class Found(ImmutableArray<Diagnostic> diagnostics) : FixAllContext.DiagnosticProvider
    {
        public override Task<IEnumerable<Diagnostic>> GetDocumentDiagnosticsAsync(
            Document document,
            CancellationToken cancellationToken
        ) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);

        public override Task<IEnumerable<Diagnostic>> GetProjectDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken
        ) => Task.FromResult<IEnumerable<Diagnostic>>([]);

        public override Task<IEnumerable<Diagnostic>> GetAllDiagnosticsAsync(
            Project project,
            CancellationToken cancellationToken
        ) => Task.FromResult<IEnumerable<Diagnostic>>(diagnostics);
    }
}
