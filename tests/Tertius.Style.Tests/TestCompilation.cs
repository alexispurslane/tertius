using AwesomeAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tertius.Style.Tests;

/// <summary>
/// An in-process compilation of one source file, for the tests that run the analyzer and the code fix. It parses C#
/// preview and allows unsafe code, so that a test can call through a function pointer.
/// </summary>
internal static class TestCompilation
{
    public static readonly CSharpParseOptions Parse = new(languageVersion: LanguageVersion.Preview);

    public static readonly CSharpCompilationOptions Options = new(outputKind: OutputKind.DynamicallyLinkedLibrary,
        nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true);

    /// <summary>The platform assemblies, which hold every type that a test source uses.</summary>
    public static readonly IReadOnlyList<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
        .Select(path => MetadataReference.CreateFromFile(path))
    ];

    /// <summary>
    /// A library of <paramref name="source"/>. The source must compile without a compiler error, so that a test whose
    /// source is wrong cannot pass because the analyzer finds no symbol.
    /// </summary>
    public static CSharpCompilation Of(string source)
    {
        var compilation = CSharpCompilation.Create(assemblyName: "Probe",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(text: source, options: Parse,
                    cancellationToken: TestContext.Current.CancellationToken)
            ],
            references: References, options: Options);
        compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .Should().BeEmpty("the source of a test compiles");
        return compilation;
    }
}
