using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0010: a local declared without <c>using</c> whose initializer makes an <c>IDisposable</c> with <c>new</c>, and
/// that the code uses only through its members: <c>x.Member</c>, <c>x?.Member</c> or <c>x[i]</c>. Every other use of
/// the local hands the object on, to a return, a field or a property, an argument, another local or a lambda that
/// captures it, so the rule leaves the object to its new owner. A local that the code never uses is reported too.
/// </summary>
internal static class UndisposedLocals
{
    public static void Analyze(SyntaxNodeAnalysisContext context)
    {
        var statement = (LocalDeclarationStatementSyntax)context.Node;
        if (!statement.UsingKeyword.IsKind(SyntaxKind.None) || statement.Parent is not { } scope)
            return;
        INamedTypeSymbol disposable = context.Compilation.GetSpecialType(SpecialType.System_IDisposable);
        foreach (VariableDeclaratorSyntax variable in statement.Declaration.Variables)
            if (IsDisposableCreation(variable, disposable, context) &&
                context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken) is ILocalSymbol local &&
                !IsHandedOn(local, scope, context))
                context.ReportDiagnostic(Diagnostic.Create(StyleRules.UndisposedLocal,
                    variable.Identifier.GetLocation(), local.Name));
    }

    /// <summary>True for a variable whose initializer makes an <c>IDisposable</c> with <c>new</c>.</summary>
    private static bool IsDisposableCreation(VariableDeclaratorSyntax variable, INamedTypeSymbol disposable,
        SyntaxNodeAnalysisContext context)
    {
        if (variable.Initializer?.Value is not BaseObjectCreationExpressionSyntax creation ||
            context.SemanticModel.GetTypeInfo(creation, context.CancellationToken).Type is not { } type)
            return false;
        return SymbolEqualityComparer.Default.Equals(type, disposable) ||
               type.AllInterfaces.Contains(disposable, SymbolEqualityComparer.Default);
    }

    /// <summary>True when a use of <paramref name="local"/> in <paramref name="scope"/> hands its object on.</summary>
    private static bool IsHandedOn(ILocalSymbol local, SyntaxNode scope, SyntaxNodeAnalysisContext context) =>
        scope.DescendantNodes().OfType<IdentifierNameSyntax>()
            .Where(name => name.Identifier.ValueText == local.Name)
            .Where(name => SymbolEqualityComparer.Default.Equals(
                context.SemanticModel.GetSymbolInfo(name, context.CancellationToken).Symbol, local))
            .Any(name => !IsMemberUse(name) || IsCaptured(name, scope));

    /// <summary>True for a use of a value through a member: <c>x.Member</c>, <c>x?.Member</c> or <c>x[i]</c>.</summary>
    private static bool IsMemberUse(IdentifierNameSyntax name) => name.Parent switch
    {
        MemberAccessExpressionSyntax access => access.Expression == name,
        ConditionalAccessExpressionSyntax access => access.Expression == name,
        ElementAccessExpressionSyntax access => access.Expression == name,
        _ => false
    };

    /// <summary>True for a use inside a lambda, an anonymous method or a local function, which captures it.</summary>
    private static bool IsCaptured(SyntaxNode name, SyntaxNode scope) =>
        name.Ancestors().TakeWhile(ancestor => ancestor != scope)
            .Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
}
