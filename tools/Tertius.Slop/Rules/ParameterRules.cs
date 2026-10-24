using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

// The verbosity rules of the user's about parameters and what a function returns (docs/plans/code-quality-tooling.md,
// decision 17).

/// <summary>A return of -1 or an empty string, in a function that is not a Try method.</summary>
public sealed class SentinelReturn : IRule
{
    public string Id => "sentinel-return";

    public string Message =>
        "A return of -1 or an empty string for a failure: write a Try method, or throw (STYLE.md, \"Expected failures\").";

    public string Source => "The user; STYLE.md, \"Make illegal states unrepresentable\" and \"Expected failures\"";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not ReturnStatementSyntax { Expression: { } value } exit || !IsSentinel(value.Unwrapped))
            return [];
        // A comparison returns -1 by its contract, and a Try method has the shape that STYLE.md asks for.
        string? name = exit.HoldingMethodName;
        return SyntaxShapes.IsTryName(name) || name is "Compare" or "CompareTo" ? [] : [exit.Span];
    }

    private static bool IsSentinel(ExpressionSyntax value) => value switch
    {
        PrefixUnaryExpressionSyntax { Operand: LiteralExpressionSyntax { Token.ValueText: "1" } } negative =>
            negative.IsKind(SyntaxKind.UnaryMinusExpression),
        LiteralExpressionSyntax text => text.IsKind(SyntaxKind.StringLiteralExpression) && text.Token.ValueText == "",
        MemberAccessExpressionSyntax
        {
            Expression: PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.StringKeyword }
                or IdentifierNameSyntax { Identifier.ValueText: "String" },
            Name.Identifier.ValueText: "Empty"
        } => true,
        _ => false
    };
}

/// <summary>A parameter that every call site passes the same constant.</summary>
public sealed class ConstantArgument : IRule
{
    // One call passes a constant to many a parameter that a second caller will vary, so the rule waits for two.
    private const int LeastCalls = 2;

    public string Id => "constant-argument";

    public string Message =>
        "A parameter that every call site passes the same constant: delete it, and use the constant inside.";

    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (CallSites.Of(node, context) is not { Count: >= LeastCalls } calls)
            return [];
        return CallSites.Parameters(node).Where((parameter, index) =>
        {
            ExpressionSyntax?[] arguments = [.. calls.Select(call => CallSites.ArgumentFor(call, parameter, index))];
            return arguments[0] is { } first && IsConstant(first) &&
                   arguments.All(argument => argument is not null && argument.SameAs(first));
        }).Select(parameter => parameter.Span);
    }

    // A literal, a default or [], or a member of a type, such as an enum member or a constant.
    private static bool IsConstant(ExpressionSyntax argument) =>
        argument.IsConstantValue || argument.Unwrapped is MemberAccessExpressionSyntax
        {
            Expression: IdentifierNameSyntax { Identifier.ValueText: [var first, ..] }
        } && char.IsUpper(first);
}

/// <summary>An optional parameter that no call site sets.</summary>
public sealed class UnusedOptionalParameter : IRule
{
    public string Id => "unused-optional-parameter";

    public string Message =>
        "An optional parameter that no call site sets: delete it, and use its default inside.";

    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (CallSites.Of(node, context) is not { Count: > 0 } calls)
            return [];
        return CallSites.Parameters(node).Where((parameter, index) =>
                parameter.Default is not null &&
                calls.All(call => CallSites.ArgumentFor(call, parameter, index) is null))
            .Select(parameter => parameter.Span);
    }
}

/// <summary>The call sites of a function, when the syntax of <c>src</c> shows all of them.</summary>
internal static class CallSites
{
    /// <summary>
    /// Every call of a method or local function, when they are all its uses and every caller is in <c>src</c>: a local
    /// function, a private method, or a method outside the public API whose name no test, benchmark or script holds.
    /// Null for another function, such as an override, an extension method or a method used as a method group.
    /// </summary>
    public static IReadOnlyList<InvocationExpressionSyntax>? Of(SyntaxNode node, RuleContext context)
    {
        string? name = node switch
        {
            LocalFunctionStatementSyntax local => local.Identifier.ValueText,
            MethodDeclarationSyntax { ExplicitInterfaceSpecifier: null } method
                when !method.Modifiers.Any(SyntaxKind.OverrideKeyword) && !node.IsPublicApi &&
                     !IsExtension(method) =>
                method.Identifier.ValueText,
            _ => null
        };
        if (name is null)
            return null;
        bool callersInSrc = node is LocalFunctionStatementSyntax || IsPrivate(node) || !context.UsedOutside(name);
        if (!callersInSrc)
            return null;
        InvocationExpressionSyntax?[] calls = [.. context.UsesOf(name).Select(use => use.Call)];
        return calls.All(call => call is not null) ? [.. calls.OfType<InvocationExpressionSyntax>()] : null;
    }

    /// <summary>
    /// The parameters of a method or local function before a <c>params</c> parameter, or none for another node. The
    /// arguments of a <c>params</c> parameter have no place of their own.
    /// </summary>
    public static IEnumerable<ParameterSyntax> Parameters(SyntaxNode node)
    {
        SeparatedSyntaxList<ParameterSyntax> parameters = node switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList.Parameters,
            LocalFunctionStatementSyntax local => local.ParameterList.Parameters,
            _ => default
        };
        return parameters.TakeWhile(parameter => !parameter.Modifiers.Any(SyntaxKind.ParamsKeyword));
    }

    /// <summary>
    /// The argument that <paramref name="call"/> passes to <paramref name="parameter"/>, the one at
    /// <paramref name="index"/>: the argument with its name, or the argument without a name at its place. Null when
    /// the call leaves it out.
    /// </summary>
    public static ExpressionSyntax? ArgumentFor(InvocationExpressionSyntax call, ParameterSyntax parameter, int index)
    {
        SeparatedSyntaxList<ArgumentSyntax> arguments = call.ArgumentList.Arguments;
        ArgumentSyntax? named = arguments.FirstOrDefault(argument =>
            argument.NameColon?.Name.Identifier.ValueText == parameter.Identifier.ValueText);
        ArgumentSyntax? placed = index < arguments.Count && arguments[index].NameColon is null ? arguments[index] : null;
        return (named ?? placed)?.Expression;
    }

    // Private by its modifiers: no access modifier but private, so only its own type calls it.
    private static bool IsPrivate(SyntaxNode node) =>
        node is MethodDeclarationSyntax method && !method.Modifiers.Any(modifier =>
            modifier.Kind() is SyntaxKind.PublicKeyword or SyntaxKind.InternalKeyword or SyntaxKind.ProtectedKeyword);

    // A call of an extension method passes its first argument as the receiver, so the places of the arguments shift.
    private static bool IsExtension(MethodDeclarationSyntax method) =>
        method.ParameterList.Parameters is [{ Modifiers: var modifiers }, ..] &&
        modifiers.Any(SyntaxKind.ThisKeyword);
}
