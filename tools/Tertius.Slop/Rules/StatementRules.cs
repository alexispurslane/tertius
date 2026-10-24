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

    // Whether a value is a string by its syntax: a string literal, an interpolated string, a ToString call,
    // string.Format, or a sum with one of these.
    private static bool IsText(ExpressionSyntax value) => value switch
    {
        LiteralExpressionSyntax literal => literal.IsKind(SyntaxKind.StringLiteralExpression),
        InterpolatedStringExpressionSyntax => true,
        InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: var name }
            } =>
            name is "ToString" or "Format",
        BinaryExpressionSyntax sum when sum.IsKind(SyntaxKind.AddExpression) => IsText(sum.Left) || IsText(sum.Right),
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

// The verbosity rules of the user's about statements.

/// <summary>A list or an array made only to loop over it.</summary>
public sealed class MaterializeToLoop : IRule
{
    public string Id => "materialize-to-loop";

    public string Message =>
        "A ToList or ToArray made only to loop over: loop over the sequence, unless the loop changes its source.";

    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        CommonForEachStatementSyntax
        {
            Expression: InvocationExpressionSyntax
            {
                Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ToList" or "ToArray" },
                ArgumentList.Arguments.Count: 0
            } copy
        } => [copy.Span],
        _ => []
    };
}

/// <summary>A run of more than 10 statements with no blank line and no branch to group them.</summary>
public sealed class StatementWall : IRule
{
    private const int MostStatements = 10;

    public string Id => "statement-wall";

    public string Message =>
        "More than 10 statements in a row with no blank line and no branch: group them with blank lines or methods.";

    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not BlockSyntax { Statements: var statements } block)
            return [];
        SourceText text = block.SyntaxTree.GetText();
        var walls = new List<TextSpan>();
        int start = 0;
        for (int index = 1; index <= statements.Count; index++)
        {
            bool runGoesOn = index < statements.Count && IsPlain(statements[index - 1]) && IsPlain(statements[index]) &&
                             !BlankLineBetween(text, statements[index - 1], statements[index]);
            if (runGoesOn)
                continue;
            if (index - start > MostStatements)
                walls.Add(TextSpan.FromBounds(statements[start].SpanStart, statements[index - 1].Span.End));
            start = index;
        }

        return walls;
    }

    // A statement that is no branch, loop or block, so it groups nothing.
    private static bool IsPlain(StatementSyntax statement) =>
        statement is ExpressionStatementSyntax or LocalDeclarationStatementSyntax or ReturnStatementSyntax
            or ThrowStatementSyntax or BreakStatementSyntax or ContinueStatementSyntax or YieldStatementSyntax
            or EmptyStatementSyntax or GotoStatementSyntax;

    private static bool BlankLineBetween(SourceText text, StatementSyntax first, StatementSyntax second)
    {
        int after = text.Lines.GetLineFromPosition(first.Span.End).LineNumber + 1;
        int before = text.Lines.GetLineFromPosition(second.SpanStart).LineNumber;
        return Enumerable.Range(after, Math.Max(0, before - after))
            .Any(line => string.IsNullOrWhiteSpace(text.Lines[line].ToString()));
    }
}

/// <summary>Output to the console outside the Main method, which hands the console's writers to the program.</summary>
public sealed class ConsoleOutput : IRule
{
    public string Id => "console-output";

    public string Message =>
        "Output to the console: write to the TextWriter that Main hands to the program, or to the World's log.";

    public string Source => "The user";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context) => node switch
    {
        MemberAccessExpressionSyntax
            {
                Expression: IdentifierNameSyntax { Identifier.ValueText: "Console" }
                    or MemberAccessExpressionSyntax { Name.Identifier.ValueText: "Console" },
                Name.Identifier.ValueText: "Write" or "WriteLine" or "Out" or "Error"
            } access
            when access.HoldingMethodName != "Main" => [access.Span],
        _ => []
    };
}
