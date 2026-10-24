using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Style;

/// <summary>
/// The code fix of TS0002: it names each argument that the rule asks a name for (<see cref="ArgumentNames.Find"/>),
/// with the name of its parameter. The arguments keep their order, so each named argument stays in its position, as C#
/// allows before the positional arguments of a <c>params</c> list. The name goes before an <c>out</c>, <c>ref</c> or
/// <c>in</c> modifier, and takes the comments and the line break in front of the argument.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(NameArgumentsFix))]
[Shared]
public sealed class NameArgumentsFix : CodeFixProvider
{
    /// <summary>The key that tells the fix of TS0002 apart from other fixes, so that a fix-all collects only its own.</summary>
    public const string EquivalenceKey = "Tertius.Style.NameArguments";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [StyleRules.UnnamedArgument.Id];

    /// <summary>
    /// Fixes every call of a document in one rewrite, so that the fixes of a call and of a call in its arguments
    /// both apply.
    /// </summary>
    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(async (context, document, diagnostics) => await Named(document,
            diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan), context.CancellationToken)
            .ConfigureAwait(false));

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (Diagnostic diagnostic in context.Diagnostics)
            context.RegisterCodeFix(CodeAction.Create(title: "Name the arguments",
                createChangedDocument: cancel => Named(context.Document, [diagnostic.Location.SourceSpan], cancel),
                equivalenceKey: EquivalenceKey), diagnostic);
        return Task.CompletedTask;
    }

    /// <summary>The document with the arguments of each argument list at <paramref name="spans"/> named.</summary>
    private static async Task<Document> Named(Document document, IEnumerable<TextSpan> spans,
        CancellationToken cancel)
    {
        if (await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false) is not { } root ||
            await document.GetSemanticModelAsync(cancel).ConfigureAwait(false) is not { } model)
            return document;
        Dictionary<SyntaxNode, string> names = [];
        foreach (TextSpan span in spans)
            if (ArgumentNames.Find(root.FindNode(span), model, cancel) is { } call)
                foreach ((SyntaxNode argument, string parameter) in call.Arguments)
                    names[argument] = parameter;
        return document.WithSyntaxRoot(root.ReplaceNodes(names.Keys,
            (original, current) => WithName(current, names[original])));
    }

    /// <summary>
    /// <paramref name="argument"/> named <paramref name="parameter"/>. The name takes the argument's leading trivia,
    /// so the line break and the comments before the argument stay before it.
    /// </summary>
    private static SyntaxNode WithName(SyntaxNode argument, string parameter)
    {
        NameColonSyntax name = SyntaxFactory.NameColon(SyntaxFactory.IdentifierName(Identifier(parameter)))
            .WithLeadingTrivia(argument.GetLeadingTrivia()).WithTrailingTrivia(SyntaxFactory.Space);
        return argument switch
        {
            ArgumentSyntax plain => plain.WithoutLeadingTrivia().WithNameColon(name),
            AttributeArgumentSyntax attribute => attribute.WithoutLeadingTrivia().WithNameColon(name),
            _ => argument
        };
    }

    /// <summary>The identifier for a parameter's name, with an <c>@</c> before a keyword such as <c>object</c>.</summary>
    private static SyntaxToken Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None
            ? SyntaxFactory.Identifier(name)
            : SyntaxFactory.Identifier(leading: SyntaxTriviaList.Empty, contextualKind: SyntaxKind.IdentifierToken,
                text: "@" + name, valueText: name, trailing: SyntaxTriviaList.Empty);
}
