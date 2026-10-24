using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Tertius.Unions.Tests;

/// <summary>
/// An in-process compilation of one source file, for the tests that run the generator and the analyzer. It parses C#
/// preview, so built-in <c>union</c> and <c>closed</c> declarations parse, and it reports every warning as an error,
/// as the projects that use the generator do.
/// </summary>
internal static class TestCompilation
{
    public static readonly CSharpParseOptions Parse = new(languageVersion: LanguageVersion.Preview);

    /// <summary>A library of <paramref name="source"/>, with the platform and Tertius.Core as references.</summary>
    public static CSharpCompilation Of(string source)
    {
        string[] platform = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator);
        IEnumerable<MetadataReference> references = platform
            .Append(typeof(GeneratedUnionAttribute).Assembly.Location)
            .Select(path => MetadataReference.CreateFromFile(path));
        return CSharpCompilation.Create(assemblyName: "Probe",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(text: source, options: Parse,
                    cancellationToken: TestContext.Current.CancellationToken)
            ],
            references: references,
            options: new(outputKind: OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable,
                generalDiagnosticOption: ReportDiagnostic.Error));
    }
}
