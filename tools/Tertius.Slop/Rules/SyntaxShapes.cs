using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

/// <summary>
/// Facts about syntax that several verbosity rules read. Roslyn owns the syntax types, so these are extension members
/// (STYLE.md, "Composition").
/// </summary>
internal static class SyntaxShapes
{
    // Whether an expression only names a value: this, base, a name, a type, or a member of one of these.
    private static bool IsChainOfNames(ExpressionSyntax expression) => expression switch
    {
        ThisExpressionSyntax or BaseExpressionSyntax or SimpleNameSyntax or PredefinedTypeSyntax => true,
        MemberAccessExpressionSyntax access => IsChainOfNames(access.Expression),
        _ => false
    };

    extension(SyntaxNode node)
    {
        /// <summary>Whether <paramref name="other"/> is the same code, whatever its trivia.</summary>
        public bool SameAs(SyntaxNode? other) => other is not null && SyntaxFactory.AreEquivalent(node, other);

        /// <summary>
        /// Whether a function belongs to the public API: a public or protected member, or a member of an interface. A
        /// public API keeps guards that are always on (STYLE.md, "Errors and contracts").
        /// </summary>
        public bool IsPublicApi => node switch
        {
            LocalFunctionStatementSyntax => false,
            AccessorDeclarationSyntax { Parent.Parent: MemberDeclarationSyntax member } => member.IsPublicApi,
            MemberDeclarationSyntax member => member.Parent is InterfaceDeclarationSyntax ||
                                              member.Modifiers.Any(SyntaxKind.PublicKeyword) ||
                                              member.Modifiers.Any(SyntaxKind.ProtectedKeyword),
            _ => false
        };
    }

    extension(StatementSyntax statement)
    {
        /// <summary>The statement after this one in its block or switch section, or null.</summary>
        public StatementSyntax? NextStatement => statement.Sibling(1);

        /// <summary>The statement before this one in its block or switch section, or null.</summary>
        public StatementSyntax? PreviousStatement => statement.Sibling(-1);

        /// <summary>The statements after this one in its block or switch section.</summary>
        public IEnumerable<StatementSyntax> FollowingStatements =>
            statement.Siblings.SkipWhile(sibling => sibling != statement).Skip(1);

        /// <summary>The one statement that this statement runs: itself, or the only statement of a block, or null.</summary>
        public StatementSyntax? Single => statement switch
        {
            BlockSyntax { Statements: [var only] } => only,
            BlockSyntax => null,
            _ => statement
        };

        /// <summary>Whether control never leaves the end of the statement: a return, a throw, or a block that ends in one.</summary>
        public bool Exits => statement switch
        {
            ReturnStatementSyntax or ThrowStatementSyntax => true,
            BlockSyntax { Statements: [.., var last] } => last.Exits,
            _ => false
        };

        private SyntaxList<StatementSyntax> Siblings => statement.Parent switch
        {
            BlockSyntax block => block.Statements,
            SwitchSectionSyntax section => section.Statements,
            _ => default
        };

        private StatementSyntax? Sibling(int offset)
        {
            SyntaxList<StatementSyntax> siblings = statement.Siblings;
            int index = siblings.IndexOf(statement) + offset;
            bool inside = index >= 0 && index < siblings.Count;
            return inside ? siblings[index] : null;
        }
    }

    extension(IfStatementSyntax branch)
    {
        /// <summary>
        /// The next branch of a ladder of conditions: the if of an else-if, or the next statement when it is an if and
        /// this one has no else. Null for neither.
        /// </summary>
        public IfStatementSyntax? NextBranch => branch.Else switch
        {
            { Statement: IfStatementSyntax elseIf } => elseIf,
            null => branch.NextStatement as IfStatementSyntax,
            _ => null
        };

        /// <summary>
        /// The span from this branch to its next branch, when both test the same value by <paramref name="tested"/>, or
        /// nothing. Two rules read ladders this way.
        /// </summary>
        public IEnumerable<TextSpan> PairedBy(Func<ExpressionSyntax, ExpressionSyntax?> tested) =>
            tested(branch.Condition) is { } value && branch.NextBranch is { } next && value.SameAs(tested(next.Condition))
                ? [TextSpan.FromBounds(branch.SpanStart, next.Span.End)]
                : [];

        /// <summary>The branch whose next branch this one is, or null.</summary>
        public IfStatementSyntax? PreviousBranch => branch.Parent switch
        {
            ElseClauseSyntax { Parent: IfStatementSyntax owner } => owner,
            _ => branch.PreviousStatement is IfStatementSyntax { Else: null } previous ? previous : null
        };
    }

    extension(ExpressionSyntax expression)
    {
        /// <summary>The expression inside any parentheses.</summary>
        public ExpressionSyntax Unwrapped => expression is ParenthesizedExpressionSyntax parenthesized
            ? parenthesized.Expression.Unwrapped
            : expression;

        /// <summary>Whether the expression is a value that does not depend on the code around it: a literal, a default or [].</summary>
        public bool IsConstantValue => expression.Unwrapped switch
        {
            LiteralExpressionSyntax or DefaultExpressionSyntax => true,
            CollectionExpressionSyntax { Elements.Count: 0 } => true,
            _ => false
        };

        /// <summary>The value that a test for null reads, such as <c>x</c> in <c>x is null</c> or <c>x == null</c>, or null.</summary>
        public ExpressionSyntax? NullTested => expression.Unwrapped switch
        {
            IsPatternExpressionSyntax { Pattern: ConstantPatternSyntax { Expression: var constant } } test
                when constant.IsKind(SyntaxKind.NullLiteralExpression) => test.Expression,
            BinaryExpressionSyntax test when test.IsKind(SyntaxKind.EqualsExpression) => test.NullCompared,
            _ => null
        };

        /// <summary>The value that a test for not null reads, such as <c>x</c> in <c>x is not null</c> or <c>x != null</c>, or null.</summary>
        public ExpressionSyntax? NotNullTested => expression.Unwrapped switch
        {
            IsPatternExpressionSyntax
                {
                    Pattern: UnaryPatternSyntax { Pattern: ConstantPatternSyntax { Expression: var constant } }
                } test when constant.IsKind(SyntaxKind.NullLiteralExpression) => test.Expression,
            BinaryExpressionSyntax test when test.IsKind(SyntaxKind.NotEqualsExpression) => test.NullCompared,
            _ => null
        };
    }

    extension(BinaryExpressionSyntax comparison)
    {
        // The side that a comparison with the null literal compares, or null when neither side is the null literal.
        private ExpressionSyntax? NullCompared => (comparison.Left.Unwrapped, comparison.Right.Unwrapped) switch
        {
            (var value, LiteralExpressionSyntax right) when right.IsKind(SyntaxKind.NullLiteralExpression) => value,
            (LiteralExpressionSyntax left, var value) when left.IsKind(SyntaxKind.NullLiteralExpression) => value,
            _ => null
        };
    }

    extension(InvocationExpressionSyntax call)
    {
        /// <summary>
        /// Whether the call only passes <paramref name="parameters"/> on: its arguments are exactly those names, in
        /// order, and it names its target without using them, as <c>F(a, b)</c> or <c>part.F(a, b)</c>, where the receiver
        /// is <c>this</c>, <c>base</c> or a chain of names.
        /// </summary>
        public bool PassesOn(IReadOnlyCollection<string> parameters)
        {
            IEnumerable<string?> arguments = call.ArgumentList.Arguments.Select(argument =>
                argument is { NameColon: null, Expression: IdentifierNameSyntax name } ? name.Identifier.ValueText : null);
            bool namedTarget = call.Expression switch
            {
                SimpleNameSyntax => true,
                MemberAccessExpressionSyntax access => IsChainOfNames(access.Expression),
                _ => false
            };
            bool targetUsesAParameter = call.Expression.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>()
                .Any(name => parameters.Contains(name.Identifier.ValueText));
            return arguments.SequenceEqual(parameters) && namedTarget && !targetUsesAParameter;
        }
    }

    extension(CatchClauseSyntax clause)
    {
        /// <summary>Whether the clause catches every exception: no type, or Exception, and no filter.</summary>
        public bool CatchesAll => clause.Filter is null &&
                                  clause.Declaration?.Type.ToString() is null or "Exception" or "System.Exception";
    }
}
