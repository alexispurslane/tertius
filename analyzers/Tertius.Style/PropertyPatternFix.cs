using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Style;

/// <summary>
/// The code fix of TS0004 for a positional pattern: it writes a property pattern that names, for each element, the
/// property or field that the matching <c>Deconstruct</c> parameter reads, found by name, ignoring case. It drops each
/// discard, keeps the type, the designation and the properties that the pattern names already, and writes the
/// pattern on one line, with one space between tokens where the source broke a line. When an element has no property or field of its name, the fix offers nothing. A deconstruction
/// in an assignment or a <c>foreach</c> has no property pattern to become, so it gets no fix.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(PropertyPatternFix))]
[Shared]
public sealed class PropertyPatternFix : CodeFixProvider
{
    /// <summary>The key that tells the fix of TS0004 apart from other fixes, so that a fix-all collects only its own.</summary>
    public const string EquivalenceKey = "Tertius.Style.PropertyPattern";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [StyleRules.PositionalDeconstruction.Id];

    /// <summary>
    /// Rewrites every pattern of a document in one pass. A pattern nested in another that the pass rewrites waits for
    /// the next pass.
    /// </summary>
    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(async (context, document, diagnostics) => await Rewritten(document,
                diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan), context.CancellationToken)
            .ConfigureAwait(false));

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        if (await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false) is not
                { } root ||
            await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false) is not
                { } model)
            return;
        foreach (Diagnostic diagnostic in context.Diagnostics)
            if (Rewrite(root.FindNode(diagnostic.Location.SourceSpan, getInnermostNodeForTie: true), model, context.CancellationToken) is not null)
                context.RegisterCodeFix(CodeAction.Create(title: "Use a property pattern",
                    createChangedDocument: cancel =>
                        Rewritten(context.Document, [diagnostic.Location.SourceSpan], cancel),
                    equivalenceKey: EquivalenceKey), diagnostic);
    }

    /// <summary>The document with the positional pattern at each of <paramref name="spans"/> rewritten.</summary>
    private static async Task<Document> Rewritten(Document document, IEnumerable<TextSpan> spans,
        CancellationToken cancel)
    {
        if (await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false) is not { } root ||
            await document.GetSemanticModelAsync(cancel).ConfigureAwait(false) is not { } model)
            return document;
        List<TextChange> changes = [];
        foreach (TextChange change in spans
                     .Select(span => Rewrite(root.FindNode(span, getInnermostNodeForTie: true), model, cancel))
                     .OfType<TextChange>()
                     .OrderBy(change => change.Span.Start))
            if (changes.Count == 0 || changes[changes.Count - 1].Span.End <= change.Span.Start)
                changes.Add(change);
        SourceText text = await document.GetTextAsync(cancel).ConfigureAwait(false);
        return document.WithText(text.WithChanges(changes));
    }

    /// <summary>
    /// The change that turns the positional pattern clause <paramref name="node"/>, with the property pattern clause
    /// after it, into one property pattern clause, or null when an element names no property or field.
    /// </summary>
    private static TextChange? Rewrite(SyntaxNode node, SemanticModel model, CancellationToken cancel)
    {
        if (node is not PositionalPatternClauseSyntax { Parent: RecursivePatternSyntax pattern } clause ||
            model.GetOperation(pattern, cancel) is not IRecursivePatternOperation
            {
                MatchedType: { } matched, DeconstructSymbol: IMethodSymbol deconstruct
            })
            return null;
        IParameterSymbol[] outs = [.. deconstruct.Parameters.Where(parameter => parameter.RefKind == RefKind.Out)];
        List<string> parts = [];
        for (int index = 0; index < clause.Subpatterns.Count; index++)
        {
            SubpatternSyntax element = clause.Subpatterns[index];
            if (element.Pattern is DiscardPatternSyntax)
                continue;
            // An element may carry the name of its parameter, but C# requires that name to be the one at its position.
            if (MemberNamed(matched, outs[index].Name) is not { } member)
                return null;
            parts.Add($"{member}: {OneLine(element.Pattern)}");
        }
        if (pattern.PropertyPatternClause is { } properties)
            parts.AddRange(properties.Subpatterns.Select(OneLine));
        string braces = parts.Count == 0 ? "{ }" : $"{{ {string.Join(", ", parts)} }}";
        int end = pattern.PropertyPatternClause?.Span.End ?? clause.Span.End;
        return new TextChange(TextSpan.FromBounds(clause.SpanStart, end),
            pattern.Type is null ? braces : " " + braces);
    }

    /// <summary>The text of <paramref name="node"/> on one line: one space wherever its tokens have trivia between them.</summary>
    private static string OneLine(SyntaxNode node)
    {
        StringBuilder text = new();
        SyntaxToken previous = default;
        foreach (SyntaxToken token in node.DescendantTokens())
        {
            if (text.Length > 0 && (previous.HasTrailingTrivia || token.HasLeadingTrivia))
                text.Append(' ');
            text.Append(token.Text);
            previous = token;
        }
        return text.ToString();
    }

    /// <summary>
    /// The name of the one property or field of <paramref name="type"/> or its bases whose name is
    /// <paramref name="parameter"/>, ignoring case, or null.
    /// </summary>
    private static string? MemberNamed(ITypeSymbol type, string parameter)
    {
        string[] names =
        [
            .. Bases(type).SelectMany(each => each.GetMembers())
                .Where(member => member is IPropertySymbol or IFieldSymbol { IsImplicitlyDeclared: false } &&
                                 !member.IsStatic &&
                                 string.Equals(member.Name, parameter, StringComparison.OrdinalIgnoreCase))
                .Select(member => member.Name).Distinct()
        ];
        return names.Length == 1 ? names[0] : null;
    }

    private static IEnumerable<ITypeSymbol> Bases(ITypeSymbol type)
    {
        for (ITypeSymbol? each = type; each != null; each = each.BaseType)
            yield return each;
    }
}
