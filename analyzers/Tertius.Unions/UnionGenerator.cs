using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Tertius.Unions;

/// <summary>
/// Writes the members of every struct marked <c>[GeneratedUnion]</c> (docs/plans/union-tooling.md): a constructor and
/// an implicit conversion per case, <c>HasValue</c>, <c>Value</c>, a <c>TryGetValue</c> per case, equality and
/// <c>ToString</c>. <see cref="UnionReader"/> decides the storage, and <see cref="UnionWriter"/> writes the source.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class UnionGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<UnionResult> unions = context.SyntaxProvider.ForAttributeWithMetadataName(
            "Tertius.GeneratedUnionAttribute",
            static (node, _) => node is StructDeclarationSyntax,
            static (union, _) => UnionReader.Read(union)
        );
        context.RegisterSourceOutput(
            unions,
            static (output, result) =>
            {
                foreach (Diagnostic diagnostic in result.Diagnostics)
                    output.ReportDiagnostic(diagnostic);
                if (result.Model is { } model)
                    output.AddSource(model.HintName, UnionWriter.Write(model));
            }
        );
    }
}
