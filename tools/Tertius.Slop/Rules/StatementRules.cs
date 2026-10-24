using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

// The verbosity rules about single statements and expressions, ported from the scb-check slop rules
// (docs/plans/code-quality-tooling.md, decision 17).

/// <summary>A lambda that only passes its parameters to a method.</summary>
public sealed class UnnecessaryLambda : IRule
{
    public string Id => "unnecessary-lambda";
    public string Message => "A lambda that only passes its parameters to a method: pass the method group.";
    public string Source => "scb-check misc.yaml: unnecessary-lambda";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not LambdaExpressionSyntax
            {
                ExpressionBody: InvocationExpressionSyntax call, AsyncKeyword.RawKind: 0
            } lambda)
            return [];
        string[] parameters = lambda switch
        {
            SimpleLambdaExpressionSyntax simple => [simple.Parameter.Identifier.ValueText],
            ParenthesizedLambdaExpressionSyntax parenthesized =>
                [.. parenthesized.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText)],
            _ => []
        };
        return parameters.Length > 0 && call.PassesOn(parameters) ? [lambda.Span] : [];
    }
}

/// <summary>A continue at the end of a loop body.</summary>
public sealed class RedundantContinue : IRule
{
    public string Id => "redundant-continue";
    public string Message => "A continue at the end of a loop body: delete it, the loop goes on anyway.";
    public string Source => "scb-check loops_and_comprehensions.yaml: redundant-continue";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        ContinueStatementSyntax { Parent: BlockSyntax { Statements: [.., var last], Parent: var loop } } skip
            when last == skip && loop is CommonForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax
                or DoStatementSyntax => [skip.Span],
        _ => []
    };
}

/// <summary>A return without a value at the end of a function.</summary>
public sealed class RedundantReturn : IRule
{
    public string Id => "redundant-return";
    public string Message => "A return at the end of a function that returns nothing: delete it.";
    public string Source => "scb-check misc.yaml: redundant-return-none";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        ReturnStatementSyntax
            {
                Expression: null, Parent: BlockSyntax { Statements: [.., var last], Parent: { } owner }
            } exit
            when last == exit && (Function.IsFunction(owner) || owner is AnonymousFunctionExpressionSyntax) =>
            [exit.Span],
        _ => []
    };
}

/// <summary>An empty public constructor without parameters that is the only constructor of a class.</summary>
public sealed class EmptyConstructor : IRule
{
    public string Id => "empty-constructor";
    public string Message => "An empty public constructor without parameters, the only one of its class: delete it.";
    public string Source => "scb-check misc.yaml: empty-init";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        // A static constructor changes when the type initializes, an attribute or an initializer gives the constructor a
        // job, and another constructor would hide the default one, so each of them keeps the constructor.
        ConstructorDeclarationSyntax
            {
                ParameterList.Parameters.Count: 0, Initializer: null, Body.Statements.Count: 0, AttributeLists.Count: 0,
                Parent: ClassDeclarationSyntax owner
            } constructor when constructor.Modifiers.Any(SyntaxKind.PublicKeyword) &&
                               !constructor.Modifiers.Any(SyntaxKind.StaticKeyword) &&
                               owner.Members.OfType<ConstructorDeclarationSyntax>().Count() == 1 => [constructor.Span],
        _ => []
    };
}

/// <summary>A string added to a variable in a loop.</summary>
public sealed class ManualStrJoin : IRule
{
    public string Id => "manual-str-join";
    public string Message => "A string added to a variable in a loop: use a StringBuilder or string.Join.";
    public string Source => "scb-check type_conversions.yaml: manual-str-join";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        AssignmentExpressionSyntax addition when addition.IsKind(SyntaxKind.AddAssignmentExpression) &&
                                                 IsText(addition.Right) && InLoop(addition) => [addition.Span],
        _ => []
    };

    // Whether a value is a string by its syntax: a string literal, an interpolated string, a ToString call or
    // string.Format.
    private static bool IsText(ExpressionSyntax value) => value switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
        InterpolatedStringExpressionSyntax => true,
        InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name }
            } =>
            name is "ToString" or "Format",
        _ => false
    };

    // Whether a node runs in a loop of its own function.
    private static bool InLoop(SyntaxNode node) =>
        node.Ancestors().TakeWhile(ancestor => !Function.IsFunction(ancestor))
            .Any(ancestor => ancestor is CommonForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax
                or DoStatementSyntax);
}

/// <summary>A deconstruction into five or more values.</summary>
public sealed class LongTupleUnpacking : IRule
{
    private const int LeastValues = 5;

    public string Id => "long-tuple-unpacking";
    public string Message => "A deconstruction into five or more values: give the group a record struct.";

    public string Source =>
        "scb-check loops_and_comprehensions.yaml: long-tuple-unpacking; STYLE.md, \"Name a type only when it earns a name\"";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        AssignmentExpressionSyntax
        {
            Left: DeclarationExpressionSyntax
            {
                Designation: ParenthesizedVariableDesignationSyntax { Variables.Count: >= LeastValues }
            }
            or TupleExpressionSyntax { Arguments.Count: >= LeastValues }
        } assignment => [assignment.Span],
        _ => []
    };
}
