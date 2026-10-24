using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Tertius.Style;

/// <summary>
/// TS0014: a static class whose name ends in one of <see cref="Suffixes"/> and that declares no extension member: no
/// extension method and no C# 14 <c>extension</c> block. Its functions belong in the types that they work on.
/// </summary>
internal static class HelperClasses
{
    private static readonly string[] Suffixes = ["Helper", "Helpers", "Util", "Utils", "Extensions"];

    public static void Analyze(SymbolAnalysisContext context)
    {
        var type = (INamedTypeSymbol)context.Symbol;
        if (
            !type.IsStatic
            || type.TypeKind != TypeKind.Class
            || !Suffixes.Any(suffix => type.Name.EndsWith(suffix, StringComparison.Ordinal))
            || HasExtensionMember(type, context)
        )
            return;
        context.ReportDiagnostic(Diagnostic.Create(StyleRules.HelperClass, type.Locations[0], type.Name));
    }

    /// <summary>
    /// True for a class with an extension method or an <c>extension</c> block. Microsoft.CodeAnalysis 4.14 has no
    /// symbol for an extension block, so the rule reads its keyword.
    /// </summary>
    private static bool HasExtensionMember(INamedTypeSymbol type, SymbolAnalysisContext context) =>
        type.GetMembers().OfType<IMethodSymbol>().Any(method => method.IsExtensionMethod)
        || type.DeclaringSyntaxReferences.Any(reference =>
            reference.GetSyntax(context.CancellationToken) is TypeDeclarationSyntax declaration
            && declaration
                .Members.OfType<TypeDeclarationSyntax>()
                .Any(member => member.Keyword.ValueText == "extension")
        );
}
