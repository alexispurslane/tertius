using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>The rules of STYLE.md that a tool can check (<see cref="StyleRules"/>).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StyleAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        [StyleRules.UnnamedTupleElement, StyleRules.UnnamedArgument, StyleRules.NullForgiving];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSyntaxNodeAction(TupleNames.AnalyzeWrittenType, SyntaxKind.TupleType);
        context.RegisterSyntaxNodeAction(TupleNames.AnalyzeInferredDeclaration, SyntaxKind.VariableDeclarator,
            SyntaxKind.ForEachStatement, SyntaxKind.Parameter, SyntaxKind.SingleVariableDesignation);
        context.RegisterSyntaxNodeAction(AnalyzeArgumentList, SyntaxKind.ArgumentList,
            SyntaxKind.AttributeArgumentList);
        context.RegisterSyntaxNodeAction(AnalyzeNullForgiving, SyntaxKind.SuppressNullableWarningExpression);
    }

    private static void AnalyzeArgumentList(SyntaxNodeAnalysisContext context)
    {
        if (ArgumentNames.Find(context.Node, context.SemanticModel, context.CancellationToken) is { } call)
            context.ReportDiagnostic(Diagnostic.Create(StyleRules.UnnamedArgument, context.Node.GetLocation(),
                call.Target.ToDisplayString()));
    }

    private static void AnalyzeNullForgiving(SyntaxNodeAnalysisContext context) =>
        context.ReportDiagnostic(Diagnostic.Create(StyleRules.NullForgiving, context.Node.GetLocation()));
}
