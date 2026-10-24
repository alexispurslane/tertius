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
/// The code fix of TS0006: it adds <c>sealed</c> to the class, after its access modifiers and <c>new</c>, and before
/// every other modifier, such as <c>unsafe</c> or <c>partial</c>. With no modifier before it, <c>sealed</c> takes the
/// trivia in front of the first one, or in front of the keyword, so a doc comment stays first.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(SealClassFix))]
[Shared]
public sealed class SealClassFix : CodeFixProvider
{
    /// <summary>The key that tells the fix of TS0006 apart from other fixes, so that a fix-all collects only its own.</summary>
    public const string EquivalenceKey = "Tertius.Style.SealClass";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [StyleRules.UnsealedClass.Id];

    /// <summary>Seals every class of a document in one rewrite.</summary>
    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(
            async (context, document, diagnostics) =>
                await Sealed(
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
                    title: "Seal the class",
                    createChangedDocument: cancel => Sealed(context.Document, [diagnostic.Location.SourceSpan], cancel),
                    equivalenceKey: EquivalenceKey
                ),
                diagnostic
            );
        return Task.CompletedTask;
    }

    /// <summary>The document with each class whose name is at one of <paramref name="spans"/> sealed.</summary>
    private static async Task<Document> Sealed(Document document, IEnumerable<TextSpan> spans, CancellationToken cancel)
    {
        if (await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false) is not { } root)
            return document;
        TypeDeclarationSyntax[] classes =
        [
            .. spans
                .Select(span => root.FindToken(span.Start).Parent?.FirstAncestorOrSelf<TypeDeclarationSyntax>())
                .OfType<TypeDeclarationSyntax>()
                .Distinct(),
        ];
        return document.WithSyntaxRoot(root.ReplaceNodes(classes, (_, current) => WithSealed(current)));
    }

    /// <summary><paramref name="type"/> with <c>sealed</c> after its access modifiers and <c>new</c>.</summary>
    private static TypeDeclarationSyntax WithSealed(TypeDeclarationSyntax type)
    {
        SyntaxTokenList modifiers = type.Modifiers;
        int index = modifiers.TakeWhile(modifier => IsAccessOrNew(modifier.Kind())).Count();
        SyntaxToken @sealed = SyntaxFactory.Token(SyntaxKind.SealedKeyword).WithTrailingTrivia(SyntaxFactory.Space);
        if (index > 0)
            return type.WithModifiers(modifiers.Insert(index, @sealed));
        if (modifiers.Count > 0)
            return type.WithModifiers(
                modifiers
                    .Replace(modifiers[0], modifiers[0].WithLeadingTrivia())
                    .Insert(0, @sealed.WithLeadingTrivia(modifiers[0].LeadingTrivia))
            );
        return type.WithKeyword(type.Keyword.WithLeadingTrivia())
            .WithModifiers(SyntaxFactory.TokenList(@sealed.WithLeadingTrivia(type.Keyword.LeadingTrivia)));
    }

    private static bool IsAccessOrNew(SyntaxKind kind) =>
        kind
            is SyntaxKind.PublicKeyword
                or SyntaxKind.InternalKeyword
                or SyntaxKind.ProtectedKeyword
                or SyntaxKind.PrivateKeyword
                or SyntaxKind.FileKeyword
                or SyntaxKind.NewKeyword;
}
