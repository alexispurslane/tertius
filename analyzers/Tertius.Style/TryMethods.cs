using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0009: a method or a local function named <c>Try</c> and an upper-case letter that does not return <c>bool</c>
/// with at least one <c>out</c> parameter. An override, and an implementation of an interface member, take their shape
/// from the member that they implement, so the rule reports only that member.
/// </summary>
internal static class TryMethods
{
    private const string Prefix = "Try";

    public static void Analyze(SyntaxNodeAnalysisContext context)
    {
        if (
            context.SemanticModel.GetDeclaredSymbol(context.Node, context.CancellationToken) is not IMethodSymbol method
            || !IsTryName(method.Name)
            || HasTryShape(method)
            || Implements(method)
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

    /// <summary>True for <c>Try</c> and an upper-case letter at the start of a name, as in <c>TryGet</c>.</summary>
    private static bool IsTryName(string name) =>
        name.Length > Prefix.Length
        && name.StartsWith(Prefix, StringComparison.Ordinal)
        && char.IsUpper(name[Prefix.Length]);

    private static bool HasTryShape(IMethodSymbol method) =>
        method.ReturnType.SpecialType == SpecialType.System_Boolean
        && method.Parameters.Any(parameter => parameter.RefKind == RefKind.Out);

    /// <summary>
    /// True for an override, and for an implicit implementation of an interface member. The name of an explicit
    /// implementation starts with its interface, such as <c>ICounter.TryCount</c>, so the rule never sees one.
    /// </summary>
    private static bool Implements(IMethodSymbol method) =>
        method.IsOverride
        || method
            .ContainingType.AllInterfaces.SelectMany(type => type.GetMembers(method.Name))
            .Any(member =>
                SymbolEqualityComparer.Default.Equals(
                    method.ContainingType.FindImplementationForInterfaceMember(member),
                    method
                )
            );
}
