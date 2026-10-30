using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Tertius.Slop;

/// <summary>Executed, statically resolved paths on one receiver supply storage and unresolved source evidence.</summary>
internal sealed class InstanceStateAccess(
    Compilation compilation,
    IReadOnlySet<SyntaxTree> sourceTrees,
    INamedTypeSymbol receiverType
) : OperationWalker
{
    private readonly HashSet<IMethodSymbol> visited = new(SymbolEqualityComparer.Default);
    private readonly HashSet<string> fields = new(StringComparer.Ordinal);
    private readonly HashSet<UnresolvedStateAccess> unresolved = [];
    internal string[] Fields => [.. fields.Order(StringComparer.Ordinal)];
    internal UnresolvedStateAccess[] Unresolved =>
        [
            .. unresolved
                .OrderBy(issue => issue.Location.File, StringComparer.Ordinal)
                .ThenBy(issue => issue.Location.Line)
                .ThenBy(issue => issue.Column)
                .ThenBy(issue => issue.Reason, StringComparer.Ordinal),
        ];

    internal void Follow(IMethodSymbol method, SyntaxNode call)
    {
        method = (method.PartialImplementationPart ?? method).OriginalDefinition;
        if (!visited.Add(method))
            return;
        bool found = false;
        foreach (
            SyntaxReference reference in method.DeclaringSyntaxReferences.Where(reference =>
                sourceTrees.Contains(reference.SyntaxTree)
            )
        )
        {
            SyntaxNode? body = reference.GetSyntax() switch
            {
                BaseMethodDeclarationSyntax declaration => (SyntaxNode?)declaration.Body ?? declaration.ExpressionBody,
                AccessorDeclarationSyntax accessor => (SyntaxNode?)accessor.Body ?? accessor.ExpressionBody,
                PropertyDeclarationSyntax property => property.ExpressionBody,
                IndexerDeclarationSyntax indexer => indexer.ExpressionBody,
                LocalFunctionStatementSyntax local => (SyntaxNode?)local.Body ?? local.ExpressionBody,
                ArrowExpressionClauseSyntax arrow => arrow,
                _ => null,
            };
            if (body is null)
                continue;
            found = true;
            Visit(compilation.GetSemanticModel(body.SyntaxTree).GetOperation(body));
        }
        if (!found)
        {
            visited.Remove(method);
            Unknown(call, "source-body-unavailable");
        }
    }

    public override void VisitFieldReference(IFieldReferenceOperation operation)
    {
        if (!operation.Field.IsStatic && IsThis(operation.Instance))
            fields.Add(operation.Field.OriginalDefinition.ToDisplayString());
        base.VisitFieldReference(operation);
    }

    public override void VisitParameterReference(IParameterReferenceOperation operation)
    {
        if (
            operation.Parameter.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax().Parent?.Parent is TypeDeclarationSyntax
            )
        )
            Unknown(operation.Syntax, "primary-constructor-storage-unavailable");
        base.VisitParameterReference(operation);
    }

    public override void VisitPropertyReference(IPropertyReferenceOperation operation)
    {
        if (!operation.Property.IsStatic && IsThis(operation.Instance))
        {
            IOperation target = operation;
            while (target.Parent is ITupleOperation)
                target = target.Parent;
            bool write =
                !operation.Property.ReturnsByRef
                && !operation.Property.ReturnsByRefReadonly
                && (
                    target.Parent is IAssignmentOperation assignment && assignment.Target == target
                    || target.Parent is IIncrementOrDecrementOperation
                );
            bool read =
                !write
                || target.Parent
                    is ICompoundAssignmentOperation
                        or ICoalesceAssignmentOperation
                        or IIncrementOrDecrementOperation;
            if (read)
                Accessor(operation.Property.GetMethod, operation);
            if (write)
                Accessor(operation.Property.SetMethod, operation);
        }
        base.VisitPropertyReference(operation);
    }

    private void Accessor(IMethodSymbol? method, IPropertyReferenceOperation operation)
    {
        if (method is null)
        {
            Unknown(operation.Syntax, "accessor-unavailable");
            return;
        }
        if (Resolve(method, operation) is not { } resolved)
            return;
        if (resolved.AssociatedSymbol is not IPropertySymbol property)
        {
            // Roslyn normally retains this association, but recovery symbols may not carry it.
            Unknown(operation.Syntax, "accessor-unavailable");
            return;
        }
        IFieldSymbol? storage = property
            .ContainingType.GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(field => SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, property));
        bool automatic =
            resolved.IsImplicitlyDeclared
                && property.DeclaringSyntaxReferences.Any(reference => sourceTrees.Contains(reference.SyntaxTree))
            || resolved.DeclaringSyntaxReferences.Any(reference =>
                sourceTrees.Contains(reference.SyntaxTree)
                && reference.GetSyntax() is AccessorDeclarationSyntax { Body: null, ExpressionBody: null }
            );
        if (automatic && storage is not null)
            fields.Add(storage.OriginalDefinition.ToDisplayString());
        else
            Follow(resolved, operation.Syntax);
    }

    public override void VisitInvocation(IInvocationOperation operation)
    {
        IMethodSymbol method = operation.TargetMethod;
        if (!method.IsStatic && (IsThis(operation.Instance) || method.MethodKind == MethodKind.LocalFunction))
        {
            if (Resolve(method, operation) is { } resolved)
                Follow(resolved, operation.Syntax);
        }
        base.VisitInvocation(operation);
    }

    private IMethodSymbol? Resolve(IMethodSymbol method, IOperation operation)
    {
        if (
            !(method.IsVirtual || method.IsOverride || method.IsAbstract)
            || method.IsSealed
            || method.DeclaredAccessibility == Accessibility.Private
            || operation.Syntax is MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax }
            || operation.Syntax
                is InvocationExpressionSyntax
                {
                    Expression: MemberAccessExpressionSyntax { Expression: BaseExpressionSyntax }
                }
        )
            return method;
        if (receiverType.IsSealed)
        {
            if (
                method.ContainingType.TypeKind == TypeKind.Interface
                && receiverType.FindImplementationForInterfaceMember(method) is IMethodSymbol implementation
            )
                return implementation;
            for (INamedTypeSymbol? current = receiverType; current is not null; current = current.BaseType)
                foreach (IMethodSymbol candidate in current.GetMembers().OfType<IMethodSymbol>())
                    for (
                        IMethodSymbol? overridden = candidate;
                        overridden is not null;
                        overridden = overridden.OverriddenMethod
                    )
                        if (
                            SymbolEqualityComparer.Default.Equals(
                                overridden.OriginalDefinition,
                                method.OriginalDefinition
                            )
                        )
                            return candidate;
        }
        Unknown(operation.Syntax, "runtime-dispatch");
        return null;
    }

    private static bool IsThis(IOperation? operation) =>
        operation switch
        {
            IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.ContainingTypeInstance } => true,
            IInstanceReferenceOperation { ReferenceKind: InstanceReferenceKind.PatternInput } pattern => PatternThis(
                pattern
            ),
            IConversionOperation conversion
                when conversion.Conversion.IsIdentity || conversion.Conversion.IsReference => IsThis(
                conversion.Operand
            ),
            IConditionalAccessInstanceOperation conditional => ConditionalThis(conditional),
            _ => false,
        };

    private static bool PatternThis(IOperation operation)
    {
        IOperation? pattern = operation.Parent;
        while (pattern is not null and not IPatternOperation)
            pattern = pattern.Parent;
        for (IOperation? parent = pattern?.Parent; parent is not null; parent = parent.Parent)
            switch (parent)
            {
                case IIsPatternOperation matched:
                    return IsThis(matched.Value);
                case ISwitchExpressionOperation expression:
                    return IsThis(expression.Value);
                case ISwitchOperation statement:
                    return IsThis(statement.Value);
                case IPropertySubpatternOperation:
                    return false;
                default:
                    break;
            }
        // Recovery patterns without a matched input cannot establish receiver identity.
        return false;
    }

    private static bool ConditionalThis(IOperation operation)
    {
        for (IOperation? parent = operation.Parent; parent is not null; parent = parent.Parent)
            if (parent is IConditionalAccessOperation access)
                return IsThis(access.Operation);
        // A recovery operation without its conditional-access parent cannot establish receiver identity.
        return false;
    }

    public override void VisitInvalid(IInvalidOperation operation)
    {
        if (PotentialThis(operation.Syntax))
            Unknown(operation.Syntax, "unresolved-binding");
        base.VisitInvalid(operation);
    }

    private bool PotentialThis(SyntaxNode syntax) =>
        syntax switch
        {
            InvocationExpressionSyntax { Expression: SimpleNameSyntax } => true,
            InvocationExpressionSyntax invocation => PotentialThis(invocation.Expression),
            MemberAccessExpressionSyntax member => ReceiverThis(member.Expression),
            ElementAccessExpressionSyntax element => ReceiverThis(element.Expression),
            ConditionalAccessExpressionSyntax conditional => ReceiverThis(conditional.Expression),
            MemberBindingExpressionSyntax or ElementBindingExpressionSyntax => ReceiverThis(
                syntax.Ancestors().OfType<ConditionalAccessExpressionSyntax>().FirstOrDefault()?.Expression
            ),
            SimpleNameSyntax name => name.Parent is not MemberAccessExpressionSyntax and not InvocationExpressionSyntax
                && !(
                    name.Parent is AssignmentExpressionSyntax assignment
                    && assignment.Left == name
                    && assignment.Parent is InitializerExpressionSyntax
                ),
            _ => false,
        };

    private bool ReceiverThis(ExpressionSyntax? expression)
    {
        while (expression is ParenthesizedExpressionSyntax parentheses)
            expression = parentheses.Expression;
        return expression is not null
            && IsThis(compilation.GetSemanticModel(expression.SyntaxTree).GetOperation(expression));
    }

    public override void VisitDynamicMemberReference(IDynamicMemberReferenceOperation operation)
    {
        if (IsThis(operation.Instance))
            Unknown(
                operation.Parent is IDynamicInvocationOperation invocation ? invocation.Syntax : operation.Syntax,
                "dynamic-dispatch"
            );
        base.VisitDynamicMemberReference(operation);
    }

    public override void VisitDynamicIndexerAccess(IDynamicIndexerAccessOperation operation)
    {
        if (IsThis(operation.Operation))
            Unknown(operation.Syntax, "dynamic-dispatch");
        base.VisitDynamicIndexerAccess(operation);
    }

    private void Unknown(SyntaxNode syntax, string reason) =>
        unresolved.Add(ClassAnalysis.UnresolvedAt(syntax, reason));

    public override void VisitNameOf(INameOfOperation operation) { }

    public override void VisitLocalFunction(ILocalFunctionOperation operation) { }

    public override void VisitAnonymousFunction(IAnonymousFunctionOperation operation) { }
}
