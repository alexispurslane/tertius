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
/// The code fix of TS0005: it puts <c>[MethodImpl(MethodImplOptions.AggressiveInlining)]</c> on its own line after the
/// attributes of the method, or first, after its doc comment, and adds <c>using System.Runtime.CompilerServices;</c>
/// when the file has none.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(InlineDefaultMethodFix))]
[Shared]
public sealed class InlineDefaultMethodFix : CodeFixProvider
{
    /// <summary>The key that tells the fix of TS0005 apart from other fixes, so that a fix-all collects only its own.</summary>
    public const string EquivalenceKey = "Tertius.Style.InlineDefaultMethod";

    private const string Namespace = "System.Runtime.CompilerServices";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [StyleRules.DefaultMethodNotInlined.Id];

    /// <summary>Marks every method of a document in one rewrite, so that the file gets the using once.</summary>
    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(
            async (context, document, diagnostics) =>
                await Marked(
                        document,
                        diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan),
                        context.CancellationToken
                    )
                    .ConfigureAwait(false)
        );

    /// <inheritdoc/>
    public override Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (Diagnostic diagnostic in context.Diagnostics)
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Mark the method for inlining",
                    createChangedDocument: cancel => Marked(context.Document, [diagnostic.Location.SourceSpan], cancel),
                    equivalenceKey: EquivalenceKey
                ),
                diagnostic
            );
        return Task.CompletedTask;
    }

    /// <summary>The document with each method whose name is at one of <paramref name="spans"/> marked.</summary>
    private static async Task<Document> Marked(Document document, IEnumerable<TextSpan> spans, CancellationToken cancel)
    {
        if (await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false) is not CompilationUnitSyntax root)
            return document;
        SyntaxTrivia endOfLine = root.DescendantTrivia()
            .FirstOrDefault(trivia => trivia.IsKind(SyntaxKind.EndOfLineTrivia))
            is { RawKind: not 0 } found
            ? found
            : SyntaxFactory.LineFeed;
        MethodDeclarationSyntax[] methods =
        [
            .. spans
                .Select(span => root.FindToken(span.Start).Parent?.FirstAncestorOrSelf<MethodDeclarationSyntax>())
                .OfType<MethodDeclarationSyntax>()
                .Distinct(),
        ];
        CompilationUnitSyntax marked = root.ReplaceNodes(methods, (_, current) => WithInlining(current, endOfLine));
        return document.WithSyntaxRoot(WithUsing(marked, endOfLine));
    }

    /// <summary><paramref name="method"/> with the attribute on its own line, after its other attributes.</summary>
    private static MethodDeclarationSyntax WithInlining(MethodDeclarationSyntax method, SyntaxTrivia endOfLine)
    {
        SyntaxTriviaList leading = method.GetLeadingTrivia();
        SyntaxTriviaList indent =
            leading.Count > 0 && leading.Last().IsKind(SyntaxKind.WhitespaceTrivia)
                ? SyntaxFactory.TriviaList(leading.Last())
                : SyntaxFactory.TriviaList();
        AttributeListSyntax inlining = SyntaxFactory
            .AttributeList(
                SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Attribute(
                        SyntaxFactory.IdentifierName("MethodImpl"),
                        SyntaxFactory.ParseAttributeArgumentList("(MethodImplOptions.AggressiveInlining)")
                    )
                )
            )
            .WithTrailingTrivia(endOfLine);
        if (method.AttributeLists.Count > 0)
            return method.AddAttributeLists(inlining.WithLeadingTrivia(indent));
        // The attribute takes the doc comment and the indent in front of the method, and the method keeps the indent.
        return method
            .WithLeadingTrivia(indent)
            .WithAttributeLists(SyntaxFactory.SingletonList(inlining.WithLeadingTrivia(leading)));
    }

    /// <summary>
    /// <paramref name="root"/> with <c>using System.Runtime.CompilerServices;</c> among its usings in order, or first in
    /// the file, when the file has none at its top level or in a namespace.
    /// </summary>
    private static CompilationUnitSyntax WithUsing(CompilationUnitSyntax root, SyntaxTrivia endOfLine)
    {
        if (
            root.DescendantNodes()
                .OfType<UsingDirectiveSyntax>()
                .Any(directive =>
                    directive.Alias == null
                    && directive.StaticKeyword.IsKind(SyntaxKind.None)
                    && directive.Name?.ToString() == Namespace
                )
        )
            return root;
        UsingDirectiveSyntax directive = SyntaxFactory
            .UsingDirective(SyntaxFactory.ParseName(Namespace))
            .WithUsingKeyword(SyntaxFactory.Token(SyntaxKind.UsingKeyword).WithTrailingTrivia(SyntaxFactory.Space))
            .WithTrailingTrivia(endOfLine);
        if (root.Usings.Count == 0)
            return root.WithUsings(SyntaxFactory.SingletonList(directive.WithTrailingTrivia(endOfLine, endOfLine)));
        int index = root
            .Usings.TakeWhile(other => string.CompareOrdinal(other.Name?.ToString(), Namespace) < 0)
            .Count();
        if (index > 0)
            return root.WithUsings(root.Usings.Insert(index, directive));
        // The new first using takes the trivia in front of the old first one, such as a file header.
        UsingDirectiveSyntax first = root.Usings[0];
        return root.WithUsings(
            root.Usings.Replace(first, first.WithoutLeadingTrivia())
                .Insert(0, directive.WithLeadingTrivia(first.GetLeadingTrivia()))
        );
    }
}
