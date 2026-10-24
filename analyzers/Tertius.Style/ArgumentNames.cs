using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tertius.Style;

/// <summary>
/// TS0002: at a call whose target takes more than <see cref="MostUnnamed"/> parameters, every argument names its
/// parameter. A call is a method call, a delegate invocation, a constructor call, a constructor initializer, a primary
/// constructor's base call or an attribute. The parameter that is <c>params</c> does not count toward the limit, and
/// the arguments that bind to it need no name, because every value of the list has the same role. A call through a
/// function pointer, where C# allows no name, binds to no method symbol, and neither does a dynamic call, so neither is
/// reported. The analyzer and the code fix share
/// <see cref="Find"/>, so the fix names exactly the arguments that the rule asks for.
/// </summary>
internal static class ArgumentNames
{
    /// <summary>The most parameters that a target can take before its calls name their arguments.</summary>
    public const int MostUnnamed = 3;

    /// <summary>
    /// The target of the call that <paramref name="argumentList"/> belongs to, and its arguments that have no name and
    /// must have one. Null when the call needs no names, and when it does not bind to one method.
    /// </summary>
    /// <param name="argumentList">An argument list of a call, or of an attribute.</param>
    public static UnnamedArguments? Find(SyntaxNode argumentList, SemanticModel model, CancellationToken cancel)
    {
        if (argumentList.Parent is not { } call)
            return null;
        if (model.GetSymbolInfo(call, cancel).Symbol is not IMethodSymbol target)
            return null;
        ImmutableArray<IParameterSymbol> parameters = target.Parameters;
        bool endsInParams = parameters.Length > 0 && parameters[parameters.Length - 1].IsParams;
        int counted = endsInParams ? parameters.Length - 1 : parameters.Length;
        if (counted <= MostUnnamed)
            return null;
        ImmutableArray<(SyntaxNode Argument, string Parameter)> unnamed =
        [
            .. PositionalArguments(argumentList)
                .Take(counted)
                .Select((argument, index) => (Argument: argument, Parameter: parameters[index].Name))
                .Where(pair => !IsNamed(pair.Argument)),
        ];
        return unnamed.IsEmpty ? null : new UnnamedArguments(target, unnamed);
    }

    /// <summary>
    /// The arguments that bind to parameters, in order. An attribute's assignments to its properties come after them,
    /// and always carry their names.
    /// </summary>
    private static IReadOnlyList<SyntaxNode> PositionalArguments(SyntaxNode argumentList) =>
        argumentList switch
        {
            ArgumentListSyntax list => [.. list.Arguments],
            AttributeArgumentListSyntax list => [.. list.Arguments.Where(argument => argument.NameEquals == null)],
            _ => [],
        };

    private static bool IsNamed(SyntaxNode argument) =>
        argument is ArgumentSyntax { NameColon: not null } or AttributeArgumentSyntax { NameColon: not null };
}

/// <summary>A call that breaks TS0002: its target, and each argument that needs a name, with that name.</summary>
internal readonly record struct UnnamedArguments(
    IMethodSymbol Target,
    ImmutableArray<(SyntaxNode Argument, string Parameter)> Arguments
);
