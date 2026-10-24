using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>The rules of STYLE.md that a tool can check (<see cref="StyleRules"/>).</summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class StyleAnalyzer : DiagnosticAnalyzer
{
    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [
        StyleRules.UnnamedTupleElement, StyleRules.UnnamedArgument, StyleRules.NullForgiving,
        StyleRules.PositionalDeconstruction, StyleRules.DefaultMethodNotInlined, StyleRules.UnsealedClass,
        StyleRules.DeepInheritance, StyleRules.Finalizer, StyleRules.TryMethodShape, StyleRules.UndisposedLocal,
        StyleRules.NestedConditional, StyleRules.CopiedGenericStruct, StyleRules.BoxedGenericStruct,
        StyleRules.HelperClass, StyleRules.UnnamedCase
    ];

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
        context.RegisterSyntaxNodeAction(PositionalPatterns.AnalyzePattern, SyntaxKind.PositionalPatternClause);
        context.RegisterSyntaxNodeAction(PositionalPatterns.AnalyzeDeconstruction,
            SyntaxKind.SimpleAssignmentExpression, SyntaxKind.ForEachVariableStatement);
        context.RegisterCompilationStartAction(InlinedDefaultMethods.Start);
        context.RegisterCompilationStartAction(SealedClasses.Start);
        context.RegisterSymbolAction(InheritanceDepth.Analyze, SymbolKind.NamedType);
        context.RegisterSyntaxNodeAction(AnalyzeFinalizer, SyntaxKind.DestructorDeclaration);
        context.RegisterSyntaxNodeAction(TryMethods.Analyze, SyntaxKind.MethodDeclaration,
            SyntaxKind.LocalFunctionStatement);
        context.RegisterSyntaxNodeAction(UndisposedLocals.Analyze, SyntaxKind.LocalDeclarationStatement);
        context.RegisterSyntaxNodeAction(NestedConditionals.Analyze, SyntaxKind.ConditionalExpression);
        context.RegisterOperationAction(GenericStructs.AnalyzeCall, OperationKind.Invocation,
            OperationKind.PropertyReference);
        context.RegisterOperationAction(GenericStructs.AnalyzeConversion, OperationKind.Conversion);
        context.RegisterSymbolAction(HelperClasses.Analyze, SymbolKind.NamedType);
        context.RegisterCompilationStartAction(SwitchStatements.Start);
    }

    private static void AnalyzeArgumentList(SyntaxNodeAnalysisContext context)
    {
        if (ArgumentNames.Find(context.Node, context.SemanticModel, context.CancellationToken) is { } call)
            context.ReportDiagnostic(Diagnostic.Create(StyleRules.UnnamedArgument, context.Node.GetLocation(),
                call.Target.ToDisplayString()));
    }

    private static void AnalyzeNullForgiving(SyntaxNodeAnalysisContext context) =>
        context.ReportDiagnostic(Diagnostic.Create(StyleRules.NullForgiving, context.Node.GetLocation()));

    private static void AnalyzeFinalizer(SyntaxNodeAnalysisContext context)
    {
        var finalizer = (DestructorDeclarationSyntax)context.Node;
        context.ReportDiagnostic(Diagnostic.Create(StyleRules.Finalizer, finalizer.Identifier.GetLocation(),
            finalizer.Identifier.ValueText));
    }
}
