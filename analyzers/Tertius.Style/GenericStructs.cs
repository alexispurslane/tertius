using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Tertius.Style;

/// <summary>
/// TS0012 and TS0013: the hot path form of polymorphism is a type parameter constrained to <c>struct</c> (or
/// <c>unmanaged</c>) and an interface. A call of an instance method or property on a <c>readonly</c> field, an
/// <c>in</c> parameter or a <c>ref readonly</c> parameter of such a type copies the struct first, because the compiler
/// cannot know that the struct is readonly (TS0012). The rule reports the call, which pays the copy: a field or a
/// parameter that the code only copies, such as an event payload, costs nothing more. A conversion of such a type to
/// an interface type boxes the struct (TS0013).
/// </summary>
internal static class GenericStructs
{
    /// <summary>Reports a call of an instance member on a readonly field or parameter of a hot path type parameter.</summary>
    public static void AnalyzeCall(OperationAnalysisContext context)
    {
        IOperation? receiver = context.Operation switch
        {
            IInvocationOperation invocation => invocation.Instance,
            IPropertyReferenceOperation property => property.Instance,
            _ => null
        };
        if (receiver is not { Type: ITypeParameterSymbol parameter } || !IsHotPath(parameter) ||
            ReadonlyName(receiver) is not { } name)
            return;
        context.ReportDiagnostic(Diagnostic.Create(StyleRules.CopiedGenericStruct, receiver.Syntax.GetLocation(), name,
            parameter.Name));
    }

    /// <summary>Reports a conversion of a hot path type parameter to an interface type.</summary>
    public static void AnalyzeConversion(OperationAnalysisContext context)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (conversion is
            {
                Operand.Type: ITypeParameterSymbol parameter,
                Type: { TypeKind: TypeKind.Interface } target
            } && IsHotPath(parameter))
            context.ReportDiagnostic(Diagnostic.Create(StyleRules.BoxedGenericStruct, conversion.Syntax.GetLocation(),
                parameter.Name, target.ToDisplayString()));
    }

    /// <summary>
    /// True for a type parameter constrained to <c>struct</c> or <c>unmanaged</c>: Roslyn sets the value type constraint
    /// for both.
    /// </summary>
    public static bool IsStructConstrained(ITypeParameterSymbol parameter) => parameter.HasValueTypeConstraint;

    /// <summary>True for a type parameter constrained to <c>struct</c> or <c>unmanaged</c> and an interface.</summary>
    private static bool IsHotPath(ITypeParameterSymbol parameter) =>
        IsStructConstrained(parameter) &&
        parameter.ConstraintTypes.Any(constraint => constraint.TypeKind == TypeKind.Interface);

    /// <summary>
    /// The name of the readonly field, or of the <c>in</c> or <c>ref readonly</c> parameter, that
    /// <paramref name="receiver"/> reads, or null for any other receiver.
    /// </summary>
    private static string? ReadonlyName(IOperation receiver) => receiver switch
    {
        IFieldReferenceOperation { Field: { IsReadOnly: true } field } => field.Name,
        IParameterReferenceOperation
        {
            Parameter: { RefKind: RefKind.In or RefKind.RefReadOnlyParameter } parameter
        } => parameter.Name,
        _ => null
    };
}
