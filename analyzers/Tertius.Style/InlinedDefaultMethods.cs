using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0005 for one compilation: a default method of an interface that a type parameter constrained to <c>struct</c> or
/// <c>unmanaged</c> names, or that such an interface inherits, without
/// <c>[MethodImpl(MethodImplOptions.AggressiveInlining)]</c>. A default method is an instance method of an interface
/// with a body that a caller can reach, so not a private one. The rule needs every constraint of the compilation, so
/// each constraint clause records its interfaces, each interface records its default methods, and the end of the
/// compilation reports. A constraint in another assembly is out of sight, which is rare: the struct-constrained code
/// lives beside its interface.
/// </summary>
internal sealed class InlinedDefaultMethods
{
    /// <summary>The value of <c>MethodImplOptions.AggressiveInlining</c>.</summary>
    private const int AggressiveInlining = 256;

    private const string MethodImplAttribute = "System.Runtime.CompilerServices.MethodImplAttribute";

    // Each interface that a struct-constrained type parameter names, with the interfaces that it inherits.
    private readonly ConcurrentDictionary<INamedTypeSymbol, bool> constrained = new(SymbolEqualityComparer.Default);

    // Each default method of an interface of this compilation that has no AggressiveInlining.
    private readonly ConcurrentDictionary<IMethodSymbol, bool> notInlined = new(SymbolEqualityComparer.Default);

    public static void Start(CompilationStartAnalysisContext context)
    {
        var rule = new InlinedDefaultMethods();
        context.RegisterSyntaxNodeAction(rule.RecordConstraint, SyntaxKind.TypeParameterConstraintClause);
        context.RegisterSymbolAction(rule.RecordMethod, SymbolKind.Method);
        context.RegisterCompilationEndAction(rule.Report);
    }

    private void RecordConstraint(SyntaxNodeAnalysisContext context)
    {
        var clause = (TypeParameterConstraintClauseSyntax)context.Node;
        if (context.SemanticModel.GetSymbolInfo(clause.Name, context.CancellationToken).Symbol is not
                ITypeParameterSymbol parameter || !GenericStructs.IsStructConstrained(parameter))
            return;
        foreach (INamedTypeSymbol constraint in parameter.ConstraintTypes.OfType<INamedTypeSymbol>()
                     .Where(type => type.TypeKind == TypeKind.Interface))
        foreach (INamedTypeSymbol named in constraint.AllInterfaces.Prepend(constraint))
            constrained[named.OriginalDefinition] = true;
    }

    private void RecordMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method is { MethodKind: MethodKind.Ordinary, IsStatic: false, IsAbstract: false } &&
            method.DeclaredAccessibility != Accessibility.Private &&
            method.ContainingType.TypeKind == TypeKind.Interface && !IsInlined(method))
            notInlined[method] = true;
    }

    private void Report(CompilationAnalysisContext context)
    {
        foreach (IMethodSymbol method in notInlined.Keys.Where(method => constrained.ContainsKey(method.ContainingType)))
            context.ReportDiagnostic(Diagnostic.Create(StyleRules.DefaultMethodNotInlined, method.Locations[0],
                $"{method.ContainingType.Name}.{method.Name}"));
    }

    private static bool IsInlined(IMethodSymbol method) =>
        method.GetAttributes().Any(attribute =>
            attribute.AttributeClass?.ToDisplayString() == MethodImplAttribute &&
            attribute.ConstructorArguments.FirstOrDefault().Value is { } options &&
            (Convert.ToInt32(options, CultureInfo.InvariantCulture) & AggressiveInlining) != 0);
}
