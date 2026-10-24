using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tertius.Style;

/// <summary>
/// TS0015 for one compilation: a switch statement over a closed set that does not name every case, or whose default
/// section does more than throw <c>UnreachableException</c>. The compiler checks a switch expression for a missing
/// case, but not a switch statement, and IDE0010 covers only enums.
/// <list type="bullet">
/// <item>A union is a type marked <c>[Union]</c>, as a <c>[GeneratedUnion]</c> struct and a built-in union of another
/// assembly are, or a type declared with the <c>union</c> keyword. Its cases are the parameter types of its public
/// constructors with one parameter, as the compiler reads them. A union without a <c>ZeroCase</c> in its
/// <c>[GeneratedUnion]</c> also needs a <c>null</c> label for its empty value.</item>
/// <item>A closed hierarchy is a class declared <c>closed</c> in this compilation. Its cases are its direct subtypes.
/// Microsoft.CodeAnalysis 4.14 cannot tell that a class of another assembly is closed, so such a hierarchy is out of
/// sight.</item>
/// </list>
/// A label names a case when it has no guard and its pattern is the case's type: a type pattern, a declaration pattern
/// or a property or positional pattern whose subpatterns match every value, alone or in an <c>or</c> pattern.
/// </summary>
internal sealed class SwitchStatements
{
    private const string UnionAttribute = "System.Runtime.CompilerServices.UnionAttribute";
    private const string GeneratedUnionAttribute = "Tertius.GeneratedUnionAttribute";
    private const string UnreachableException = "System.Diagnostics.UnreachableException";

    // The direct subtypes of each class of the compilation, by the original definition of the class. Only a switch over
    // a closed hierarchy reads it, so the first such switch builds it.
    private readonly Lazy<Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>> subtypes;

    private SwitchStatements(Compilation compilation)
    {
        subtypes = new(() => DirectSubtypes(compilation));
    }

    public static void Start(CompilationStartAnalysisContext context)
    {
        var rule = new SwitchStatements(context.Compilation);
        context.RegisterOperationAction(rule.Analyze, OperationKind.Switch);
    }

    private void Analyze(OperationAnalysisContext context)
    {
        var statement = (ISwitchOperation)context.Operation;
        if (statement.Value.Type is not INamedTypeSymbol type || SetOf(type, context) is not { } set)
            return;
        string name = type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        List<string> missing = Missing(statement, set);
        if (missing.Count > 0)
            context.ReportDiagnostic(
                Diagnostic.Create(
                    StyleRules.UnnamedCase,
                    ((SwitchStatementSyntax)statement.Syntax).SwitchKeyword.GetLocation(),
                    name,
                    $"does not name {Listed(missing)}"
                )
            );
        foreach (ISwitchCaseOperation section in statement.Cases.Where(section => !OnlyThrowsUnreachable(section)))
        foreach (IDefaultCaseClauseOperation label in section.Clauses.OfType<IDefaultCaseClauseOperation>())
            context.ReportDiagnostic(
                Diagnostic.Create(
                    StyleRules.UnnamedCase,
                    label.Syntax.GetLocation(),
                    name,
                    "has a default section that does more than throw UnreachableException"
                )
            );
    }

    /// <summary>The closed set that <paramref name="type"/> is, or null for a type that is no closed set.</summary>
    private ClosedSet? SetOf(INamedTypeSymbol type, OperationAnalysisContext context)
    {
        if (HasAttribute(type, UnionAttribute) || IsDeclaredWith(type, "union", context))
            return new ClosedSet(
                Cases:
                [
                    .. type
                        .InstanceConstructors.Where(constructor =>
                            constructor is { DeclaredAccessibility: Accessibility.Public, Parameters.Length: 1 }
                        )
                        .Select(constructor => constructor.Parameters[0].Type),
                ],
                HasEmptyValue: ZeroCase(type) == null
            );
        return
            IsDeclaredWith(type, "closed", context)
            && subtypes.Value.TryGetValue(type.OriginalDefinition, out List<INamedTypeSymbol>? cases)
            ? new ClosedSet(Cases: [.. cases], HasEmptyValue: false)
            : null;
    }

    /// <summary>
    /// The cases of <paramref name="set"/> that no label of <paramref name="statement"/> names, in the order of the
    /// set, and then <c>null</c> when the set has an empty value that no label names.
    /// </summary>
    private static List<string> Missing(ISwitchOperation statement, ClosedSet set)
    {
        var named = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        bool namesNull = false;
        foreach (
            IPatternCaseClauseOperation label in statement
                .Cases.SelectMany(section => section.Clauses)
                .OfType<IPatternCaseClauseOperation>()
                .Where(label => label.Guard == null)
        )
            Name(label.Pattern, named, ref namesNull);
        List<string> missing =
        [
            .. set
                .Cases.Where(type => !named.Contains(type))
                .Select(type => type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)),
        ];
        if (set.HasEmptyValue && !namesNull)
            missing.Add("null");
        return missing;
    }

    /// <summary>
    /// Adds to <paramref name="named"/> each type that <paramref name="pattern"/> matches in full, and sets
    /// <paramref name="namesNull"/> when it matches the constant <c>null</c>.
    /// </summary>
    private static void Name(IPatternOperation pattern, HashSet<ITypeSymbol> named, ref bool namesNull)
    {
        if (pattern is IBinaryPatternOperation { OperatorKind: BinaryOperatorKind.Or } either)
        {
            Name(either.LeftPattern, named, ref namesNull);
            Name(either.RightPattern, named, ref namesNull);
            return;
        }

        if (pattern is IConstantPatternOperation { Value.ConstantValue: { HasValue: true, Value: null } })
            namesNull = true;
        else if (TypeMatchedInFull(pattern) is { } type)
            named.Add(type);
    }

    /// <summary>The type whose every value <paramref name="pattern"/> matches, or null.</summary>
    private static ITypeSymbol? TypeMatchedInFull(IPatternOperation pattern) =>
        pattern switch
        {
            ITypePatternOperation type => type.MatchedType,
            IDeclarationPatternOperation { MatchedType: { } type } => type,
            IRecursivePatternOperation { MatchedType: { } type } recursive when MatchesAll(recursive) => type,
            // Any other pattern, such as a subpattern that tests a value, a relation or a negation, names no case in full.
            _ => null,
        };

    /// <summary>
    /// True for a property or positional pattern whose subpatterns all match every value. The input of each subpattern
    /// is the declared type of its property, field or <c>Deconstruct</c> parameter, which carries its nullable
    /// annotation.
    /// </summary>
    private static bool MatchesAll(IRecursivePatternOperation pattern) =>
        pattern.PropertySubpatterns.All(property =>
            MatchesAll(property.Pattern, DeclaredType(property.Member) ?? property.Pattern.InputType)
        )
        && pattern
            .DeconstructionSubpatterns.Select(
                (element, index) =>
                    MatchesAll(
                        element,
                        pattern.DeconstructSymbol is IMethodSymbol deconstruct
                            ? deconstruct.Parameters[index].Type
                            : element.InputType
                    )
            )
            .All(matches => matches);

    /// <summary>
    /// True for a subpattern that matches every value of <paramref name="input"/>: a <c>var</c>, a discard, or the
    /// type of an input that cannot be null, such as <c>{ Ticks: int ticks }</c>, or <c>{ Name: string name }</c> of a
    /// <c>string</c> that is not nullable. The compiler reads a switch expression that way when it checks for a missing
    /// case.
    /// </summary>
    private static bool MatchesAll(IPatternOperation pattern, ITypeSymbol input) =>
        pattern switch
        {
            IDiscardPatternOperation or IDeclarationPatternOperation { MatchesNull: true } => true,
            IDeclarationPatternOperation { MatchedType: { } type } => IsEveryValueOf(type, input),
            ITypePatternOperation { MatchedType: var type } => IsEveryValueOf(type, input),
            _ => false,
        };

    /// <summary>The declared type of the property or field that a property subpattern reads, or null.</summary>
    private static ITypeSymbol? DeclaredType(IOperation member) =>
        member switch
        {
            IPropertyReferenceOperation { Property.Type: var type } => type,
            IFieldReferenceOperation { Field.Type: var type } => type,
            _ => null,
        };

    /// <summary>
    /// True when every value of <paramref name="input"/> is a <paramref name="type"/>, which is not null. Roslyn gives
    /// a struct that is not <c>Nullable&lt;T&gt;</c> the annotation <c>NotAnnotated</c> even where nullable is off.
    /// </summary>
    private static bool IsEveryValueOf(ITypeSymbol type, ITypeSymbol input) =>
        input.NullableAnnotation == NullableAnnotation.NotAnnotated
        && SymbolEqualityComparer.Default.Equals(type, input);

    /// <summary>True for a section whose only statement throws a new <c>UnreachableException</c>.</summary>
    private static bool OnlyThrowsUnreachable(ISwitchCaseOperation section) =>
        section.Body.Length == 1
        && section.Body[0] is IThrowOperation { Exception: { } thrown }
        && Unconverted(thrown) is IObjectCreationOperation { Type: { } type }
        && type.ToDisplayString() == UnreachableException;

    private static IOperation Unconverted(IOperation operation) =>
        operation is IConversionOperation { IsImplicit: true } conversion ? conversion.Operand : operation;

    /// <summary>The <c>ZeroCase</c> that the <c>[GeneratedUnion]</c> of <paramref name="union"/> names, or null.</summary>
    private static ITypeSymbol? ZeroCase(INamedTypeSymbol union) =>
        union
            .GetAttributes()
            .Where(attribute => attribute.AttributeClass?.ToDisplayString() == GeneratedUnionAttribute)
            .SelectMany(attribute => attribute.NamedArguments)
            .Where(argument => argument.Key == "ZeroCase")
            .Select(argument => argument.Value.Value as ITypeSymbol)
            .FirstOrDefault();

    private static bool HasAttribute(INamedTypeSymbol type, string name) =>
        type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);

    /// <summary>
    /// True for a type declared with the keyword or the modifier <paramref name="word"/>. Microsoft.CodeAnalysis 4.14
    /// has no symbol property for a built-in union or a closed class, and the compiler marks a built-in union with
    /// <c>[Union]</c> only in metadata, so the rule reads the declaration.
    /// </summary>
    private static bool IsDeclaredWith(INamedTypeSymbol type, string word, OperationAnalysisContext context) =>
        type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax declaration
            && (
                declaration.Keyword.ValueText == word
                || declaration.Modifiers.Any(modifier => modifier.ValueText == word)
            )
        );

    /// <summary>The direct subtypes of each class that a type of <paramref name="compilation"/> derives from.</summary>
    private static Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>> DirectSubtypes(Compilation compilation)
    {
        var subtypes = new Dictionary<INamedTypeSymbol, List<INamedTypeSymbol>>(SymbolEqualityComparer.Default);
        foreach (INamedTypeSymbol type in Types(compilation.Assembly.GlobalNamespace))
            if (type.BaseType is { } parent)
            {
                if (!subtypes.TryGetValue(parent.OriginalDefinition, out List<INamedTypeSymbol>? children))
                    subtypes[parent.OriginalDefinition] = children = [];
                children.Add(type);
            }

        return subtypes;
    }

    /// <summary>Every type of <paramref name="space"/> and of the namespaces in it, with the types nested in each.</summary>
    private static IEnumerable<INamedTypeSymbol> Types(INamespaceSymbol space) =>
        space.GetNamespaceMembers().SelectMany(Types).Concat(space.GetTypeMembers().SelectMany(WithNested));

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type) =>
        type.GetTypeMembers().SelectMany(WithNested).Prepend(type);

    /// <summary>"A", "A and B", or "A, B and C".</summary>
    private static string Listed(List<string> items) =>
        items.Count == 1 ? items[0] : $"{string.Join(", ", items.Take(items.Count - 1))} and {items[items.Count - 1]}";

    /// <summary>The cases of a closed set, and whether it has an empty value that a <c>null</c> label names.</summary>
    private readonly record struct ClosedSet(ImmutableArray<ITypeSymbol> Cases, bool HasEmptyValue);
}
