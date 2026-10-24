using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Tertius.Slop.Rules;

// The verbosity rules about whole functions and their locals (docs/plans/code-quality-tooling.md, decision 17): three
// rules of the user's, and the scb-check rule for a short helper used once.

/// <summary>A method whose whole body passes its parameters to another method.</summary>
public sealed class PassThroughMethod : IRule
{
    public string Id => "pass-through-method";

    public string Message =>
        "A method whose whole body passes its parameters to another method: call that method instead.";

    public string Source =>
        "The user; STYLE.md, \"Composition\" (never hold a part only to pass calls through to it); scb-check rules/trivial_wrapper.py, misc.yaml: identity-wrapper-function";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        // An override, an explicit interface member and a method with an attribute exist for their contract, not for
        // their body.
        if (node is not MethodDeclarationSyntax { AttributeLists.Count: 0, ExplicitInterfaceSpecifier: null } method ||
            method.Modifiers.Any(SyntaxKind.OverrideKeyword))
            return [];
        ExpressionSyntax? body = method switch
        {
            { ExpressionBody.Expression: var expression } => expression,
            { Body.Statements: [ReturnStatementSyntax { Expression: { } returned }] } => returned,
            { Body.Statements: [ExpressionStatementSyntax { Expression: var done }] } => done,
            _ => null
        };
        string[] parameters = [.. method.ParameterList.Parameters.Select(parameter => parameter.Identifier.ValueText)];
        return body is InvocationExpressionSyntax call && call.PassesOn(parameters) ? [method.Span] : [];
    }
}

/// <summary>A short private method or local function that one call uses.</summary>
public sealed class LowUseShortFunction : IRule
{
    // The settings of scb-check's rule are not in its rule file, so these are chosen to match this project's limits: a
    // short function is at most 3 lines, and the caller that takes it in stays within the complexity limit of the
    // erosion and the length limit of MA0051 (decision 3).
    private const int MostSloc = 3;
    private const int MostCallerSloc = 60;

    public string Id => "low-use-short-function";

    public string Message =>
        "A short private function with one call: write its code at the call, as a named local if it needs a name.";

    public string Source =>
        "scb-check rules/low_use_short_function.py; STYLE.md, \"Name the parts of a complex condition\"";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (!Function.IsFunction(node) || PrivateName(node) is not { } name)
            return [];
        var callee = new Function(node);
        SimpleNameSyntax[] uses = [.. context.UsesOf(name).Where(use => !use.Ancestors().Contains(node))];
        if (callee.Sloc > MostSloc || uses is not [var use] || CallOf(use) is not { } call || IsDispatch(call) ||
            Function.Holding(call) is not { } caller)
            return [];
        bool callerStaysSimple = caller.Complexity + callee.Complexity - 1 <= ErosionReport.ComplexityLimit;
        bool callerStaysShort = caller.Sloc + callee.Sloc - 1 <= MostCallerSloc;
        return callerStaysSimple && callerStaysShort ? [node.Span] : [];
    }

    // The name of a private method or of a local function, or null for another function.
    private static string? PrivateName(SyntaxNode node) => node switch
    {
        LocalFunctionStatementSyntax local => local.Identifier.ValueText,
        // An attribute gives a method a job beside its code, such as [Conditional], which inlining would lose.
        MethodDeclarationSyntax
            {
                Parent: TypeDeclarationSyntax and not InterfaceDeclarationSyntax, AttributeLists.Count: 0
            } method
            when IsPrivate(method.Modifiers) => method.Identifier.ValueText,
        _ => null
    };

    // Private by its modifiers: private and not protected, or no access modifier at all. An override, an abstract or
    // partial declaration and an extern method keep their declaration.
    private static bool IsPrivate(SyntaxTokenList modifiers)
    {
        bool keepsDeclaration = modifiers.Any(SyntaxKind.OverrideKeyword) ||
                                modifiers.Any(SyntaxKind.AbstractKeyword) ||
                                modifiers.Any(SyntaxKind.PartialKeyword) || modifiers.Any(SyntaxKind.ExternKeyword);
        bool widened = modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword) ||
                       modifiers.Any(SyntaxKind.InternalKeyword);
        return !keepsDeclaration && !widened;
    }

    // The call that a name is the target of, as F(...) or this.F(...), or null for another use, such as a method group.
    private static InvocationExpressionSyntax? CallOf(SimpleNameSyntax use) => use.Parent switch
    {
        InvocationExpressionSyntax call when call.Expression == use => call,
        MemberAccessExpressionSyntax { Parent: InvocationExpressionSyntax call } access
            when access.Name == use && call.Expression == access => call,
        _ => null
    };

    // Whether a call is the whole job of one case of a switch. The state machines of STYLE.md give each case a method of
    // its own, so such a method stays.
    private static bool IsDispatch(InvocationExpressionSyntax call) => call.Parent switch
    {
        SwitchExpressionArmSyntax arm => arm.Expression == call,
        ExpressionStatementSyntax { Parent: SwitchSectionSyntax }
            or ReturnStatementSyntax { Parent: SwitchSectionSyntax } => true,
        _ => false
    };
}

/// <summary>A local that only feeds the next statement.</summary>
public sealed class SingleUseLocal : IRule
{
    public string Id => "single-use-local";
    public string Message => "A local that only feeds the next statement: write its value there.";

    public string Source =>
        "The user; STYLE.md, \"Names\" (remove a variable that only exists as scratch for another one)";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        if (node is not LocalDeclarationStatementSyntax
            {
                IsConst: false, UsingKeyword.RawKind: 0, AwaitKeyword.RawKind: 0,
                Declaration: { Type: var type, Variables: [{ Initializer.Value: var value } variable] }
            } local || type is RefTypeSyntax || KeepsName(type, value))
            return [];
        string name = variable.Identifier.ValueText;
        IdentifierNameSyntax[] uses =
        [
            .. local.FollowingStatements.SelectMany(statement => statement.DescendantNodes())
                .OfType<IdentifierNameSyntax>().Where(use => use.Identifier.ValueText == name)
        ];
        return local.NextStatement is { } next && !IsLoop(next) && uses is [var only] &&
               next.Span.Contains(only.Span) &&
               CanTakeValue(only, next)
            ? [local.Span]
            : [];
    }

    // A local keeps its name when STYLE.md asks for one ("Name the parts of a complex condition") or when its value
    // takes its type from the declaration.
    private static bool KeepsName(TypeSyntax type, ExpressionSyntax value)
    {
        bool isCondition = type is PredefinedTypeSyntax { Keyword.RawKind: (int)SyntaxKind.BoolKeyword } ||
                           value.Unwrapped is IsPatternExpressionSyntax ||
                           value.Unwrapped.Kind() is SyntaxKind.LogicalAndExpression or SyntaxKind.LogicalOrExpression
                               or SyntaxKind.LogicalNotExpression or SyntaxKind.EqualsExpression
                               or SyntaxKind.NotEqualsExpression or SyntaxKind.GreaterThanExpression
                               or SyntaxKind.GreaterThanOrEqualExpression or SyntaxKind.LessThanExpression
                               or SyntaxKind.LessThanOrEqualExpression or SyntaxKind.IsExpression;
        bool isTargetTyped = value is ImplicitObjectCreationExpressionSyntax or CollectionExpressionSyntax
                                 or AnonymousFunctionExpressionSyntax
                                 or ImplicitStackAllocArrayCreationExpressionSyntax ||
                             value.Kind() is SyntaxKind.DefaultLiteralExpression or SyntaxKind.NullLiteralExpression;
        return isCondition || isTargetTyped;
    }

    // A loop would run the value again on every pass.
    private static bool IsLoop(StatementSyntax statement) =>
        statement is CommonForEachStatementSyntax or ForStatementSyntax or WhileStatementSyntax or DoStatementSyntax;

    // Whether the use reads the value once, where the value could stand: not in a lambda or a local function, which
    // could run it later or again, and not as a variable that the statement changes or refers to.
    private static bool CanTakeValue(IdentifierNameSyntax use, StatementSyntax next)
    {
        bool deferred = use.Ancestors().TakeWhile(ancestor => ancestor != next)
            .Any(ancestor => ancestor is AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax);
        bool asVariable = use.Parent switch
        {
            ArgumentSyntax argument => argument.RefKindKeyword.RawKind != 0,
            AssignmentExpressionSyntax assignment => assignment.Left == use,
            PrefixUnaryExpressionSyntax or PostfixUnaryExpressionSyntax => use.Parent.Kind() is
                SyntaxKind.PreIncrementExpression or SyntaxKind.PreDecrementExpression
                or SyntaxKind.PostIncrementExpression or SyntaxKind.PostDecrementExpression
                or SyntaxKind.AddressOfExpression,
            _ => false
        };
        return !deferred && !asVariable;
    }
}

/// <summary>A null guard on a parameter that the caller's compiler has already checked.</summary>
public sealed class GuardRepeatsCallerCheck : IRule
{
    public string Id => "guard-repeats-caller-check";

    public string Message =>
        "A null guard on a parameter that is not nullable, in a function that is not public API: the callers' nullable checks already made it.";

    public string Source => "The user; STYLE.md, \"Nulls\" (the shell tests for null once)";

    public IEnumerable<TextSpan> Find(SyntaxNode node, RuleContext context)
    {
        // Nullable is on and its warnings are errors, so a caller in this codebase cannot pass null for a parameter
        // whose type is not nullable. A public API keeps its guards for callers outside (STYLE.md, "Errors and
        // contracts"). A parameter of a type parameter can hold null, so it keeps its guard too.
        ParameterListSyntax? list = node switch
        {
            BaseMethodDeclarationSyntax method => method.ParameterList,
            LocalFunctionStatementSyntax local => local.ParameterList,
            _ => null
        };
        if (list is null || !Function.IsFunction(node) || node.IsPublicApi)
            return [];
        HashSet<string> typeParameters = [.. node.AncestorsAndSelf().SelectMany(TypeParameters)];
        HashSet<string> notNullable =
        [
            .. list.Parameters.Where(parameter => parameter.Type is { } type && type is not NullableTypeSyntax &&
                                                  !(type is IdentifierNameSyntax
                                                    {
                                                        Identifier.ValueText: var typeName
                                                    } &&
                                                    typeParameters.Contains(typeName)))
                .Select(parameter => parameter.Identifier.ValueText)
        ];
        return new Function(node).OwnNodes.SelectMany(each => Guard(each, notNullable));
    }

    private static IEnumerable<string> TypeParameters(SyntaxNode node)
    {
        TypeParameterListSyntax? list = node switch
        {
            MethodDeclarationSyntax method => method.TypeParameterList,
            LocalFunctionStatementSyntax local => local.TypeParameterList,
            TypeDeclarationSyntax type => type.TypeParameterList,
            _ => null
        };
        return list?.Parameters.Select(parameter => parameter.Identifier.ValueText) ?? [];
    }

    // The span of a guard on one of the parameters: an if that returns or throws when it is null, ThrowIfNull, or
    // `?? throw`.
    private static IEnumerable<TextSpan> Guard(SyntaxNode node, HashSet<string> parameters) => node switch
    {
        IfStatementSyntax { Condition.NullTested: IdentifierNameSyntax { Identifier.ValueText: var name } } branch
            when parameters.Contains(name) &&
                 branch.Statement.Single is ReturnStatementSyntax or ThrowStatementSyntax =>
            [branch.Span],
        InvocationExpressionSyntax
        {
            Expression: MemberAccessExpressionSyntax { Name.Identifier.ValueText: "ThrowIfNull" },
            ArgumentList.Arguments: [{ Expression: IdentifierNameSyntax { Identifier.ValueText: var name } }, ..]
        } call when parameters.Contains(name) => [call.Span],
        BinaryExpressionSyntax
            {
                Left: IdentifierNameSyntax { Identifier.ValueText: var name }, Right: ThrowExpressionSyntax
            } coalesce
            when coalesce.IsKind(SyntaxKind.CoalesceExpression) && parameters.Contains(name) => [coalesce.Span],
        _ => []
    };
}
