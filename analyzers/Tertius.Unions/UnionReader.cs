using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tertius.Unions;

/// <summary>The model of one union, or the diagnostics that refuse its declaration.</summary>
internal sealed record UnionResult(UnionModel? Model, EquatableArray<Diagnostic> Diagnostics);

/// <summary>
/// Reads a struct marked <c>[GeneratedUnion]</c> into a <see cref="UnionModel"/>: it checks the declaration, decides
/// how each case is stored, and shares the slots by type (docs/plans/union-tooling.md, "Storage").
/// </summary>
internal static class UnionReader
{
    private static readonly SymbolDisplayFormat TypeFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.AddMiscellaneousOptions(
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
        );

    public static UnionResult Read(GeneratorAttributeSyntaxContext context)
    {
        var union = (INamedTypeSymbol)context.TargetSymbol;
        AttributeData attribute = context.Attributes[0];
        Location location = context.TargetNode.GetLocation();
        var diagnostics = new List<Diagnostic>();

        if (!IsPartial(union))
            diagnostics.Add(Diagnostic.Create(UnionDiagnostics.NotPartial, location, union.Name));
        for (INamedTypeSymbol? outer = union.ContainingType; outer != null; outer = outer.ContainingType)
            if (!IsPartial(outer))
                diagnostics.Add(
                    Diagnostic.Create(UnionDiagnostics.ContainerNotPartial, location, outer.Name, union.Name)
                );

        ImmutableArray<ITypeSymbol> cases =
            attribute.ConstructorArguments.Length == 1
                ? [.. attribute.ConstructorArguments[0].Values.Select(value => value.Value).OfType<ITypeSymbol>()]
                : [];
        if (cases.IsEmpty)
            diagnostics.Add(Diagnostic.Create(UnionDiagnostics.NoCases, location, union.Name));
        var seen = new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default);
        foreach (ITypeSymbol type in cases)
        {
            string name = type.ToDisplayString();
            if (type.IsReferenceType)
                diagnostics.Add(Diagnostic.Create(UnionDiagnostics.ClassCase, location, name, union.Name));
            else if (type.IsRefLikeType || type.TypeKind is not (TypeKind.Struct or TypeKind.Enum))
                diagnostics.Add(Diagnostic.Create(UnionDiagnostics.NoField, location, name, union.Name));
            if (!seen.Add(type))
                diagnostics.Add(Diagnostic.Create(UnionDiagnostics.DuplicateCase, location, name, union.Name));
        }

        ITypeSymbol? zeroCase = attribute
            .NamedArguments.Where(argument => argument.Key == "ZeroCase")
            .Select(argument => argument.Value.Value as ITypeSymbol)
            .FirstOrDefault();
        if (zeroCase != null && !seen.Contains(zeroCase))
            diagnostics.Add(
                Diagnostic.Create(UnionDiagnostics.ZeroCaseNotACase, location, zeroCase.ToDisplayString(), union.Name)
            );

        return diagnostics.Count > 0
            ? new(null, new([.. diagnostics]))
            : new UnionResult(Model(union, cases, zeroCase), default);
    }

    private static UnionModel Model(INamedTypeSymbol union, ImmutableArray<ITypeSymbol> cases, ITypeSymbol? zeroCase)
    {
        // The zero case takes kind 0. Without one, kind 0 is the empty value, and the cases count from 1.
        IEnumerable<ITypeSymbol> kindOrder =
            zeroCase == null
                ? cases
                : cases
                    .Where(type => SymbolEqualityComparer.Default.Equals(type, zeroCase))
                    .Concat(cases.Where(type => !SymbolEqualityComparer.Default.Equals(type, zeroCase)));
        var slots = new SlotTable();
        var displays = new HashSet<string>();
        var models = new List<CaseModel>();
        int kind = zeroCase == null ? 1 : 0;
        foreach (ITypeSymbol type in kindOrder)
        {
            string display = type.Name;
            for (int n = 2; !displays.Add(display); n++)
                display = type.Name + n;
            slots.StartCase();
            ImmutableArray<IParameterSymbol>? parameters = PositionalParameters(type);
            ValueModel[] values = parameters is { } fields
                ? [.. fields.Select(p => new ValueModel(p.Name, slots.Place(p.Type, $"{display}.{p.Name}")))]
                : [new(null, slots.Place(type, display))];
            models.Add(
                new(
                    Type: type.ToDisplayString(TypeFormat),
                    Display: display,
                    Kind: kind++,
                    Values: new([.. values]),
                    Whole: parameters == null
                )
            );
        }

        (ImmutableArray<SlotModel> ordered, int[] renumber) = slots.Ordered();
        EquatableArray<CaseModel> renumbered = new([
            .. models.Select(model =>
                model with
                {
                    Values = new([.. model.Values.Select(value => value with { Slot = renumber[value.Slot] })]),
                }
            ),
        ]);

        var containers = new List<string>();
        for (INamedTypeSymbol? outer = union.ContainingType; outer != null; outer = outer.ContainingType)
            containers.Insert(
                0,
                $"partial {Keyword(outer)} {outer.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
            );
        string? space = union.ContainingNamespace.IsGlobalNamespace
            ? null
            : union.ContainingNamespace.ToDisplayString();
        string hint =
            string.Join(
                ".",
                new[] { space }
                    .Concat(containers.Select(c => c.Split(' ').Last()))
                    .Append(union.Name)
                    .Where(part => part != null)
            ) + ".g.cs";
        return new(
            Namespace: space,
            Containers: new([.. containers]),
            Name: union.Name,
            IsReadOnly: union.IsReadOnly,
            Cases: renumbered,
            Slots: new(ordered),
            HasZeroCase: zeroCase != null,
            HintName: hint.Replace('<', '{').Replace('>', '}')
        );
    }

    /// <summary>
    /// The parameters of a positional record struct whose state is exactly its parameters, each read back by the property
    /// that the compiler made for it, or null for any other case, which the union stores whole. A record struct with no
    /// parameter list and no instance fields has no values.
    /// </summary>
    private static ImmutableArray<IParameterSymbol>? PositionalParameters(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { IsRecord: true, IsValueType: true } record)
            return null;
        IMethodSymbol? primary = record.InstanceConstructors.FirstOrDefault(constructor =>
            constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is RecordDeclarationSyntax)
        );
        ImmutableArray<IParameterSymbol> parameters = primary?.Parameters ?? [];
        var properties = new List<IPropertySymbol>();
        foreach (IParameterSymbol parameter in parameters)
        {
            IPropertySymbol? property = record.GetMembers(parameter.Name).OfType<IPropertySymbol>().FirstOrDefault();
            // The compiler's property is declared by the parameter itself. A hand-written one can change the value.
            if (
                property == null
                || !SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type)
                || !property.DeclaringSyntaxReferences.All(reference => reference.GetSyntax() is ParameterSyntax)
            )
                return null;
            properties.Add(property);
        }

        bool onlyParameters = record
            .GetMembers()
            .OfType<IFieldSymbol>()
            .Where(field => !field.IsStatic && !field.IsConst)
            .All(field =>
                properties.Contains(field.AssociatedSymbol as IPropertySymbol, SymbolEqualityComparer.Default)
            );
        bool fromSource = record.DeclaringSyntaxReferences.Length > 0;
        return onlyParameters && fromSource ? parameters : null;
    }

    private static bool IsPartial(INamedTypeSymbol type) =>
        type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax() is TypeDeclarationSyntax declaration
            && declaration.Modifiers.Any(SyntaxKind.PartialKeyword)
        );

    private static string Keyword(INamedTypeSymbol type) =>
        type switch
        {
            { IsRecord: true, IsValueType: true } => "record struct",
            { IsRecord: true } => "record",
            { TypeKind: TypeKind.Struct } => "struct",
            { TypeKind: TypeKind.Interface } => "interface",
            _ => "class",
        };

    /// <summary>
    /// The slots of one union: the k-th value of a type in a case takes the k-th slot of that type, so each type gets as
    /// many slots as the case with the most values of it.
    /// </summary>
    private sealed class SlotTable
    {
        private readonly List<(string Type, int Alignment, bool IsReference, List<string> Holds)> slots = [];
        private readonly Dictionary<string, List<int>> byType = [];
        private readonly Dictionary<string, int> usedInCase = [];

        public void StartCase() => usedInCase.Clear();

        public int Place(ITypeSymbol type, string holds)
        {
            string key = type.ToDisplayString(TypeFormat);
            usedInCase.TryGetValue(key, out int used);
            usedInCase[key] = used + 1;
            if (!byType.TryGetValue(key, out List<int>? ofType))
                byType[key] = ofType = [];
            if (used == ofType.Count)
            {
                ofType.Add(slots.Count);
                slots.Add((key, Alignment(type, 0), type.IsReferenceType, []));
            }

            int slot = ofType[used];
            slots[slot].Holds.Add(holds);
            return slot;
        }

        /// <summary>The slots, the most aligned first and otherwise in the order of their first use, and the new number of each.</summary>
        public (ImmutableArray<SlotModel> Slots, int[] Renumber) Ordered()
        {
            int[] order =
            [
                .. Enumerable.Range(0, slots.Count).OrderByDescending(i => slots[i].Alignment).ThenBy(i => i),
            ];
            int[] renumber = new int[slots.Count];
            for (int position = 0; position < order.Length; position++)
                renumber[order[position]] = position;
            return (
                [
                    .. order.Select(i => new SlotModel(
                        slots[i].Type,
                        string.Join(", ", slots[i].Holds),
                        slots[i].IsReference
                    )),
                ],
                renumber
            );
        }

        /// <summary>The alignment of a field of <paramref name="type"/>, in bytes, as the runtime lays out a struct.</summary>
        private static int Alignment(ITypeSymbol type, int depth)
        {
            if (type.IsReferenceType || type.TypeKind == TypeKind.Pointer)
                return 8;
            if (type is INamedTypeSymbol { EnumUnderlyingType: { } underlying })
                return Alignment(underlying, depth);
            int primitive = type.SpecialType switch
            {
                SpecialType.System_Boolean or SpecialType.System_Byte or SpecialType.System_SByte => 1,
                SpecialType.System_Char or SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
                SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single => 4,
                SpecialType.System_Int64
                or SpecialType.System_UInt64
                or SpecialType.System_Double
                or SpecialType.System_IntPtr
                or SpecialType.System_UIntPtr
                or SpecialType.System_Decimal => 8,
                // Not a primitive: a struct, aligned as its most aligned field.
                _ => 0,
            };
            if (primitive > 0)
                return primitive;
            if (depth > 8)
                return 8;
            return type.GetMembers()
                .OfType<IFieldSymbol>()
                .Where(field => !field.IsStatic && !field.IsConst)
                .Select(field => Alignment(field.Type, depth + 1))
                .DefaultIfEmpty(1)
                .Max();
        }
    }
}
