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
    public static CSharpCompilation Of(string source) => Of(source, assembly: "Probe", libraries: []);

    /// <summary>A program of <paramref name="source"/>, which may hold top-level statements.</summary>
    public static CSharpCompilation Program(string source) =>
        Checked(CSharpCompilation.Create(assemblyName: "Probe", syntaxTrees: [Parsed(source)], references: References,
            options: Options.WithOutputKind(OutputKind.ConsoleApplication)));

    /// <summary>
    /// A library named <paramref name="assembly"/> of <paramref name="source"/> that references the built assemblies
    /// of <paramref name="libraries"/>, as a project references the assemblies of other projects.
    /// </summary>
    public static CSharpCompilation Of(string source, string assembly, IEnumerable<CSharpCompilation> libraries)
    {
        return Checked(CSharpCompilation.Create(assemblyName: assembly, syntaxTrees: [Parsed(source)],
            references: [.. References, .. libraries.Select(Built)], options: Options));
    }

    private static SyntaxTree Parsed(string source) =>
        CSharpSyntaxTree.ParseText(text: source, options: Parse, cancellationToken: TestContext.Current.CancellationToken);

    /// <summary><paramref name="compilation"/>, after a check that it has no compiler error.</summary>
    private static CSharpCompilation Checked(CSharpCompilation compilation)
    {
        compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.ToString())
            .Should().BeEmpty("the source of a test compiles");
        return compilation;
    }

    /// <summary>The assembly that <paramref name="library"/> builds, as metadata.</summary>
    private static MetadataReference Built(CSharpCompilation library)
    {
        using MemoryStream image = new();
        library.Emit(peStream: image, cancellationToken: TestContext.Current.CancellationToken).Success.Should()
            .BeTrue("a library of a test builds");
        return MetadataReference.CreateFromImage(peImage: image.ToArray());
    }
}
