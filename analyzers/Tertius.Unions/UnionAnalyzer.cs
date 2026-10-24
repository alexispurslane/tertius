using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Unions;

/// <summary>
/// Steers each closed set to its form (docs/plans/union-tooling.md, "The analyzer"). It reports a hand-written
/// <c>[Union]</c> struct (TU0001), a built-in <c>union</c> with a struct case (TU0002), an abstract base whose subtypes
/// are all sealed (TU0004, in <see cref="SealedHierarchies"/>), and a type with an enum <c>Kind</c> beside other fields
/// (TU0005). A class case of a generated union is the generator's TU1002.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class UnionAnalyzer : DiagnosticAnalyzer
{
    private const string UnionAttribute = "System.Runtime.CompilerServices.UnionAttribute";
    private const string GeneratedUnionAttribute = "Tertius.GeneratedUnionAttribute";

    /// <inheritdoc/>
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
    [ClosedSetRules.HandWrittenUnion, ClosedSetRules.BoxedCase, ClosedSetRules.OpenHierarchy, ClosedSetRules.KindTag];

    /// <inheritdoc/>
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeType, SymbolKind.NamedType);
        context.RegisterCompilationStartAction(SealedHierarchies.Start);
    }

    private static void AnalyzeType(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (IsHandWrittenUnion(type))
            context.ReportDiagnostic(Diagnostic.Create(ClosedSetRules.HandWrittenUnion, type.Locations[0], type.Name));
        if (BoxedCase(type, context) is { } boxed)
            context.ReportDiagnostic(
                Diagnostic.Create(ClosedSetRules.BoxedCase, type.Locations[0], type.Name, boxed.ToDisplayString())
            );
        if (KindBesideFields(type) is { } kind)
            context.ReportDiagnostic(
                Diagnostic.Create(ClosedSetRules.KindTag, type.Locations[0], type.Name, kind.ToDisplayString())
            );
    }

    /// <summary>
    /// True for a struct that carries <c>[Union]</c> and is not marked <c>[GeneratedUnion]</c>. The compiler marks a
    /// built-in union with <c>[Union]</c> only in metadata, so its source symbol does not carry it.
    /// </summary>
    private static bool IsHandWrittenUnion(INamedTypeSymbol type) =>
        type.TypeKind == TypeKind.Struct
        && HasAttribute(type, UnionAttribute)
        && !HasAttribute(type, GeneratedUnionAttribute);

    /// <summary>
    /// The first struct case of a built-in <c>union</c> declaration, or null. The cases of a built-in union are the
    /// parameters of the constructors that the compiler declares for it, one per case.
    /// </summary>
    private static ITypeSymbol? BoxedCase(INamedTypeSymbol type, SymbolAnalysisContext context)
    {
        bool builtIn = type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax { Keyword.ValueText: "union" }
        );
        return !builtIn
            ? null
            : type
                .InstanceConstructors.Where(constructor =>
                    constructor.IsImplicitlyDeclared && constructor.Parameters.Length == 1
                )
                .Select(constructor => constructor.Parameters[0].Type)
                .FirstOrDefault(caseType => caseType.IsValueType);
    }

    /// <summary>
    /// The enum type of an instance field or property named <c>Kind</c>, when the type has another instance field
    /// beside it, or null. The backing field of a <c>Kind</c> property is not another field.
    /// </summary>
    private static ITypeSymbol? KindBesideFields(INamedTypeSymbol type)
    {
        ISymbol? kind = type.GetMembers("Kind")
            .FirstOrDefault(member => !member.IsStatic && TypeOf(member) is { TypeKind: TypeKind.Enum });
        if (kind == null)
            return null;
        bool others = type.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(field =>
                !field.IsStatic
                && !SymbolEqualityComparer.Default.Equals(field, kind)
                && !SymbolEqualityComparer.Default.Equals(field.AssociatedSymbol, kind)
            );
        return others ? TypeOf(kind) : null;
    }

    private static ITypeSymbol? TypeOf(ISymbol member) =>
        member switch
        {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };

    private static bool HasAttribute(INamedTypeSymbol type, string name) =>
        type.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == name);
}
