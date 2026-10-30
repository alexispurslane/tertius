using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tertius.Slop;

/// <summary>A source compilation binds names, partial declarations, aliases and shadowed fields without emitting code.</summary>
internal sealed class ClassAnalysis
{
    private readonly CSharpCompilation compilation;
    private readonly Dictionary<INamedTypeSymbol, List<SyntaxNode>> declarations =
    [
        with(SymbolEqualityComparer.Default),
    ];
    private readonly HashSet<SyntaxTree> sourceTrees;
    internal ClassMetricProvenance Provenance { get; }

    internal ClassAnalysis(Codebase codebase, ClassMetricOptions options)
    {
        sourceTrees = [.. codebase.Files.Where(file => !file.IsGenerated).Select(file => file.Tree)];
        (MetadataReference[] References, string Fingerprint) references = options.References();
        SyntaxTree globals = CSharpSyntaxTree.ParseText(
            text: "global using System; global using System.Collections.Generic; global using System.IO; global using System.Linq; global using System.Net.Http; global using System.Threading; global using System.Threading.Tasks;",
            options: new CSharpParseOptions(languageVersion: LanguageVersion.Preview)
        );
        compilation = CSharpCompilation.Create(
            assemblyName: "SlopSnapshot",
            syntaxTrees: sourceTrees.Append(globals),
            references: references.References,
            options: new CSharpCompilationOptions(
                outputKind: OutputKind.DynamicallyLinkedLibrary,
                allowUnsafe: true,
                metadataImportOptions: MetadataImportOptions.All
            )
        );
        Provenance = new("type-metrics-v3", references.Fingerprint);
        foreach (SyntaxTree tree in sourceTrees)
        {
            SemanticModel model = compilation.GetSemanticModel(tree);
            foreach (
                SyntaxNode declaration in tree.GetRoot()
                    .DescendantNodes()
                    .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
            )
            {
                if (
                    declaration is ExtensionBlockDeclarationSyntax
                    || model.GetDeclaredSymbol(declaration) is not INamedTypeSymbol type
                )
                    continue;
                if (!declarations.TryGetValue(type, out List<SyntaxNode>? parts))
                    declarations[type] = parts = [];
                parts.Add(declaration);
            }
        }
    }

    internal ClassMeasure[] Measure() =>
        [
            .. declarations
                .Select(pair => MeasureType(pair.Key, pair.Value))
                .OrderBy(type => type.Identity, StringComparer.Ordinal),
        ];

    internal BindingIssue[] BindingDiagnostics() =>
        [
            .. compilation
                .GetDiagnostics()
                .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                .Select(diagnostic => new BindingIssue(
                    diagnostic.Id,
                    diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture),
                    diagnostic.Location.SourceTree is { } tree
                        ? Place(tree.GetRoot().FindNode(diagnostic.Location.SourceSpan))
                        : null
                )),
        ];

    private ClassMeasure MeasureType(INamedTypeSymbol type, List<SyntaxNode> parts)
    {
        ISymbol[] members = Members(type, parts);
        (StateMethod[] Methods, SharedStatePair[] Pairs, ClassRatio Tcc, UnresolvedStateAccess[] Unresolved) cohesion =
            Cohesion(type, members);
        ClassMember[] entries = [.. members.Where(IsEntry).Select(Entry)];
        FunctionMeasure[] functions = [.. Functions(parts)];
        return new()
        {
            Name = type.ToDisplayString(),
            Identity = Identity(type),
            Kind = type.TypeKind.ToString(),
            IsRecord = type.IsRecord,
            IsStatic = type.IsStatic,
            Locations = [.. parts.Select(Place)],
            Members = entries,
            Functions = functions,
            DepthApplicability = ClassMeasure.ImplementationApplicability(
                functions.Length,
                entries.Count(member => member.IsApiEntry)
            ),
            Methods = cohesion.Methods,
            SharedPairs = cohesion.Pairs,
            Tcc = cohesion.Tcc,
            UnresolvedAccesses = cohesion.Unresolved,
        };
    }

    private static IEnumerable<FunctionMeasure> Functions(IEnumerable<SyntaxNode> parts) =>
        from part in parts
        from function in Function.In(part)
        where
            function
                .Node.Ancestors()
                .Where(node => node is BaseTypeDeclarationSyntax or DelegateDeclarationSyntax)
                .First(type => type is not ExtensionBlockDeclarationSyntax) == part
        select function.Measure(part.SyntaxTree.FilePath);

    private ISymbol[] Members(INamedTypeSymbol type, List<SyntaxNode> parts) =>
        [
            .. type.GetMembers()
                .Concat(
                    parts
                        .OfType<TypeDeclarationSyntax>()
                        .SelectMany(part => part.Members.OfType<ExtensionBlockDeclarationSyntax>())
                        .SelectMany(block =>
                            compilation.GetSemanticModel(block.SyntaxTree).GetDeclaredSymbol(block)
                                is INamedTypeSymbol extension
                                ? extension.GetMembers()
                                : []
                        )
                )
                .Where(IsHandwritten),
        ];

    private (
        StateMethod[] Methods,
        SharedStatePair[] Pairs,
        ClassRatio Tcc,
        UnresolvedStateAccess[] Unresolved
    ) Cohesion(INamedTypeSymbol type, ISymbol[] members)
    {
        StateMethod[] methods =
        [
            .. members
                .OfType<IMethodSymbol>()
                .Where(method => !method.IsStatic && method.MethodKind == MethodKind.Ordinary)
                .Select(method => State(method, type)),
        ];
        StateMethod[] visible = [.. methods.Where(method => method.Visible)];
        SharedStatePair[] pairs = [.. SharedPairs(visible)];
        var unresolved = visible.SelectMany(method => method.UnresolvedAccesses ?? []).ToHashSet();
        bool state = false;
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            state |= current.GetMembers().OfType<IFieldSymbol>().Any(field => !field.IsStatic);
            if (current.TypeKind == TypeKind.Error)
                unresolved.Add(UnresolvedAt(declarations[type][0], "base-storage-unavailable"));
        }
        UnresolvedStateAccess[] evidence =
        [
            .. unresolved
                .OrderBy(issue => issue.Location.File, StringComparer.Ordinal)
                .ThenBy(issue => issue.Location.Line)
                .ThenBy(issue => issue.Column)
                .ThenBy(issue => issue.Reason, StringComparer.Ordinal),
        ];
        MetricApplicability applicability = CohesionApplicability(visible.Length, evidence.Length, state);
        long possible = (long)visible.Length * (visible.Length - 1) / 2;
        return (methods, pairs, new(pairs.Length, possible, applicability), evidence);
    }

    private static MetricApplicability CohesionApplicability(int visibleMethods, int unresolved, bool state)
    {
        if (visibleMethods < 2)
            return new("not-applicable", "fewer-than-two-methods");
        if (unresolved > 0)
            return new("incomplete", "unresolved-state-access");
        if (!state)
            return new("not-applicable", "no-instance-storage");
        return new("complete");
    }

    private bool IsHandwritten(ISymbol member) =>
        !member.IsImplicitlyDeclared
        && member.DeclaringSyntaxReferences.Any(reference =>
            sourceTrees.Contains(reference.SyntaxTree)
            && reference.GetSyntax() switch
            {
                BaseMethodDeclarationSyntax
                or MethodDeclarationSyntax
                or BasePropertyDeclarationSyntax
                or AccessorDeclarationSyntax
                or VariableDeclaratorSyntax
                or EnumMemberDeclarationSyntax => true,
                TypeDeclarationSyntax { ParameterList: not null } => member
                    is IMethodSymbol { MethodKind: MethodKind.Constructor },
                _ => false,
            }
        );

    private static bool Visible(ISymbol member) =>
        member.DeclaredAccessibility
            is Accessibility.Public
                or Accessibility.Internal
                or Accessibility.Protected
                or Accessibility.ProtectedOrInternal
                or Accessibility.ProtectedAndInternal;

    private static bool IsEntry(ISymbol member) =>
        member
            is IFieldSymbol
                or IPropertySymbol
                or IEventSymbol
                or IMethodSymbol
                {
                    MethodKind: MethodKind.Ordinary
                        or MethodKind.Constructor
                        or MethodKind.UserDefinedOperator
                        or MethodKind.Conversion
                };

    private static ClassMember Entry(ISymbol member)
    {
        SyntaxNode node = member.DeclaringSyntaxReferences[0].GetSyntax();
        int parameters = member switch
        {
            IMethodSymbol method => method.Parameters.Length,
            IPropertySymbol property => property.Parameters.Length,
            _ => 0,
        };
        return new(
            Name: member.ToDisplayString(),
            Location: Place(node),
            ParameterCount: parameters,
            Visibility: member.DeclaredAccessibility.ToString(),
            Kind: member.Kind.ToString(),
            IsStatic: member.IsStatic,
            IsApiEntry: Visible(member)
        );
    }

    private StateMethod State(IMethodSymbol method, INamedTypeSymbol type)
    {
        var access = new InstanceStateAccess(compilation, sourceTrees, type);
        access.Follow(method, method.DeclaringSyntaxReferences[0].GetSyntax());
        return new(
            Member: Entry(method),
            Fields: access.Fields,
            Visible: Visible(method),
            UnresolvedAccesses: access.Unresolved
        );
    }

    private static IEnumerable<SharedStatePair> SharedPairs(StateMethod[] methods)
    {
        for (int first = 0; first < methods.Length; first++)
        for (int second = first + 1; second < methods.Length; second++)
        {
            string[] fields = [.. methods[first].Fields.Intersect(methods[second].Fields, StringComparer.Ordinal)];
            if (fields.Length > 0)
                yield return new(methods[first].Member.Name, methods[second].Member.Name, fields);
        }
    }

    internal static SourcePlace Place(SyntaxNode node)
    {
        FileLinePositionSpan span = node.GetLocation().GetLineSpan();
        return new(span.Path, span.StartLinePosition.Line + 1, span.EndLinePosition.Line + 1);
    }

    internal static UnresolvedStateAccess UnresolvedAt(SyntaxNode node, string reason)
    {
        FileLinePositionSpan span = node.GetLocation().GetLineSpan();
        return new(
            Location: Place(node),
            Reason: reason,
            Column: span.StartLinePosition.Character + 1,
            EndColumn: span.EndLinePosition.Character + 1
        );
    }

    private static string Identity(INamedTypeSymbol type) =>
        $"source:{type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
}
