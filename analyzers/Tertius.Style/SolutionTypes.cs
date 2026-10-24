using System;
using Microsoft.CodeAnalysis;

namespace Tertius.Style;

/// <summary>
/// The types that this solution declares: the types of the compilation, and of the assemblies whose names start with
/// <see cref="Prefix"/>, which the other projects build. A build sees the other projects as metadata, so the name of
/// the assembly is what tells them apart from the framework and the packages.
/// </summary>
internal static class SolutionTypes
{
    /// <summary>The start of the name of every assembly that a project of this solution builds.</summary>
    public const string Prefix = "Tertius";

    /// <summary>True for a type that a project of this solution declares.</summary>
    public static bool Declares(ITypeSymbol type, Compilation compilation) =>
        type.ContainingAssembly is { } assembly
        && (
            SymbolEqualityComparer.Default.Equals(assembly, compilation.Assembly)
            || assembly.Name.StartsWith(Prefix, StringComparison.Ordinal)
        );
}
