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
/// The code fix of TS0016: it deletes a discard arm whose expression is only <c>throw new UnreachableException(...)</c>,
/// with or without a message. It offers nothing for any other discard arm, because the value of that arm is behavior,
/// and nothing for the only arm of a switch expression, because the switch would have no arm left.
/// <para>
/// The switch expression keeps its trailing comma style. When the deleted arm is the last one, the comma before it
/// becomes the trailing comma if the deleted arm had one, and goes away if it had not. The trivia of that comma, such
/// as the line break, moves to the arm before it.
/// </para>
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(DiscardArmFix))]
[Shared]
public sealed class DiscardArmFix : CodeFixProvider
{
    /// <summary>The key that tells the fix of TS0016 apart from other fixes, so that a fix-all collects only its own.</summary>
    public const string EquivalenceKey = "Tertius.Style.DiscardArm";

    private const string UnreachableException = "System.Diagnostics.UnreachableException";

    /// <inheritdoc/>
    public override ImmutableArray<string> FixableDiagnosticIds { get; } = [StyleRules.DiscardArm.Id];

    /// <summary>Deletes every such arm of a document in one rewrite.</summary>
    public override FixAllProvider GetFixAllProvider() =>
        FixAllProvider.Create(
            async (context, document, diagnostics) =>
                await Deleted(
                        document,
                        diagnostics.Select(diagnostic => diagnostic.Location.SourceSpan),
                        context.CancellationToken
                    )
                    .ConfigureAwait(false)
        );

    /// <inheritdoc/>
    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        if (await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false) is not { } root)
            return;
        foreach (Diagnostic diagnostic in context.Diagnostics)
            if (
                ArmAt(root, diagnostic.Location.SourceSpan) is { } arm
                && await IsDeletable(context.Document, arm, context.CancellationToken).ConfigureAwait(false)
            )
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title: "Delete the discard arm",
                        createChangedDocument: cancel =>
                            Deleted(context.Document, [diagnostic.Location.SourceSpan], cancel),
                        equivalenceKey: EquivalenceKey
                    ),
                    diagnostic
                );
    }

    /// <summary>
    /// The document with the arm at each of <paramref name="spans"/> deleted, for the arms that only throw
    /// <c>UnreachableException</c>.
    /// </summary>
    private static async Task<Document> Deleted(
        Document document,
        IEnumerable<TextSpan> spans,
        CancellationToken cancel
    )
    {
        if (await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false) is not { } root)
            return document;
        List<SwitchExpressionArmSyntax> arms = [];
        foreach (TextSpan span in spans)
            if (ArmAt(root, span) is { } arm && await IsDeletable(document, arm, cancel).ConfigureAwait(false))
                arms.Add(arm);
        // Each deletion changes the tree, so the arms are tracked through the changes that come before them.
        SyntaxNode current = root.TrackNodes(arms);
        foreach (SwitchExpressionArmSyntax arm in arms)
            if (current.GetCurrentNode(arm) is { Parent: SwitchExpressionSyntax switchExpression } tracked)
                current = current.ReplaceNode(switchExpression, Without(switchExpression, tracked));
        return document.WithSyntaxRoot(current);
    }

    private static SwitchExpressionArmSyntax? ArmAt(SyntaxNode root, TextSpan span) =>
        root.FindNode(span).FirstAncestorOrSelf<SwitchExpressionArmSyntax>();

    /// <summary>
    /// True for an arm that is not the only arm and whose expression throws a new <c>UnreachableException</c>, with
    /// any arguments.
    /// </summary>
    private static async Task<bool> IsDeletable(
        Document document,
        SwitchExpressionArmSyntax arm,
        CancellationToken cancel
    )
    {
        if (
            arm is not { Parent: SwitchExpressionSyntax { Arms.Count: > 1 }, Expression: ThrowExpressionSyntax thrown }
            || thrown.Expression is not BaseObjectCreationExpressionSyntax creation
            || await document.GetSemanticModelAsync(cancel).ConfigureAwait(false) is not { } model
        )
            return false;
        return model.GetTypeInfo(creation, cancel).Type?.ToDisplayString() == UnreachableException;
    }

    /// <summary><paramref name="switchExpression"/> without <paramref name="arm"/>, which is one of its arms.</summary>
    private static SwitchExpressionSyntax Without(
        SwitchExpressionSyntax switchExpression,
        SwitchExpressionArmSyntax arm
    )
    {
        List<SyntaxNodeOrToken> items = [.. switchExpression.Arms.GetWithSeparators()];
        int index = items.FindIndex(item => item.IsNode && item.AsNode() == arm);
        if (index == items.Count - 1)
        {
            // The last arm has no comma after it. The comma before it goes, and the trivia of that comma, the line
            // break, moves to the arm before it.
            SyntaxToken before = items[index - 1].AsToken();
            items.RemoveRange(index - 1, 2);
            SyntaxNode previous = items[index - 2].AsNode()!;
            items[index - 2] = previous.WithTrailingTrivia(
                previous.GetTrailingTrivia().AddRange(before.TrailingTrivia)
            );
        }
        else
            // The arm and the comma after it go. When the arm was last, the comma before it is the trailing comma now.
            items.RemoveRange(index, 2);

        return switchExpression.WithArms(SyntaxFactory.SeparatedList<SwitchExpressionArmSyntax>(items));
    }
}
