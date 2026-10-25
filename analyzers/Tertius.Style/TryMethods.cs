using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0009: a method or a local function that returns <c>bool</c> and has at least one <c>out</c> parameter, whatever
/// its name. Three kinds of method are not reported:
/// <list type="bullet">
/// <item><c>TryGetValue</c> on a union, which the compiler calls by name for pattern matching.</item>
/// <item>
/// An override, and an implementation of an interface member, explicit or not. They take their shape from the member
/// that they implement, so the rule reports only that member, where the solution declares it.
/// </item>
/// <item>A method whose leading comment holds the word "benchmark", which names the benchmark that shows the
/// cost.</item>
/// </list>
/// </summary>
internal static class TryMethods
{
    private const string TryGetValue = "TryGetValue";
    private const string BenchmarkWord = "benchmark";
    private const string UnionAttribute = "System.Runtime.CompilerServices.UnionAttribute";
    private const string GeneratedUnionAttribute = "Tertius.GeneratedUnionAttribute";

    public static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (
            context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol method
            || !HasTryShape(method)
            || IsUnionTryGetValue(method)
            || Implements(method)
            || HasBenchmarkComment(context.Node)
        )
            return;
        SyntaxToken name = context.Node switch
        {
            MethodDeclarationSyntax declaration => declaration.Identifier,
            LocalFunctionStatementSyntax local => local.Identifier,
            _ => context.Node.GetFirstToken(),
        };
        context.ReportDiagnostic(Diagnostic.Create(StyleRules.TryMethodShape, name.GetLocation(), method.Name));
    }

    private static bool HasTryShape(IMethodSymbol method) =>
        method.ReturnType.SpecialType == SpecialType.System_Boolean
        && method.Parameters.Any(parameter => parameter.RefKind == RefKind.Out);

    /// <summary>True for <c>TryGetValue</c> on a type that carries <c>[Union]</c> or <c>[GeneratedUnion]</c>.</summary>
    private static bool IsUnionTryGetValue(IMethodSymbol method) =>
        method.Name == TryGetValue
        && method
            .ContainingType.GetAttributes()
            .Any(attribute => attribute.AttributeClass?.ToDisplayString() is UnionAttribute or GeneratedUnionAttribute);

    /// <summary>
    /// True for an override, and for an implementation of an interface member, explicit or not. An implementation of an
    /// interface that the solution does not declare, such as <c>IParsable&lt;T&gt;.TryParse</c>, takes its shape from
    /// the framework, so the rule does not report it either.
    /// </summary>
    private static bool Implements(IMethodSymbol method) =>
        method.IsOverride
        || method.ExplicitInterfaceImplementations.Length > 0
        || method
            .ContainingType.AllInterfaces.SelectMany(type => type.GetMembers(method.Name))
            .Any(member =>
                SymbolEqualityComparer.Default.Equals(
                    method.ContainingType.FindImplementationForInterfaceMember(member),
                    method
                )
            );

    /// <summary>
    /// True when a comment before the declaration, with its attributes, holds the word "benchmark" in any case.
    /// STYLE.md asks for that comment where a benchmark shows that the nullable form costs real time.
    /// </summary>
    private static bool HasBenchmarkComment(SyntaxNode declaration) =>
        declaration
            .GetLeadingTrivia()
            .Any(trivia =>
                trivia.Kind()
                    is SyntaxKind.SingleLineCommentTrivia
                        or SyntaxKind.MultiLineCommentTrivia
                        or SyntaxKind.SingleLineDocumentationCommentTrivia
                        or SyntaxKind.MultiLineDocumentationCommentTrivia
                && trivia.ToString().IndexOf(BenchmarkWord, StringComparison.OrdinalIgnoreCase) >= 0
            );
}
