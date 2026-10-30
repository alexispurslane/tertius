using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

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
            options: new CSharpCompilationOptions(outputKind: OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true)
        );
        Provenance = new("type-metrics-v2", references.Fingerprint);
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
        (StateMethod[] Methods, SharedStatePair[] Pairs, ClassRatio Tcc) cohesion = Cohesion(type, members);
        return new()
        {
            Name = type.ToDisplayString(),
            Identity = Identity(type),
            Kind = type.TypeKind.ToString(),
            IsRecord = type.IsRecord,
            IsStatic = type.IsStatic,
            Locations = [.. parts.Select(Place)],
            Members = [.. members.Where(IsEntry).Select(Entry)],
            Functions = [.. Functions(parts)],
            Methods = cohesion.Methods,
            SharedPairs = cohesion.Pairs,
            Tcc = cohesion.Tcc,
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

    private (StateMethod[] Methods, SharedStatePair[] Pairs, ClassRatio Tcc) Cohesion(
        INamedTypeSymbol type,
        ISymbol[] members
    )
    {
        StateMethod[] methods =
        [
            .. members
                .OfType<IMethodSymbol>()
                .Where(method => !method.IsStatic && method.MethodKind == MethodKind.Ordinary)
                .Select(State),
        ];
        StateMethod[] visible = [.. methods.Where(method => method.Visible)];
        SharedStatePair[] pairs = [.. SharedPairs(visible)];
        bool state = HasInstanceState(type);
        long possible = state ? (long)visible.Length * (visible.Length - 1) / 2 : 0;
        return (methods, pairs, new(pairs.Length, possible));
    }

    private static bool HasInstanceState(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
            if (current.GetMembers().OfType<IFieldSymbol>().Any(field => !field.IsStatic))
                return true;
        return false;
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

    private StateMethod State(IMethodSymbol method)
    {
        var fields = new HashSet<string>(StringComparer.Ordinal);
        foreach (SyntaxReference reference in (method.PartialImplementationPart ?? method).DeclaringSyntaxReferences)
        {
            SyntaxNode declaration = reference.GetSyntax();
            if (declaration is not MethodDeclarationSyntax methodDeclaration)
                continue;
            SyntaxNode? node = (SyntaxNode?)methodDeclaration.Body ?? methodDeclaration.ExpressionBody;
            if (node is null)
                continue;
            SemanticModel model = compilation.GetSemanticModel(node.SyntaxTree);
            SyntaxNode[] body =
            [
                .. node.DescendantNodes(n =>
                    n is not LocalFunctionStatementSyntax and not AnonymousFunctionExpressionSyntax
                ),
            ];
            foreach (IdentifierNameSyntax name in body.OfType<IdentifierNameSyntax>())
            {
                ExpressionSyntax expression =
                    name.Parent is MemberAccessExpressionSyntax access && access.Name == name ? access : name;
                if (
                    model.GetOperation(expression)
                        is IFieldReferenceOperation
                        {
                            Field.IsStatic: false,
                            Instance: IInstanceReferenceOperation
                            {
                                ReferenceKind: InstanceReferenceKind.ContainingTypeInstance
                            }
                        } field
                    && !IsNameOnly(name, model)
                )
                    fields.Add(field.Field.ToDisplayString());
            }
        }
        return new(Member: Entry(method), Fields: [.. fields.Order(StringComparer.Ordinal)], Visible: Visible(method));
    }

    private static bool IsNameOnly(IdentifierNameSyntax name, SemanticModel model) =>
        name.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
                && model.GetOperation(invocation) is INameOfOperation
            );

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

    private static string Identity(INamedTypeSymbol type) =>
        $"source:{type.OriginalDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}";
}
