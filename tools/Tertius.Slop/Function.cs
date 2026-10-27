using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tertius.Slop;

/// <summary>
/// A function in a syntax tree, and the code that it owns (docs/plans/code-quality-tooling.md, decision 16). A function
/// is a method, a constructor, a destructor, an operator, a conversion operator, a property, indexer or event accessor
/// with a body, an expression-bodied property or indexer, or a local function. A declaration without a body, such as the
/// accessor of an auto-property, holds no code, so it is not a function. A lambda or an anonymous method belongs to the
/// function that holds it. A local function is its own function: its branches and its lines are not in the function
/// that holds it.
/// </summary>
public sealed record Function(SyntaxNode Node)
{
    /// <summary>
    /// The syntactic cyclomatic complexity: 1, plus 1 for each <c>if</c>, <c>while</c>, <c>do</c>, <c>for</c>,
    /// <c>foreach</c>, <c>catch</c>, <c>?:</c>, <c>??</c>, <c>??=</c>, <c>&amp;&amp;</c>, <c>||</c> and pattern
    /// <c>and</c> or <c>or</c>, and 1 for each switch statement and switch expression as a whole.
    /// </summary>
    public int Complexity => 1 + Node.ChildNodes().Sum(BranchesIn);

    /// <summary>The number of lines in the function that hold code, and not the lines of its local functions.</summary>
    public int Sloc =>
        Node.DescendantTokens(descendIntoChildren: node => node == Node || node is not LocalFunctionStatementSyntax)
            .CodeLineCount();

    /// <summary>
    /// The name, after the names of the types and functions that hold it, such as <c>World.Step</c>,
    /// <c>Body.Mass.get</c> or <c>World.Step.Local</c>. A namespace adds nothing, because the file path says it.
    /// </summary>
    public string Name => string.Join('.', Node.AncestorsAndSelf().Reverse().Select(NamePart).OfType<string>());

    /// <summary>The line where the declaration starts after its attributes, from 1.</summary>
    public int Line =>
        Node.DescendantTokens(descendIntoChildren: node => node is not AttributeListSyntax)
            .First()
            .GetLocation()
            .GetLineSpan()
            .StartLinePosition.Line + 1;

    /// <summary>The last line of the declaration, from 1: the line of its closing brace or semicolon.</summary>
    public int EndLine => Node.GetLocation().GetLineSpan().EndLinePosition.Line + 1;

    /// <summary>The nodes that the function owns: its descendants, but not those of its local functions.</summary>
    public IEnumerable<SyntaxNode> OwnNodes =>
        Node.DescendantNodes(descendIntoChildren: node => node == Node || node is not LocalFunctionStatementSyntax)
            .Where(node => node is not LocalFunctionStatementSyntax);

    /// <summary>Every function in <paramref name="root"/>, in the order of the source.</summary>
    public static IEnumerable<Function> In(SyntaxNode root) =>
        root.DescendantNodes().Where(IsFunction).Select(node => new Function(node));

    /// <summary>The measures of this function, in the file at <paramref name="file"/>.</summary>
    public FunctionMeasure Measure(string file) =>
        new()
        {
            File = file,
            Line = Line,
            EndLine = EndLine,
            Name = Name,
            Complexity = Complexity,
            Sloc = Sloc,
        };

    /// <summary>The function that holds <paramref name="node"/>, or null outside every function.</summary>
    public static Function? Holding(SyntaxNode node) =>
        node.Ancestors().FirstOrDefault(IsFunction) is { } function ? new Function(function) : null;

    /// <summary>Whether <paramref name="node"/> is a function with code (the doc comment of this type lists the kinds).</summary>
    public static bool IsFunction(SyntaxNode node) =>
        node switch
        {
            BaseMethodDeclarationSyntax method => HasCode(method.Body, method.ExpressionBody),
            AccessorDeclarationSyntax accessor => HasCode(accessor.Body, accessor.ExpressionBody),
            LocalFunctionStatementSyntax local => HasCode(local.Body, local.ExpressionBody),
            PropertyDeclarationSyntax property => property.ExpressionBody is not null,
            IndexerDeclarationSyntax indexer => indexer.ExpressionBody is not null,
            _ => false,
        };

    private static bool HasCode(BlockSyntax? body, ArrowExpressionClauseSyntax? expressionBody) =>
        body is not null || expressionBody is not null;

    // A switch is one branch, whatever its sections, arms, guards and patterns, because the switch rule asks for one
    // arm per case of a closed set (decision 16). The code that a section or an arm runs still counts.
    private static int BranchesIn(SyntaxNode node) =>
        node switch
        {
            LocalFunctionStatementSyntax => 0,
            SwitchStatementSyntax statement => 1
                + BranchesIn(statement.Expression)
                + statement.Sections.SelectMany(section => section.Statements).Sum(BranchesIn),
            SwitchExpressionSyntax expression => 1
                + BranchesIn(expression.GoverningExpression)
                + expression.Arms.Sum(arm => BranchesIn(arm.Expression)),
            _ => (Branches(node.Kind()) ? 1 : 0) + node.ChildNodes().Sum(BranchesIn),
        };

    // A pattern's and and or count like && and ||, because `x is 1 or 2` makes the same decision as `x == 1 || x == 2`.
    private static bool Branches(SyntaxKind kind) =>
        kind
            is SyntaxKind.IfStatement
                or SyntaxKind.WhileStatement
                or SyntaxKind.DoStatement
                or SyntaxKind.ForStatement
                or SyntaxKind.ForEachStatement
                or SyntaxKind.ForEachVariableStatement
                or SyntaxKind.CatchClause
                or SyntaxKind.ConditionalExpression
                or SyntaxKind.CoalesceExpression
                or SyntaxKind.CoalesceAssignmentExpression
                or SyntaxKind.LogicalAndExpression
                or SyntaxKind.LogicalOrExpression
                or SyntaxKind.AndPattern
                or SyntaxKind.OrPattern;

    private static string? NamePart(SyntaxNode node) =>
        node switch
        {
            ExtensionBlockDeclarationSyntax extension => $"extension{extension.ParameterList}",
            BaseTypeDeclarationSyntax type => type.Identifier.ValueText,
            MethodDeclarationSyntax method => method.Identifier.ValueText,
            ConstructorDeclarationSyntax constructor => constructor.Identifier.ValueText,
            DestructorDeclarationSyntax destructor => $"~{destructor.Identifier.ValueText}",
            OperatorDeclarationSyntax op => $"operator {op.OperatorToken.ValueText}",
            ConversionOperatorDeclarationSyntax conversion =>
                $"{conversion.ImplicitOrExplicitKeyword.ValueText} operator {conversion.Type}",
            PropertyDeclarationSyntax property => property.Identifier.ValueText,
            EventDeclarationSyntax @event => @event.Identifier.ValueText,
            IndexerDeclarationSyntax => "this[]",
            AccessorDeclarationSyntax accessor => accessor.Keyword.ValueText,
            LocalFunctionStatementSyntax local => local.Identifier.ValueText,
            _ => null,
        };
}
